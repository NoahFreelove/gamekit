// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameKit.Core.Data;

/// <summary>
/// Runs EF Core migrations serialized by a Postgres advisory lock so concurrent startups
/// (multiple app replicas) do not attempt to apply the same migrations in parallel.
/// </summary>
/// <remarks>
/// Per design decision D-09, the advisory lock key is pinned in
/// <see cref="GameKitMigrationConstants.AdvisoryLockKey"/>. Operators can observe the lock in
/// <c>pg_locks</c> during migration:
/// <code>SELECT pid FROM pg_locks WHERE locktype='advisory' AND objsubid = 1;</code>
/// If a replica hangs holding the lock, terminate it with <c>pg_terminate_backend(pid)</c> to release.
/// </remarks>
public static class MigrationRunner
{
    /// <summary>
    /// Wraps <c>Database.MigrateAsync</c> in <c>pg_advisory_lock</c> /
    /// <c>pg_advisory_unlock</c> serialization. Safe to call concurrently from multiple replicas —
    /// only one replica at a time holds the lock; others wait until it completes.
    /// </summary>
    /// <param name="context">The <see cref="GameKitDbContext"/> to migrate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task MigrateWithLockAsync(
        GameKitDbContext context,
        CancellationToken cancellationToken = default)
    {
        var connection = context.Database.GetDbConnection();
        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            opened = true;
        }

        try
        {
            await using (var lockCmd = connection.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@k)";
                var param = new NpgsqlParameter("k", GameKitMigrationConstants.AdvisoryLockKey);
                lockCmd.Parameters.Add(param);
                await lockCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Best-effort unlock. We pass CancellationToken.None because the outer token may
                // have already fired (the reason we're in the finally) and we still want to try
                // releasing the lock. If the unlock itself throws (e.g. connection broken by
                // cancellation), the session-level advisory lock is released automatically when
                // the connection is closed/returned to the pool below — so swallowing here is safe.
                try
                {
                    await using var unlockCmd = connection.CreateCommand();
                    unlockCmd.CommandText = "SELECT pg_advisory_unlock(@k)";
                    var param = new NpgsqlParameter("k", GameKitMigrationConstants.AdvisoryLockKey);
                    unlockCmd.Parameters.Add(param);
                    await unlockCmd.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Intentionally swallowed: the session-level advisory lock is released when
                    // the connection is closed (see outer finally). Do not mask the original
                    // exception from MigrateAsync with an unlock failure.
                }
            }
        }
        finally
        {
            if (opened)
                await connection.CloseAsync().ConfigureAwait(false);
        }
    }
}
