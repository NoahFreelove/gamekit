// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Data;

/// <summary>
/// Migration-related constants for <c>GameKit.Auth</c>. Pinned alongside
/// <see cref="GameKit.Core.Data.GameKitMigrationConstants"/> so the two packages cannot collide
/// on history-table name or advisory-lock key.
/// </summary>
public static class AuthMigrationConstants
{
    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Auth</c> (separate from Core's
    /// <c>__ef_migrations_core</c> — required by the per-package migration pattern, PITFALLS #3).
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_auth";

    /// <summary>
    /// Postgres advisory-lock key for Auth migration serialization.
    /// Computed as <c>SELECT hashtext('gamekit.auth.migrations')::bigint</c> on live Postgres 17.9
    /// via Testcontainers and re-verified on every integration-test run by
    /// <c>AuthAdvisoryLockKeyTests.PinnedAdvisoryKey_Matches_Postgres_Hashtext_Computation</c>.
    /// <para>
    /// The value is negative because <c>hashtext</c> returns <c>int4</c>; the <c>::bigint</c> cast
    /// preserves the sign. Postgres advisory-lock keys accept any <c>bigint</c>, positive or negative.
    /// </para>
    /// <para>
    /// <b>MUST</b> differ from <see cref="GameKit.Core.Data.GameKitMigrationConstants.AdvisoryLockKey"/>
    /// (1800940027) so Core and Auth migrations do not deadlock each other at startup (PITFALLS §8.12 #9).
    /// <c>AuthAdvisoryLockKeyTests.AuthKey_Is_Distinct_From_Core_Key</c> asserts the non-equality.
    /// </para>
    /// </summary>
    public const long AdvisoryLockKey = -298890956L;
}
