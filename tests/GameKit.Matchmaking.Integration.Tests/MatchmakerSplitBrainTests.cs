// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking;
using GameKit.Matchmaking.Integration.Tests.TestDoubles;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// SCALE-04 + SCALE-03 — CI gate: two-replica split-brain test and concurrent
/// session-creation idempotency verification. Two <see cref="MatchmakingTestApp"/>
/// instances share a single Testcontainers Postgres database and Redis, simulating two
/// replicas competing for the leader lease. The <see cref="IChaosInterceptor"/> seam
/// pauses Replica A's ticker mid-claim past the lock TTL, allowing Replica B to acquire
/// the lease and form the match — asserts zero duplicate <c>game_sessions</c> rows.
/// A second fact directly validates the <c>ON CONFLICT DO NOTHING</c> idempotency guard
/// against concurrent same-key session creation.
/// </summary>
/// <remarks>
/// <para>
/// The split-brain scenario (SCALE-04): Replica A acquires the leader lock (lockTtlSeconds=2),
/// then stalls for 3 000 ms in <see cref="IChaosInterceptor.BeforeLuaClaim"/> via
/// <see cref="DelayingChaosInterceptor"/>. The 2 s TTL expires; Replica B acquires the lock,
/// runs its tick, forms the match, and writes the <c>game_sessions</c> row. Replica A's
/// <see cref="Services.AtomicClaimScript"/> returns <c>LEASE_LOST</c> because the Redis
/// lock value no longer matches Replica A's <c>InstanceId</c> — no duplicate row is created.
/// </para>
/// <para>
/// The idempotency scenario (SCALE-03): two concurrent Postgres INSERT operations for the
/// same <c>IdempotencyKey</c> using <c>ON CONFLICT DO NOTHING</c>. Exactly one row is
/// inserted; the other is a no-op (zero rows affected). This directly validates the
/// secondary Postgres-level guard documented in RESEARCH §Idempotency Design.
/// </para>
/// </remarks>
[Collection("Matchmaking")]
[Trait("Category", "SplitBrain")]
public sealed class MatchmakerSplitBrainTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp _appA = default!;
    private MatchmakingTestApp _appB = default!;

    /// <summary>Constructs the test class with collection-injected fixtures.</summary>
    public MatchmakerSplitBrainTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Apply migrations ONCE via AppA's startup (Pitfall 3 from RESEARCH).
        // Both apps share the SAME Postgres database so game_sessions rows are visible to
        // both replicas — required to detect duplicate rows in the split-brain test.
        //
        // AppA gets a DelayingChaosInterceptor on BeforeLuaClaim so its ticker pauses for
        // 3 000 ms > 2 s TTL, allowing AppB to acquire the leader lease and form the match.
        //
        // IMPORTANT: both apps set TickIntervalMs to a very long value (1 hour) so the
        // background PeriodicTimer never fires spontaneously during the test. The test
        // drives explicit RunOnceAsync calls instead. Without this, the background tickers
        // compete for the lock and may process tickets before the staged explicit ticks,
        // making the split-brain scenario non-deterministic (WR-01 fix).
        _appA = new MatchmakingTestApp(lockTtlSeconds: 2);
        await _appA.StartAsync(_pg, _redis, serviceOverrides: services =>
        {
            services.RemoveAll<IChaosInterceptor>();
            // 3 000 ms delay > 2 s TTL → Redis lock expires while AppA holds BeforeLuaClaim.
            services.AddSingleton<IChaosInterceptor>(
                new DelayingChaosInterceptor(delayMs: 3000, delayLuaClaim: true));
            // Suppress background periodic ticks so only explicit RunOnceAsync calls run.
            services.PostConfigure<GameKitMatchmakingOptions>(o => o.Ticker.TickIntervalMs = 3_600_000);
        });

        // AppB shares AppA's connection string — same DB, no second migration run.
        _appB = new MatchmakingTestApp(lockTtlSeconds: 2);
        await _appB.StartAsync(_pg, _redis, connectionString: _appA.ConnectionString,
            serviceOverrides: services =>
            {
                // Same long interval for AppB's background ticker.
                services.PostConfigure<GameKitMatchmakingOptions>(o => o.Ticker.TickIntervalMs = 3_600_000);
            });
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _appA.DisposeAsync();
        await _appB.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // SCALE-04 — split-brain: zero duplicate game_sessions under leader churn
    // -------------------------------------------------------------------------

    /// <summary>
    /// SCALE-04 CI gate. Two replicas share one Redis and one Postgres. Replica A acquires
    /// the leader lock (TTL = 2 s), then stalls for 3 000 ms in
    /// <see cref="IChaosInterceptor.BeforeLuaClaim"/> via <see cref="DelayingChaosInterceptor"/>.
    /// After waiting <c>lockTtlSeconds + 500 ms</c> (2 500 ms) for AppA to hold the lock and
    /// the TTL to expire, Replica B's tick is staged — so B is GUARANTEED to win the lock
    /// rather than vacuously returning <see cref="MatcherTickResult.LockNotAcquired"/>.
    /// B forms the match and writes one <c>game_sessions</c> row. A's Lua atomic-claim then
    /// returns <c>LEASE_LOST</c> — no duplicate row.
    /// <para>
    /// Non-vacuous precondition: asserts <c>matchedCount &gt;= 1</c> (at least one replica
    /// formed a match). Primary assertion: <c>sessionCount &lt;= 1</c> (no duplicates).
    /// </para>
    /// </summary>
    [Fact(DisplayName = "SCALE-04: zero duplicate game_sessions rows under leader churn")]
    public async Task SplitBrain_NoDuplicateSessions()
    {
        // The lock TTL is 2 s (set in InitializeAsync). AppA stalls 3 s at BeforeLuaClaim.
        // Staging: start AppA's tick first so it acquires the lock. After lockTtlSeconds +
        // 500 ms buffer the TTL has expired and AppB can acquire the lock and form the match.
        // This removes the vacuous-pass scenario where AppB returned LockNotAcquired before
        // AppA's TTL expired (sessionCount=0, matchedCount=0 both ≤ 1 but prove nothing).
        const int lockTtlSeconds = 2;
        const int stagingDelayMs = lockTtlSeconds * 1000 + 500; // 2 500 ms

        // --- Arrange: seed tickets in Redis -------------------------------------
        var muxOpts = ConfigurationOptions.Parse(_redis.ConnectionString);
        muxOpts.AllowAdmin = true;
        await using var adminMux = await ConnectionMultiplexer.ConnectAsync(muxOpts);
        var db = adminMux.GetDatabase();

        // Use AppA's TestLadderId so both tickers find the tickets via
        // server.Keys(pattern: "mm:queue:*:default") — both replicas share the same Redis.
        var ladderId = _appA.TestLadderId;
        const string pool = "default";
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, pool);

        var player1 = await IntegrationTestHelpers.SeedPlayerAsync(_appA.ConnectionString);
        var player2 = await IntegrationTestHelpers.SeedPlayerAsync(_appA.ConnectionString);
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();
        var queuedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Seed Postgres ticket rows (keeps the DB consistent; the ticker reads from Redis).
        await IntegrationTestHelpers.SeedTicketAsync(
            _appA.ConnectionString, ladderId, GameKit.Matchmaking.Entities.TicketStatus.Queued,
            DateTimeOffset.UtcNow.AddMilliseconds(-1));
        await IntegrationTestHelpers.SeedTicketAsync(
            _appA.ConnectionString, ladderId, GameKit.Matchmaking.Entities.TicketStatus.Queued,
            DateTimeOffset.UtcNow.AddMilliseconds(-2));

        // Seed Redis ticket hashes + sorted-set entries (ticker reads from both).
        await SeedRedisTicketAsync(db, ticket1, ladderId, pool, player1, queuedAtMs);
        await SeedRedisTicketAsync(db, ticket2, ladderId, pool, player2, queuedAtMs + 1);
        await db.SortedSetAddAsync(queueKey, ticket1.ToString(), queuedAtMs);
        await db.SortedSetAddAsync(queueKey, ticket2.ToString(), queuedAtMs + 1);

        // --- Act: staged execution — A first, then B after the TTL expires -----
        // 1. Start AppA's tick. Because AppA has DelayingChaosInterceptor(delayMs:3000,
        //    delayLuaClaim:true), AppA will acquire the lock and immediately stall 3 s
        //    at BeforeLuaClaim. The 2 s TTL will expire during the stall.
        // 2. Wait stagingDelayMs (2 500 ms) — enough for AppA to hold the lock AND for
        //    the 2 s TTL to expire, but not long enough for AppA to finish its 3 s delay.
        // 3. Start AppB's tick. The lock is now expired; AppB acquires it, forms the match,
        //    and writes one game_sessions row.
        // 4. Await both tasks together.
        var tickerA = _appA.GetTicker();
        var tickerB = _appB.GetTicker();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Start AppA's tick; do NOT await — it is intentionally stalling for 3 s.
        var taskA = tickerA.RunOnceAsync(cts.Token);

        // Wait for the lock TTL to expire while AppA holds the lock but is stalled.
        await Task.Delay(stagingDelayMs, cts.Token);

        // AppA's TTL has expired. Start AppB's tick now — it will acquire the free lock.
        var taskB = tickerB.RunOnceAsync(cts.Token);

        var resultA = await taskA;
        var resultB = await taskB;

        // --- Assert: the race was non-vacuous AND produced no duplicates ---------
        var sessionCount = await CountGameSessionsAsync(_appA.ConnectionString, ladderId);
        var matchedCount = new[] { resultA, resultB }.Count(r => r == MatcherTickResult.Matched);

        // Non-vacuous precondition: at least one replica must have formed a match.
        // If matchedCount == 0, the test setup is broken (tickets not visible to either
        // ticker, or the staging delay was insufficient for the TTL to expire).
        Assert.True(matchedCount >= 1,
            $"SCALE-04 PRECONDITION FAIL: no replica formed a match (matchedCount=0). " +
            $"TickerA={resultA}, TickerB={resultB}. " +
            "Ensure the staging delay ({stagingDelayMs} ms) exceeds lockTtlSeconds ({lockTtlSeconds} s) " +
            "so AppB can acquire the lock after AppA's TTL expires.");

        // Primary SCALE-04 assertion: zero duplicate game_sessions rows.
        Assert.True(sessionCount <= 1,
            $"SCALE-04 FAIL: {sessionCount} game_sessions row(s) found for ladderId={ladderId} — " +
            "expected at most 1. ON CONFLICT DO NOTHING + LEASE_LOST guard should prevent duplicates. " +
            $"TickerA={resultA}, TickerB={resultB}");

        // Secondary assertion: the split-brain guard allows at most one successful match.
        Assert.True(matchedCount <= 1,
            $"SCALE-04 FAIL: both replicas returned Matched (matchedCount={matchedCount}). " +
            $"TickerA={resultA}, TickerB={resultB}");
    }

    // -------------------------------------------------------------------------
    // SCALE-03 — idempotency: concurrent same-key formation → exactly one row
    // -------------------------------------------------------------------------

    /// <summary>
    /// SCALE-03 idempotency verification. Drives two concurrent Postgres INSERT operations
    /// for the same <c>IdempotencyKey</c> using <c>ON CONFLICT ("IdempotencyKey")
    /// WHERE "IdempotencyKey" IS NOT NULL DO NOTHING</c>. Exactly one row must be inserted;
    /// the concurrent duplicate must produce zero affected rows — proving the partial unique
    /// index and the idempotent INSERT path in <c>ProposalService.CreateSessionAsync</c>
    /// prevent duplicate <c>game_sessions</c> rows even when two replicas both reach the
    /// write path for the same proposal.
    /// </summary>
    [Fact(DisplayName = "SCALE-03: concurrent session-creation for same IdempotencyKey → exactly one row")]
    [Trait("Category", "Idempotency")]
    public async Task ConcurrentSessionCreate_SameIdempotencyKey_ExactlyOneRow()
    {
        var proposalId = Guid.NewGuid();
        var idempotencyKey = proposalId.ToString();
        var sessionId1 = Guid.NewGuid();
        var sessionId2 = Guid.NewGuid();
        var ladderId = _appA.TestLadderId;
        var now = DateTimeOffset.UtcNow;

        // Use a SemaphoreSlim to force both inserts to start simultaneously so they race
        // for the unique index slot, maximising the chance of a conflict being detected.
        using var gate = new SemaphoreSlim(0, 2);

        var insert1Task = InsertSessionIdempotentAsync(
            _appA.ConnectionString, sessionId1, ladderId, idempotencyKey, now, gate);
        var insert2Task = InsertSessionIdempotentAsync(
            _appA.ConnectionString, sessionId2, ladderId, idempotencyKey, now, gate);

        // Release both tasks simultaneously so they race.
        gate.Release(2);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var rows1 = await insert1Task.WaitAsync(cts.Token);
        var rows2 = await insert2Task.WaitAsync(cts.Token);

        // Exactly one insert must have succeeded; the other must return 0.
        Assert.True(rows1 + rows2 == 1,
            $"SCALE-03 FAIL: expected rows1+rows2 == 1 (exactly one insert). " +
            $"Got rows1={rows1}, rows2={rows2}. " +
            "Both inserts succeeded — the partial unique index or ON CONFLICT DO NOTHING guard may be missing.");

        // Confirm exactly one row exists in the DB.
        var count = await CountGameSessionsByIdempotencyKeyAsync(_appA.ConnectionString, idempotencyKey);
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Seeds a Redis ticket hash mirroring <c>MatchmakingService.EnqueueAsync</c>'s shape,
    /// including the <c>members</c> JSON field that the ticker uses to rebuild
    /// <see cref="QueuedParty.Members"/> for team assignment.
    /// </summary>
    private static async Task SeedRedisTicketAsync(
        IDatabase db, Guid ticketId, Guid ladderId, string poolName, Guid playerId, long queuedAtMs)
    {
        var members = new[]
        {
            new { PlayerId = playerId, Rating = 1500.0, RatingDeviation = 350.0, Volatility = 0.06 },
        };
        await db.HashSetAsync(
            MatchmakingRedisKeys.Ticket(ticketId),
            [
                new HashEntry("status", "queued"),
                new HashEntry("ladderId", ladderId.ToString()),
                new HashEntry("poolName", poolName),
                new HashEntry("queuedAt", queuedAtMs.ToString(CultureInfo.InvariantCulture)),
                new HashEntry("aggregateRating", "1500"),
                new HashEntry("partyId", string.Empty),
                new HashEntry("playerId", playerId.ToString()),
                new HashEntry("members", JsonSerializer.Serialize(members)),
            ]);
    }

    /// <summary>
    /// Counts <c>game_sessions</c> rows for the given ladder id. Used to verify that no
    /// duplicate rows were created for a contested proposal under leader churn (SCALE-04).
    /// </summary>
    private static async Task<int> CountGameSessionsAsync(string cs, Guid ladderId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*)::int FROM gamekit.game_sessions WHERE ""LadderId"" = @ladderId";
        cmd.Parameters.AddWithValue("ladderId", ladderId);
        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : Convert.ToInt32(result);
    }

    /// <summary>
    /// Counts <c>game_sessions</c> rows matching the given idempotency key. Used to verify
    /// the idempotent insert path produces exactly one row (SCALE-03).
    /// </summary>
    private static async Task<int> CountGameSessionsByIdempotencyKeyAsync(string cs, string idempotencyKey)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*)::int FROM gamekit.game_sessions WHERE ""IdempotencyKey"" = @key";
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        var result = await cmd.ExecuteScalarAsync();
        return result is int i ? i : Convert.ToInt32(result);
    }

    /// <summary>
    /// Executes the idempotent <c>game_sessions</c> INSERT with <c>ON CONFLICT
    /// ("IdempotencyKey") WHERE "IdempotencyKey" IS NOT NULL DO NOTHING</c>. Waits on the
    /// <paramref name="gate"/> semaphore before executing so two concurrent calls can be
    /// released simultaneously to maximise race coverage. Returns the number of rows
    /// inserted (1 = success, 0 = idempotent no-op).
    /// </summary>
    private static async Task<int> InsertSessionIdempotentAsync(
        string cs, Guid sessionId, Guid ladderId, string idempotencyKey,
        DateTimeOffset now, SemaphoreSlim gate)
    {
        // Wait for the gate before executing so both tasks start simultaneously.
        await gate.WaitAsync();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            @"INSERT INTO gamekit.game_sessions
                  (""Id"", ""State"", ""LadderId"", ""IdempotencyKey"", ""CreatedAt"", ""StartedAt"")
              VALUES (@id, 'Active', @ladderId, @idempotencyKey, @createdAt, @startedAt)
              ON CONFLICT (""IdempotencyKey"") WHERE ""IdempotencyKey"" IS NOT NULL DO NOTHING";
        cmd.Parameters.AddWithValue("id", sessionId);
        cmd.Parameters.AddWithValue("ladderId", ladderId);
        cmd.Parameters.AddWithValue("idempotencyKey", idempotencyKey);
        cmd.Parameters.AddWithValue("createdAt", now);
        cmd.Parameters.AddWithValue("startedAt", now);
        return await cmd.ExecuteNonQueryAsync();
    }
}
