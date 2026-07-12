// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in
// the same assembly as the tests that consume it. Mirrors the Rankings pattern in
// tests/GameKit.Rankings.Integration.Tests/CollectionDefinitions.cs.

/// <summary>Local xUnit collection bundling Postgres + Redis for Matchmaking integration tests.</summary>
[CollectionDefinition("Matchmaking")]
public sealed class MatchmakingCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

/// <summary>Local xUnit collection for Postgres-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

/// <summary>Local xUnit collection for Redis-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }
