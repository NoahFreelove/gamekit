// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.TestFixtures;

/// <summary>
/// Composite fixture bundling a shared Postgres container + Redis container for
/// <c>GameKit.Admin.UI</c> integration tests. No WireMock — the admin surface has zero
/// outbound HTTP to any external OAuth provider (health probe talks to the in-process
/// Postgres + Redis clients only). Mirrors <see cref="AuthIntegrationFixture"/>.
/// </summary>
public sealed class AdminIntegrationFixture
{
    /// <summary>The Postgres fixture providing the three-role connection strings.</summary>
    public PostgresFixture Postgres { get; }

    /// <summary>The Redis fixture — consumed by the admin health panel probe (D-10).</summary>
    public RedisFixture Redis { get; }

    /// <summary>Constructs the composite; xUnit injects the two fixtures via the collection scope.</summary>
    /// <param name="postgres">Shared Postgres container fixture.</param>
    /// <param name="redis">Shared Redis container fixture.</param>
    public AdminIntegrationFixture(PostgresFixture postgres, RedisFixture redis)
    {
        Postgres = postgres;
        Redis = redis;
    }
}
