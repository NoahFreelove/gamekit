// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using GameKit.Core.Data;
using Xunit;

namespace GameKit.Core.Tests.Data;

public class MigrationRunnerTests
{
    [Fact]
    public void MigrationRunner_IsStaticClass()
    {
        Assert.True(typeof(MigrationRunner).IsAbstract && typeof(MigrationRunner).IsSealed);
    }

    [Fact]
    public void MigrateWithLockAsync_MethodExists()
    {
        var method = typeof(MigrationRunner).GetMethod("MigrateWithLockAsync", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
    }

    [Fact]
    public void MigrateWithLockAsync_AcceptsGameKitDbContextParameter()
    {
        var method = typeof(MigrationRunner).GetMethod("MigrateWithLockAsync", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.True(parameters.Length >= 1);
        Assert.Equal(typeof(GameKitDbContext), parameters[0].ParameterType);
    }

    [Fact]
    public void MigrateWithLockAsync_ReturnsTask()
    {
        var method = typeof(MigrationRunner).GetMethod("MigrateWithLockAsync", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(typeof(System.Threading.Tasks.Task), method!.ReturnType);
    }

    [Fact]
    public void MigrationRunner_SourceContains_PgAdvisoryLock()
    {
        // Verify at the source level that both lock and unlock SQL are present.
        var sourceFile = System.IO.Path.Combine(
            System.IO.Directory.GetCurrentDirectory(),
            "..", "..", "..", "..", "..", "src", "GameKit.Core", "Data", "MigrationRunner.cs");
        var resolvedPath = System.IO.Path.GetFullPath(sourceFile);

        // This test guards against accidentally removing the advisory lock mechanism.
        if (System.IO.File.Exists(resolvedPath))
        {
            var content = System.IO.File.ReadAllText(resolvedPath);
            Assert.Contains("pg_advisory_lock", content);
            Assert.Contains("pg_advisory_unlock", content);
        }
        else
        {
            // File should exist — fail to flag the issue.
            Assert.Fail($"MigrationRunner.cs not found at expected path: {resolvedPath}");
        }
    }
}
