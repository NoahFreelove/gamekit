// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

// xUnit analyzer rule xUnit1041 requires the [CollectionDefinition] attribute to live in the
// same assembly as the tests that consume it. The canonical AdminCollection definition in
// GameKit.TestFixtures/CollectionDefinitions.cs serves as the shared template; we re-declare
// it here so xUnit's in-assembly discovery picks up the fixture wiring. Matches the Phase 2
// pattern used by tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs.

/// <summary>
/// Local xUnit collection re-declaration bundling Postgres + Redis for Admin integration tests
/// in this assembly. No WireMock — admin surface has no outbound HTTP. The composite
/// <see cref="AdminIntegrationFixture"/> is intentionally NOT injected as an
/// <see cref="ICollectionFixture{TFixture}"/> (xUnit 2.9 cannot resolve its
/// <c>PostgresFixture+RedisFixture</c> constructor arguments at collection-fixture scope —
/// later plans construct it by hand inside their WebApplicationFactory bootstrap code, matching
/// the Phase-2 <c>AuthIntegrationFixture</c> usage pattern).
/// </summary>
[CollectionDefinition("Admin")]
public sealed class AdminCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }
