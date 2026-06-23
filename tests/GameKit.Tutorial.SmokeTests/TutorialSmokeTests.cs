// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Tutorial.SmokeTests;

/// <summary>
/// DOCS-02 tutorial happy-path smoke test.
///
/// Proves the documented tutorial path — two guest players enqueue, the in-process ticker
/// forms a proposal, both players accept, and the sample is in a healthy state — works
/// end-to-end against a real Testcontainers Postgres + Redis stack with zero cloud credentials.
///
/// This is NOT a vacuous compile-check. A real match genuinely forms: the in-process
/// matchmaking ticker (running inside <see cref="TutorialSmokeTestApp"/>) advances both
/// tickets to <c>proposed</c>, the test extracts the non-null <c>ProposalId</c> from the
/// <see cref="TicketStatusResponse"/>, and drives BOTH accept calls with that same id.
/// The second accept confirms the all-accepted / match-formed outcome (Status = "matched").
/// </summary>
[Collection("TutorialSmoke")]
[Trait("Category", "Integration")]
public sealed class TutorialSmokeTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public TutorialSmokeTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact(DisplayName = "DOCS-02: tutorial happy-path forms a match and reaches readiness")]
    public async Task TutorialHappyPath_FormsMatchAndReachesReadiness()
    {
        await using var app = await TutorialSmokeTestApp.StartAsync(_pg, _redis);

        // Step 1: Two guest logins — each player has a distinct X-GameKit-Device value.
        // The X-GameKit-Device header is required for refresh-family tracking (T-02-06-02).
        // Missing it causes 400 on the guest-login endpoint.
        using var clientA = app.CreateClient("tutorial-device-A");
        using var clientB = app.CreateClient("tutorial-device-B");

        var tokenA = await GuestLoginAsync(clientA);
        var tokenB = await GuestLoginAsync(clientB);

        // Authenticate both clients with their JWT Bearer tokens.
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        // Step 2: Two enqueues — BOTH with poolName null.
        // poolName null routes to the "default" pool (EnqueueRequest.PoolName defaults to null).
        // CRITICAL: a named pool (e.g. "tictactoe") never forms a match in TicTacToeDuel because
        // the matchmaking ladder only pairs tickets in the "default" pool.
        var ladderId = app.TicTacToeLadderId;
        Assert.NotEqual(Guid.Empty, ladderId); // ladder must have been seeded

        var (ticketIdA, _) = await EnqueueAsync(clientA, ladderId, poolName: null);
        var (ticketIdB, _) = await EnqueueAsync(clientB, ladderId, poolName: null);

        // Step 3: Poll GET /api/mm/queue/{ticketId}/status until Status == "proposed".
        // The in-process ticker fires every 500 ms and pairs both tickets immediately (they
        // have the same default Glicko-2 rating so their bracket delta = 0 < BracketStart = 100).
        // Deadline: 10 seconds. Assert.Fail (do NOT hang) if no proposal forms in time.
        var statusA = await PollUntilProposedAsync(clientA, ticketIdA, deadlineSeconds: 10);

        // Extract the non-null ProposalId from the proposed-status response.
        // TicketStatusResponse.ProposalId is populated when Status == "proposed".
        // "matched" carries SessionId; "proposed" carries ProposalId — do NOT mix them up.
        Assert.NotNull(statusA.ProposalId);
        var proposalId = statusA.ProposalId!.Value;

        // Step 4: Both players accept the SAME proposal.
        // First accept returns Status = "queued" with the ProposalId (one player pending).
        // Second accept returns Status = "matched" (all players accepted → match formed).
        var acceptResponseA = await AcceptProposalAsync(clientA, proposalId, ticketIdA);
        Assert.NotNull(acceptResponseA);
        // Player A accepted; the proposal is pending the second player.

        var acceptResponseB = await AcceptProposalAsync(clientB, proposalId, ticketIdB);
        Assert.NotNull(acceptResponseB);

        // The second accept response MUST confirm the all-accepted / match-formed outcome.
        // MatchmakingEndpoints.AcceptAsync returns AllAccepted → TicketStatusResponse(Status:"matched", ProposalId:proposalId).
        Assert.Equal("matched", acceptResponseB!.Status);
        // When Status is "matched" the ProposalId is still populated (the endpoint returns it).
        // We verify the proposal id round-trips to confirm we're looking at the right proposal.
        Assert.Equal(proposalId, acceptResponseB.ProposalId);

        // Step 5: Assert /health/ready returns 200.
        // This confirms Postgres + Redis + all migration reporters are healthy after the match formed.
        using var healthClient = app.CreateClient("health-check-device");
        var healthResp = await healthClient.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
    }

    // ---- helpers ----

    /// <summary>
    /// POST /auth/login/guest — X-GameKit-Device header must already be set on the client.
    /// Returns the access token string.
    /// </summary>
    private static async Task<string> GuestLoginAsync(HttpClient client)
    {
        var resp = await client.PostAsync("/auth/login/guest", JsonContent.Create(new { }));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrEmpty(token), "Guest login must return a non-empty access token.");
        return token!;
    }

    /// <summary>
    /// POST /api/mm/queue — returns (ticketId, status).
    /// poolName null routes to the "default" pool (the only pool in TicTacToeDuel).
    /// </summary>
    private static async Task<(Guid ticketId, string status)> EnqueueAsync(
        HttpClient client,
        Guid ladderId,
        string? poolName)
    {
        // poolName: null is EXPLICIT — a named pool never matches in TicTacToeDuel.
        var req = new EnqueueRequest(ladderId, PoolName: poolName);
        var resp = await client.PostAsJsonAsync("/api/mm/queue", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var ticketId = body.GetProperty("ticketId").GetGuid();
        var status   = body.GetProperty("status").GetString() ?? string.Empty;
        Assert.Equal("queued", status);
        return (ticketId, status);
    }

    /// <summary>
    /// Polls GET /api/mm/queue/{ticketId}/status in a deadline-bounded loop until
    /// Status == "proposed". The loop FAILS (Assert.Fail) if no proposal forms within the
    /// deadline — it never hangs.
    /// </summary>
    private static async Task<TicketStatusResponse> PollUntilProposedAsync(
        HttpClient client,
        Guid ticketId,
        int deadlineSeconds)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(deadlineSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var resp = await client.GetAsync($"/api/mm/queue/{ticketId}/status");
            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var body = await resp.Content.ReadFromJsonAsync<TicketStatusResponse>();
                if (body is not null && body.Status == "proposed")
                    return body;
            }

            // Small delay between polls — the ticker runs every 500 ms so 100 ms polls
            // are responsive without hammering the endpoint.
            await Task.Delay(100);
        }

        Assert.Fail(
            $"No matchmaking proposal formed for ticket {ticketId} within {deadlineSeconds}s. " +
            "The in-process ticker may not be running, the ladder may not be seeded, " +
            "or both tickets may not be in the 'default' pool (check that poolName was null, not a named pool).");

        // Unreachable — Assert.Fail throws. Required to satisfy the non-nullable return type.
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// POST /api/mm/proposal/{proposalId}/accept — returns the <see cref="TicketStatusResponse"/>.
    /// </summary>
    private static async Task<TicketStatusResponse?> AcceptProposalAsync(
        HttpClient client,
        Guid proposalId,
        Guid ticketId)
    {
        var req = new AcceptDeclineRequest(ticketId);
        var resp = await client.PostAsJsonAsync($"/api/mm/proposal/{proposalId}/accept", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return await resp.Content.ReadFromJsonAsync<TicketStatusResponse>();
    }
}
