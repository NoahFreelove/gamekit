// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Lobby.Entities;
using GameKit.Lobby.Http.Contracts;
using GameKit.Lobby.Hubs;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using Xunit;

namespace GameKit.Platformer3D.Integration.Tests.Lobby;

/// <summary>
/// R9: Verifies the lobby → ready-check → matchmaking → 1v1 flow and the abort path.
/// <para>
/// <b>Happy path</b>: two solo players each create their own lobby (MaxMembers=1, LadderId=platformer),
/// mark ready (triggering solo-party enqueue per player), then the BestTimeMatchmakingStrategy
/// pairs them into a single 1v1 session. Both players land in the same session id.
/// </para>
/// <para>
/// <b>Abort path</b> (R9/D-04): a lobby owner removes the joining player before the ready-check
/// completes. No matchmaking tickets are enqueued and the lobby remains intact.
/// </para>
/// </summary>
[Collection("Platformer3D")]
[Trait("Category", "Integration")]
[Trait("RequiresDocker", "true")]
public sealed class LobbyToMatchTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private PlatformerTestApp _app = default!;

    public LobbyToMatchTests(PostgresFixture pg, RedisFixture redis)
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

    // ─── Happy path ───────────────────────────────────────────────────────────

    /// <summary>
    /// R9 happy path: two players each seed their own ReadyChecking lobby (solo, MaxMembers=1).
    /// Both call MarkReady via the SignalR hub — each triggers TryStartMatchmakingAsync,
    /// creating a solo party + enqueuing one ticket per player. The BestTimeMatchmakingStrategy
    /// pairs the two solo parties into one 1v1 session; both players land in the same session id.
    /// <para>
    /// Note: MaxMembers=1 lobbies start in Open state. We seed them in ReadyChecking state via
    /// <see cref="PlatformerTestApp.SeedLobbyAsync"/> so MarkReady's all-ready gate fires on the
    /// first (and only) member. This mirrors the real flow where the second joiner triggers
    /// Open→ReadyChecking for a 2-person party; for a solo lobby the seed takes that role.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "R9: two solo-lobby ready-checks → two solo parties → 1v1 match (both in same session)")]
    public async Task LobbyToMatch_TwoSoloLobbies_BothLandInOneSession()
    {
        // Arrange: two guest players, each with their own ReadyChecking lobby (seeded).
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        // Seed two separate ReadyChecking lobbies (MaxMembers=1, State=ReadyChecking).
        var lobbyIdA = await _app.SeedLobbyAsync(new[] { playerA }, _app.PlatformerLadderId);
        var lobbyIdB = await _app.SeedLobbyAsync(new[] { playerB }, _app.PlatformerLadderId);

        // Connect both players to the lobby hub.
        var connA = _app.ConnectLobbyHub(playerA);
        var connB = _app.ConnectLobbyHub(playerB);

        // Capture InGame broadcast (fired when TryStartMatchmakingAsync succeeds).
        var inGameTcsA = new TaskCompletionSource<LobbyStateUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var inGameTcsB = new TaskCompletionSource<LobbyStateUpdate>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connA.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", upd =>
        {
            if (upd.State == LobbyState.InGame) inGameTcsA.TrySetResult(upd);
        });
        connB.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", upd =>
        {
            if (upd.State == LobbyState.InGame) inGameTcsB.TrySetResult(upd);
        });

        await connA.StartAsync();
        await connB.StartAsync();

        try
        {
            // Join the hub group so SignalR broadcasts reach the client.
            await connA.InvokeAsync("JoinLobbyAsync", lobbyIdA);
            await connB.InvokeAsync("JoinLobbyAsync", lobbyIdB);

            // Both mark ready concurrently — each triggers solo-party enqueue.
            await Task.WhenAll(
                connA.InvokeAsync("MarkReadyAsync", lobbyIdA),
                connB.InvokeAsync("MarkReadyAsync", lobbyIdB));

            // Wait for both InGame broadcasts (= both enqueued successfully).
            using var hubCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await Task.WhenAll(
                inGameTcsA.Task.WaitAsync(hubCts.Token),
                inGameTcsB.Task.WaitAsync(hubCts.Token));

            var updA = await inGameTcsA.Task;
            var updB = await inGameTcsB.Task;
            Assert.Equal(LobbyState.InGame, updA.State);
            Assert.Equal(LobbyState.InGame, updB.State);
        }
        finally
        {
            await connA.StopAsync();
            await connB.StopAsync();
            await connA.DisposeAsync();
            await connB.DisposeAsync();
        }

        // Assert: each player's ticket was enqueued.
        var ticketCountA = await CountQueuedOrProposedTicketsAsync(playerA);
        var ticketCountB = await CountQueuedOrProposedTicketsAsync(playerB);
        Assert.True(ticketCountA >= 1, $"Expected player A to have a queued ticket; found {ticketCountA}");
        Assert.True(ticketCountB >= 1, $"Expected player B to have a queued ticket; found {ticketCountB}");

        // Poll both tickets concurrently until matched (ticker pairs the two solo parties).
        var (ticketIdA, ticketIdB) = await GetTicketIdsAsync(playerA, playerB);

        using var clientA = _app.CreateAuthenticatedClient(playerA);
        using var clientB = _app.CreateAuthenticatedClient(playerB);

        var (matchedA, matchedB) = await PollBothUntilMatchedAsync(
            clientA, ticketIdA,
            clientB, ticketIdB,
            TimeSpan.FromSeconds(30));

        // Assert: both players land in the SAME session (R9 — one 1v1 match).
        Assert.NotNull(matchedA.SessionId);
        Assert.NotNull(matchedB.SessionId);
        Assert.Equal(matchedA.SessionId, matchedB.SessionId);
        Assert.Equal("matched", matchedA.Status, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("matched", matchedB.Status, StringComparer.OrdinalIgnoreCase);

        // Normal stranger matchmaking (two separate solo parties) stays RANKED — the session
        // keeps its LadderId so the rating pipeline applies. (Contrast with the inter-party
        // self-match, which is unranked.)
        var soloMatchLadderId = await GetSessionLadderIdAsync(matchedA.SessionId!.Value);
        Assert.Equal(_app.PlatformerLadderId, soloMatchLadderId);
    }

    // ─── Inter-party 1v1 (Phase 21) ──────────────────────────────────────────

    /// <summary>
    /// Phase 21 inter-party 1v1: TWO friends in ONE party (owner creates a 2-person lobby, a
    /// second player joins → Open→ReadyChecking). Both mark ready → the all-ready gate enqueues
    /// a SINGLE party ticket carrying both members. The matcher's full-party self-match forms a
    /// 1v1 directly from that one ticket; both members land in the SAME session as opponents
    /// (one on team 0, one on team 1).
    /// <para>
    /// This is the flow that was previously stuck forever ("Waiting for all players…"): the
    /// single party ticket never self-paired. It exercises the relaxed ticker gates +
    /// BestTimeMatchmakingStrategy self-match + TeamAssignmentService lone-party split.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "R9: one 2-member party ready-check → single ticket self-matches → both land in same session on opposing teams")]
    public async Task InterParty_TwoMemberParty_SelfMatchesIntoOneVsOne()
    {
        // Arrange: two guests; owner creates a 2-person lobby, joiner joins (→ ReadyChecking).
        var owner = Guid.NewGuid();
        var joiner = Guid.NewGuid();
        _app.EnsurePlayerRow(owner);
        _app.EnsurePlayerRow(joiner);

        using (var ownerClient = _app.CreateAuthenticatedClient(owner))
        using (var joinerClient = _app.CreateAuthenticatedClient(joiner))
        {
            var createResp = await ownerClient.PostAsJsonAsync("/api/lobbies",
                new CreateLobbyRequest(MaxMembers: 2, LadderId: _app.PlatformerLadderId));
            Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
            var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
            var lobbyId = Guid.Parse(createBody.GetProperty("lobbyId").GetString()!);

            var joinResp = await joinerClient.PostAsJsonAsync(
                $"/api/lobbies/{lobbyId}/join",
                new JoinLobbyRequest(LobbyId: lobbyId));
            Assert.Equal(HttpStatusCode.OK, joinResp.StatusCode);

            // Both members connect to the hub for the same lobby and mark ready.
            var connOwner = _app.ConnectLobbyHub(owner);
            var connJoiner = _app.ConnectLobbyHub(joiner);

            var inGameOwner = new TaskCompletionSource<LobbyStateUpdate>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var inGameJoiner = new TaskCompletionSource<LobbyStateUpdate>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            connOwner.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", upd =>
            {
                if (upd.State == LobbyState.InGame) inGameOwner.TrySetResult(upd);
            });
            connJoiner.On<LobbyStateUpdate>("ReceiveStateUpdateAsync", upd =>
            {
                if (upd.State == LobbyState.InGame) inGameJoiner.TrySetResult(upd);
            });

            await connOwner.StartAsync();
            await connJoiner.StartAsync();
            try
            {
                await connOwner.InvokeAsync("JoinLobbyAsync", lobbyId);
                await connJoiner.InvokeAsync("JoinLobbyAsync", lobbyId);

                // Mark ready sequentially on the SAME lobby — owner first (1/2 ready, no gate),
                // then joiner (2/2 ready → all-ready gate fires once → one party ticket). The
                // two-solo happy-path test marks ready concurrently because each is on its OWN
                // lobby; here both members share one lobby, so the gate must fire on the last.
                await connOwner.InvokeAsync("MarkReadyAsync", lobbyId);
                await connJoiner.InvokeAsync("MarkReadyAsync", lobbyId);

                using var hubCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await Task.WhenAll(
                    inGameOwner.Task.WaitAsync(hubCts.Token),
                    inGameJoiner.Task.WaitAsync(hubCts.Token));
            }
            finally
            {
                await connOwner.StopAsync();
                await connJoiner.StopAsync();
                await connOwner.DisposeAsync();
                await connJoiner.DisposeAsync();
            }
        }

        // Assert: a SINGLE party ticket covers both members (not one ticket each).
        var (ticketOwner, ticketJoiner) = await GetTicketIdsAsync(owner, joiner);
        Assert.Equal(ticketOwner, ticketJoiner); // one shared party ticket
        var partyTicketId = ticketOwner;

        // Both members poll the SAME party ticket (as both browser tabs would via /demo/my-ticket).
        using var pollOwner = _app.CreateAuthenticatedClient(owner);
        using var pollJoiner = _app.CreateAuthenticatedClient(joiner);

        var (matchedOwner, matchedJoiner) = await PollBothUntilMatchedAsync(
            pollOwner, partyTicketId,
            pollJoiner, partyTicketId,
            TimeSpan.FromSeconds(30));

        // Both see the same session id.
        Assert.NotNull(matchedOwner.SessionId);
        Assert.Equal(matchedOwner.SessionId, matchedJoiner.SessionId);

        // The session has exactly two participants — the two friends — on OPPOSING teams.
        var sessionId = matchedOwner.SessionId!.Value;
        var participants = await GetSessionParticipantsAsync(sessionId);
        Assert.Equal(2, participants.Count);
        Assert.True(participants.ContainsKey(owner), "Owner must be a participant.");
        Assert.True(participants.ContainsKey(joiner), "Joiner must be a participant.");
        Assert.NotEqual(participants[owner], participants[joiner]); // opposing teams
        var teams = new SortedSet<int> { participants[owner], participants[joiner] };
        Assert.Equal(new[] { 0, 1 }, teams);

        // Anti-abuse: the inter-party match is UNRANKED — the session has a null LadderId, so
        // the rating pipeline awards no elo (prevents "party up, friend AFKs → free elo").
        var ladderId = await GetSessionLadderIdAsync(sessionId);
        Assert.Null(ladderId);
    }

    // ─── Abort path ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "R9/D-04: lobby owner removes joiner before ready-check → zero tickets enqueued, lobby intact")]
    public async Task LobbyAbort_OwnerRemovesJoiner_ZeroTicketsEnqueued()
    {
        // Arrange: owner creates a 2-person lobby and a second player joins.
        var owner = Guid.NewGuid();
        var joiner = Guid.NewGuid();
        _app.EnsurePlayerRow(owner);
        _app.EnsurePlayerRow(joiner);

        using var ownerClient = _app.CreateAuthenticatedClient(owner);
        using var joinerClient = _app.CreateAuthenticatedClient(joiner);

        // Owner creates a 2-person lobby.
        var createResp = await ownerClient.PostAsJsonAsync("/api/lobbies",
            new CreateLobbyRequest(MaxMembers: 2, LadderId: _app.PlatformerLadderId));
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);
        var createBody = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var lobbyId = Guid.Parse(createBody.GetProperty("lobbyId").GetString()!);

        // Joiner joins the lobby via REST.
        var joinResp = await joinerClient.PostAsJsonAsync(
            $"/api/lobbies/{lobbyId}/join",
            new JoinLobbyRequest(LobbyId: lobbyId));
        Assert.Equal(HttpStatusCode.OK, joinResp.StatusCode);

        // Act: owner removes the joiner BEFORE anyone marks ready.
        var removeResp = await ownerClient.DeleteAsync(
            $"/api/lobbies/{lobbyId}/members/{joiner}");
        Assert.Equal(HttpStatusCode.NoContent, removeResp.StatusCode);

        // Assert 1: No matchmaking tickets were created (abort path = zero enqueues).
        var ticketCount = await CountQueuedOrProposedTicketsForLobbyAsync(owner, joiner);
        Assert.Equal(0, ticketCount);

        // Assert 2: The lobby still exists (party NOT destroyed — D-04).
        var getResp = await ownerClient.GetAsync($"/api/lobbies/{lobbyId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

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
                if (status is null)
                {
                    await Task.Delay(200, cts.Token);
                    continue;
                }
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

    /// <summary>Reads game_sessions.LadderId for a session (null = unranked).</summary>
    private async Task<Guid?> GetSessionLadderIdAsync(Guid sessionId)
    {
        await using var conn = new NpgsqlConnection(_app.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""LadderId"" FROM gamekit.game_sessions WHERE ""Id"" = @sid";
        cmd.Parameters.AddWithValue("sid", sessionId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null || result is DBNull ? (Guid?)null : (Guid)result;
    }

    /// <summary>Reads the session's participants as a player-id → team-index map.</summary>
    private async Task<Dictionary<Guid, int>> GetSessionParticipantsAsync(Guid sessionId)
    {
        await using var conn = new NpgsqlConnection(_app.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""PlayerId"", ""Team""
            FROM gamekit.session_participants
            WHERE ""SessionId"" = @sid AND ""PlayerId"" IS NOT NULL";
        cmd.Parameters.AddWithValue("sid", sessionId);

        var map = new Dictionary<Guid, int>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            map[reader.GetGuid(0)] = reader.GetInt32(1);
        return map;
    }

    private async Task<int> CountQueuedOrProposedTicketsAsync(Guid playerId)
    {
        await using var conn = new NpgsqlConnection(_app.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        // Status 0 = Queued, 1 = Proposed (awaiting accept)
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.matchmaking_tickets t
            INNER JOIN gamekit.party_members pm ON t.""PartyId"" = pm.""PartyId""
            WHERE pm.""PlayerId"" = @pid AND t.""Status"" IN (0, 1)";
        cmd.Parameters.AddWithValue("pid", playerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private async Task<int> CountQueuedOrProposedTicketsForLobbyAsync(params Guid[] playerIds)
    {
        var total = 0;
        foreach (var pid in playerIds)
            total += await CountQueuedOrProposedTicketsAsync(pid);
        return total;
    }

    private async Task<(Guid TicketA, Guid TicketB)> GetTicketIdsAsync(Guid playerA, Guid playerB)
    {
        await using var conn = new NpgsqlConnection(_app.ConnectionString);
        await conn.OpenAsync();

        Guid? tidA = null, tidB = null;

        // Retry briefly since the enqueue is async after the hub call.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while ((!tidA.HasValue || !tidB.HasValue) && !cts.Token.IsCancellationRequested)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT t.""Id"", pm.""PlayerId""
                FROM gamekit.matchmaking_tickets t
                INNER JOIN gamekit.party_members pm ON t.""PartyId"" = pm.""PartyId""
                WHERE pm.""PlayerId"" = ANY(@pids)
                  AND t.""Status"" IN (0, 1)
                ORDER BY t.""QueuedAt"" ASC";
            var pidsParam = cmd.CreateParameter();
            pidsParam.ParameterName = "pids";
            pidsParam.Value = new[] { playerA, playerB };
            cmd.Parameters.Add(pidsParam);

            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var tid = reader.GetGuid(0);
                var pid = reader.GetGuid(1);
                if (pid == playerA && !tidA.HasValue) tidA = tid;
                if (pid == playerB && !tidB.HasValue) tidB = tid;
            }

            if (!tidA.HasValue || !tidB.HasValue)
                await Task.Delay(200, cts.Token);
        }

        if (!tidA.HasValue || !tidB.HasValue)
            throw new TimeoutException(
                $"Could not locate tickets for playerA={playerA} (found: {tidA?.ToString() ?? "null"}) " +
                $"and playerB={playerB} (found: {tidB?.ToString() ?? "null"}) within 10s.");

        return (tidA.Value, tidB.Value);
    }
}
