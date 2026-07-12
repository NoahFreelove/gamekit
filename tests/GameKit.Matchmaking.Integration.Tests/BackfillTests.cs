// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Redis;
using GameKit.TestFixtures;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for backfill ticket creation and priority ordering (MATCH-19 SC#3).
/// Verifies that <c>POST /api/matchmaking/backfill</c> creates a <see cref="MatchmakingTicketType.Backfill"/>
/// typed ticket in Postgres and inserts it into the Redis sorted set at score 0 — so it
/// sorts before all normal tickets (which use Unix millisecond timestamps as scores).
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class BackfillTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;
    private ConnectionMultiplexer? _mux;

    /// <summary>Constructs the test with injected fixtures.</summary>
    public BackfillTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _app = new MatchmakingTestApp();
        await _app.StartAsync(_pg, _redis);
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_mux is not null) await _mux.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    /// <summary>
    /// SC#3: POST /api/matchmaking/backfill creates a ticket row with TicketType = 1 (Backfill) in Postgres.
    /// </summary>
    [Fact]
    public async Task SC3_Backfill_CreatesBackfillTypedTicket()
    {
        // Arrange — seed an Active game session (LadderId=null is fine; session just needs Active state).
        var sessionId = await IntegrationTestHelpers.SeedActiveGameSessionAsync(
            _app!.ConnectionString, DateTimeOffset.UtcNow);

        var playerId = Guid.NewGuid();
        using var client = _app.CreateClient(playerId);

        // Act — POST /api/matchmaking/backfill (SC#3 exact route literal).
        var resp = await client.PostAsJsonAsync("/api/matchmaking/backfill",
            new BackfillRequest(_app.TestLadderId, sessionId));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ticketId = body.GetProperty("ticketId").GetGuid();
        Assert.Equal("queued", body.GetProperty("status").GetString());

        // Assert — Postgres ticket row has TicketType = 1 (Backfill).
        using var ctx = IntegrationTestHelpers.BuildMatchmakingContext(_app.ConnectionString);
        var ticket = await ctx.Set<MatchmakingTicket>()
            .FindAsync(ticketId);
        Assert.NotNull(ticket);
        Assert.Equal(MatchmakingTicketType.Backfill, ticket!.TicketType);
        Assert.Equal((int)MatchmakingTicketType.Backfill, (int)ticket.TicketType); // explicit == 1
    }

    /// <summary>
    /// SC#3: A Backfill ticket (score = 0) is processed before a Normal ticket (score = Unix ms)
    /// by the Redis sorted-set ZRANGEBYSCORE Ascending ordering.
    /// </summary>
    [Fact]
    public async Task SC3_Priority_BackfillTicket_ProcessedBeforeNormalTicket()
    {
        // Arrange — seed an Active session for backfill.
        var sessionId = await IntegrationTestHelpers.SeedActiveGameSessionAsync(
            _app!.ConnectionString, DateTimeOffset.UtcNow);

        var normalPlayer = Guid.NewGuid();
        var backfillPlayer = Guid.NewGuid();

        using var normalClient = _app.CreateClient(normalPlayer);
        using var backfillClient = _app.CreateClient(backfillPlayer);

        // Step 1 — Enqueue a Normal ticket via POST /api/mm/queue (score ≈ Unix ms now).
        var normalResp = await normalClient.PostAsJsonAsync("/api/mm/queue",
            new EnqueueRequest(_app.TestLadderId, _app.TestLadderName));
        Assert.Equal(HttpStatusCode.OK, normalResp.StatusCode);

        var normalBody = await normalResp.Content.ReadFromJsonAsync<JsonElement>();
        var normalTicketId = normalBody.GetProperty("ticketId").GetGuid();

        // Step 2 — POST /api/matchmaking/backfill (score = 0).
        var backfillResp = await backfillClient.PostAsJsonAsync("/api/matchmaking/backfill",
            new BackfillRequest(_app.TestLadderId, sessionId));
        Assert.Equal(HttpStatusCode.OK, backfillResp.StatusCode);

        var backfillBody = await backfillResp.Content.ReadFromJsonAsync<JsonElement>();
        var backfillTicketId = backfillBody.GetProperty("ticketId").GetGuid();

        // Step 3 — Assert Redis sorted set ordering: ZRANGEBYSCORE Ascending returns backfill first.
        var db = _mux!.GetDatabase();
        var queueKey = MatchmakingRedisKeys.Queue(_app.TestLadderId, "default");

        var members = await db.SortedSetRangeByScoreWithScoresAsync(
            queueKey,
            double.NegativeInfinity, double.PositiveInfinity,
            Exclude.None, Order.Ascending, 0, 2);

        Assert.True(members.Length >= 2, $"Expected >= 2 members in queue; got {members.Length}.");

        // Backfill ticket (score 0) must be at index 0 — sorted before Normal ticket (score ~1.75e12).
        Assert.Equal(backfillTicketId.ToString(), (string?)members[0].Element);
        Assert.Equal(0d, members[0].Score);

        // Normal ticket must be at index 1 with a real Unix-ms timestamp score.
        Assert.Equal(normalTicketId.ToString(), (string?)members[1].Element);
        Assert.True(members[1].Score > 1_000_000_000_000d,
            $"Normal ticket score should be Unix milliseconds (>= 10^12); got {members[1].Score}.");
    }

    /// <summary>
    /// SC#3: POST /api/matchmaking/backfill against a non-existent session returns 404 with
    /// error = session_not_found.
    /// </summary>
    [Fact]
    public async Task SC3_Backfill_MissingSession_Returns404()
    {
        var playerId = Guid.NewGuid();
        using var client = _app!.CreateClient(playerId);

        var resp = await client.PostAsJsonAsync("/api/matchmaking/backfill",
            new BackfillRequest(_app.TestLadderId, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("session_not_found", body.GetProperty("error").GetString());
    }
}
