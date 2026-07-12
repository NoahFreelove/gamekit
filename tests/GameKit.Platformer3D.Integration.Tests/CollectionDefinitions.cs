// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires [CollectionDefinition] to live in the same
// assembly as the tests that consume it. Mirrors GameKit.Lobby.Integration.Tests pattern.

/// <summary>
/// xUnit collection definition for Platformer3D integration tests that require both
/// Postgres (Testcontainers) and Redis (Testcontainers).
/// </summary>
[CollectionDefinition("Platformer3D")]
public sealed class Platformer3DCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

/// <summary>
/// xUnit collection definition for Platformer3D integration tests that require only
/// Postgres (Testcontainers, no Redis).
/// </summary>
[CollectionDefinition("Platformer3DPostgres")]
public sealed class Platformer3DPostgresCollection
    : ICollectionFixture<PostgresFixture> { }
