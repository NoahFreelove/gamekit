// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Services;

namespace GameKit.Matchmaking.Integration.Tests.TestDoubles;

/// <summary>
/// Test-only <see cref="IChaosInterceptor"/> that throws
/// <see cref="OperationCanceledException"/> at configured probe points to simulate a process
/// crash mid-match. Used by <c>MatchmakingChaosTests</c> (SC#2 phase gate) per
/// RESEARCH §Decision 14 (in-process abort over child-process simulation).
/// </summary>
/// <remarks>
/// <para>
/// Configure by setting <see cref="AbortOnNextLuaClaim"/> or <see cref="AbortOnNextSessionInsert"/>
/// to <see langword="true"/>. Each probe invocation that fires the abort resets the flag back to
/// <see langword="false"/> — so a single arming triggers exactly one abort. To abort multiple
/// times, re-arm between probe calls. <see cref="LuaClaimCallCount"/> and
/// <see cref="SessionInsertCallCount"/> are <see cref="Interlocked"/>-incremented on every call
/// (whether or not the abort fired) so tests can defensively assert the probe was actually
/// exercised — guards against a future refactor accidentally removing the probe site.
/// </para>
/// <para>
/// The class is thread-safe by virtue of <see cref="Interlocked"/> + boolean field semantics; it
/// is intended for single-test use (each <c>[Fact]</c> instantiates a fresh interceptor).
/// </para>
/// </remarks>
internal sealed class AbortingChaosInterceptor : IChaosInterceptor
{
    /// <summary>
    /// When <see langword="true"/>, the next call to <see cref="BeforeLuaClaim"/> throws and
    /// resets this flag to <see langword="false"/>.
    /// </summary>
    public volatile bool AbortOnNextLuaClaim;

    /// <summary>
    /// When <see langword="true"/>, the next call to <see cref="BeforeSessionInsert"/> throws and
    /// resets this flag to <see langword="false"/>.
    /// </summary>
    public volatile bool AbortOnNextSessionInsert;

    private long _luaClaimCallCount;
    private long _sessionInsertCallCount;

    /// <summary>Total number of <see cref="BeforeLuaClaim"/> invocations on this instance.</summary>
    public long LuaClaimCallCount => Interlocked.Read(ref _luaClaimCallCount);

    /// <summary>Total number of <see cref="BeforeSessionInsert"/> invocations on this instance.</summary>
    public long SessionInsertCallCount => Interlocked.Read(ref _sessionInsertCallCount);

    /// <inheritdoc />
    public Task BeforeLuaClaim(CancellationToken ct)
    {
        Interlocked.Increment(ref _luaClaimCallCount);
        if (AbortOnNextLuaClaim)
        {
            AbortOnNextLuaClaim = false;
            throw new OperationCanceledException("chaos abort: BeforeLuaClaim");
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task BeforeSessionInsert(CancellationToken ct)
    {
        Interlocked.Increment(ref _sessionInsertCallCount);
        if (AbortOnNextSessionInsert)
        {
            AbortOnNextSessionInsert = false;
            throw new OperationCanceledException("chaos abort: BeforeSessionInsert");
        }
        return Task.CompletedTask;
    }
}
