// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Data;

/// <summary>
/// Migration-related constants for <c>GameKit.Matchmaking</c>. Pinned alongside
/// <see cref="GameKit.Core.Data.GameKitMigrationConstants"/>,
/// <c>GameKit.Auth.Data.AuthMigrationConstants</c>,
/// <c>GameKit.Admin.UI.Data.AdminMigrationConstants</c>, and
/// <c>GameKit.Rankings.Data.RankingsMigrationConstants</c> so the five packages
/// cannot collide on history-table name or advisory-lock key.
/// </summary>
public static class MatchmakingMigrationConstants
{
    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Matchmaking</c> (separate from Core's
    /// <c>__ef_migrations_core</c>, Auth's <c>__ef_migrations_auth</c>, Admin's
    /// <c>__ef_migrations_admin</c>, and Rankings's <c>__ef_migrations_rankings</c> — required
    /// by the per-package migration pattern, PITFALLS #3).
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_matchmaking";

    /// <summary>
    /// Postgres advisory-lock key for Matchmaking migration serialization.
    /// Computed as <c>SELECT hashtext('gamekit.matchmaking.migrations')::bigint</c> on live Postgres 17.9
    /// via Testcontainers and re-verified on every integration-test run by
    /// <c>MatchmakingAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation</c>.
    /// <para>
    /// The value may be negative because <c>hashtext</c> returns <c>int4</c>; the <c>::bigint</c> cast
    /// preserves the sign. Postgres advisory-lock keys accept any <c>bigint</c>, positive or negative.
    /// </para>
    /// <para>
    /// <b>MUST</b> differ from <see cref="GameKit.Core.Data.GameKitMigrationConstants.AdvisoryLockKey"/>
    /// (1800940027), <c>AuthMigrationConstants.AdvisoryLockKey</c> (-298890956),
    /// <c>AdminMigrationConstants.AdvisoryLockKey</c> (-2101739634), and
    /// <c>RankingsMigrationConstants.AdvisoryLockKey</c> (-156812172) so the five packages'
    /// migrations do not deadlock each other at startup (PITFALLS §11).
    /// <c>MatchmakingAdvisoryLockKeyTests.MatchmakingKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Keys</c>
    /// asserts the pairwise non-equality.
    /// </para>
    /// </summary>
    /// Live-verified on Postgres 17.9 (Testcontainers, 2026-05-17) by executing
    /// <c>SELECT hashtext('gamekit.matchmaking.migrations')::bigint</c>. Re-verified on every
    /// integration-test run by <c>MatchmakingAdvisoryLockKeyTests</c>.
    public const long AdvisoryLockKey = 388956820L;
}
