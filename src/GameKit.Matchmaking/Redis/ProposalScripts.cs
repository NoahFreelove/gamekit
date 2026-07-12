// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Redis;

/// <summary>
/// Lua source for the proposal complete + decline-and-reap scripts (Plan 05-06).
/// Held as <c>const</c> strings so <c>IDatabase.ScriptEvaluateAsync</c> caches the SHA1 on
/// first call and falls back automatically on NOSCRIPT.
/// </summary>
/// <remarks>
/// <para>
/// Both scripts are kept compact (≤20 lines each) and return literal bulk strings rather
/// than Lua tables — simpler to parse, harder to corrupt. The scripts deliberately use only
/// O(1) Redis commands (HGET, HSET, SADD, SCARD, ZADD, DEL) so each invocation completes
/// well inside the per-call budget.
/// </para>
/// </remarks>
internal static class ProposalScripts
{
    /// <summary>
    /// Atomic accept-and-complete check (RESEARCH §Decision 2 + Plan 05-06 Task 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>KEYS layout:</b> <c>[proposalKey, acceptsSetKey]</c>.
    /// <b>ARGV layout:</b> <c>[ticketId, expectedCount, ttlSeconds]</c>.
    /// </para>
    /// <para>
    /// <b>Returns (bulk-string):</b>
    /// <list type="bullet">
    ///   <item><c>COMPLETE</c> — this accept brought the count to <c>expectedCount</c>; the
    ///         script HSET <c>state=complete</c> on the proposal hash. The caller is responsible
    ///         for the Postgres write + status PUBLISH.</item>
    ///   <item><c>PENDING</c> — accept was recorded but more acceptors are still expected.</item>
    ///   <item><c>ALREADY</c> — this ticket id was already in the acceptors set (idempotent accept).</item>
    ///   <item><c>COMPLETED</c> — the proposal was already <c>state=complete</c>; the caller's
    ///         late accept is a no-op (T-05-06-04 idempotency).</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Race-safety:</b> SADD + SCARD inside a single Lua step is the atomic anchor that
    /// closes Pitfall §10 (partial-accept race vs proposal sweeper).
    /// </para>
    /// </remarks>
    public const string CompleteLuaSource = @"
-- Step 1: if the proposal is already complete, accept is idempotent.
local state = redis.call('HGET', KEYS[1], 'state')
if state == 'complete' then return 'COMPLETED' end
-- Step 2: record the acceptor (SADD is idempotent — returns 0 if already a member).
local added = redis.call('SADD', KEYS[2], ARGV[1])
-- Refresh the TTL on the acceptors set so it expires together with the proposal hash.
redis.call('EXPIRE', KEYS[2], tonumber(ARGV[3]))
if added == 0 then return 'ALREADY' end
-- Step 3: check whether this accept closes the proposal.
local count = redis.call('SCARD', KEYS[2])
if count >= tonumber(ARGV[2]) then
  redis.call('HSET', KEYS[1], 'state', 'complete')
  return 'COMPLETE'
end
return 'PENDING'
";

    /// <summary>
    /// Atomic decline-and-reap (CONTEXT D-09; Plan 05-06 Task 2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>KEYS layout:</b> <c>[proposalKey, acceptsSetKey, queueKey]</c>.
    /// <b>ARGV layout:</b> <c>[decliningTicketId, ticketCount, ticketId1, queuedAtMs1, ticketId2, queuedAtMs2, ...]</c>.
    /// </para>
    /// <para>
    /// <b>Returns (bulk-string):</b> <c>OK</c> always (the script is purely idempotent
    /// teardown — it cannot fail logically. Caller treats a non-OK reply as an unexpected
    /// Redis error).
    /// </para>
    /// <para>
    /// <b>Behaviour:</b>
    /// <list type="number">
    ///   <item>For each ticket in ARGV: if it is in the <c>acceptors</c> set AND not the
    ///         declining ticket, <c>ZADD</c> it back into the queue with the original
    ///         <c>queuedAtMs</c> score (CONTEXT D-09 — preserves bracket-flex accumulator).</item>
    ///   <item>DEL the acceptors set and the proposal hash.</item>
    /// </list>
    /// </para>
    /// </remarks>
    public const string DeclineLuaSource = @"
-- Step 1: re-queue every ACCEPTING ticket (in acceptors set, not the declining one).
local declining = ARGV[1]
local n = tonumber(ARGV[2])
for i = 1, n do
  local tid = ARGV[1 + i * 2]
  local score = tonumber(ARGV[2 + i * 2])
  if tid ~= declining and redis.call('SISMEMBER', KEYS[2], tid) == 1 then
    redis.call('ZADD', KEYS[3], score, tid)
  end
end
-- Step 2: tear down the proposal hash + acceptors set.
redis.call('DEL', KEYS[2])
redis.call('DEL', KEYS[1])
return 'OK'
";
}
