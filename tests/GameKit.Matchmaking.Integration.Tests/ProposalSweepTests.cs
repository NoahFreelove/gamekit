// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="ProposalSweeper"/> against a live Testcontainer Redis.
/// Verifies the Pitfall §10 partial-accept race: a 4-player proposal where 3 players accept
/// but 1 times out → the 3 accepting tickets are re-ZADDed with original <c>queuedAt</c>
/// preserved (CONTEXT D-09); the declining ticket receives a <c>"cancelled"</c> PUBLISH and
/// stays out of the queue; the proposal hash is deleted.
/// </summary>
[Collection("Redis")]
[Trait("Category", "Integration")]
public sealed class ProposalSweepTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer? _mux;
    private IDatabase _db = null!;
    private IServer _server = null!;

    /// <summary>Constructs the test class with the shared Redis fixture.</summary>
    public ProposalSweepTests(RedisFixture redis) => _redis = redis;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        var opts = ConfigurationOptions.Parse(_redis.ConnectionString);
        opts.AllowAdmin = true;
        _mux = await ConnectionMultiplexer.ConnectAsync(opts);
        _db = _mux.GetDatabase();
        _server = _mux.GetServer(_mux.GetEndPoints().First());
        await _server.FlushDatabaseAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_mux is not null)
            await _mux.DisposeAsync();
    }

    private (ProposalSweeper sweeper, ChannelReader<TicketEvent> reader) BuildSweeper(IClock clock)
    {
        var channel = Channel.CreateBounded<TicketEvent>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropNewest,
            SingleReader = true,
            SingleWriter = false,
        });
        var sweeper = new ProposalSweeper(
            _mux!,
            clock,
            channel.Writer,
            NullLogger<ProposalSweeper>.Instance);
        return (sweeper, channel.Reader);
    }

    [Fact]
    public async Task PartialAccept_Reaper_ReQueues_Accepting_Parties_With_Original_QueuedAt()
    {
        // Pitfall §10 / D-09: 4-player proposal where 3 accept and 1 times out — the 3
        // accepting tickets MUST be re-ZADDed with their ORIGINAL queuedAt score (i.e. they
        // do NOT lose queue position to the partial-decline).
        var clock = new FixedClock(DateTimeOffset.Parse("2026-05-17T12:00:00Z"));
        var (sweeper, reader) = BuildSweeper(clock);

        var ladderId = Guid.NewGuid();
        var poolName = "default";
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, poolName);

        var ticket1 = Guid.NewGuid(); // accepted
        var ticket2 = Guid.NewGuid(); // accepted
        var ticket3 = Guid.NewGuid(); // accepted
        var ticket4 = Guid.NewGuid(); // timed out

        // Original queuedAt scores (Unix ms). These MUST survive the sweep for the 3 accepting
        // tickets — the declining ticket's score is irrelevant (it's not re-added).
        var t1Score = 1_700_000_001L;
        var t2Score = 1_700_000_002L;
        var t3Score = 1_700_000_003L;
        var t4Score = 1_700_000_004L;

        // Seed each ticket hash with the metadata the sweeper reads on re-ZADD.
        await SeedTicketHashAsync(ticket1, ladderId, poolName, t1Score);
        await SeedTicketHashAsync(ticket2, ladderId, poolName, t2Score);
        await SeedTicketHashAsync(ticket3, ladderId, poolName, t3Score);
        await SeedTicketHashAsync(ticket4, ladderId, poolName, t4Score);

        // Proposal hash with deadlineMs already elapsed (10 seconds before the fixed clock).
        // The sweeper reads deadlineMs from the hash (NOT the Redis KEY TTL — see
        // ProposalSweeper.cs for the rationale) so we can set it deterministically without
        // racing wall-clock-based EXPIRE.
        var proposalId = Guid.NewGuid();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);
        var expiredDeadlineMs = clock.UtcNow.AddSeconds(-10).ToUnixTimeMilliseconds();
        await _db.HashSetAsync(proposalKey,
        [
            new HashEntry("fields", "{}"),
            new HashEntry("tickets", string.Join(",", new[] { ticket1, ticket2, ticket3, ticket4 })),
            new HashEntry("deadlineMs", expiredDeadlineMs),
        ]);
        // Mark 3 of 4 tickets as accepted.
        var acceptsKey = MatchmakingRedisKeys.ProposalAccepts(proposalId);
        await _db.SetAddAsync(acceptsKey, new RedisValue[]
        {
            "ticket:" + ticket1,
            "ticket:" + ticket2,
            "ticket:" + ticket3,
        });

        // Subscribe to mm:status:{ticket4} BEFORE the sweep so we can assert the cancelled
        // PUBLISH was delivered.
        var sub = _mux!.GetSubscriber();
        var cancelledReceived = new TaskCompletionSource<string>();
        var statusChannel = RedisChannel.Literal(MatchmakingRedisKeys.StatusChannel(ticket4));
        await sub.SubscribeAsync(statusChannel, (_, value) =>
        {
            cancelledReceived.TrySetResult(value.ToString());
        });

        var reaped = await sweeper.SweepAsync(CancellationToken.None);
        Assert.Equal(1, reaped);

        // 3 accepting tickets re-ZADDed; original queuedAt scores preserved (D-09 invariant).
        var queueLen = await _db.SortedSetLengthAsync(queueKey);
        Assert.Equal(3, queueLen);

        var t1Restored = await _db.SortedSetScoreAsync(queueKey, ticket1.ToString());
        var t2Restored = await _db.SortedSetScoreAsync(queueKey, ticket2.ToString());
        var t3Restored = await _db.SortedSetScoreAsync(queueKey, ticket3.ToString());
        Assert.Equal((double)t1Score, t1Restored);
        Assert.Equal((double)t2Score, t2Restored);
        Assert.Equal((double)t3Score, t3Restored);

        // Declining ticket is NOT re-ZADDed.
        var t4Restored = await _db.SortedSetScoreAsync(queueKey, ticket4.ToString());
        Assert.Null(t4Restored);

        // Proposal hash + accept-tracker both deleted.
        Assert.False(await _db.KeyExistsAsync(proposalKey));
        Assert.False(await _db.KeyExistsAsync(acceptsKey));

        // PUBLISH "cancelled" delivered to ticket4's status channel.
        var publishResult = await Task.WhenAny(
            cancelledReceived.Task,
            Task.Delay(TimeSpan.FromSeconds(3)));
        Assert.Same(cancelledReceived.Task, publishResult);
        var receivedValue = await cancelledReceived.Task;
        Assert.Equal("cancelled", receivedValue);

        // Accepting tickets' Status hash field flipped back to "Queued".
        var t1Status = await _db.HashGetAsync(MatchmakingRedisKeys.Ticket(ticket1), "status");
        Assert.Equal("Queued", t1Status.ToString());

        // Drain the analytics channel — expect 3 Queued events (re-queue) + 1 TimedOut event
        // for the declining ticket.
        var events = new List<TicketEvent>();
        while (reader.TryRead(out var evt))
            events.Add(evt);

        var requeuedCount = events.Count(e => e.EventType == TicketEventType.Queued);
        var timedOutCount = events.Count(e => e.EventType == TicketEventType.TimedOut);
        Assert.Equal(3, requeuedCount);
        Assert.Equal(1, timedOutCount);
    }

    [Fact]
    public async Task ProposalNotNearExpiry_NotSwept()
    {
        // A proposal with TTL well above the 1-second sweep threshold must NOT be reaped.
        var clock = new FixedClock(DateTimeOffset.Parse("2026-05-17T12:00:00Z"));
        var (sweeper, _) = BuildSweeper(clock);

        var ladderId = Guid.NewGuid();
        var poolName = "default";
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();

        await SeedTicketHashAsync(ticket1, ladderId, poolName, queuedAtMs: 1_700_000_001L);
        await SeedTicketHashAsync(ticket2, ladderId, poolName, queuedAtMs: 1_700_000_002L);

        var proposalId = Guid.NewGuid();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);
        // Deadline 30 seconds in the future — well past the 1-second sweep threshold.
        var futureDeadlineMs = clock.UtcNow.AddSeconds(30).ToUnixTimeMilliseconds();
        await _db.HashSetAsync(proposalKey,
        [
            new HashEntry("fields", "{}"),
            new HashEntry("tickets", string.Join(",", new[] { ticket1, ticket2 })),
            new HashEntry("deadlineMs", futureDeadlineMs),
        ]);

        var reaped = await sweeper.SweepAsync(CancellationToken.None);
        Assert.Equal(0, reaped);

        // Proposal hash is still intact.
        Assert.True(await _db.KeyExistsAsync(proposalKey));
    }

    [Fact]
    public void ProposalSweeper_Source_Uses_SCAN_Not_KEYS()
    {
        // Pitfall §11: the sweeper must never call raw Redis KEYS — only SCAN (via
        // IServer.Keys). We can't easily assert this against the Redis wire protocol, so we
        // grep the source file for raw-command KEYS usage. The IServer.Keys API is the only
        // SCAN-wrapping path used in production matchmaking code; raw "KEYS" wire calls would
        // show up as ExecuteAsync("KEYS", ...) or ConditionResult.KeyExists arguments.
        var sourcePath = ResolveSourceFile("ProposalSweeper.cs");
        var text = System.IO.File.ReadAllText(sourcePath);

        // Permit the literal "KEYS" inside a comment / XML doc (we DO discuss "KEYS vs SCAN"
        // educationally). The dangerous pattern is the raw command call.
        Assert.DoesNotContain("ExecuteAsync(\"KEYS", text);
        Assert.DoesNotContain("server.KeysAsync(", text); // legacy API
    }

    [Fact]
    public void ProposalSweeper_Source_Uses_IServer_Keys()
    {
        // Positive complement: the SCAN path goes through IServer.Keys. The exact call shape
        // is "server.Keys(pattern: ...)". Verify the production code uses it.
        var sourcePath = ResolveSourceFile("ProposalSweeper.cs");
        var text = System.IO.File.ReadAllText(sourcePath);
        Assert.Contains("server.Keys(pattern", text);
    }

    private async Task SeedTicketHashAsync(Guid ticketId, Guid ladderId, string poolName, long queuedAtMs)
    {
        await _db.HashSetAsync(
            MatchmakingRedisKeys.Ticket(ticketId),
            [
                new HashEntry("ladderId", ladderId.ToString()),
                new HashEntry("poolName", poolName),
                new HashEntry("queuedAt", queuedAtMs.ToString(CultureInfo.InvariantCulture)),
                new HashEntry("aggregateRating", "1500"),
                new HashEntry("status", "Proposed"),
            ]);
    }

    private static string ResolveSourceFile(string fileName)
    {
        // Test runner CWD is the test project's bin/Debug/.../publish folder; walk up to the
        // repo root, then dive into src/GameKit.Matchmaking/Services.
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var candidate = System.IO.Path.Combine(dir, "src", "GameKit.Matchmaking", "Services", fileName);
            if (System.IO.File.Exists(candidate))
                return candidate;
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        throw new System.IO.FileNotFoundException(fileName);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
