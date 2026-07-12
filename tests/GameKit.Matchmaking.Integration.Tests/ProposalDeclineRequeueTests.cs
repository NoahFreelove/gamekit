// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Matchmaking;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Integration.Tests.Fixtures;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// End-to-end decline-and-requeue test for the D-09 flow. Drives a 2-player proposal in which
/// player A accepts and player B declines; asserts:
/// <list type="bullet">
///   <item>Player A's ticket is re-ZADDed into the original queue with the <em>original</em>
///         <c>queuedAt</c> Unix-ms score preserved verbatim (CONTEXT D-09).</item>
///   <item>Player B's ticket is <b>not</b> in the queue (declining party doesn't auto-rejoin).</item>
///   <item>A <c>decline_history</c> row exists for player B with the proposal id and the
///         clock's UTC timestamp.</item>
///   <item>The proposal hash + acceptors set are deleted.</item>
///   <item>"cancelled" is published to the declining ticket's status channel; "requeued" to
///         the accepting ticket's status channel.</item>
/// </list>
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class ProposalDeclineRequeueTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;
    private ConnectionMultiplexer? _mux;
    private IDatabase _db = null!;

    public ProposalDeclineRequeueTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(_cs);

        var redisOpts = ConfigurationOptions.Parse(_redis.ConnectionString);
        redisOpts.AllowAdmin = true;
        _mux = await ConnectionMultiplexer.ConnectAsync(redisOpts);
        _db = _mux.GetDatabase();
        await _mux.GetServer(_mux.GetEndPoints().First()).FlushDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null)
            await _mux.DisposeAsync();
    }

    [Fact]
    public async Task PlayerA_Accepts_PlayerB_Declines_Requeues_A_With_OriginalQueuedAt()
    {
        // Arrange
        var playerA = await IntegrationTestHelpers.SeedPlayerAsync(_cs);
        var playerB = await IntegrationTestHelpers.SeedPlayerAsync(_cs);
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "decline-requeue");
        var ticketA = Guid.NewGuid();
        var ticketB = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        // Player A's original queuedAt is 47 seconds in the past — preserving this through
        // the re-ZADD is the load-bearing assertion (D-09).
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, "default");
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);
        var acceptsKey = MatchmakingRedisKeys.ProposalAccepts(proposalId);
        var originalQueuedAtA = DateTimeOffset.UtcNow.AddSeconds(-47).ToUnixTimeMilliseconds();
        var originalQueuedAtB = DateTimeOffset.UtcNow.AddSeconds(-30).ToUnixTimeMilliseconds();

        // Seed the proposal hash + TTL.
        var fields = new ProposalFields
        {
            LadderId = ladderId,
            QueueKey = queueKey,
            Deadline = DateTimeOffset.UtcNow.AddSeconds(10).ToString("O"),
            Tickets =
            {
                new ProposalTicket { TicketId = ticketA, QueuedAtUnixMs = originalQueuedAtA, PlayerIds = { playerA } },
                new ProposalTicket { TicketId = ticketB, QueuedAtUnixMs = originalQueuedAtB, PlayerIds = { playerB } },
            },
        };
        await _db.HashSetAsync(proposalKey, "fields", JsonSerializer.Serialize(fields));
        await _db.KeyExpireAsync(proposalKey, TimeSpan.FromSeconds(15));

        // Subscribe to both status channels before acting.
        var statusA = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var statusB = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = _mux!.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticketA)),
            (_, value) => statusA.TrySetResult((string?)value ?? string.Empty));
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticketB)),
            (_, value) => statusB.TrySetResult((string?)value ?? string.Empty));

        await using var sp = BuildServiceProvider();

        // Act — player A accepts; player B declines.
        await using (var s1 = sp.CreateAsyncScope())
        {
            var svc = s1.ServiceProvider.GetRequiredService<IProposalService>();
            var rA = await svc.AcceptAsync(proposalId, ticketA, playerA);
            Assert.Equal(AcceptResult.Accepted, rA);
        }
        await using (var s2 = sp.CreateAsyncScope())
        {
            var svc = s2.ServiceProvider.GetRequiredService<IProposalService>();
            var rB = await svc.DeclineAsync(proposalId, ticketB, playerB);
            Assert.Equal(DeclineResult.Declined, rB);
        }

        // Assert — player A's ticket is in the queue with the ORIGINAL queuedAt score.
        var aScore = await _db.SortedSetScoreAsync(queueKey, ticketA.ToString());
        Assert.NotNull(aScore);
        Assert.Equal((double)originalQueuedAtA, aScore!.Value);

        // Assert — player B's ticket is NOT in the queue.
        var bScore = await _db.SortedSetScoreAsync(queueKey, ticketB.ToString());
        Assert.Null(bScore);

        // Assert — exactly 1 decline_history row for player B.
        await using (var verifyScope = sp.CreateAsyncScope())
        {
            var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var declines = await ctx.Set<DeclineHistory>()
                .Where(d => d.PlayerId == playerB)
                .ToListAsync();
            Assert.Single(declines);
            Assert.Equal(proposalId, declines[0].ProposalId);
        }

        // Assert — proposal hash + acceptors set are gone.
        Assert.False(await _db.KeyExistsAsync(proposalKey));
        Assert.False(await _db.KeyExistsAsync(acceptsKey));

        // Assert — status channels: A received "requeued", B received "cancelled".
        var winnerA = await Task.WhenAny(statusA.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        var winnerB = await Task.WhenAny(statusB.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(statusA.Task, winnerA);
        Assert.Equal(statusB.Task, winnerB);
        var msgA = await statusA.Task;
        var msgB = await statusB.Task;
        Assert.Equal("requeued", msgA);
        Assert.Equal("cancelled", msgB);
    }

    [Fact]
    public async Task Decline_With_NotInProposal_TicketId_Returns_NotInProposal()
    {
        // T-05-06-01 spoofing guard: a player decline with a ticketId not in the proposal
        // must return NotInProposal and NOT write decline_history.
        var playerA = await IntegrationTestHelpers.SeedPlayerAsync(_cs);
        var attacker = await IntegrationTestHelpers.SeedPlayerAsync(_cs);
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "spoof-decline");
        var legitTicket = Guid.NewGuid();
        var spoofedTicket = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, "default");

        var fields = new ProposalFields
        {
            LadderId = ladderId,
            QueueKey = queueKey,
            Deadline = DateTimeOffset.UtcNow.AddSeconds(10).ToString("O"),
            Tickets = { new ProposalTicket { TicketId = legitTicket, QueuedAtUnixMs = 0, PlayerIds = { playerA } } },
        };
        await _db.HashSetAsync(proposalKey, "fields", JsonSerializer.Serialize(fields));
        await _db.KeyExpireAsync(proposalKey, TimeSpan.FromSeconds(15));

        await using var sp = BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProposalService>();

        var result = await svc.DeclineAsync(proposalId, spoofedTicket, attacker);

        Assert.Equal(DeclineResult.NotInProposal, result);

        // No decline_history row was written.
        await using var verifyScope = sp.CreateAsyncScope();
        var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var count = await ctx.Set<DeclineHistory>().CountAsync(d => d.PlayerId == attacker);
        Assert.Equal(0, count);
    }

    private ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(_mux!);
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, UuidV7IdGenerator>();
        services.AddSingleton<TeamAssignmentService>();
        services.AddOptions<GameKitMatchmakingOptions>();
        services.AddDbContext<GameKitDbContext>(opts =>
            opts.UseNpgsql(_cs)
                .ReplaceService<IModelCustomizer, MatchmakingTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddScoped<IDeclineHistoryReader, EfDeclineHistoryReader>();
        services.AddSingleton<IChaosInterceptor, NullChaosInterceptor>();
        services.AddScoped<IProposalService, ProposalService>();

        var channel = Channel.CreateBounded<TicketEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropNewest,
            SingleReader = true,
            SingleWriter = false,
        });
        services.AddSingleton(channel);
        services.AddSingleton(channel.Writer);
        services.AddSingleton(channel.Reader);

        return services.BuildServiceProvider();
    }
}
