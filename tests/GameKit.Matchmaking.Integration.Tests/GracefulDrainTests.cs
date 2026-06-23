// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.TestFixtures;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// SCALE-05 graceful-drain CI gate. Fires 100 concurrent in-flight HTTP matchmaking
/// requests, stops the host mid-flight, and asserts:
/// <list type="number">
///   <item><description>Zero HTTP 5xx responses — ASP.NET Core drains in-flight requests before stop.</description></item>
///   <item><description>The matchmaker leader lock key is absent in Redis after stop, proving the
///   <c>CancellationToken.None</c> lease release fix from plan 16-03 fires proactively
///   rather than waiting for the TTL to expire.</description></item>
///   <item><description>Zero duplicate <c>game_sessions</c> rows result from the request storm.</description></item>
/// </list>
/// Demonstrates rolling-deploy safety: in-flight requests complete with no server errors
/// and the surviving replica can acquire the leader lock immediately after stop (not after
/// up to 90 s TTL expiry).
/// </summary>
/// <remarks>
/// <para>
/// Requests that race the host shutdown may fail with a client-side
/// <see cref="HttpRequestException"/> or <see cref="TaskCanceledException"/> — these are
/// connection-refused / connection-reset events, not server 5xx errors. The test catches
/// these and treats them as acceptable. Any response object carrying a 5xx status code
/// is a test failure.
/// </para>
/// <para>
/// The lock-absent assertion is the end-to-end proof of SCALE-02 (16-03). If the
/// <c>CancellationToken.None</c> fix were absent, the lock would sit in Redis for up to
/// the full <c>LockTtlSeconds</c> (90 s default) after stop — this test would observe a
/// non-empty <see cref="MatchmakingTestApp.MatcherLockKey"/> value and fail.
/// </para>
/// </remarks>
[Collection("Matchmaking")]
[Trait("Category", "GracefulDrain")]
public sealed class GracefulDrainTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp _app = default!;

    /// <summary>Constructs the test class with collection-injected fixtures.</summary>
    public GracefulDrainTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Start with the default lock TTL (90 s) — the test does not rely on a short TTL.
        // The point is to prove the lock is released proactively via CancellationToken.None,
        // not via TTL expiry. If the fix is absent the assertion will fail because the key
        // remains in Redis after stop (instead of being absent).
        _app = new MatchmakingTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        // DisposeAsync calls StopAsync internally; this is a no-op if StopHostAsync was
        // already called in the test body (IHost.StopAsync is idempotent).
        await _app.DisposeAsync();
    }

    /// <summary>
    /// SCALE-05: 100 concurrent in-flight matchmaking requests + host stop →
    /// zero 5xx responses, leader lock absent in Redis, zero duplicate game_sessions.
    /// </summary>
    [Fact(DisplayName = "SCALE-05: 100 concurrent requests + host stop → zero 5xx, lease released, zero duplicate matches")]
    public async Task GracefulDrain_NoFiveXx_LeaseReleased_NoDuplicateSessions()
    {
        // --- Arrange: 100 unique players (one request each) ----------------------
        // Use distinct player ids so the per-player rate-limit (5/min) is never hit.
        // Each request is an enqueue into the default pool with no PoolName override
        // (TicTacToeDuel only pairs tickets in the "default" pool — memory note).
        const int requestCount = 100;

        var playerIds = Enumerable.Range(0, requestCount)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        // Pre-create player rows so enqueue FK constraints are satisfied even for
        // requests that arrive after the host stops accepting new work.
        foreach (var pid in playerIds)
            _app.EnsurePlayerRow(pid);

        // --- Act: kick off 100 concurrent requests, then stop the host ----------
        // Build all tasks WITHOUT awaiting — they start immediately. Use the shared
        // _app.Client (thread-safe HttpClient) rather than creating 100 clients to
        // avoid flooding the connection pool.
        var requestTasks = Enumerable.Range(0, requestCount)
            .Select(i => SendEnqueueRequestAsync(playerIds[i]))
            .ToArray();

        // Trigger graceful host shutdown while the 100 requests are in flight.
        // ASP.NET Core Kestrel drains in-flight requests up to ShutdownTimeout (5 s
        // default) before stopping — this is the drain window under test.
        await _app.StopHostAsync();

        // Collect all outcomes (responses or client-side exceptions).
        var outcomes = await Task.WhenAll(requestTasks.Select(t => t
            .ContinueWith(completed => completed, TaskContinuationOptions.ExecuteSynchronously)));

        // --- Assert 1: zero 5xx responses ----------------------------------------
        // Requests that lost the connection race throw HttpRequestException or
        // TaskCanceledException — those are acceptable (not a server 5xx). Only
        // response objects with a 5xx status code are failures.
        var fiveXxResponses = outcomes
            .Where(t => t.IsCompletedSuccessfully && t.Result is not null)
            .Select(t => t.Result)
            .Where(r => (int)r!.StatusCode >= 500)
            .ToList();

        Assert.True(
            fiveXxResponses.Count == 0,
            $"SCALE-05 FAIL: {fiveXxResponses.Count} HTTP 5xx response(s) observed during graceful drain. " +
            $"Status codes: {string.Join(", ", fiveXxResponses.Select(r => (int)r!.StatusCode))}. " +
            "ASP.NET Core should drain in-flight requests before stop — no 5xx should escape.");

        // --- Assert 2: leader lock key absent in Redis after stop ----------------
        // Connect a fresh multiplexer (the host's own multiplexer is now disposed).
        // The key should be absent because MatchmakerTickerService.RunOnceAsync releases
        // the lease with CancellationToken.None in its finally block — SCALE-02 fix.
        // If the fix were absent, the key would linger for up to 90 s (the lock TTL).
        await using var freshMux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
        var db = freshMux.GetDatabase();
        var lockValue = await db.StringGetAsync(_app.MatcherLockKey);

        Assert.True(
            lockValue.IsNullOrEmpty,
            $"SCALE-05 / SCALE-02 FAIL: matcher lock key '{_app.MatcherLockKey}' is still present " +
            $"in Redis after host stop (value: '{lockValue}'). " +
            "Expected the lease to be released proactively via CancellationToken.None on the " +
            "MatchmakerTickerService finally path (plan 16-03 fix). " +
            "If the key has a value, the CancellationToken.None fix is not effective — " +
            "the lock will be held for up to LockTtlSeconds preventing leader re-election.");

        // --- Assert 3: zero duplicate game_sessions rows -------------------------
        // Even if multiple enqueue requests landed and triggered a match formation,
        // the ON CONFLICT DO NOTHING idempotency guard (SCALE-03) must prevent duplicates.
        var duplicateCount = await CountDuplicateGameSessionsAsync(_app.ConnectionString);
        Assert.True(
            duplicateCount == 0,
            $"SCALE-05 / SCALE-03 FAIL: {duplicateCount} IdempotencyKey value(s) appear in " +
            "more than one game_sessions row. " +
            "The ON CONFLICT DO NOTHING guard on game_sessions should prevent duplicate rows " +
            "even under concurrent match formation during drain.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends a single <c>POST /api/mm/queue</c> request for the given player and
    /// returns the <see cref="HttpResponseMessage"/>, or <see langword="null"/> when a
    /// client-side connection error occurs (request lost the connection race with shutdown).
    /// Client-side errors (<see cref="HttpRequestException"/>, <see cref="TaskCanceledException"/>)
    /// are caught and treated as acceptable non-5xx outcomes — they are not server errors.
    /// </summary>
    /// <param name="playerId">Player to enqueue.</param>
    /// <returns>The HTTP response, or <see langword="null"/> on client-side connection failure.</returns>
    private async Task<HttpResponseMessage?> SendEnqueueRequestAsync(Guid playerId)
    {
        try
        {
            // Mint a JWT for this player and send the enqueue request.
            // Using _app.Client (shared TestServer client) rather than CreateClient so we
            // do not flood with 100 separate HttpClient instances. Authorization header
            // is set per-request via the request message to avoid shared-header mutation.
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/mm/queue");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", _app.MintPlayerJwt(playerId));
            request.Content = JsonContent.Create(
                new EnqueueRequest(_app.TestLadderId, PoolName: null));

            return await _app.Client.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            // Connection refused or reset — the request lost the drain race.
            // This is not a server 5xx; treat it as an acceptable outcome.
            return null;
        }
        catch (TaskCanceledException)
        {
            // Client-side timeout or cancellation — also not a server 5xx.
            return null;
        }
        catch (OperationCanceledException)
        {
            // Cancellation during shutdown — not a server 5xx.
            return null;
        }
    }

    /// <summary>
    /// Counts the number of <c>IdempotencyKey</c> values that appear in more than one
    /// <c>game_sessions</c> row. Zero means no duplicates — the idempotency guard worked.
    /// Non-null <c>IdempotencyKey</c> values are the ones subject to the partial unique index
    /// (<c>WHERE "IdempotencyKey" IS NOT NULL</c>) — only those can be duplicates.
    /// </summary>
    /// <param name="connectionString">Postgres connection string for the test database.</param>
    /// <returns>Count of duplicated IdempotencyKey values (0 = no duplicates).</returns>
    private static async Task<int> CountDuplicateGameSessionsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();

        // Count distinct IdempotencyKey values that appear more than once.
        // A non-zero result means the ON CONFLICT DO NOTHING guard failed.
        cmd.CommandText =
            @"SELECT COUNT(*)::int
              FROM (
                  SELECT ""IdempotencyKey""
                  FROM gamekit.game_sessions
                  WHERE ""IdempotencyKey"" IS NOT NULL
                  GROUP BY ""IdempotencyKey""
                  HAVING COUNT(*) > 1
              ) AS duplicates";

        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : Convert.ToInt32(result);
    }
}
