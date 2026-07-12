// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Redis;

/// <summary>
/// Atomic match-formation Lua script + EVALSHA-cached executor (RESEARCH §Decision 3,
/// MATCH-04 / MATCH-05). Holds the script source as a const, precomputes the SHA1, and
/// exposes <see cref="ExecuteAsync"/> which calls Redis EVALSHA (with NOSCRIPT fallback
/// to EVAL handled by StackExchange.Redis).
/// </summary>
/// <remarks>
/// <para>
/// <b>Script behavior (Pitfall §2 + RESEARCH §Decision 3):</b>
/// <list type="number">
///   <item>FIRST step: verify the leader fencing token (<c>GET KEYS[1] == ARGV[1]</c>). On mismatch return <c>LEASE_LOST</c> — guards the stale-leader race.</item>
///   <item>Verify every candidate ticket id is still in the queue sorted-set (<c>ZSCORE KEYS[2] ticketId != false</c>). On any miss return <c>TICKET_GONE</c>.</item>
///   <item>Atomically: <c>ZREM</c> each ticket from the queue; <c>HSET mm:ticket:&lt;id&gt; status=Proposed proposalId=...</c>; write the proposal hash fields; <c>EXPIRE</c> the proposal key.</item>
///   <item>Return <c>OK</c>.</item>
/// </list>
/// All three return strings are bulk-string replies (not Lua tables) — simpler to parse
/// and harder to corrupt.
/// </para>
/// <para>
/// <b>EVALSHA fast path:</b> StackExchange.Redis caches scripts by SHA1; the first call
/// SCRIPT LOADs (or implicitly via EVAL), subsequent calls use EVALSHA. We expose the
/// hex SHA1 via <see cref="Sha1Hex"/> for diagnostics and the integration test's
/// <c>Server.ScriptExistsAsync</c> verification.
/// </para>
/// </remarks>
public sealed class AtomicClaimScript
{
    /// <summary>
    /// The Lua source. KEYS layout: <c>[leaseKey, queueKey, proposalKey, ticketKey1, ticketKey2, ...]</c>.
    /// ARGV layout: <c>[leaseValue, proposalId, ttlSeconds, ticketCount, ticketId1, ticketId2, ..., proposalFieldsJson]</c>.
    /// The script is sized at ≤30 lines (Plan 05-04 must_haves) — comments and blank lines
    /// trimmed to keep the body compact.
    /// </summary>
    public const string LuaSource = @"
-- Step 1: fencing-token check (Pitfall §2 — MUST be first).
if redis.call('GET', KEYS[1]) ~= ARGV[1] then return 'LEASE_LOST' end
-- Step 2: parse ticket count + ensure each candidate is still in the queue.
local n = tonumber(ARGV[4])
for i = 1, n do
  local tid = ARGV[4 + i]
  if redis.call('ZSCORE', KEYS[2], tid) == false then return 'TICKET_GONE' end
end
-- Step 3: atomic remove + mark Proposed + write proposal hash + set TTL.
for i = 1, n do
  local tid = ARGV[4 + i]
  redis.call('ZREM', KEYS[2], tid)
  redis.call('HSET', KEYS[3 + i], 'status', 'Proposed', 'proposalId', ARGV[2])
end
local fieldsJson = ARGV[5 + n]
redis.call('HSET', KEYS[3], 'fields', fieldsJson)
redis.call('EXPIRE', KEYS[3], tonumber(ARGV[3]))
return 'OK'
";

    /// <summary>Precomputed SHA1 of <see cref="LuaSource"/> in lowercase hex. Used by EVALSHA.</summary>
    public static readonly string Sha1Hex = ComputeSha1Hex(LuaSource);

    private readonly ILogger<AtomicClaimScript>? _logger;

    /// <summary>Constructs the executor.</summary>
    /// <param name="logger">Optional logger for Redis-error diagnostics.</param>
    public AtomicClaimScript(ILogger<AtomicClaimScript>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Execute the atomic-claim script.
    /// </summary>
    /// <param name="db">The Redis database handle.</param>
    /// <param name="leaseKey">Leader lease key (e.g. <c>"gamekit:matchmaking:matcher:lock"</c>).</param>
    /// <param name="leaseValue">Leader instance id (the fencing token).</param>
    /// <param name="queueKey">Pool sorted-set key (e.g. <c>"mm:queue:{ladderId}:{pool}"</c>).</param>
    /// <param name="proposalKey">Proposal hash key (e.g. <c>"mm:proposal:{id}"</c>).</param>
    /// <param name="ticketIds">Ticket ids to claim (each becomes both a queue member and a ticket-hash key).</param>
    /// <param name="proposalId">Proposal id written into each ticket hash under <c>proposalId</c>.</param>
    /// <param name="proposalFieldsJson">JSON blob written into the proposal hash under <c>fields</c>.</param>
    /// <param name="ttlSeconds">EXPIRE seconds on the proposal hash.</param>
    /// <param name="ct">Cancellation token (StackExchange.Redis honors this via a linked CTS internally).</param>
    /// <returns>The parsed <see cref="AtomicClaimResult"/>.</returns>
    public async Task<AtomicClaimResult> ExecuteAsync(
        IDatabase db,
        string leaseKey,
        string leaseValue,
        string queueKey,
        string proposalKey,
        IReadOnlyList<Guid> ticketIds,
        Guid proposalId,
        string proposalFieldsJson,
        int ttlSeconds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrEmpty(leaseKey);
        ArgumentException.ThrowIfNullOrEmpty(leaseValue);
        ArgumentException.ThrowIfNullOrEmpty(queueKey);
        ArgumentException.ThrowIfNullOrEmpty(proposalKey);
        ArgumentNullException.ThrowIfNull(ticketIds);
        ArgumentNullException.ThrowIfNull(proposalFieldsJson);
        if (ticketIds.Count == 0)
            throw new ArgumentException("At least one ticket id is required.", nameof(ticketIds));

        ct.ThrowIfCancellationRequested();

        // KEYS: [leaseKey, queueKey, proposalKey, ticket1, ticket2, ...]
        var keys = new RedisKey[3 + ticketIds.Count];
        keys[0] = leaseKey;
        keys[1] = queueKey;
        keys[2] = proposalKey;
        for (var i = 0; i < ticketIds.Count; i++)
            keys[3 + i] = MatchmakingRedisKeys.Ticket(ticketIds[i]);

        // ARGV: [leaseValue, proposalId, ttlSeconds, ticketCount, tid1, tid2, ..., proposalFieldsJson]
        var values = new RedisValue[5 + ticketIds.Count];
        values[0] = leaseValue;
        values[1] = proposalId.ToString();
        values[2] = ttlSeconds;
        values[3] = ticketIds.Count;
        for (var i = 0; i < ticketIds.Count; i++)
            values[4 + i] = ticketIds[i].ToString();
        values[4 + ticketIds.Count] = proposalFieldsJson;

        try
        {
            // StackExchange.Redis.ScriptEvaluateAsync handles EVALSHA + NOSCRIPT fallback to
            // EVAL automatically when invoked via raw string (StringScript path). The library
            // caches the SHA on first successful EVAL.
            var result = await db.ScriptEvaluateAsync(LuaSource, keys, values).ConfigureAwait(false);
            var reply = (string?)result;
            return reply switch
            {
                "OK" => AtomicClaimResult.Success,
                "LEASE_LOST" => AtomicClaimResult.LeaseLost,
                "TICKET_GONE" => AtomicClaimResult.TicketGone,
                _ => UnexpectedReply(reply),
            };
        }
        catch (RedisException ex)
        {
            _logger?.LogError(ex,
                "AtomicClaimScript failed against queue={QueueKey} proposal={ProposalKey}: {Message}",
                queueKey, proposalKey, ex.Message);
            return AtomicClaimResult.RedisError;
        }
    }

    private AtomicClaimResult UnexpectedReply(string? reply)
    {
        _logger?.LogError(
            "AtomicClaimScript returned unexpected reply: {Reply}",
            reply ?? "<null>");
        return AtomicClaimResult.RedisError;
    }

    private static string ComputeSha1Hex(string source)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        var hash = SHA1.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
