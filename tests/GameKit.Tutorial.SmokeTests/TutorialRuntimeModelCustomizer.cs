// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Tutorial.SmokeTests;

/// <summary>
/// Runtime <see cref="IModelCustomizer"/> for the tutorial smoke-test
/// <see cref="GameKit.Core.Data.GameKitDbContext"/>. Applies Auth + Admin + Rankings +
/// Matchmaking entity configurations directly so all hosted services
/// (<c>SuperadminGateHostedService</c>, <c>StartupLadderUpserter</c>, matchmaking ticker,
/// etc.) can query their respective entity sets at boot.
/// </summary>
/// <remarks>
/// Mirrors <c>OpenApiRuntimeModelCustomizer</c> in
/// <c>tests/GameKit.OpenApi.Integration.Tests</c>, which was introduced for the same reason:
/// the FOLLOW-UP-02-03-01 <c>ApplicationServiceProvider</c> issue captures the generic-host's
/// service provider under <c>Host.CreateDefaultBuilder + ConfigureWebHostDefaults</c>, missing
/// the web-host's <c>IModelBuilderExtension</c> registrations. The workaround is to apply
/// each package's entity configurations in a test-local <see cref="RelationalModelCustomizer"/>
/// subclass and register it via
/// <c>.ReplaceService&lt;IModelCustomizer, TutorialRuntimeModelCustomizer&gt;()</c>.
/// </remarks>
internal sealed class TutorialRuntimeModelCustomizer : RelationalModelCustomizer
{
    /// <summary>Constructs the customizer.</summary>
    public TutorialRuntimeModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        // Core entity configurations land via base.Customize → GameKitDbContext.OnModelCreating
        // → ApplyConfigurationsFromAssembly.
        base.Customize(modelBuilder, context);

        // Auth entity configurations.
        modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new GameKit.Auth.Data.Configurations.RefreshTokenConfiguration());

        // Admin entity configuration.
        modelBuilder.ApplyConfiguration(new GameKit.Admin.UI.Data.Configurations.AdminUserConfiguration());

        // Rankings + Matchmaking — both ship internal *ModelBuilderExtension types whose
        // ApplyTo(ModelBuilder) registers every entity in the package. Mirrors OpenApiRuntimeModelCustomizer.
        new GameKit.Rankings.Data.RankingsModelBuilderExtension().ApplyTo(modelBuilder);
        new GameKit.Matchmaking.Data.MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
