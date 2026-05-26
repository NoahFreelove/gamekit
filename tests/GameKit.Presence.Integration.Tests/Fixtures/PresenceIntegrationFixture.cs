// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Presence.Integration.Tests.Fixtures;

/// <summary>
/// Per-test-class fixture that composes <see cref="PostgresFixture"/> +
/// <see cref="RedisFixture"/> and exposes the connection strings + a
/// <c>BuildServiceProvider</c> factory so the heartbeat / in-match integration
/// tests in Plan 06-04 + 06-05 can stand up an <see cref="IServiceProvider"/>
/// sharing the same Testcontainer pair.
/// </summary>
/// <remarks>
/// <para>
/// Wave 0 scaffold (Plan 06-03 Task 2). The current implementation is a thin
/// pass-through over the two collection fixtures — Plan 06-04 wires the
/// <c>RedisPresenceProvider</c> registration + the Presence option-binding
/// pipeline, and Plan 06-05 layers on the session-lifecycle observer for
/// in-match transition tests.
/// </para>
/// <para>
/// Mirrors the construction shape of
/// <c>tests/GameKit.Matchmaking.Integration.Tests/Fixtures/MatchmakingIntegrationFixture.cs</c>.
/// The xUnit <c>ICollectionFixture&lt;T&gt;</c> with parameterless-ctor constraint
/// does NOT apply here — the fixture composite is manually instantiated per the
/// Phase 5 precedent (test classes inject <see cref="PostgresFixture"/> +
/// <see cref="RedisFixture"/> and pass them to this fixture's constructor).
/// </para>
/// </remarks>
internal sealed class PresenceIntegrationFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>Owner-role Postgres connection string from the shared <see cref="PostgresFixture"/>.</summary>
    public string ConnectionString => _pg.OwnerConnectionString;

    /// <summary>Redis connection string from the shared <see cref="RedisFixture"/>.</summary>
    public string RedisConnectionString => _redis.ConnectionString;

    /// <summary>Constructs the fixture with the shared Postgres + Redis collection fixtures.</summary>
    public PresenceIntegrationFixture(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Wave 0: no-op. Plan 06-04 adds Presence DI registration / multiplexer
    /// construction here (analogous to <c>BuildServiceProvider</c> in
    /// <c>MatchmakingIntegrationFixture</c>).
    /// </remarks>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Builds an <see cref="IServiceProvider"/> wired against the shared Postgres + Redis
    /// containers. The <paramref name="instanceSuffix"/> is propagated to per-test Redis
    /// key prefixes so two providers can share one Redis container without colliding on
    /// the <c>presence:{playerId}</c> key space (Plan 06-04 / 06-05 isolation pattern).
    /// </summary>
    /// <param name="instanceSuffix">Distinguishes parallel tests in multi-test scenarios.</param>
    /// <returns>A configured <see cref="IServiceProvider"/>. Caller is responsible for disposal.</returns>
    /// <remarks>
    /// Wave 0 placeholder. Body is filled in by Plan 06-04 once
    /// <c>GameKit.Presence.AddPresence(…)</c> ships its DI extension method.
    /// </remarks>
    public IServiceProvider BuildServiceProvider(string instanceSuffix)
    {
        throw new NotImplementedException(
            $"Wave 0 scaffold (Plan 06-03): BuildServiceProvider('{instanceSuffix}') is implemented by Plan 06-04 " +
            "once GameKit.Presence ships AddPresence() + RedisPresenceProvider. " +
            "See .planning/phases/06-presence-openapi-distribution/06-04-PLAN.md for the contract.");
    }
}
