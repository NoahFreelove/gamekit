// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests.Fixtures;

/// <summary>
/// Per-test-class fixture that composes <see cref="PostgresFixture"/> +
/// <see cref="RedisFixture"/> and exposes the connection strings + a
/// <c>BuildServiceProvider</c> factory so leader-election tests (Plan 05-05)
/// can stand up two providers sharing the same Testcontainer pair.
/// </summary>
/// <remarks>
/// <para>
/// Wave 0 scaffold (Plan 05-01). The current implementation is a thin pass-through
/// over the two collection fixtures — Plan 05-02 wires the Matchmaking migration
/// application + per-suffix Redis multiplexer construction, and Plans 05-03 through
/// 05-08 layer on <see cref="IServiceProvider"/> builders for matchmaker/ticker/
/// proposal/reconciler harnesses.
/// </para>
/// <para>
/// Mirrors the construction shape of
/// <c>tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs:236-257</c>
/// (<c>BuildTickerServiceProvider</c>) so the downstream leader-election test
/// (Plan 05-05) can call <see cref="BuildServiceProvider"/> with two distinct
/// suffixes to simulate two replicas racing for the matchmaker lock.
/// </para>
/// </remarks>
internal sealed class MatchmakingIntegrationFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>Owner-role Postgres connection string from the shared <see cref="PostgresFixture"/>.</summary>
    public string ConnectionString => _pg.OwnerConnectionString;

    /// <summary>Redis connection string from the shared <see cref="RedisFixture"/>.</summary>
    public string RedisConnectionString => _redis.ConnectionString;

    /// <summary>Constructs the fixture with the shared Postgres + Redis collection fixtures.</summary>
    public MatchmakingIntegrationFixture(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Wave 0: no-op. Plan 05-02 adds Matchmaking-migration application here
    /// (analogous to <c>ApplyMigrationsAsync</c> in
    /// <c>RankingsTickerLeaderElectionTests</c>).
    /// </remarks>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> wired against the shared Postgres + Redis
    /// containers. The <paramref name="instanceSuffix"/> is propagated to the matchmaker
    /// lease helper's <c>InstanceId</c> so two providers can race for the lock in a
    /// single test (Plan 05-05 leader-election scenario).
    /// </summary>
    /// <param name="instanceSuffix">Distinguishes replicas in multi-replica tests (e.g. "1" / "2").</param>
    /// <returns>A configured <see cref="IServiceProvider"/>. Caller is responsible for disposal.</returns>
    /// <remarks>
    /// Wave 0 placeholder. Body is filled in by Plan 05-02 (migration wiring) and
    /// Plan 05-04/05-05 (matchmaker + lease helper DI registration).
    /// </remarks>
    public IServiceProvider BuildServiceProvider(string instanceSuffix)
    {
        throw new NotImplementedException(
            $"Wave 0 scaffold (Plan 05-01): BuildServiceProvider('{instanceSuffix}') is implemented by Plan 05-02+ " +
            "once GameKit.Matchmaking ships its EF model-builder extension and DI registrations. " +
            "See .planning/phases/05-matchmaking-parties/05-01-PLAN.md Task 2 for the contract.");
    }
}
