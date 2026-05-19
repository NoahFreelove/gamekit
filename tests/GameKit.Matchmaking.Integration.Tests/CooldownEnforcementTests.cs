// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking;
using GameKit.Matchmaking.Services;
using GameKit.Matchmaking.Integration.Tests.Fixtures;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// End-to-end cooldown escalation tests (CONTEXT D-08). Drives
/// <see cref="DeclineCooldownService"/> against a real Postgres
/// <c>decline_history</c> table via <see cref="EfDeclineHistoryReader"/>; pins the
/// retry-after arithmetic for the three step thresholds (3 / 15 / 30 min) and the
/// rolling-window roll-forward behaviour.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class CooldownEnforcementTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    public CooldownEnforcementTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(_cs);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Player_With_3_Declines_In_60min_Gets_30min_Cooldown()
    {
        var clock = new StepClock(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        var playerId = await IntegrationTestHelpers.SeedPlayerAsync(_cs);

        // Seed 3 declines within the rolling 60-min window; the most recent is at -1 min.
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-45));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-20));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-1));

        await using var sp = BuildServiceProvider(_cs, clock);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDeclineCooldownService>();

        var status = await svc.GetCurrentCooldownAsync(playerId, clock.UtcNow);

        Assert.True(status.IsLocked);
        Assert.NotNull(status.RetryAfter);
        // Step3 = 30 min; latest decline 1 min ago → ~29 min remain. Allow ±1 min for clock skew.
        Assert.InRange(status.RetryAfter!.Value.TotalMinutes, 28.0, 30.0);
    }

    [Fact]
    public async Task Cooldown_Expires_After_Step3_Duration()
    {
        var clock = new StepClock(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        var playerId = await IntegrationTestHelpers.SeedPlayerAsync(_cs);

        // Seed 3 declines; the most recent is 31 min before "now after advance". The Step3
        // cooldown is 30 min, so at "now", the cooldown has expired.
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-50));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-45));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-31));

        await using var sp = BuildServiceProvider(_cs, clock);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDeclineCooldownService>();

        var status = await svc.GetCurrentCooldownAsync(playerId, clock.UtcNow);

        Assert.False(status.IsLocked);
        Assert.Null(status.RetryAfter);
    }

    [Fact]
    public async Task Decline_Window_Rolls_Forward()
    {
        // 4 declines spaced over 80 minutes; only the 3 most-recent within the 60-min window
        // count. Wave them through StepClock to confirm the rolling window logic.
        var clock = new StepClock(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        var playerId = await IntegrationTestHelpers.SeedPlayerAsync(_cs);

        // -80 min: outside the 60-min window after StepClock advance.
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-80));
        // -55 / -35 / -15 min: all inside the 60-min window.
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-55));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-35));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, clock.UtcNow.AddMinutes(-15));

        await using var sp = BuildServiceProvider(_cs, clock);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDeclineCooldownService>();

        var status = await svc.GetCurrentCooldownAsync(playerId, clock.UtcNow);

        // 3 declines visible inside the window → Step3 = 30 min; latest is 15 min ago →
        // ~15 min remain. The -80 min row is ignored.
        Assert.True(status.IsLocked);
        Assert.NotNull(status.RetryAfter);
        Assert.InRange(status.RetryAfter!.Value.TotalMinutes, 14.0, 16.0);
    }

    private static ServiceProvider BuildServiceProvider(string cs, IClock clock)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();
        services.AddOptions<GameKitMatchmakingOptions>();
        services.AddDbContext<GameKitDbContext>(opts =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<IDeclineHistoryReader, EfDeclineHistoryReader>();
        services.AddScoped<IDeclineCooldownService, DeclineCooldownService>();
        return services.BuildServiceProvider();
    }
}
