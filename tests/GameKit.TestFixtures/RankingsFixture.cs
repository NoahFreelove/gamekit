// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.TestFixtures;

/// <summary>
/// Pass-through composite that bundles the two Rankings-tier fixtures (Postgres + Redis)
/// so test classes can accept a single parameter instead of two. Rankings has no outbound
/// HTTP, so <c>WireMockFixture</c> is intentionally excluded (contrast with
/// <see cref="AuthIntegrationFixture" /> which includes WireMock for Steam/Discord stubs).
/// </summary>
public sealed class RankingsFixture
{
    /// <summary>The Postgres fixture providing the three-role connection strings.</summary>
    public PostgresFixture Postgres { get; }

    /// <summary>The Redis fixture used for ranked-matchmaking queue and ticker leader election.</summary>
    public RedisFixture Redis { get; }

    /// <summary>Constructs the composite.</summary>
    public RankingsFixture(PostgresFixture postgres, RedisFixture redis)
    {
        Postgres = postgres;
        Redis = redis;
    }
}
