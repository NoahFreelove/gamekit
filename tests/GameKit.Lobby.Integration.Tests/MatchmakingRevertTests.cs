// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;
using LobbyState = GameKit.Lobby.Entities.LobbyState;

namespace GameKit.Lobby.Integration.Tests;

/// <summary>
/// CR-02 — <c>TryStartMatchmakingAsync</c> must revert lobby state from <c>InGame</c> back to
/// <c>ReadyChecking</c> when party creation or member join fails, ensuring the lobby is never
/// permanently stranded in <c>InGame</c>.
/// <para>
/// The test injects a broken <see cref="IPartyService"/> stub (via the <c>serviceOverrides</c>
/// callback on <see cref="LobbyTestApp.StartAsync"/>) that always throws on
/// <see cref="IPartyService.CreateAsync"/>. When all members mark ready, the all-ready transaction
/// transitions to <c>InGame</c>, then <c>TryStartMatchmakingAsync</c> fails, and the revert path
/// must restore the lobby row to <c>ReadyChecking</c>.
/// </para>
/// </summary>
[Collection("Lobby")]
[Trait("Category", "Integration")]
public sealed class MatchmakingRevertTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private LobbyTestApp _app = default!;

    /// <summary>Constructs the test class.</summary>
    public MatchmakingRevertTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _app = new LobbyTestApp();
        await _app.StartAsync(_pg, _redis, services =>
        {
            // Replace the scoped IPartyService with a stub that always throws, simulating
            // a party creation failure in TryStartMatchmakingAsync (CR-02).
            var existing = services.FindDescriptor(typeof(IPartyService));
            if (existing is not null) services.Remove(existing);
            services.AddScoped<IPartyService, AlwaysThrowingPartyService>();
        });
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    [Fact(DisplayName = "CR-02: lobby reverts to ReadyChecking when party creation fails — not stuck InGame")]
    public async Task AllReady_PartyCreationFails_LobbyRevertsToReadyChecking()
    {
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        _app.EnsurePlayerRow(playerA);
        _app.EnsurePlayerRow(playerB);

        // Seed a lobby in ReadyChecking with both players.
        var lobbyId = await _app.SeedLobbyAsync(new[] { playerA, playerB }, _app.TestLadderId);

        var connA = _app.ConnectLobbyHubAsync(playerA);
        var connB = _app.ConnectLobbyHubAsync(playerB);

        // Capture any state update (InGame or ReadyChecking) broadcast.
        var updateTcs = new TaskCompletionSource<LobbyState>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connA.On<GameKit.Lobby.Hubs.LobbyStateUpdate>("ReceiveStateUpdateAsync", update =>
            updateTcs.TrySetResult(update.State));
        connB.On<GameKit.Lobby.Hubs.LobbyStateUpdate>("ReceiveStateUpdateAsync", update =>
            updateTcs.TrySetResult(update.State));

        try
        {
            await connA.StartAsync();
            await connB.StartAsync();

            await connA.InvokeAsync("JoinLobbyAsync", lobbyId);
            await connB.InvokeAsync("JoinLobbyAsync", lobbyId);

            // Both players mark ready.  The second MarkReady fires TryStartMatchmakingAsync
            // which will throw via AlwaysThrowingPartyService and must revert InGame.
            await connA.InvokeAsync("MarkReadyAsync", lobbyId);
            await connB.InvokeAsync("MarkReadyAsync", lobbyId);

            // Wait briefly for the async revert to complete — MarkReadyAsync awaits
            // the revert before returning.
            await Task.Delay(TimeSpan.FromSeconds(2));

            // The lobby row in Postgres must be ReadyChecking (1), NOT InGame (3).
            var dbState = await GetLobbyStateAsync(_app.ConnectionString, lobbyId);
            Assert.True(
                dbState == (int)LobbyState.ReadyChecking,
                $"Expected lobby {lobbyId} to be in ReadyChecking (state 1) after party creation failure, " +
                $"but found state {dbState}. The lobby must NOT be permanently stranded in InGame (CR-02).");
        }
        finally
        {
            await connA.StopAsync();
            await connB.StopAsync();
            await connA.DisposeAsync();
            await connB.DisposeAsync();
        }
    }

    // ---- helpers ----

    private static async Task<int> GetLobbyStateAsync(string cs, Guid lobbyId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""State"" FROM gamekit.lobbies WHERE ""Id"" = @id";
        cmd.Parameters.AddWithValue("id", lobbyId);
        var result = await cmd.ExecuteScalarAsync();
        return result is null ? -1 : Convert.ToInt32(result);
    }

    /// <summary>
    /// Stub <see cref="IPartyService"/> that always throws <see cref="InvalidOperationException"/>
    /// on <see cref="CreateAsync"/> to simulate a matchmaking-path failure for CR-02 testing.
    /// </summary>
    private sealed class AlwaysThrowingPartyService : IPartyService
    {
        /// <inheritdoc />
        public Task<Party> CreateAsync(Guid ownerPlayerId, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated party creation failure (CR-02 test).");

        /// <inheritdoc />
        public Task<Party> JoinAsync(string code, Guid playerId, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated party join failure (CR-02 test).");

        /// <inheritdoc />
        public Task DissolveAsync(Guid partyId, Guid actorPlayerId, CancellationToken ct = default)
            => Task.CompletedTask;

        /// <inheritdoc />
        public Task<Party?> GetByCodeAsync(string code, CancellationToken ct = default)
            => Task.FromResult<Party?>(null);
    }
}

/// <summary>Extension helpers used by Lobby integration tests.</summary>
internal static class ServiceCollectionExtensions
{
    /// <summary>
    /// Finds the first <see cref="ServiceDescriptor"/> whose
    /// <see cref="ServiceDescriptor.ServiceType"/> matches <paramref name="serviceType"/>,
    /// or <see langword="null"/> if none exists.
    /// </summary>
    internal static ServiceDescriptor? FindDescriptor(
        this IServiceCollection services, Type serviceType)
    {
        foreach (var d in services)
            if (d.ServiceType == serviceType) return d;
        return null;
    }
}
