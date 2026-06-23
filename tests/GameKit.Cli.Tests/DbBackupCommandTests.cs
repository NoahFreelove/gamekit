// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics;
using GameKit.Cli.Commands.Db;
using Xunit;

namespace GameKit.Cli.Tests;

/// <summary>
/// Unit tests for <see cref="DbBackupCommand"/> and <see cref="BackupPathValidator"/>.
/// These are pure unit tests — no real <c>pg_dump</c> binary is invoked and no Docker
/// container is started. The round-trip integration test lives in <c>GameKit.DR.Tests</c> (Plan 17-05).
///
/// Covers the threat mitigations required by the plan STRIDE register:
/// <list type="bullet">
///   <item>T-17-04-01 — path traversal rejected before any process starts.</item>
///   <item>T-17-04-02 — PGPASSWORD is in <see cref="ProcessStartInfo.Environment"/> and
///     is absent from <see cref="ProcessStartInfo.Arguments"/>.</item>
/// </list>
/// </summary>
public class DbBackupCommandTests
{
    // ─── BackupPathValidator ────────────────────────────────────────────────

    [Fact]
    public void IsSafeAbsolutePath_RelativePath_ReturnsFalse()
    {
        // A relative path must be rejected (T-17-04-01)
        Assert.False(BackupPathValidator.IsSafeAbsolutePath("./backup.pgdump"));
    }

    [Fact]
    public void IsSafeAbsolutePath_RelativePathNoDot_ReturnsFalse()
    {
        Assert.False(BackupPathValidator.IsSafeAbsolutePath("backup.pgdump"));
    }

    [Fact]
    public void IsSafeAbsolutePath_AbsolutePathWithDotDot_ReturnsFalse()
    {
        // A traversal segment in an absolute path must be rejected (T-17-04-01)
        Assert.False(BackupPathValidator.IsSafeAbsolutePath("/tmp/../etc/passwd"));
    }

    [Fact]
    public void IsSafeAbsolutePath_AbsolutePathWithDotDotAtEnd_ReturnsFalse()
    {
        Assert.False(BackupPathValidator.IsSafeAbsolutePath("/srv/backups/.."));
    }

    [Fact]
    public void IsSafeAbsolutePath_AbsolutePathWithDotDotInMiddle_ReturnsFalse()
    {
        Assert.False(BackupPathValidator.IsSafeAbsolutePath("/srv/../backups/game.pgdump"));
    }

    [Fact]
    public void IsSafeAbsolutePath_CleanAbsolutePath_ReturnsTrue()
    {
        // A clean absolute path must be accepted (T-17-04-01 — positive case)
        Assert.True(BackupPathValidator.IsSafeAbsolutePath("/srv/backups/game.pgdump"));
    }

    [Fact]
    public void IsSafeAbsolutePath_RootPath_ReturnsTrue()
    {
        Assert.True(BackupPathValidator.IsSafeAbsolutePath("/game.pgdump"));
    }

    [Fact]
    public void IsSafeAbsolutePath_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(BackupPathValidator.IsSafeAbsolutePath(null!));
        Assert.False(BackupPathValidator.IsSafeAbsolutePath(""));
        Assert.False(BackupPathValidator.IsSafeAbsolutePath("   "));
    }

    // ─── DbBackupCommand.BuildPgDumpStartInfo (internal seam) ──────────────

    [Fact]
    public void BuildPgDumpStartInfo_PGPASSWORD_IsInEnvironment_NotInArguments()
    {
        // T-17-04-02: PGPASSWORD must be in Environment, never in Arguments
        const string secret = "s3cr3t!";
        var psi = DbBackupCommand.BuildPgDumpStartInfo(
            host: "db.example.com",
            port: 5432,
            database: "gamekit",
            username: "gamekit_owner",
            password: secret,
            outputPath: "/srv/backups/game.pgdump");

        // PGPASSWORD must be in the process environment
        Assert.True(
            psi.Environment.ContainsKey("PGPASSWORD"),
            "PGPASSWORD must be set in ProcessStartInfo.Environment (T-17-04-02).");
        Assert.Equal(secret, psi.Environment["PGPASSWORD"]);

        // PGPASSWORD must NOT appear anywhere in the arguments string
        Assert.DoesNotContain(secret, psi.Arguments);
        Assert.DoesNotContain("PGPASSWORD", psi.Arguments,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPgDumpStartInfo_NoPassword_DoesNotAddPGPASSWORD()
    {
        var psi = DbBackupCommand.BuildPgDumpStartInfo(
            host: "localhost",
            port: 5432,
            database: "gamekit",
            username: "postgres",
            password: null,
            outputPath: "/srv/backups/game.pgdump");

        Assert.False(
            psi.Environment.ContainsKey("PGPASSWORD"),
            "PGPASSWORD must not be set in the environment when no password is supplied.");
    }

    [Fact]
    public void BuildPgDumpStartInfo_ArgumentsContainExpectedFlags()
    {
        var psi = DbBackupCommand.BuildPgDumpStartInfo(
            host: "pghost",
            port: 5433,
            database: "gamekit_db",
            username: "owner",
            password: "pw",
            outputPath: "/backups/out.pgdump");

        Assert.Equal("pg_dump", psi.FileName);
        Assert.Contains("--host=pghost",      psi.Arguments);
        Assert.Contains("--port=5433",        psi.Arguments);
        Assert.Contains("--username=owner",   psi.Arguments);
        Assert.Contains("--format=custom",    psi.Arguments);
        Assert.Contains("--file=/backups/out.pgdump", psi.Arguments);
        Assert.Contains("gamekit_db",         psi.Arguments);

        // Shell execute must be false so redirection works and PGPASSWORD doesn't leak
        Assert.False(psi.UseShellExecute);
    }

    // ─── DbRestoreCommand.BuildPgRestoreStartInfo (internal seam) ──────────

    [Fact]
    public void BuildPgRestoreStartInfo_PGPASSWORD_IsInEnvironment_NotInArguments()
    {
        // T-17-04-02 applies to restore as well
        const string secret = "r3st0r3pw!";
        var psi = DbRestoreCommand.BuildPgRestoreStartInfo(
            host: "db.example.com",
            port: 5432,
            database: "gamekit",
            username: "gamekit_owner",
            password: secret,
            filePath: "/srv/backups/game.pgdump");

        Assert.True(
            psi.Environment.ContainsKey("PGPASSWORD"),
            "PGPASSWORD must be set in ProcessStartInfo.Environment for pg_restore (T-17-04-02).");
        Assert.Equal(secret, psi.Environment["PGPASSWORD"]);

        Assert.DoesNotContain(secret, psi.Arguments);
        Assert.DoesNotContain("PGPASSWORD", psi.Arguments,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPgRestoreStartInfo_ArgumentsContainExpectedFlags()
    {
        var psi = DbRestoreCommand.BuildPgRestoreStartInfo(
            host: "pghost",
            port: 5433,
            database: "target_db",
            username: "owner",
            password: "pw",
            filePath: "/backups/restore.pgdump");

        Assert.Equal("pg_restore", psi.FileName);
        Assert.Contains("--host=pghost",            psi.Arguments);
        Assert.Contains("--port=5433",              psi.Arguments);
        Assert.Contains("--username=owner",         psi.Arguments);
        Assert.Contains("--dbname=target_db",       psi.Arguments);
        Assert.Contains("--no-owner",               psi.Arguments);
        Assert.Contains("--no-privileges",          psi.Arguments);
        Assert.Contains("/backups/restore.pgdump",  psi.Arguments);
        Assert.False(psi.UseShellExecute);
    }
}
