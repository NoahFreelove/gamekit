// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Services;

namespace GameKit.Matchmaking.Integration.Tests.TestDoubles;

/// <summary>
/// Test-only <see cref="IChaosInterceptor"/> that delays configured probe points by a fixed
/// duration to simulate lease expiry mid-tick. Used by the SCALE-04 split-brain test
/// (<c>MatchmakerSplitBrainTests</c>) to pause Replica A's ticker past the short lock TTL so
/// Replica B can acquire the leader lease and form the match.
/// </summary>
/// <remarks>
/// <para>
/// Configure the delay via <paramref name="delayMs"/> in the constructor. The delay is applied
/// to the seam(s) indicated by <paramref name="delayLuaClaim"/> and
/// <paramref name="delaySessionInsert"/>:
/// <list type="bullet">
///   <item>
///     <see cref="BeforeLuaClaim"/> — delays BEFORE the Lua atomic-claim script so the
///     leader lock TTL expires while this replica is "stuck", allowing another replica to
///     acquire the lock. After the delay, the Lua script runs and returns <c>LEASE_LOST</c>
///     — no duplicate session row is created on this replica.
///   </item>
///   <item>
///     <see cref="BeforeSessionInsert"/> — delays BEFORE the <c>game_sessions</c> INSERT
///     so two replicas racing the formation write observe the Postgres
///     <c>ON CONFLICT DO NOTHING</c> guard (SCALE-03).
///   </item>
/// </list>
/// By default only <see cref="BeforeLuaClaim"/> is delayed; pass <c>delayLuaClaim: false</c>
/// and <c>delaySessionInsert: true</c> to test the session-insert race instead.
/// </para>
/// <para>
/// The class is thread-safe. <see cref="LuaClaimCallCount"/> and
/// <see cref="SessionInsertCallCount"/> are <see cref="Interlocked"/>-incremented on every
/// call so tests can defensively assert the probe was exercised.
/// </para>
/// </remarks>
internal sealed class DelayingChaosInterceptor : IChaosInterceptor
{
    private readonly int _delayMs;
    private readonly bool _delayLuaClaim;
    private readonly bool _delaySessionInsert;
    private long _luaClaimCallCount;
    private long _sessionInsertCallCount;

    /// <summary>Total number of <see cref="BeforeLuaClaim"/> invocations on this instance.</summary>
    public long LuaClaimCallCount => Interlocked.Read(ref _luaClaimCallCount);

    /// <summary>Total number of <see cref="BeforeSessionInsert"/> invocations on this instance.</summary>
    public long SessionInsertCallCount => Interlocked.Read(ref _sessionInsertCallCount);

    /// <summary>
    /// Constructs the interceptor.
    /// </summary>
    /// <param name="delayMs">Delay duration in milliseconds applied to the targeted seam(s).</param>
    /// <param name="delayLuaClaim">
    /// When <see langword="true"/> (default), <see cref="BeforeLuaClaim"/> sleeps for
    /// <paramref name="delayMs"/> milliseconds before returning. Set to
    /// <see langword="false"/> to make <see cref="BeforeLuaClaim"/> a no-op.
    /// </param>
    /// <param name="delaySessionInsert">
    /// When <see langword="true"/>, <see cref="BeforeSessionInsert"/> sleeps for
    /// <paramref name="delayMs"/> milliseconds before returning. Default
    /// <see langword="false"/>. Set to <see langword="true"/> to test the Postgres
    /// <c>ON CONFLICT DO NOTHING</c> idempotency guard on the session-insert race.
    /// </param>
    public DelayingChaosInterceptor(
        int delayMs,
        bool delayLuaClaim = true,
        bool delaySessionInsert = false)
    {
        if (delayMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(delayMs), delayMs, "delayMs must be > 0.");
        _delayMs = delayMs;
        _delayLuaClaim = delayLuaClaim;
        _delaySessionInsert = delaySessionInsert;
    }

    /// <inheritdoc />
    /// <remarks>
    /// When <c>delayLuaClaim</c> was <see langword="true"/> in the constructor, sleeps for
    /// the configured delay duration before returning. If the delay exceeds the matchmaker
    /// lock TTL, the Redis lease expires and the Lua atomic-claim script returns
    /// <c>LEASE_LOST</c> — the split-brain scenario the SCALE-04 test exercises.
    /// </remarks>
    public async Task BeforeLuaClaim(CancellationToken ct)
    {
        Interlocked.Increment(ref _luaClaimCallCount);
        if (_delayLuaClaim)
            await Task.Delay(_delayMs, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// When <c>delaySessionInsert</c> was <see langword="true"/> in the constructor, sleeps
    /// for the configured delay duration before returning to exercise the Postgres
    /// <c>ON CONFLICT DO NOTHING</c> idempotency guard on concurrent session-formation writes.
    /// </remarks>
    public async Task BeforeSessionInsert(CancellationToken ct)
    {
        Interlocked.Increment(ref _sessionInsertCallCount);
        if (_delaySessionInsert)
            await Task.Delay(_delayMs, ct).ConfigureAwait(false);
    }
}
