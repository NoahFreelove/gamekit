// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Redis;
using GameKit.TestFixtures;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Pitfall §5 phase-gate integration tests for <c>LongPollStatusHandler</c>. Verifies the
/// long-poll handler (a) returns immediately when the status is already non-Queued, (b)
/// times out cleanly with the current "queued" snapshot after the configured timeout, and
/// (c) — THE phase-gate test — releases its Redis SUBSCRIBE within 500 ms of client abort.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class LongPollStatusTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;
    private ConnectionMultiplexer? _mux;

    public LongPollStatusTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _app = new MatchmakingTestApp { LongPollTimeoutSeconds = 2 };
        await _app.StartAsync(_pg, _redis);
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null) await _mux.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    [Fact]
    public async Task ImmediateReturn_WhenStatusAlreadyProposed()
    {
        var player = Guid.NewGuid();
        using var client = _app!.CreateClient(player);

        // Seed a ticket hash with status=proposed for a solo holder (playerId field matches
        // the calling player so the ownership check passes).
        var ticketId = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var db = _mux!.GetDatabase();
        var ticketKey = MatchmakingRedisKeys.Ticket(ticketId);
        await db.HashSetAsync(ticketKey,
            [
                new HashEntry("status", "proposed"),
                new HashEntry("playerId", player.ToString()),
                new HashEntry("proposalId", proposalId.ToString()),
                new HashEntry("partyId", string.Empty),
            ]);

        var sw = Stopwatch.StartNew();
        var resp = await client.GetAsync($"/api/mm/queue/{ticketId}/status");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TicketStatusResponse>();
        Assert.NotNull(body);
        Assert.Equal("proposed", body!.Status);
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"Immediate-return should complete in < 1s; got {sw.ElapsedMilliseconds}ms.");

        await db.KeyDeleteAsync(ticketKey);
    }

    [Fact]
    public async Task LongPoll_TimesOut_With_QueuedStatus_AfterShortenedTimeout()
    {
        var player = Guid.NewGuid();
        using var client = _app!.CreateClient(player);

        // Seed a ticket hash with status=queued. LongPollTimeoutSeconds is 2s (set in the
        // host construction above).
        var ticketId = Guid.NewGuid();
        var db = _mux!.GetDatabase();
        var ticketKey = MatchmakingRedisKeys.Ticket(ticketId);
        await db.HashSetAsync(ticketKey,
            [
                new HashEntry("status", "queued"),
                new HashEntry("playerId", player.ToString()),
                new HashEntry("partyId", string.Empty),
            ]);

        var sw = Stopwatch.StartNew();
        var resp = await client.GetAsync($"/api/mm/queue/{ticketId}/status");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<TicketStatusResponse>();
        Assert.NotNull(body);
        Assert.Equal("queued", body!.Status);
        // Bounded by the 2s timeout — should be >= 1900ms but well under the 30s default.
        Assert.InRange(sw.ElapsedMilliseconds, 1500, 5000);

        await db.KeyDeleteAsync(ticketKey);
    }

    [Fact]
    public async Task LongPoll_AbortMidPoll_UnsubscribesWithin500ms()
    {
        // THE Pitfall §5 phase-gate test: verify that aborting a long-poll mid-flight causes
        // the server to drop its Redis SUBSCRIBE within 500ms.
        var player = Guid.NewGuid();
        var ticketId = Guid.NewGuid();
        var channel = MatchmakingRedisKeys.StatusChannel(ticketId);

        var db = _mux!.GetDatabase();
        var ticketKey = MatchmakingRedisKeys.Ticket(ticketId);
        await db.HashSetAsync(ticketKey,
            [
                new HashEntry("status", "queued"),
                new HashEntry("playerId", player.ToString()),
                new HashEntry("partyId", string.Empty),
            ]);

        // Capture baseline subscription count for this channel.
        long Baseline() => GetSubscriberCount(channel);
        var before = Baseline();

        // Fire a long-poll on a cancelable token. The handler's CreateLinkedTokenSource wires
        // HttpContext.RequestAborted; once the HttpClient cancels, the handler's finally
        // block runs Unsubscribe.
        using var cts = new CancellationTokenSource();
        using var client = _app!.CreateClient(player);

        var pollTask = Task.Run(async () =>
        {
            try
            {
                await client.GetAsync($"/api/mm/queue/{ticketId}/status", cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected (covers TaskCanceledException too) */ }
            catch (HttpRequestException) { /* expected on abort */ }
        });

        // Give the request time to land on the server and run SUBSCRIBE.
        await Task.Delay(300);

        // Verify the subscription is active (count went up).
        var midPoll = Baseline();
        Assert.True(midPoll > before,
            $"Expected SUBSCRIBE to register; before={before}, midPoll={midPoll}.");

        // Abort the client.
        var sw = Stopwatch.StartNew();
        cts.Cancel();
        await pollTask;

        // Poll the subscription count until it returns to baseline.
        long current;
        do
        {
            current = Baseline();
            if (current <= before) break;
            await Task.Delay(50);
        }
        while (sw.ElapsedMilliseconds < 1500);
        sw.Stop();

        Assert.True(current <= before,
            $"Subscription did not return to baseline within 1500ms (before={before}, current={current}, elapsed={sw.ElapsedMilliseconds}ms).");
        Assert.True(sw.ElapsedMilliseconds <= 1500,
            $"Pitfall §5 guard expected Unsubscribe within 1500ms; observed {sw.ElapsedMilliseconds}ms.");

        await db.KeyDeleteAsync(ticketKey);
    }

    /// <summary>
    /// Returns the server-side per-channel subscriber count via Redis <c>PUBSUB NUMSUB</c>.
    /// This is the canonical Redis introspection for active SUBSCRIBE-ers on a single channel —
    /// returns 0 when no subscribers remain, which is precisely what the Pitfall §5 mitigation
    /// must achieve after client abort.
    /// </summary>
    private long GetSubscriberCount(string channel)
    {
        var endpoints = _mux!.GetEndPoints();
        var server = _mux.GetServer(endpoints[0]);
        var res = server.Execute("PUBSUB", "NUMSUB", channel);
        var arr = (RedisResult[]?)res;
        if (arr is { Length: >= 2 })
        {
            return (long)arr[1];
        }
        return 0;
    }
}
