// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Data;

/// <summary>
/// Migration-related constants for <c>GameKit.Admin.UI</c>. Pinned alongside
/// <see cref="GameKit.Core.Data.GameKitMigrationConstants"/> and
/// <see cref="GameKit.Auth.Data.AuthMigrationConstants"/> so the three packages cannot collide
/// on history-table name or advisory-lock key.
/// </summary>
public static class AdminMigrationConstants
{
    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Admin.UI</c> (separate from Core's
    /// <c>__ef_migrations_core</c> and Auth's <c>__ef_migrations_auth</c> — required by the
    /// per-package migration pattern, PITFALLS #3).
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_admin";

    /// <summary>
    /// Postgres advisory-lock key for Admin migration serialization.
    /// Computed as <c>SELECT hashtext('gamekit.admin.migrations')::bigint</c> on live Postgres 17.9
    /// via Testcontainers; re-verified on every integration-test run by
    /// <c>AdminAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation</c>.
    /// <para>
    /// <b>MUST</b> differ from <see cref="GameKit.Core.Data.GameKitMigrationConstants.AdvisoryLockKey"/>
    /// (1800940027) and <see cref="GameKit.Auth.Data.AuthMigrationConstants.AdvisoryLockKey"/>
    /// (-298890956) so Core, Auth, and Admin migrations do not deadlock each other at startup
    /// (PITFALLS §8.12 #9). <c>AdminAdvisoryLockKeyTests.AdminKey_Is_Distinct_From_Core_And_Auth_Keys</c>
    /// asserts the non-equality.
    /// </para>
    /// <para>
    /// The value is negative because <c>hashtext</c> returns <c>int4</c>; the <c>::bigint</c> cast
    /// preserves the sign. Postgres advisory-lock keys accept any <c>bigint</c>, positive or negative
    /// (mirrors the same property documented on <c>AuthMigrationConstants.AdvisoryLockKey</c>).
    /// </para>
    /// <para>
    /// The value was live-verified on Postgres 17.9 via Testcontainers on 2026-04-19 by
    /// <c>AdminAdvisoryLockKeyTests</c>.
    /// </para>
    /// </summary>
    public const long AdvisoryLockKey = -2101739634L;
}
