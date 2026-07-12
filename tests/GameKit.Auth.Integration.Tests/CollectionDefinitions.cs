// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in
// the same assembly as the tests that consume it. The canonical AuthCollection definition
// in GameKit.TestFixtures/CollectionDefinitions.cs serves as the shared template, but we
// re-declare it here so xUnit's in-assembly discovery picks up the fixture wiring.
// Matches the Phase 1 pattern in tests/GameKit.Core.Integration.Tests/CollectionDefinitions.cs.

/// <summary>Local xUnit collection re-declaration bundling Postgres + Redis + WireMock for Auth integration tests.</summary>
[CollectionDefinition("Auth")]
public sealed class AuthCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture>,
      ICollectionFixture<WireMockFixture> { }

/// <summary>Local xUnit collection re-declaration for Postgres-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
