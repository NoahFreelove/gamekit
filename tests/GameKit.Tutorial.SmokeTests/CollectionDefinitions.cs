// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Tutorial.SmokeTests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in
// the same assembly as the tests that consume it. Mirrors the Matchmaking pattern in
// tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs.

/// <summary>
/// Local xUnit collection bundling Postgres + Redis for tutorial smoke tests.
/// Test classes opt in via <c>[Collection("TutorialSmoke")]</c>.
/// </summary>
[CollectionDefinition("TutorialSmoke")]
public sealed class TutorialSmokeCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture>
{
}
