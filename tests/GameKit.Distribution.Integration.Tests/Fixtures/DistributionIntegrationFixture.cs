// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Distribution.Integration.Tests.Fixtures;

/// <summary>
/// Per-test-class fixture that composes <see cref="PostgresFixture"/> +
/// <see cref="RedisFixture"/> for the Distribution suite (DIST-02 reader-INSERT
/// denial, DIST-03 sample smoke, OPS-04 version stamping, OPS-06 clean-install
/// migration). The fixture is a thin pass-through — PATTERNS warning #11
/// confirms <see cref="PostgresFixture"/> already bind-mounts
/// <c>docker/postgres/init/</c> (the 3-role bootstrap script) and exposes
/// <see cref="PostgresFixture.ReaderConnectionString"/> verbatim. No custom
/// Testcontainer construction lives here.
/// </summary>
/// <remarks>
/// <para>
/// Wave 0 scaffold (Plan 06-03 Task 3). Plan 06-08 + Plan 06-09 manually
/// instantiate this fixture in their test-class constructors (the xUnit
/// <c>ICollectionFixture&lt;T&gt;</c> with parameterless-ctor constraint does
/// NOT apply — composite fixtures are constructed by the test class from the
/// shared collection fixtures, mirroring the Phase 5 precedent).
/// </para>
/// <para>
/// DIST-02 specifically opens a SECOND Npgsql connection against
/// <see cref="ReaderConnectionString"/> and attempts an INSERT into
/// <c>gamekit.game_sessions</c>; the test asserts SQLSTATE 42501
/// ("permission denied for table"). This proves the 3-role default-privileges
/// grants in <c>docker/postgres/init/01-roles.sql</c> deny writes on
/// <c>gamekit_reader</c> as documented.
/// </para>
/// </remarks>
internal sealed class DistributionIntegrationFixture : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>The shared <see cref="PostgresFixture"/> exposing all three role connection strings.</summary>
    public PostgresFixture Postgres => _pg;

    /// <summary>The shared <see cref="RedisFixture"/> exposing the Redis connection string + multiplexer.</summary>
    public RedisFixture Redis => _redis;

    /// <summary>
    /// Verbatim re-exposure of <see cref="PostgresFixture.ReaderConnectionString"/> —
    /// the read-only role connection string consumed by the DIST-02 INSERT-denied
    /// test (Plan 06-08). PATTERNS warning #11 + Pitfall 8 mitigation: the 3-role
    /// bootstrap is ALREADY done by <see cref="PostgresFixture"/>; this fixture
    /// just passes the string through.
    /// </summary>
    public string ReaderConnectionString => _pg.ReaderConnectionString;

    /// <summary>Owner-role connection string (verbatim from <see cref="PostgresFixture"/>) for OPS-06 clean-install migration tests.</summary>
    public string OwnerConnectionString => _pg.OwnerConnectionString;

    /// <summary>Redis connection string (verbatim from <see cref="RedisFixture"/>).</summary>
    public string RedisConnectionString => _redis.ConnectionString;

    /// <summary>Constructs the fixture with the shared Postgres + Redis collection fixtures.</summary>
    public DistributionIntegrationFixture(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    /// <remarks>Wave 0: no-op pass-through over the shared fixtures.</remarks>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;
}
