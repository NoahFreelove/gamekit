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
