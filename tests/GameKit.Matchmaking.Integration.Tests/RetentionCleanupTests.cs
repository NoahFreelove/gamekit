// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// MATCH-15 / D-17 integration tests for <see cref="MatchmakingRetentionCleanupService"/>.
/// Verifies (a) old terminal tickets past the 30-day retention are deleted, (b) old
/// decline_history rows beyond WindowMinutes * 2 are deleted.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class RetentionCleanupTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    public RetentionCleanupTests(PostgresFixture pg, RedisFixture _) => _pg = pg;

    public async Task InitializeAsync()
    {
        _cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(_cs);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OldTerminalTickets_Deleted()
    {
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "retention-tickets");
        var now = DateTimeOffset.UtcNow;

        // Five old terminal tickets — beyond 30 days.
        for (var i = 0; i < 5; i++)
        {
            await IntegrationTestHelpers.SeedTicketAsync(_cs, ladderId,
                status: TicketStatus.Expired,
                queuedAt: now - TimeSpan.FromDays(60),
                terminalAt: now - TimeSpan.FromDays(35));
        }

        // Five recent terminal tickets — well within retention.
        for (var i = 0; i < 5; i++)
        {
            await IntegrationTestHelpers.SeedTicketAsync(_cs, ladderId,
                status: TicketStatus.Matched,
                queuedAt: now - TimeSpan.FromDays(2),
                terminalAt: now - TimeSpan.FromDays(2));
        }

        await using var sp = BuildRetentionServiceProvider(_cs, isLeader: true);
        var svc = sp.GetRequiredService<MatchmakingRetentionCleanupService>();
        var result = await svc.RunCleanupOnceAsync(CancellationToken.None);

        Assert.False(result.SkippedBecauseNotLeader);
        Assert.Equal(5, result.TicketsDeleted);

        await using var verifyCtx = IntegrationTestHelpers.BuildMatchmakingContext(_cs);
        var remaining = await verifyCtx.Set<MatchmakingTicket>().CountAsync();
        Assert.Equal(5, remaining);
    }

    [Fact]
    public async Task OldDeclineHistory_Deleted_BeyondWindowDoubled()
    {
        // Default cooldown WindowMinutes = 60 (CONTEXT D-08); retention boundary = 120 min.
        var now = DateTimeOffset.UtcNow;
        var playerId = await IntegrationTestHelpers.SeedPlayerAsync(_cs);

        // 3 old rows beyond 2x window — should be deleted.
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, now - TimeSpan.FromMinutes(125));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, now - TimeSpan.FromMinutes(180));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, now - TimeSpan.FromMinutes(500));

        // 2 recent rows inside the window — should be retained.
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, now - TimeSpan.FromMinutes(30));
        await IntegrationTestHelpers.SeedDeclineHistoryAsync(_cs, playerId, now - TimeSpan.FromMinutes(60));

        await using var sp = BuildRetentionServiceProvider(_cs, isLeader: true);
        var svc = sp.GetRequiredService<MatchmakingRetentionCleanupService>();
        var result = await svc.RunCleanupOnceAsync(CancellationToken.None);

        Assert.Equal(3, result.DeclineHistoriesDeleted);

        await using var verifyCtx = IntegrationTestHelpers.BuildMatchmakingContext(_cs);
        var remaining = await verifyCtx.Set<DeclineHistory>().Where(d => d.PlayerId == playerId).CountAsync();
        Assert.Equal(2, remaining);
    }

    private static ServiceProvider BuildRetentionServiceProvider(string cs, bool isLeader)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<GameKitMatchmakingOptions>();

        services.AddDbContext<GameKitDbContext>(opts =>
            opts.UseNpgsql(cs)
                .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IMatchmakerLease>(new StubMatchmakerLease(isLeader));
        services.AddSingleton<MatchmakingRetentionCleanupService>();

        return services.BuildServiceProvider();
    }
}
