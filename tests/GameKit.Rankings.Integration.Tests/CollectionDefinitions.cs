// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in
// the same assembly as the tests that consume it. Mirrors the Auth pattern in
// tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs.

/// <summary>Local xUnit collection bundling Postgres + Redis for Rankings integration tests.</summary>
[CollectionDefinition("Rankings")]
public sealed class RankingsCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

/// <summary>Local xUnit collection for Postgres-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
