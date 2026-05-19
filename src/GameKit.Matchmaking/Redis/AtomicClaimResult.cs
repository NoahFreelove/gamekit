// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Redis;

/// <summary>
/// Result of <see cref="AtomicClaimScript.ExecuteAsync"/>: the Lua script's literal
/// string return value mapped to a strongly-typed enum.
/// </summary>
/// <remarks>
/// <para>
/// The Lua script returns one of three literal strings:
/// <list type="bullet">
///   <item><c>"OK"</c> → <see cref="Success"/></item>
///   <item><c>"LEASE_LOST"</c> → <see cref="LeaseLost"/> (fencing-token check failed; Pitfall §2)</item>
///   <item><c>"TICKET_GONE"</c> → <see cref="TicketGone"/> (another claim got there first)</item>
/// </list>
/// Any other failure (Redis connection error, malformed reply) maps to <see cref="RedisError"/>
/// — the caller catches <see cref="StackExchange.Redis.RedisException"/> and converts.
/// </para>
/// </remarks>
public enum AtomicClaimResult
{
    /// <summary>Atomic claim succeeded: both tickets removed from the queue, proposal hash written.</summary>
    Success = 0,

    /// <summary>
    /// Fencing token mismatch — the leader lock expired or was acquired by a different
    /// replica between this leader's last renewal and the EVAL call. The script wrote
    /// nothing (Pitfall §2). The caller MUST stop processing and let the new leader take over.
    /// </summary>
    LeaseLost = 1,

    /// <summary>
    /// One of the candidate tickets is no longer in the queue sorted-set — another
    /// claim got there first. The script wrote nothing; the surviving tickets remain
    /// in the queue.
    /// </summary>
    TicketGone = 2,

    /// <summary>
    /// Redis itself failed (connection drop, timeout, malformed reply). The caller
    /// MUST log + treat as a transient failure (Polly retry at the call-site level).
    /// </summary>
    RedisError = 3,
}
