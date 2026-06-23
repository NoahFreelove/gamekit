// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.IO;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests.Packaging;

/// <summary>
/// Regression guard for the two-role Postgres design in the Platformer3D compose stack.
/// Asserts that <c>samples/Platformer3D/docker/postgres/init/01-init.sql</c> grants
/// <c>gamekit_app</c> the runtime privileges it needs on the <c>gamekit</c> schema.
/// </summary>
/// <remarks>
/// Background: EF Core migrations run as <c>gamekit_owner</c> and create the
/// <c>gamekit</c> schema + tables. Without explicit grants, <c>gamekit_app</c>
/// (the runtime connection string) receives only <c>CONNECT ON DATABASE</c> and
/// cannot access the schema, causing a 42501 "permission denied for schema gamekit"
/// crash-loop on startup.
///
/// This is a pure file-content assertion — no Docker daemon required.
/// </remarks>
[Trait("Category", "Unit")]
[Trait("RequiresDocker", "false")]
public sealed class InitSqlGrantTests
{
    private static readonly string InitSqlPath = Path.Combine(
        GitRootLocator.FindRepoRoot(),
        "samples",
        "Platformer3D",
        "docker",
        "postgres",
        "init",
        "01-init.sql");

    [Fact(DisplayName = "InitSql: 01-init.sql exists at expected path")]
    public void InitSql_Exists()
    {
        Assert.True(File.Exists(InitSqlPath),
            $"01-init.sql not found at: {InitSqlPath}");
    }

    [Fact(DisplayName = "InitSql: grants USAGE ON SCHEMA gamekit to gamekit_app")]
    public void InitSql_Grants_Schema_Usage_To_AppRole()
    {
        var sql = File.ReadAllText(InitSqlPath);
        Assert.Contains(
            "GRANT USAGE ON SCHEMA gamekit TO gamekit_app",
            sql,
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "InitSql: sets ALTER DEFAULT PRIVILEGES for gamekit_owner granting to gamekit_app")]
    public void InitSql_Sets_Default_Privileges_For_OwnerRole_Granting_AppRole()
    {
        var sql = File.ReadAllText(InitSqlPath);

        // Must contain ALTER DEFAULT PRIVILEGES for the owner role...
        Assert.Contains(
            "ALTER DEFAULT PRIVILEGES FOR ROLE gamekit_owner",
            sql,
            System.StringComparison.OrdinalIgnoreCase);

        // ...and it must grant to the app role so future migration-created objects
        // are automatically accessible without manual re-grants.
        Assert.Contains(
            "GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO gamekit_app",
            sql,
            System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "InitSql: connects to gamekit DB before issuing schema grants")]
    public void InitSql_Connects_To_GameKit_Db_Before_Schema_Grants()
    {
        var sql = File.ReadAllText(InitSqlPath);

        // The \\connect meta-command must appear before the GRANT USAGE line so
        // the grants are issued inside the gamekit database, not the default postgres DB.
        var connectIdx = sql.IndexOf("\\connect gamekit", System.StringComparison.OrdinalIgnoreCase);
        var grantIdx = sql.IndexOf("GRANT USAGE ON SCHEMA gamekit TO gamekit_app", System.StringComparison.OrdinalIgnoreCase);

        Assert.True(connectIdx >= 0, "\\connect gamekit not found in 01-init.sql");
        Assert.True(grantIdx >= 0, "GRANT USAGE ON SCHEMA gamekit TO gamekit_app not found in 01-init.sql");
        Assert.True(connectIdx < grantIdx,
            $"\\connect gamekit (pos {connectIdx}) must appear before GRANT USAGE (pos {grantIdx})");
    }

    [Fact(DisplayName = "InitSql: retains SPDX license header")]
    public void InitSql_Has_Spdx_Header()
    {
        var sql = File.ReadAllText(InitSqlPath);
        Assert.Contains("SPDX-License-Identifier: GPL-3.0-or-later", sql, System.StringComparison.Ordinal);
    }
}
