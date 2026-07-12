// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Lobby.Data;

/// <summary>
/// Migration-related constants for <c>GameKit.Lobby</c>. Pinned alongside all five
/// sibling-package constants so packages cannot collide on history-table name or
/// advisory-lock key.
/// </summary>
public static class LobbyMigrationConstants
{
    /// <summary>
    /// Per-package migrations history table for <c>GameKit.Lobby</c>.
    /// </summary>
    public const string MigrationsHistoryTable = "__ef_migrations_lobby";

    /// <summary>
    /// Postgres advisory-lock key for Lobby migration serialization.
    /// Live-verified value of <c>SELECT hashtext('gamekit.lobby.migrations')::bigint</c>
    /// on Postgres 17.9 (Wave 0 gate satisfied — Plan 11-01).
    /// Pairwise-distinct from Core (1800940027), Auth (-298890956), Admin (-2101739634),
    /// Rankings (-156812172), and Matchmaking (388956820) — asserted by
    /// <c>LobbyAdvisoryLockKeyTests.LobbyKey_Is_Distinct_From_Core_Auth_Admin_Rankings_Matchmaking_Keys</c>.
    /// </summary>
    public const long AdvisoryLockKey = 12178347L; // live-verified on Postgres 17.9 via Testcontainers
}
