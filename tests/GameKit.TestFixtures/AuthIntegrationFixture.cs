// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.TestFixtures;

/// <summary>
/// Pass-through composite that bundles the three Auth-tier fixtures (Postgres + Redis + WireMock)
/// so test classes can accept a single parameter instead of three. Used alongside the
/// <c>[Collection("Auth")]</c> attribute via an alternate constructor ergonomics path — the
/// xUnit runner injects the three fixtures individually; this type is constructed by hand in
/// WebApplicationFactory bootstrap code (plan 02-07) to centralize the three endpoints.
/// </summary>
public sealed class AuthIntegrationFixture
{
    /// <summary>The Postgres fixture providing the three-role connection strings.</summary>
    public PostgresFixture Postgres { get; }

    /// <summary>The Redis fixture (unused in Phase-2 auth logic but reserved for future partitioning).</summary>
    public RedisFixture Redis { get; }

    /// <summary>The WireMock fixture providing Steam + Discord stub URLs.</summary>
    public WireMockFixture WireMock { get; }

    /// <summary>Constructs the composite.</summary>
    public AuthIntegrationFixture(PostgresFixture postgres, RedisFixture redis, WireMockFixture wireMock)
    {
        Postgres = postgres;
        Redis = redis;
        WireMock = wireMock;
    }
}
