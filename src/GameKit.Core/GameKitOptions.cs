// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core;

/// <summary>Configuration options for <c>AddGameKit(...)</c>. All strings are required at configuration time.</summary>
public sealed class GameKitOptions
{
    /// <summary>Runtime Postgres connection string (DML). Uses the <c>gamekit_app</c> role in the shipped <c>docker-compose.yml</c>.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Optional migrations-only connection string (DDL). When unset, falls back to <see cref="ConnectionString"/>
    /// with a startup warning. Production deployments should set this to a <c>gamekit_owner</c>-roled connection
    /// and leave <see cref="ConnectionString"/> as the less-privileged <c>gamekit_app</c>.
    /// </summary>
    public string? MigrationsConnectionString { get; set; }

    /// <summary>Optional Redis connection string for sibling packages (matchmaking, presence). Phase 1 unused.</summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>When true (default), <c>UseGameKit()</c> applies pending migrations at startup. Set false to disable and apply via <c>gamekit migrate</c> CLI.</summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>Display name rendered in place of a deleted player's identity. Operator-configurable per design decision D-11.</summary>
    public string DeletedPlayerDisplayName { get; set; } = "Deleted Player";
}
