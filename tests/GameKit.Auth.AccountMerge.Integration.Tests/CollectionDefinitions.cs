// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Auth.AccountMerge.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in
// the same assembly as the tests that consume it. Mirrors the pattern established by
// GameKit.Auth.Integration.Tests/CollectionDefinitions.cs (Plan 02-06).

/// <summary>
/// Local xUnit collection re-declaration bundling Postgres + Redis for account-merge
/// integration tests. Tests that exercise the full merge service (FK surgery + Redis cleanup)
/// need both fixtures; Postgres-only tests can use the lighter-weight collection below.
/// </summary>
[CollectionDefinition("AccountMerge")]
public sealed class AccountMergeCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

/// <summary>Local xUnit collection re-declaration for Postgres-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
