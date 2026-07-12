// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.IO;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Core.Tests;

/// <summary>
/// DR-01 / DR-02 / DR-07 — file-existence regression test for the three canonical operator
/// runbooks. Fails immediately (naming the missing or empty file) if any runbook is deleted
/// or accidentally emptied, providing a fast-feedback Wave-0 gate before the full integration
/// suite runs.
/// </summary>
/// <remarks>
/// Repo root is resolved via <see cref="GitRootLocator.FindRepoRoot()"/> (walks up from
/// <see cref="System.AppContext.BaseDirectory"/> looking for a <c>.git</c> entry), so the
/// test works regardless of where the test runner places its output directory.
/// </remarks>
public class RunbookFilesTests
{
    private const int MinimumByteCount = 200;

    private static string RepoRoot => GitRootLocator.FindRepoRoot();

    /// <summary>
    /// DR-01 — asserts that <c>docs/runbooks/postgres-backup-restore.md</c> exists and
    /// contains at least <see cref="MinimumByteCount"/> bytes of content.
    /// </summary>
    [Fact]
    public void PostgresBackupRestoreRunbook_Exists_AndIsNonTrivial()
    {
        var path = Path.Combine(RepoRoot, "docs", "runbooks", "postgres-backup-restore.md");

        Assert.True(
            File.Exists(path),
            $"Missing runbook: docs/runbooks/postgres-backup-restore.md (expected at {path}). " +
            "DR-01 requires this file to exist.");

        var size = new FileInfo(path).Length;
        Assert.True(
            size > MinimumByteCount,
            $"Runbook docs/runbooks/postgres-backup-restore.md is too small ({size} bytes). " +
            $"Expected > {MinimumByteCount} bytes of content. The file may have been emptied.");
    }

    /// <summary>
    /// DR-02 — asserts that <c>docs/runbooks/redis-backup-restore.md</c> exists and
    /// contains at least <see cref="MinimumByteCount"/> bytes of content.
    /// </summary>
    [Fact]
    public void RedisBackupRestoreRunbook_Exists_AndIsNonTrivial()
    {
        var path = Path.Combine(RepoRoot, "docs", "runbooks", "redis-backup-restore.md");

        Assert.True(
            File.Exists(path),
            $"Missing runbook: docs/runbooks/redis-backup-restore.md (expected at {path}). " +
            "DR-02 requires this file to exist.");

        var size = new FileInfo(path).Length;
        Assert.True(
            size > MinimumByteCount,
            $"Runbook docs/runbooks/redis-backup-restore.md is too small ({size} bytes). " +
            $"Expected > {MinimumByteCount} bytes of content. The file may have been emptied.");
    }

    /// <summary>
    /// DR-07 — asserts that <c>docs/migration-ops.md</c> exists and contains at least
    /// <see cref="MinimumByteCount"/> bytes of content.
    /// </summary>
    [Fact]
    public void MigrationOpsDoc_Exists_AndIsNonTrivial()
    {
        var path = Path.Combine(RepoRoot, "docs", "migration-ops.md");

        Assert.True(
            File.Exists(path),
            $"Missing doc: docs/migration-ops.md (expected at {path}). " +
            "DR-07 requires this file to exist.");

        var size = new FileInfo(path).Length;
        Assert.True(
            size > MinimumByteCount,
            $"Doc docs/migration-ops.md is too small ({size} bytes). " +
            $"Expected > {MinimumByteCount} bytes of content. The file may have been emptied.");
    }

    /// <summary>
    /// DOCS-05 — asserts that <c>docs/runbooks/rolling-deploy.md</c> exists and
    /// contains at least <see cref="MinimumByteCount"/> bytes of content.
    /// Prevents the zero-downtime rolling-deploy runbook from being deleted or emptied.
    /// </summary>
    [Fact]
    public void RollingDeployRunbook_Exists_AndIsNonTrivial()
    {
        var path = Path.Combine(RepoRoot, "docs", "runbooks", "rolling-deploy.md");

        Assert.True(
            File.Exists(path),
            $"Missing runbook: docs/runbooks/rolling-deploy.md (expected at {path}). " +
            "DOCS-05 requires this file to exist.");

        var size = new FileInfo(path).Length;
        Assert.True(
            size > MinimumByteCount,
            $"Runbook docs/runbooks/rolling-deploy.md is too small ({size} bytes). " +
            $"Expected > {MinimumByteCount} bytes of content. The file may have been emptied.");
    }

    /// <summary>
    /// DOCS-05 — asserts that <c>docs/runbooks/matchmaking-outage.md</c> exists and
    /// contains at least <see cref="MinimumByteCount"/> bytes of content.
    /// Prevents the matchmaking-outage incident-response runbook from being deleted or emptied.
    /// </summary>
    [Fact]
    public void MatchmakingOutageRunbook_Exists_AndIsNonTrivial()
    {
        var path = Path.Combine(RepoRoot, "docs", "runbooks", "matchmaking-outage.md");

        Assert.True(
            File.Exists(path),
            $"Missing runbook: docs/runbooks/matchmaking-outage.md (expected at {path}). " +
            "DOCS-05 requires this file to exist.");

        var size = new FileInfo(path).Length;
        Assert.True(
            size > MinimumByteCount,
            $"Runbook docs/runbooks/matchmaking-outage.md is too small ({size} bytes). " +
            $"Expected > {MinimumByteCount} bytes of content. The file may have been emptied.");
    }
}
