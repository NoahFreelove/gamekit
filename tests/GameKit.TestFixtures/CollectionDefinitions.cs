// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.TestFixtures;

/// <summary>xUnit collection for tests that need a shared Postgres container.</summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

/// <summary>xUnit collection for tests that need a shared Redis container.</summary>
[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }

/// <summary>xUnit collection for tests that need both Postgres and Redis containers.</summary>
[CollectionDefinition("PostgresAndRedis")]
public sealed class PostgresAndRedisCollection
    : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture> { }

/// <summary>
/// xUnit collection for Phase-2 Auth integration tests. Bundles Postgres + Redis + WireMock
/// into a single shared-fixture scope so container startup cost is paid once per run.
/// Test classes reference it via <c>[Collection("Auth")]</c>.
/// </summary>
[CollectionDefinition("Auth")]
public sealed class AuthCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture>,
      ICollectionFixture<WireMockFixture> { }

/// <summary>
/// xUnit collection for Phase-3 Admin UI integration tests. Bundles Postgres + Redis into a
/// shared-fixture scope; no WireMock since the admin surface has zero outbound HTTP. Test
/// classes opt in via <c>[Collection("Admin")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the Phase-2 <see cref="AuthCollection"/> shape. The composite
/// <see cref="AdminIntegrationFixture"/> type still exists and may be constructed by hand by
/// plans 03-04 / 03-07 / 03-13 inside their WebApplicationFactory bootstrap code (matches
/// <see cref="AuthIntegrationFixture"/> usage); xUnit 2.9 does NOT support fixture-into-fixture
/// constructor injection on <see cref="ICollectionFixture{TFixture}"/>, so registering the
/// composite directly here would fail with "unresolved constructor arguments" at test discovery.
/// </para>
/// </remarks>
[CollectionDefinition("Admin")]
public sealed class AdminCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }
