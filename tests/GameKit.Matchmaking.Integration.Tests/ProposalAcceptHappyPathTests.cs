// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
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
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// End-to-end happy-path test for the D-06 accept-step proposal flow. Wires Redis + Postgres
/// via Testcontainers, manually seeds a proposal hash (simulating the Wave-3 ticker's output;
/// the live ticker lives in Plan 05-05 which ships in the same wave), and drives both
/// players through <see cref="IProposalService.AcceptAsync"/>. Asserts:
/// <list type="bullet">
///   <item>The 1st accept returns <see cref="AcceptResult.Accepted"/>; the 2nd returns
///         <see cref="AcceptResult.AllAccepted"/>.</item>
///   <item>A <see cref="GameSession"/> row exists with <see cref="GameSessionState.Active"/>
///         and the proposal's LadderId.</item>
///   <item>Two <see cref="SessionParticipant"/> rows exist with teams 0 and 1.</item>
///   <item>The proposal hash and acceptors set are torn down (state=complete; subsequent
///         accept calls are idempotent).</item>
///   <item>A "matched" message is published to each ticket's status channel — verified via
///         a pre-subscribed handler.</item>
/// </list>
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class ProposalAcceptHappyPathTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;
    private ConnectionMultiplexer? _mux;
    private IDatabase _db = null!;

    public ProposalAcceptHappyPathTests(PostgresFixture pg, RedisFixture redis)
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
    public async Task TwoPlayer_BothAccept_CreatesSession_With_TwoTeams_AndPublishesMatched()
    {
        // Arrange — two players, two parties (single-member), two tickets in a proposal.
        var player1 = await IntegrationTestHelpers.SeedPlayerAsync(_cs);
        var player2 = await IntegrationTestHelpers.SeedPlayerAsync(_cs);
        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, "happy-path");
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();
        var proposalId = Guid.NewGuid();

        var queuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, "default");
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);

        // Seed the proposal hash (simulates the ticker's AtomicClaimScript output).
        var fields = new ProposalFields
        {
            LadderId = ladderId,
            QueueKey = queueKey,
            Deadline = DateTimeOffset.UtcNow.AddSeconds(10).ToString("O"),
            Tickets =
            {
                new ProposalTicket { TicketId = ticket1, QueuedAtUnixMs = queuedAtMs,     PlayerIds = { player1 } },
                new ProposalTicket { TicketId = ticket2, QueuedAtUnixMs = queuedAtMs + 1, PlayerIds = { player2 } },
            },
        };
        await _db.HashSetAsync(proposalKey, "fields", JsonSerializer.Serialize(fields));
        await _db.KeyExpireAsync(proposalKey, TimeSpan.FromSeconds(15));

        // Subscribe to both status channels BEFORE driving accepts so we don't miss the PUBLISH.
        var matched1 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var matched2 = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscriber = _mux!.GetSubscriber();
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticket1)),
            (_, value) => matched1.TrySetResult((string?)value ?? string.Empty));
        await subscriber.SubscribeAsync(
            RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticket2)),
            (_, value) => matched2.TrySetResult((string?)value ?? string.Empty));

        await using var sp = BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProposalService>();

        // Act — player 1 accepts → PENDING; player 2 accepts → ALL.
        var r1 = await svc.AcceptAsync(proposalId, ticket1, player1);
        var r2 = await svc.AcceptAsync(proposalId, ticket2, player2);

        // Assert — result codes.
        Assert.Equal(AcceptResult.Accepted, r1);
        Assert.Equal(AcceptResult.AllAccepted, r2);

        // Assert — game_session row created with Active state + correct ladder.
        await using var verifyScope = sp.CreateAsyncScope();
        var ctx = verifyScope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var sessions = await ctx.Set<GameSession>().Where(s => s.LadderId == ladderId).ToListAsync();
        Assert.Single(sessions);
        var session = sessions[0];
        Assert.Equal(GameSessionState.Active, session.State);

        // Assert — 2 session_participants with teams 0 and 1 (one each).
        var participants = await ctx.Set<SessionParticipant>()
            .Where(p => p.SessionId == session.Id)
            .ToListAsync();
        Assert.Equal(2, participants.Count);
        var teams = participants.Select(p => p.Team).OrderBy(t => t).ToList();
        Assert.Equal(new[] { 0, 1 }, teams);
        var participantPlayers = participants
            .Select(p => p.PlayerId!.Value)
            .OrderBy(g => g)
            .ToList();
        var expectedPlayers = new[] { player1, player2 }.OrderBy(g => g).ToList();
        Assert.Equal(expectedPlayers, participantPlayers);

        // Assert — proposal hash state was flipped to "complete" by the Lua script.
        var state = (string?)await _db.HashGetAsync(proposalKey, "state");
        Assert.Equal("complete", state);

        // Assert — both status channels received a "matched:<sessionId>" message.
        var winner1 = await Task.WhenAny(matched1.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        var winner2 = await Task.WhenAny(matched2.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Equal(matched1.Task, winner1);
        Assert.Equal(matched2.Task, winner2);
        var msg1 = await matched1.Task;
        var msg2 = await matched2.Task;
        Assert.Equal($"matched:{session.Id}", msg1);
        Assert.Equal($"matched:{session.Id}", msg2);

        // Assert — a third accept by either player is idempotent (AlreadyAccepted).
        await using var idemScope = sp.CreateAsyncScope();
        var svc2 = idemScope.ServiceProvider.GetRequiredService<IProposalService>();
        var r3 = await svc2.AcceptAsync(proposalId, ticket1, player1);
        Assert.Equal(AcceptResult.AlreadyAccepted, r3);
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

        // The ChannelWriter<TicketEvent> placeholder: same shape Plan 05-04 registers.
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
