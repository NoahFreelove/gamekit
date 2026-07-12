// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Cross-package Testcontainers integration tests for the participation-fraction rating
/// guard (MATCH-19 SC#4). Proves that a session participant whose
/// <c>session_participants.ParticipationFraction</c> is below the ladder's configured
/// <c>MinParticipationFractionForRating</c> threshold receives no <c>PendingRatingUpdate</c>
/// row — the guard in <c>PendingRatingUpdatesAdapter.OnCompletedAsync</c> fires, skipping
/// the INSERT. Wave 2 — Plan 09-04.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class BackfillParticipationTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;
    private ConnectionMultiplexer? _mux;

    /// <summary>Constructs the test with injected fixtures.</summary>
    public BackfillParticipationTests(PostgresFixture pg, RedisFixture redis)
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
    /// SC#4: A session participant with ParticipationFraction below MinParticipationFractionForRating
    /// receives no PendingRatingUpdate row — the guard fires and skips the rating change.
    /// Also verifies the positive control: a participant with ParticipationFraction above the
    /// threshold receives exactly one PendingRatingUpdate row.
    /// Wave 2 — Plan 09-04.
    /// </summary>
    [Fact]
    public async Task SC4_ParticipationFractionBelowMinimum_SkipsRatingChange()
    {
        var cs = _app!.ConnectionString;

        // -----------------------------------------------------------------------
        // Arrange: seed a ladder with MinParticipationFractionForRating = 0.5
        // Use raw SQL to inject the JSONB directly (mirrors IntegrationTestHelpers.SeedLadderAsync
        // but with real config content so the adapter can read it via ReadMinParticipationFraction).
        // -----------------------------------------------------------------------
        var ladderId = await SeedLadderWithMinFractionAsync(cs, "participation-guard-test", 0.5);

        // -----------------------------------------------------------------------
        // Below-threshold case: ParticipationFraction = 0.3 < 0.5 min → guard fires → 0 rows
        // -----------------------------------------------------------------------
        var belowPlayerId = Guid.NewGuid();
        _app.EnsurePlayerRow(belowPlayerId);
        await SeedPlayerRankAsync(cs, ladderId, belowPlayerId);
        var belowSessionId = await SeedActiveGameSessionWithLadderAsync(cs, ladderId);
        await SeedSessionParticipantAsync(cs, belowSessionId, belowPlayerId, participationFraction: 0.3);

        await InvokeAdapterAsync(
            cs,
            belowSessionId,
            new SessionParticipantSnapshot(belowPlayerId, ladderId, SessionResult.Win, null));

        // Assert: no PendingRatingUpdate row for the below-threshold player.
        var belowCount = await CountPendingRatingUpdatesAsync(cs, belowPlayerId, belowSessionId);
        Assert.Equal(0, belowCount);

        // -----------------------------------------------------------------------
        // Positive control: ParticipationFraction = 0.8 > 0.5 min → guard skips → 1 row
        // -----------------------------------------------------------------------
        var abovePlayerId = Guid.NewGuid();
        _app.EnsurePlayerRow(abovePlayerId);
        await SeedPlayerRankAsync(cs, ladderId, abovePlayerId);
        var aboveSessionId = await SeedActiveGameSessionWithLadderAsync(cs, ladderId);
        await SeedSessionParticipantAsync(cs, aboveSessionId, abovePlayerId, participationFraction: 0.8);

        await InvokeAdapterAsync(
            cs,
            aboveSessionId,
            new SessionParticipantSnapshot(abovePlayerId, ladderId, SessionResult.Win, null));

        // Assert: exactly one PendingRatingUpdate row for the above-threshold player.
        var aboveCount = await CountPendingRatingUpdatesAsync(cs, abovePlayerId, aboveSessionId);
        Assert.Equal(1, aboveCount);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Seeds a <c>session_participants</c> row with the given <c>ParticipationFraction</c> value
    /// using a raw NpgsqlCommand INSERT (follows IntegrationTestHelpers convention).
    /// </summary>
    /// <param name="cs">Postgres connection string.</param>
    /// <param name="sessionId">Session FK for the participant row.</param>
    /// <param name="playerId">Player FK for the participant row.</param>
    /// <param name="participationFraction">Fraction [0.0–1.0] or null.</param>
    /// <returns>The new participant row id.</returns>
    public static async Task<Guid> SeedSessionParticipantAsync(
        string cs, Guid sessionId, Guid playerId, double? participationFraction)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.session_participants
            (""Id"", ""SessionId"", ""PlayerId"", ""Team"", ""Result"", ""Score"",
             ""RatingBefore"", ""RatingAfter"", ""RatingDelta"", ""ParticipationFraction"")
            VALUES (@id, @session, @player, 0, NULL, NULL, NULL, NULL, NULL, @fraction)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("session", sessionId);
        cmd.Parameters.AddWithValue("player", playerId);
        cmd.Parameters.AddWithValue("fraction", (object?)participationFraction ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Seeds a ladder row whose JSONB Config contains
    /// <c>MinParticipationFractionForRating</c> so the adapter guard can read it.
    /// </summary>
    private static async Task<Guid> SeedLadderWithMinFractionAsync(
        string cs, string name, double minParticipationFraction)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        // Inject the JSONB with the exact property name that ReadMinParticipationFraction reads.
        var jsonb = $"{{\"MinParticipationFractionForRating\": {minParticipationFraction.ToString(CultureInfo.InvariantCulture)}}}";
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, @n, 'glicko2', true, NOW(), @cfg::jsonb)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("n", name);
        cmd.Parameters.AddWithValue("cfg", jsonb);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Seeds a player_ranks row for a player/ladder so PendingRatingUpdatesAdapter
    /// can find an existing rank (required to build the PendingRatingUpdate row for
    /// the above-threshold positive control).
    /// </summary>
    private static async Task SeedPlayerRankAsync(string cs, Guid ladderId, Guid playerId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var rankId = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            INSERT INTO gamekit.player_ranks
                (""Id"", ""PlayerId"", ""LadderId"", ""Rating"", ""RatingDeviation"", ""Volatility"",
                 ""Wins"", ""Losses"", ""Draws"", ""IsInPlacement"", ""PlacementMatchesRemaining"")
            VALUES
                ('{rankId}', '{playerId}', '{ladderId}',
                 1500.0, 200.0, 0.06,
                 0, 0, 0, false, 0)";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Seeds an Active game session tied to a specific ladder id.
    /// </summary>
    private static async Task<Guid> SeedActiveGameSessionWithLadderAsync(string cs, Guid ladderId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        var id = Guid.NewGuid();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.game_sessions
            (""Id"", ""State"", ""LadderId"", ""CreatedAt"", ""StartedAt"", ""CompletedAt"", ""Metadata"")
            VALUES (@id, 'Active', @ladder, NOW(), NOW(), NULL, NULL)";
        cmd.Parameters.AddWithValue("id", id);
        cmd.Parameters.AddWithValue("ladder", ladderId);
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>
    /// Directly instantiates and invokes <c>PendingRatingUpdatesAdapter.OnCompletedAsync</c>
    /// using a <see cref="GameKitDbContext"/> built against the integration-test database with
    /// the <see cref="MatchmakingTestModelCustomizer"/> (mirrors IntegrationTestHelpers pattern).
    /// Wraps the call in a transaction that is committed, mirroring
    /// <c>SessionCompleteService.CompleteAsync</c> behaviour.
    /// </summary>
    private static async Task InvokeAdapterAsync(
        string cs,
        Guid sessionId,
        SessionParticipantSnapshot participant)
    {
        await using var ctx = IntegrationTestHelpers.BuildMatchmakingContext(cs);
        await using var tx = await ctx.Database.BeginTransactionAsync(CancellationToken.None);
        try
        {
            var clock = new SystemClock();
            var ids = new UuidV7IdGenerator();
            var adapter = new PendingRatingUpdatesAdapter(ctx, clock, ids);
            await adapter.OnCompletedAsync(
                sessionId,
                new List<SessionParticipantSnapshot> { participant },
                CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    /// <summary>
    /// Counts <c>pending_rating_updates</c> rows for a specific (PlayerId, SessionId) pair.
    /// </summary>
    private static async Task<int> CountPendingRatingUpdatesAsync(
        string cs, Guid playerId, Guid sessionId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.pending_rating_updates
            WHERE ""PlayerId"" = @p AND ""SessionId"" = @s";
        cmd.Parameters.AddWithValue("p", playerId);
        cmd.Parameters.AddWithValue("s", sessionId);
        var result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }
}
