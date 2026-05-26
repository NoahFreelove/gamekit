// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in
// the same assembly as the tests that consume it. Mirrors the Matchmaking pattern in
// tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs.

/// <summary>Local xUnit collection bundling Postgres + Redis for Distribution integration tests.</summary>
/// <remarks>
/// PATTERNS warning #11: the existing <c>PostgresFixture</c> ALREADY bind-mounts
/// <c>docker/postgres/init/</c> (the 3-role bootstrap script) at
/// <c>tests/GameKit.TestFixtures/PostgresFixture.cs:36-53</c> and exposes
/// <c>ReaderConnectionString</c>. The DIST-02 INSERT-denied test (Plan 06-08)
/// consumes <c>ReaderConnectionString</c> verbatim — no custom Testcontainer here.
/// </remarks>
[CollectionDefinition("Distribution")]
public sealed class DistributionCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

/// <summary>Local xUnit collection for Postgres-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

/// <summary>Local xUnit collection for Redis-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }
