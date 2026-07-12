// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Rankings.Data;

/// <summary>
/// Migration-related constants for <c>GameKit.Rankings</c>. Pinned alongside
/// <see cref="GameKit.Core.Data.GameKitMigrationConstants"/>,
/// <c>GameKit.Auth.Data.AuthMigrationConstants</c>, and
/// <c>GameKit.Admin.UI.Data.AdminMigrationConstants</c> so the four packages
/// cannot collide on history-table name or advisory-lock key.
/// </summary>
public static class RankingsMigrationConstants
{
    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Rankings</c> (separate from Core's
    /// <c>__ef_migrations_core</c>, Auth's <c>__ef_migrations_auth</c>, and Admin's
    /// <c>__ef_migrations_admin</c> — required by the per-package migration pattern, PITFALLS #3).
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_rankings";

    /// <summary>
    /// Postgres advisory-lock key for Rankings migration serialization.
    /// Computed as <c>SELECT hashtext('gamekit.rankings.migrations')::bigint</c> on live Postgres 17.9
    /// via Testcontainers and re-verified on every integration-test run by
    /// <c>RankingsAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation</c>.
    /// <para>
    /// The value may be negative because <c>hashtext</c> returns <c>int4</c>; the <c>::bigint</c> cast
    /// preserves the sign. Postgres advisory-lock keys accept any <c>bigint</c>, positive or negative.
    /// </para>
    /// <para>
    /// <b>MUST</b> differ from <see cref="GameKit.Core.Data.GameKitMigrationConstants.AdvisoryLockKey"/>
    /// (1800940027), <c>AuthMigrationConstants.AdvisoryLockKey</c> (-298890956),
    /// and <c>AdminMigrationConstants.AdvisoryLockKey</c> (-2101739634)
    /// so Core, Auth, Admin, and Rankings migrations do not deadlock each other at startup (PITFALLS §11).
    /// <c>RankingsAdvisoryLockKeyTests.RankingsKey_Is_Distinct_From_Core_Auth_Admin_Keys</c> asserts the
    /// non-equality.
    /// </para>
    /// </summary>
    /// Live-verified on Postgres 17.9 (gamekit-postgres docker container, 2026-05-16) by executing
    /// <c>SELECT hashtext('gamekit.rankings.migrations')::bigint</c>. Re-verified on every
    /// integration-test run by <c>RankingsAdvisoryLockKeyTests</c>.
    public const long AdvisoryLockKey = -156812172L;
}
