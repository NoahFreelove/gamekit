// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// D-13 computed-property proof — <see cref="IsGuestResolver"/> returns true iff the player has
/// no identities AND no credentials. Three seed permutations, one assertion each.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class IsGuestResolverTests
{
    private readonly PostgresFixture _pg;

    public IsGuestResolverTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Returns_True_When_No_Identity_Or_Credential()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IIsGuestResolver>();

        Assert.True(await resolver.IsGuestAsync(playerId));
    }

    [Fact]
    public async Task Returns_False_When_Identity_Linked()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        await SeedIdentity(playerId, "steam", $"76561198{Random.Shared.NextInt64(100_000, 999_999):D9}");
        var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IIsGuestResolver>();

        Assert.False(await resolver.IsGuestAsync(playerId));
    }

    [Fact]
    public async Task Returns_False_When_Credential_Set()
    {
        await ApplyMigrations();
        var playerId = await SeedPlayer();
        await SeedCredential(playerId, $"alice_{Guid.NewGuid():N}".Substring(0, 24), "$2a$04$abcdefghijklmnopqrstuu.abcdefghijklmnopqrstuvwxyzabcdef");
        var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IIsGuestResolver>();

        Assert.False(await resolver.IsGuestAsync(playerId));
    }

    // ---------- Helpers ----------

    private async Task<Guid> SeedPlayer()
    {
        var sp = BuildProvider();
        var id = Guid.CreateVersion7();
        await using var scope = sp.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        ctx.Players.Add(new Player { Id = id, DisplayName = $"test-{id:N}".Substring(0, 24), CreatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
        return id;
    }

    private async Task SeedIdentity(Guid playerId, string provider, string ext)
    {
        var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        // Use the direct-DI-bypass context for write-side since DI routing is audited for FOLLOW-UP-02-03-01.
        await using var ctx = OpenQueryContext();
        ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
        {
            Id = Guid.CreateVersion7(),
            PlayerId = playerId,
            Provider = provider,
            ExternalId = ext,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private async Task SeedCredential(Guid playerId, string username, string hash)
    {
        await using var ctx = OpenQueryContext();
        ctx.Set<PlayerCredential>().Add(new PlayerCredential
        {
            PlayerId = playerId,
            Username = username,
            PasswordHash = hash,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());

        // Rewire the DbContext to use the Auth-runtime-query customizer so PlayerIdentity/PlayerCredential
        // appear in the model at query time (mirrors PlayerIdentityUniqueTests per 02-03 FOLLOW-UP-02-03-01).
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts.UseNpgsql(_pg.OwnerConnectionString)
                  .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>());

        services.AddScoped<IIsGuestResolver, IsGuestResolver>();
        return services.BuildServiceProvider();
    }

    private GameKitDbContext OpenQueryContext()
    {
        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(_pg.OwnerConnectionString)
            .ReplaceService<IModelCustomizer, AuthRuntimeQueryCustomizer>()
            .Options;
        return new GameKitDbContext(opts);
    }

    private async Task ApplyMigrations()
    {
        // Apply Core first (Auth FKs cascade to players.id).
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o => { o.ConnectionString = _pg.OwnerConnectionString; o.AutoMigrate = false; });
        coreServices.TryAddEnumerable(ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());
        await using var coreSp = coreServices.BuildServiceProvider();
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        // Apply Auth (separate context built ad-hoc to use AuthMigrationModelCustomizer).
        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(_pg.OwnerConnectionString, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(AuthMigrationConstants.MigrationsHistoryTable, GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .UseApplicationServiceProvider(coreSp)
            .Options;
        await using var authCtx = new GameKitDbContext(authOpts);
        await authCtx.Database.MigrateAsync();
    }

    /// <summary>
    /// Runtime customizer for Auth query-side tests — applies Core's OnModelCreating (via base) AND
    /// Auth's three configurations directly. Mirrors the 02-02 workaround documented in
    /// FOLLOW-UP-02-03-01 (DI-gap around IModelBuilderExtension resolution through EF's internal SP).
    /// </summary>
    internal sealed class AuthRuntimeQueryCustomizer : RelationalModelCustomizer
    {
        public AuthRuntimeQueryCustomizer(ModelCustomizerDependencies dependencies) : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            new AuthModelBuilderExtension().ApplyTo(modelBuilder);
        }
    }
}
