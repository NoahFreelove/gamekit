// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.OpenApi.Integration.Tests;

/// <summary>
/// Runtime <see cref="IModelCustomizer"/> for the OpenApi-test
/// <see cref="DbContext"/>. Applies Core + Auth + Admin + Rankings +
/// Matchmaking entity configurations directly so the host hosted services
/// (<c>SuperadminGateHostedService</c>, <c>StartupLadderUpserter</c>, etc.)
/// can query their respective entity sets at boot.
/// </summary>
/// <remarks>
/// <para>
/// Bypasses the FOLLOW-UP-02-03-01 broken
/// <c>ApplicationServiceProvider</c> path (the EF model cache captures the
/// generic-host's service provider under <c>Host.CreateDefaultBuilder +
/// ConfigureWebHostDefaults</c>, missing the web-host's
/// <c>IModelBuilderExtension</c> registrations). Mirrors the
/// <c>AdminRuntimeQueryCustomizer</c> pattern from
/// <c>tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs:359-372</c>
/// but extended to Rankings + Matchmaking via their public
/// <c>*MigrationModelCustomizer</c> classes (which apply each package's
/// entity configurations on top of <see cref="RelationalModelCustomizer"/>).
/// </para>
/// <para>
/// Auth / Admin entity configurations are reached via the
/// <c>InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")</c> grants
/// added to each package's <c>AssemblyInfo.cs</c> in Plan 06-06.
/// </para>
/// </remarks>
internal sealed class OpenApiRuntimeModelCustomizer : RelationalModelCustomizer
{
    public OpenApiRuntimeModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        // Core entity configurations land via base.Customize → GameKitDbContext.OnModelCreating
        // → ApplyConfigurationsFromAssembly.
        base.Customize(modelBuilder, context);

        // Auth.
        modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.RefreshTokenConfiguration());

        // Admin.
        modelBuilder.ApplyConfiguration(new GameKit.Admin.UI.Data.Configurations.AdminUserConfiguration());

        // Rankings + Matchmaking — both ship internal *ModelBuilderExtension types whose
        // ApplyTo(ModelBuilder) method registers every entity in the package. Going through
        // the extension keeps us insulated from future entity additions.
        new GameKit.Rankings.Data.RankingsModelBuilderExtension().ApplyTo(modelBuilder);
        new GameKit.Matchmaking.Data.MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
