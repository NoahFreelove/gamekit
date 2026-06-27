// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Platformer3D.GameServer;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests.Smoke;

/// <summary>
/// R10 end-to-end smoke tests: full guest → enqueue → match → WS game session →
/// session complete → rating-changed loop. Verifies that all GameKit packages compose
/// correctly as a consumer-assembled stack in the Platformer3D demo host.
/// </summary>
/// <remarks>
/// Each test creates its own isolated <see cref="PlatformerTestApp"/> so tests do not
/// share EF Core DbContext or Redis state. The host is started against the shared
/// Testcontainers Postgres and Redis containers (<see cref="PostgresFixture"/>, <see cref="RedisFixture"/>).
///
/// The "full loop" exercise path:
/// <list type="number">
///   <item><description>Two players call <c>POST /auth/login/guest</c> and receive JWTs.</description></item>
///   <item><description>Both enqueue on the platformer ladder via <c>POST /api/mm/queue</c>.</description></item>
///   <item><description>The BestTime ticker pairs them; both poll until "matched".</description></item>
///   <item><description>Both connect to <c>/ws/game/{sessionId}</c> and exchange WS frames:
///       <c>run_start</c> → <c>checkpoint</c> → <c>run_finish</c> → receive "validated".</description></item>
///   <item><description>The embedded <see cref="PlatformerGameServerService"/> POSTs session-complete with
///       service-token auth and idempotency key.</description></item>
///   <item><description>Both players' <c>player_ranks</c> rows are updated (rating changed from default).</description></item>
/// </list>
/// </remarks>
[Collection("Platformer3D")]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class EndToEndSmokeTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private PlatformerTestApp _app = default!;

    /// <summary>Initialises with shared Testcontainers fixtures.</summary>
    public EndToEndSmokeTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _app = new PlatformerTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    // ─── R10 full loop ─────────────────────────────────────────────────────────

    /// <summary>
    /// R10 primary smoke: guest login → enqueue → matched → WebSocket race session →
    /// session complete posted by game server → rating row changed.
    /// </summary>
    [Fact(DisplayName = "R10: FullLoop_GuestToLeaderboard — guest login → match → WS race → rating changed")]
    public async Task FullLoop_GuestToLeaderboard()
    {
        // ── Step 1: Two guests log in ──────────────────────────────────────────
        var (playerA, tokenA) = await LoginAsGuestAsync();
        var (playerB, tokenB) = await LoginAsGuestAsync();

        // ── Step 2: Enqueue both players ───────────────────────────────────────
        using var clientA = CreateBearerClient(tokenA);
        using var clientB = CreateBearerClient(tokenB);

        var enqueueBody = new EnqueueRequest(
            LadderId: _app.PlatformerLadderId,
            PoolName: null,
            PartyId: null);

        var respA = await clientA.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
        var bodyA = await respA.Content.ReadFromJsonAsync<EnqueueResponseBody>();
        Assert.NotNull(bodyA);

        var respB = await clientB.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
        var bodyB = await respB.Content.ReadFromJsonAsync<EnqueueResponseBody>();
        Assert.NotNull(bodyB);

        // ── Step 3: Poll until matched (concurrent) ────────────────────────────
        var (matchedA, matchedB) = await PollBothUntilMatchedAsync(
            clientA, bodyA!.TicketId,
            clientB, bodyB!.TicketId,
            TimeSpan.FromSeconds(30));

        Assert.NotNull(matchedA.SessionId);
        Assert.NotNull(matchedB.SessionId);
        Assert.Equal(matchedA.SessionId, matchedB.SessionId);

        var sessionId = matchedA.SessionId!.Value;

        // ── Step 4: Both players run a WS game session ─────────────────────────
        // Player A: 60,100ms. Player B: 70,200ms. Player A wins.
        var runTaskA = RunPlayerAsync(sessionId, tokenA, startMs: 1_000L, checkpointMs: 5_000L, finishMs: 61_100L);
        var runTaskB = RunPlayerAsync(sessionId, tokenB, startMs: 1_000L, checkpointMs: 5_000L, finishMs: 71_200L);
        await Task.WhenAll(runTaskA, runTaskB);

        var runA = await runTaskA;
        var runB = await runTaskB;
        Assert.True(runA.IsValidated, $"Player A run was rejected: {runA.Reason}");
        Assert.True(runB.IsValidated, $"Player B run was rejected: {runB.Reason}");

        // ── Step 5: Wait for session-complete to be posted (game server is async) ─
        await WaitForSessionCompleteAsync(sessionId, TimeSpan.FromSeconds(10));

        // ── Step 6: Force the rankings ticker to flush pending updates ──────────
        // Session complete enqueues PendingRatingUpdate rows; RankingsTickerService
        // processes them on a 60-second schedule. We invoke RunOnceAsync() directly
        // to avoid a 60-second test delay (D-22).
        var ticker = _app.Server.Services.GetRequiredService<GameKit.Rankings.Services.IRankingsTicker>();
        await ticker.RunOnceAsync(CancellationToken.None);

        // ── Step 7: Verify rating rows updated ─────────────────────────────────
        var ratingA = await _app.GetPlayerRatingAsync(playerA);
        var ratingB = await _app.GetPlayerRatingAsync(playerB);

        Assert.NotNull(ratingA);
        Assert.NotNull(ratingB);

        // Default rating is 1000. After a Win/Loss, ratings should differ from the default.
        const double defaultRating = 1000.0;
        const double tolerance = 0.001;
        Assert.False(
            Math.Abs(ratingA.Value - defaultRating) < tolerance,
            $"Player A's rating ({ratingA.Value:F4}) did not change from default {defaultRating} after winning.");
        Assert.False(
            Math.Abs(ratingB.Value - defaultRating) < tolerance,
            $"Player B's rating ({ratingB.Value:F4}) did not change from default {defaultRating} after losing.");
    }

    /// <summary>
    /// D-05 idempotency: double-posting the same <c>Idempotency-Key</c> on
    /// <c>POST /api/sessions/{id}/complete</c> produces exactly one <c>game_sessions</c> row.
    /// </summary>
    [Fact(DisplayName = "R10/D-05: DoublePost_SessionComplete_IsIdempotent — two identical posts → one outcome row")]
    public async Task DoublePost_SessionComplete_IsIdempotent()
    {
        // Arrange: two players, matched — get a real session id + service token.
        var (playerA, tokenA) = await LoginAsGuestAsync();
        var (playerB, tokenB) = await LoginAsGuestAsync();

        using var clientA = CreateBearerClient(tokenA);
        using var clientB = CreateBearerClient(tokenB);

        var (sessionId, serviceToken) = await MatchAndGetServiceTokenAsync(
            playerA, clientA,
            playerB, clientB);

        // Build the complete request manually (bypass WS — idempotency test only).
        var completeRequest = PlatformerGameServerService.BuildCompleteRequest(
            playerA, 30_000L, playerB, 45_000L);

        var idempotencyKey = PlatformerGameServerService.IdempotencyKeyFor(sessionId);

        // ── First POST ────────────────────────────────────────────────────────
        using var svc1 = CreateServiceTokenClient(serviceToken);
        svc1.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        var resp1 = await svc1.PostAsJsonAsync($"/api/sessions/{sessionId}/complete", completeRequest);
        Assert.True(
            resp1.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
            $"First POST /api/sessions/{sessionId}/complete → unexpected {(int)resp1.StatusCode}");

        // ── Second POST (duplicate, same key) ─────────────────────────────────
        using var svc2 = CreateServiceTokenClient(serviceToken);
        svc2.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        var resp2 = await svc2.PostAsJsonAsync($"/api/sessions/{sessionId}/complete", completeRequest);
        Assert.True(
            resp2.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent,
            $"Second POST /api/sessions/{sessionId}/complete → unexpected {(int)resp2.StatusCode}");

        // ── Assert: exactly one outcome row ───────────────────────────────────
        var sessionCount = await _app.CountGameSessionOutcomesAsync(sessionId);
        Assert.Equal(1, sessionCount);
    }

    /// <summary>
    /// R10 backstop: run the full guest→match→session loop twice on the same host
    /// to verify no residual state from the first loop causes failures.
    /// </summary>
    [Fact(DisplayName = "R10: Rerun_FullLoopPassesTwice — sequential runs on same host both pass")]
    public async Task Rerun_FullLoopPassesTwice()
    {
        for (var run = 1; run <= 2; run++)
        {
            var (playerA, tokenA) = await LoginAsGuestAsync();
            var (playerB, tokenB) = await LoginAsGuestAsync();

            using var clientA = CreateBearerClient(tokenA);
            using var clientB = CreateBearerClient(tokenB);

            var enqueueBody = new EnqueueRequest(
                LadderId: _app.PlatformerLadderId,
                PoolName: null,
                PartyId: null);

            var respA = await clientA.PostAsJsonAsync("/api/mm/queue", enqueueBody);
            respA.EnsureSuccessStatusCode();
            var bodyA = await respA.Content.ReadFromJsonAsync<EnqueueResponseBody>();

            var respB = await clientB.PostAsJsonAsync("/api/mm/queue", enqueueBody);
            respB.EnsureSuccessStatusCode();
            var bodyB = await respB.Content.ReadFromJsonAsync<EnqueueResponseBody>();

            var (matchedA, _) = await PollBothUntilMatchedAsync(
                clientA, bodyA!.TicketId,
                clientB, bodyB!.TicketId,
                TimeSpan.FromSeconds(30));

            Assert.NotNull(matchedA.SessionId);
            var sessionId = matchedA.SessionId!.Value;

            var runTaskA = RunPlayerAsync(sessionId, tokenA, 1_000L, 6_000L, 35_000L);
            var runTaskB = RunPlayerAsync(sessionId, tokenB, 1_000L, 7_000L, 55_000L);
            await Task.WhenAll(runTaskA, runTaskB);

            var runA = await runTaskA;
            var runB = await runTaskB;
            Assert.True(runA.IsValidated, $"Run {run} player A rejected: {runA.Reason}");
            Assert.True(runB.IsValidated, $"Run {run} player B rejected: {runB.Reason}");

            await WaitForSessionCompleteAsync(sessionId, TimeSpan.FromSeconds(10));
        }
    }

    /// <summary>
    /// R10 backstop: two independent pairs queue simultaneously and each pair is placed
    /// into its own distinct session (no cross-pair contamination).
    /// </summary>
    [Fact(DisplayName = "R10: ConcurrentParties_EachFormExactlyOneMatch — two pairs → two distinct sessions")]
    public async Task ConcurrentParties_EachFormExactlyOneMatch()
    {
        // Pair 1 players
        var (_, t1A) = await LoginAsGuestAsync();
        var (_, t1B) = await LoginAsGuestAsync();
        // Pair 2 players
        var (_, t2A) = await LoginAsGuestAsync();
        var (_, t2B) = await LoginAsGuestAsync();

        using var c1A = CreateBearerClient(t1A);
        using var c1B = CreateBearerClient(t1B);
        using var c2A = CreateBearerClient(t2A);
        using var c2B = CreateBearerClient(t2B);

        var enqueueBody = new EnqueueRequest(
            LadderId: _app.PlatformerLadderId,
            PoolName: null,
            PartyId: null);

        // All four enqueue simultaneously.
        var eq1A = await c1A.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        var eq1B = await c1B.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        var eq2A = await c2A.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        var eq2B = await c2B.PostAsJsonAsync("/api/mm/queue", enqueueBody);

        eq1A.EnsureSuccessStatusCode();
        eq1B.EnsureSuccessStatusCode();
        eq2A.EnsureSuccessStatusCode();
        eq2B.EnsureSuccessStatusCode();

        var b1A = await eq1A.Content.ReadFromJsonAsync<EnqueueResponseBody>();
        var b1B = await eq1B.Content.ReadFromJsonAsync<EnqueueResponseBody>();
        var b2A = await eq2A.Content.ReadFromJsonAsync<EnqueueResponseBody>();
        var b2B = await eq2B.Content.ReadFromJsonAsync<EnqueueResponseBody>();

        // Poll all four tickets concurrently (auto-accept proposals).
        var poll1A = PollUntilMatchedAsync(c1A, b1A!.TicketId, TimeSpan.FromSeconds(30));
        var poll1B = PollUntilMatchedAsync(c1B, b1B!.TicketId, TimeSpan.FromSeconds(30));
        var poll2A = PollUntilMatchedAsync(c2A, b2A!.TicketId, TimeSpan.FromSeconds(30));
        var poll2B = PollUntilMatchedAsync(c2B, b2B!.TicketId, TimeSpan.FromSeconds(30));
        await Task.WhenAll(poll1A, poll1B, poll2A, poll2B);

        var r1A = await poll1A;
        var r1B = await poll1B;
        var r2A = await poll2A;
        var r2B = await poll2B;

        var sessionIds = new HashSet<Guid>();
        foreach (var r in new[] { r1A, r1B, r2A, r2B })
        {
            Assert.NotNull(r.SessionId);
            sessionIds.Add(r.SessionId!.Value);
        }

        // Four tickets matched, exactly 2 distinct sessions formed.
        Assert.Equal(2, sessionIds.Count);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>Logs in as a new guest, returning (playerId, accessToken).</summary>
    private async Task<(Guid PlayerId, string AccessToken)> LoginAsGuestAsync()
    {
        var resp = await _app.Client.PostAsJsonAsync(
            "/auth/login/guest",
            new GameKit.Auth.Http.Contracts.LoginRequest(Username: null, Password: null));
        resp.EnsureSuccessStatusCode();

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = body.GetProperty("accessToken").GetString()!;

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(accessToken);
        var sub = token.Subject ?? throw new InvalidOperationException("JWT missing 'sub'");
        return (Guid.Parse(sub), accessToken);
    }

    /// <summary>Creates an HTTP client with Bearer token set.</summary>
    private HttpClient CreateBearerClient(string token)
    {
        var client = _app.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates an HTTP client with a service token Bearer.</summary>
    private HttpClient CreateServiceTokenClient(string serviceToken)
    {
        var client = _app.Server.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", serviceToken);
        return client;
    }

    /// <summary>
    /// Polls two tickets concurrently until both reach "matched".
    /// Each poll loop auto-accepts the proposal when it appears.
    /// </summary>
    private static async Task<(TicketStatusResponse A, TicketStatusResponse B)> PollBothUntilMatchedAsync(
        HttpClient clientA, Guid ticketIdA,
        HttpClient clientB, Guid ticketIdB,
        TimeSpan timeout)
    {
        var taskA = PollUntilMatchedAsync(clientA, ticketIdA, timeout);
        var taskB = PollUntilMatchedAsync(clientB, ticketIdB, timeout);
        await Task.WhenAll(taskA, taskB).ConfigureAwait(false);
        return (await taskA, await taskB);
    }

    private static async Task<TicketStatusResponse> PollUntilMatchedAsync(
        HttpClient client, Guid ticketId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        Guid? acceptedProposalId = null;

        while (!cts.Token.IsCancellationRequested)
        {
            var resp = await client.GetAsync($"/api/mm/queue/{ticketId}/status", cts.Token);
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var status = await resp.Content.ReadFromJsonAsync<TicketStatusResponse>(
                    cancellationToken: cts.Token);
                if (status is null) { await Task.Delay(200, cts.Token); continue; }

                if (status.Status is "matched")
                    return status;

                if (status.Status is "proposed" &&
                    status.ProposalId.HasValue &&
                    status.ProposalId != acceptedProposalId)
                {
                    var proposalId = status.ProposalId.Value;
                    acceptedProposalId = proposalId;
                    var acceptResp = await client.PostAsJsonAsync(
                        $"/api/mm/proposal/{proposalId}/accept",
                        new AcceptDeclineRequest(TicketId: ticketId),
                        cts.Token);
                    if (acceptResp.StatusCode == HttpStatusCode.OK)
                    {
                        var acceptBody = await acceptResp.Content.ReadFromJsonAsync<TicketStatusResponse>(
                            cancellationToken: cts.Token);
                        if (acceptBody?.Status is "matched")
                        {
                            var finalResp = await client.GetAsync(
                                $"/api/mm/queue/{ticketId}/status", cts.Token);
                            if (finalResp.StatusCode == HttpStatusCode.OK)
                            {
                                var finalStatus = await finalResp.Content.ReadFromJsonAsync<TicketStatusResponse>(
                                    cancellationToken: cts.Token);
                                if (finalStatus?.Status is "matched")
                                    return finalStatus;
                            }
                        }
                    }
                }
            }
            await Task.Delay(200, cts.Token);
        }
        throw new TimeoutException($"Ticket {ticketId} did not reach 'matched' within {timeout}.");
    }

    /// <summary>
    /// Runs a full WS game session for one player: connect → run_start → checkpoint → run_finish.
    /// Returns (IsValidated, Reason) indicating whether the server accepted the run.
    /// </summary>
    /// <param name="sessionId">The matched session id (used as matchId in WS URL).</param>
    /// <param name="token">The player's Bearer JWT.</param>
    /// <param name="startMs">Epoch-ms run start timestamp.</param>
    /// <param name="checkpointMs">Single checkpoint epoch-ms timestamp.</param>
    /// <param name="finishMs">Epoch-ms run finish timestamp.</param>
    private async Task<RunResult> RunPlayerAsync(
        Guid sessionId,
        string token,
        long startMs,
        long checkpointMs,
        long finishMs)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var wsClient = _app.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req =>
        {
            // Append rather than Add to avoid ArgumentException on duplicate header (ASP0019).
            req.Headers["Authorization"] = $"Bearer {token}";
        };

        using var ws = await wsClient.ConnectAsync(
            new Uri($"ws://localhost/ws/game/{sessionId}"),
            cts.Token);

        // Send run_start
        await SendWsFrameAsync(ws,
            new { type = "run_start", startMs },
            cts.Token);

        // Send checkpoint
        await SendWsFrameAsync(ws,
            new { type = "checkpoint", index = 0, timestampMs = checkpointMs },
            cts.Token);

        // Send run_finish
        await SendWsFrameAsync(ws,
            new { type = "run_finish", finishMs },
            cts.Token);

        // Read response frames until "validated" or "rejected" (or timeout).
        var buffer = new byte[16 * 1024];
        while (!cts.Token.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await ws.ReceiveAsync(buffer, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                break;
            if (result.MessageType != WebSocketMessageType.Text)
                continue;

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var typeProp))
                continue;

            var type = typeProp.GetString();
            if (type is "validated")
            {
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
                catch { /* best-effort */ }
                return new RunResult(IsValidated: true, Reason: null);
            }
            if (type is "rejected")
            {
                var reason = doc.RootElement.TryGetProperty("reason", out var r)
                    ? r.GetString() : "unknown";
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); }
                catch { /* best-effort */ }
                return new RunResult(IsValidated: false, Reason: reason);
            }
            // type = "ping" → ignore and continue reading
        }

        return new RunResult(IsValidated: false, Reason: "timeout_or_no_response");
    }

    private static Task SendWsFrameAsync<T>(WebSocket ws, T message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var bytes = Encoding.UTF8.GetBytes(json);
        return ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    /// <summary>
    /// Polls the <c>game_sessions</c> table until the session with <paramref name="sessionId"/>
    /// reaches the <c>Completed</c> state (game server has successfully posted session-complete),
    /// or throws <see cref="TimeoutException"/>.
    /// </summary>
    private async Task WaitForSessionCompleteAsync(Guid sessionId, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.Token.IsCancellationRequested)
        {
            var completed = await _app.IsSessionCompletedAsync(sessionId);
            if (completed) return;
            await Task.Delay(200, cts.Token);
        }
        throw new TimeoutException(
            $"game_sessions/{sessionId} did not reach Completed state within {timeout}.");
    }

    /// <summary>
    /// Matches two players, extracts the session id, and retrieves a fresh service token
    /// for direct HTTP session-complete calls in the idempotency test.
    /// </summary>
    private async Task<(Guid SessionId, string ServiceToken)> MatchAndGetServiceTokenAsync(
        Guid playerA, HttpClient clientA,
        Guid playerB, HttpClient clientB)
    {
        var enqueueBody = new EnqueueRequest(
            LadderId: _app.PlatformerLadderId,
            PoolName: null,
            PartyId: null);

        var respA = await clientA.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        respA.EnsureSuccessStatusCode();
        var bodyA = await respA.Content.ReadFromJsonAsync<EnqueueResponseBody>();

        var respB = await clientB.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        respB.EnsureSuccessStatusCode();
        var bodyB = await respB.Content.ReadFromJsonAsync<EnqueueResponseBody>();

        var (matchedA, _) = await PollBothUntilMatchedAsync(
            clientA, bodyA!.TicketId,
            clientB, bodyB!.TicketId,
            TimeSpan.FromSeconds(30));

        Assert.NotNull(matchedA.SessionId);
        var sessionId = matchedA.SessionId!.Value;

        // Issue a fresh service token under a test-specific name for the idempotency test.
        // (The embedded game server already holds one issued at startup — we issue a separate
        //  one so the test can drive PostAsJsonAsync directly without exposing the server's.)
        await using var scope = _app.Server.Services.CreateAsyncScope();
        var tokenSvc = scope.ServiceProvider.GetRequiredService<GameKit.Rankings.Services.IServiceTokenService>();
        var testTokenName = $"test-idempotency-{Guid.NewGuid():N}";
        var issueResult = await tokenSvc.IssueAsync(testTokenName, expiresAt: null, default);

        return (sessionId, issueResult.Raw);
    }

    // ─── Private DTOs ──────────────────────────────────────────────────────────

    private sealed record EnqueueResponseBody(Guid TicketId, string Status);

    private sealed record RunResult(bool IsValidated, string? Reason);
}
