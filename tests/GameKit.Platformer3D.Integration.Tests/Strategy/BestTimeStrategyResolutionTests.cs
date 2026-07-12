// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Strategy;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Platformer3D.Strategy;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests.Strategy;

/// <summary>
/// R5 / A3: Verifies that the custom <see cref="BestTimeMatchmakingStrategy"/> is the
/// sole <see cref="IMatchmakingStrategy"/> resolved by the Platformer3D host, and that
/// a match is formed through it.
/// </summary>
/// <remarks>
/// Two sub-tests:
/// <list type="bullet">
///   <item>
///     <b>Resolution test</b> (no Docker — resolves <see cref="IMatchmakingStrategy"/> from
///     the DI container): can run without Testcontainers. Tests the A3 wiring:
///     <c>services.Replace(ServiceDescriptor.Singleton&lt;IMatchmakingStrategy, BestTimeMatchmakingStrategy&gt;())</c>
///     after <c>AddMatchmaking()</c>.
///   </item>
///   <item>
///     <b>Match-formation test</b> (Docker): enqueues two parties, polls status until matched,
///     asserts both are in the same 1v1 session. Requires Testcontainers Postgres + Redis.
///   </item>
/// </list>
/// </remarks>
[Collection("Platformer3D")]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class BestTimeStrategyResolutionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private PlatformerTestApp _app = default!;

    public BestTimeStrategyResolutionTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _app = new PlatformerTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    [Fact(DisplayName = "R5/A3: resolved IMatchmakingStrategy is BestTimeMatchmakingStrategy (not EloRangeMatchmakingStrategy)")]
    public void Resolved_Strategy_Is_BestTimeMatchmakingStrategy()
    {
        // Resolve IMatchmakingStrategy from the test host's DI container.
        var strategy = _app.Server.Services.GetRequiredService<IMatchmakingStrategy>();

        // Assert the concrete type is the custom strategy (R5/A3 gate).
        Assert.IsType<BestTimeMatchmakingStrategy>(strategy);

        // Confirm it is NOT the default EloRangeMatchmakingStrategy.
        Assert.False(strategy is EloRangeMatchmakingStrategy,
            "Resolved strategy should not be EloRangeMatchmakingStrategy — A3 wiring broken.");

        // Confirm the Name discriminator is the custom one (not "elo-range").
        Assert.Equal("best-time", strategy.Name);
        Assert.NotEqual("elo-range", strategy.Name);
    }

    [Fact(DisplayName = "R5: two cold-start parties enqueued → matched through BestTimeMatchmakingStrategy")]
    public async Task TwoParties_EnqueuedOnPlatformerLadder_FormOneMatch()
    {
        // Arrange: two guest players.
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        using var clientA = _app.CreateAuthenticatedClient(playerA);
        using var clientB = _app.CreateAuthenticatedClient(playerB);

        var enqueueBody = new EnqueueRequest(
            LadderId: _app.PlatformerLadderId,
            PoolName: null,  // "default" pool
            PartyId: null);

        // Act: both enqueue solo.
        var respA = await clientA.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        Assert.Equal(HttpStatusCode.OK, respA.StatusCode);
        var bodyA = await respA.Content.ReadFromJsonAsync<EnqueueResponseBody>();
        Assert.NotNull(bodyA);

        var respB = await clientB.PostAsJsonAsync("/api/mm/queue", enqueueBody);
        Assert.Equal(HttpStatusCode.OK, respB.StatusCode);
        var bodyB = await respB.Content.ReadFromJsonAsync<EnqueueResponseBody>();
        Assert.NotNull(bodyB);

        // Both received ticket IDs.
        Assert.NotEqual(bodyA!.TicketId, bodyB!.TicketId);

        // Poll status for both tickets concurrently until matched (or timeout).
        // IMPORTANT: these MUST run in parallel. Each player's poll loop auto-accepts when it
        // sees "proposed". If polled sequentially, player A accepts and waits for "matched" that
        // never arrives because player B hasn't accepted yet — a sequential deadlock.
        var (matchedA, matchedB) = await PollBothUntilMatchedAsync(
            clientA, bodyA.TicketId,
            clientB, bodyB.TicketId,
            TimeSpan.FromSeconds(30));

        Assert.NotNull(matchedA.SessionId);
        Assert.NotNull(matchedB.SessionId);

        // Both tickets landed in the SAME session (1v1 match).
        Assert.Equal(matchedA.SessionId, matchedB.SessionId);
        Assert.Equal("matched", matchedA.Status, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("matched", matchedB.Status, StringComparer.OrdinalIgnoreCase);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Polls two tickets concurrently until both reach "matched". Each poll loop
    /// auto-accepts when it sees "proposed" — both loops must run simultaneously so
    /// each player's accept goes through before the match can complete.
    /// </summary>
    private static async Task<(TicketStatusResponse A, TicketStatusResponse B)> PollBothUntilMatchedAsync(
        HttpClient clientA, Guid ticketIdA,
        HttpClient clientB, Guid ticketIdB,
        TimeSpan timeout)
    {
        var taskA = PollUntilMatchedAsync(clientA, ticketIdA, timeout);
        var taskB = PollUntilMatchedAsync(clientB, ticketIdB, timeout);
        await Task.WhenAll(taskA, taskB).ConfigureAwait(false);
        return (taskA.Result, taskB.Result);
    }

    /// <summary>
    /// Polls ticket status until "matched". When the ticker transitions the ticket to
    /// "proposed", automatically accepts the proposal (mirroring client-side accept-step
    /// logic) so the poll can reach "matched" without human interaction.
    /// </summary>
    private static async Task<TicketStatusResponse> PollUntilMatchedAsync(
        HttpClient client,
        Guid ticketId,
        TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);

        // Track whether we have already accepted a proposal for this ticket
        // so we don't spam the accept endpoint.
        Guid? acceptedProposalId = null;

        while (!cts.Token.IsCancellationRequested)
        {
            var resp = await client.GetAsync(
                $"/api/mm/queue/{ticketId}/status",
                cts.Token);

            if (resp.StatusCode == HttpStatusCode.OK)
            {
                var status = await resp.Content.ReadFromJsonAsync<TicketStatusResponse>(
                    cancellationToken: cts.Token);

                if (status is null)
                {
                    await Task.Delay(200, cts.Token);
                    continue;
                }

                if (status.Status is "matched")
                    return status;

                // The ticker has formed a proposal — auto-accept so the match completes.
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

                    // AcceptResult.AllAccepted → 200 with status="matched" (but no sessionId in the
                    // accept response body — the accept endpoint doesn't include it). Fall through
                    // to the next status poll which will include sessionId from the ticket hash.
                    if (acceptResp.StatusCode == HttpStatusCode.OK)
                    {
                        var acceptBody = await acceptResp.Content.ReadFromJsonAsync<TicketStatusResponse>(
                            cancellationToken: cts.Token);
                        if (acceptBody?.Status is "matched")
                        {
                            // Fetch the full status (with sessionId) from the ticket hash.
                            var finalResp = await client.GetAsync(
                                $"/api/mm/queue/{ticketId}/status", cts.Token);
                            if (finalResp.StatusCode == HttpStatusCode.OK)
                            {
                                var finalStatus = await finalResp.Content.ReadFromJsonAsync<TicketStatusResponse>(
                                    cancellationToken: cts.Token);
                                if (finalStatus?.Status is "matched")
                                    return finalStatus;
                            }
                            // If the final GET didn't return matched+sessionId, keep polling.
                        }
                    }
                    // AcceptResult.Accepted (waiting for other player) — keep polling
                }
            }

            await Task.Delay(200, cts.Token);
        }

        throw new TimeoutException(
            $"Ticket {ticketId} did not reach 'matched' status within {timeout}.");
    }

    // ─── Private DTOs ─────────────────────────────────────────────────────────

    // Matches the anonymous object returned by EnqueueAsync on success.
    private sealed record EnqueueResponseBody(Guid TicketId, string Status);
}
