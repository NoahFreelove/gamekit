// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Lobby.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in
// the same assembly as the tests that consume it. Mirrors the Matchmaking pattern in
// tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs.

/// <summary>Local xUnit collection bundling Postgres + Redis for Lobby integration tests.</summary>
[CollectionDefinition("Lobby")]
public sealed class LobbyCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

/// <summary>Local xUnit collection for Postgres-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

/// <summary>Local xUnit collection for Redis-only integration tests in this assembly (xUnit1041).</summary>
[CollectionDefinition("Redis")]
public sealed class RedisCollection : ICollectionFixture<RedisFixture> { }

/// <summary>
/// xUnit collection definition that serialises all
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>-based tests for
/// <c>GameKit.Lobby</c> so they never run concurrently.
/// </summary>
/// <remarks>
/// <c>LobbyMeter</c> instruments are static singletons. When multiple test classes subscribe
/// concurrent <see cref="System.Diagnostics.Metrics.MeterListener"/> instances to the same
/// instrument, measurement callbacks fire on the <c>Add</c> caller's thread — writing to
/// <see cref="System.Collections.Generic.List{T}"/> instances that belong to other tests.
/// Placing all meter-based tests in this collection ensures xUnit runs them sequentially
/// (no cross-test listener contamination). Mirrors the MatchmakingMeterCollection pattern
/// from Plan 15-02 fix commit 5737385.
/// </remarks>
[CollectionDefinition("LobbyMeterTests", DisableParallelization = true)]
public sealed class LobbyMeterCollection
{
    // Marker class — no fixture state. The [CollectionDefinition] attribute is the sole purpose.
}
