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
    public void BuildPgDumpStartInfo_PGPASSWORD_IsInEnvironment_NotInArgumentList()
    {
        // T-17-04-02: PGPASSWORD must be in Environment, never in ArgumentList (WR-01)
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

        // PGPASSWORD must NOT appear in any argument list entry
        foreach (var arg in psi.ArgumentList)
        {
            Assert.DoesNotContain(secret, arg);
            Assert.DoesNotContain("PGPASSWORD", arg, StringComparison.OrdinalIgnoreCase);
        }
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
    public void BuildPgDumpStartInfo_ArgumentListContainsExpectedFlags()
    {
        // WR-01: uses ArgumentList so paths with spaces survive verbatim
        var psi = DbBackupCommand.BuildPgDumpStartInfo(
            host: "pghost",
            port: 5433,
            database: "gamekit_db",
            username: "owner",
            password: "pw",
            outputPath: "/backups/out.pgdump");

        Assert.Equal("pg_dump", psi.FileName);
        Assert.Contains("--host=pghost",              psi.ArgumentList);
        Assert.Contains("--port=5433",                psi.ArgumentList);
        Assert.Contains("--username=owner",           psi.ArgumentList);
        Assert.Contains("--format=custom",            psi.ArgumentList);
        Assert.Contains("--file=/backups/out.pgdump", psi.ArgumentList);
        Assert.Contains("gamekit_db",                 psi.ArgumentList);

        // Shell execute must be false so redirection works and PGPASSWORD doesn't leak
        Assert.False(psi.UseShellExecute);

        // Arguments string must remain empty — only ArgumentList is populated (WR-01)
        Assert.True(string.IsNullOrEmpty(psi.Arguments),
            "Arguments string must be empty when ArgumentList is used.");
    }

    [Fact]
    public void BuildPgDumpStartInfo_PathWithSpaces_SurvivesVerbatimInArgumentList()
    {
        // WR-01 regression: a path containing spaces must appear as a single entry, not split
        const string spaceyPath = "/srv/my backups/game db.pgdump";
        var psi = DbBackupCommand.BuildPgDumpStartInfo(
            host: "localhost",
            port: 5432,
            database: "gamekit",
            username: "postgres",
            password: null,
            outputPath: spaceyPath);

        Assert.Contains($"--file={spaceyPath}", psi.ArgumentList);
    }

    // ─── DbRestoreCommand.BuildPgRestoreStartInfo (internal seam) ──────────

    [Fact]
    public void BuildPgRestoreStartInfo_PGPASSWORD_IsInEnvironment_NotInArgumentList()
    {
        // T-17-04-02 applies to restore as well (WR-01)
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

        foreach (var arg in psi.ArgumentList)
        {
            Assert.DoesNotContain(secret, arg);
            Assert.DoesNotContain("PGPASSWORD", arg, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void BuildPgRestoreStartInfo_ArgumentListContainsExpectedFlags()
    {
        // WR-01: uses ArgumentList so paths with spaces survive verbatim
        var psi = DbRestoreCommand.BuildPgRestoreStartInfo(
            host: "pghost",
            port: 5433,
            database: "target_db",
            username: "owner",
            password: "pw",
            filePath: "/backups/restore.pgdump");

        Assert.Equal("pg_restore", psi.FileName);
        Assert.Contains("--host=pghost",           psi.ArgumentList);
        Assert.Contains("--port=5433",             psi.ArgumentList);
        Assert.Contains("--username=owner",        psi.ArgumentList);
        Assert.Contains("--dbname=target_db",      psi.ArgumentList);
        Assert.Contains("--no-owner",              psi.ArgumentList);
        Assert.Contains("--no-privileges",         psi.ArgumentList);
        Assert.Contains("/backups/restore.pgdump", psi.ArgumentList);
        Assert.False(psi.UseShellExecute);

        // Arguments string must remain empty — only ArgumentList is populated (WR-01)
        Assert.True(string.IsNullOrEmpty(psi.Arguments),
            "Arguments string must be empty when ArgumentList is used.");
    }

    [Fact]
    public void BuildPgRestoreStartInfo_PathWithSpaces_SurvivesVerbatimInArgumentList()
    {
        // WR-01 regression: a file path containing spaces must appear as a single argument entry
        const string spaceyPath = "/srv/my backups/restore file.pgdump";
        var psi = DbRestoreCommand.BuildPgRestoreStartInfo(
            host: "localhost",
            port: 5432,
            database: "target_db",
            username: "postgres",
            password: null,
            filePath: spaceyPath);

        Assert.Contains(spaceyPath, psi.ArgumentList);
    }
}
