// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core.Data;

/// <summary>
/// Migration-related constants for <c>GameKit.Core</c>. Pin-sited here to make per-package
/// naming (history table, schema, advisory-lock key) a one-file change.
/// </summary>
public static class GameKitMigrationConstants
{
    /// <summary>
    /// Postgres schema that owns every GameKit table, including the per-package
    /// migrations history tables (<c>__ef_migrations_core</c>, <c>__ef_migrations_auth</c>, etc.).
    /// </summary>
    public const string SchemaName = "gamekit";

    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Core</c>. Per PITFALLS.md #3,
    /// each GameKit package uses its own history table (naming convention: <c>__ef_migrations_{package}</c>)
    /// so cross-package model snapshots do not collide.
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_core";

    /// <summary>
    /// Pinned Postgres advisory-lock key for migration serialization. Value is
    /// <c>SELECT hashtext('gamekit.migrations')::bigint</c> — deterministic across Postgres versions
    /// per Postgres documented behavior. Operators can observe this lock in <c>pg_locks</c>:
    /// <c>SELECT pid FROM pg_locks WHERE locktype = 'advisory' AND objid = &lt;low32(key)&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Per design decision D-09, <c>Database.Migrate()</c> is wrapped in
    /// <c>pg_advisory_lock(AdvisoryLockKey)</c> so concurrent startups serialize on this lock
    /// and only one replica applies migrations at a time.
    /// Plan 06 integration test re-verifies this value against a live Postgres 17.9 container.
    /// </remarks>
    public const long AdvisoryLockKey = 1800940027L;
}
