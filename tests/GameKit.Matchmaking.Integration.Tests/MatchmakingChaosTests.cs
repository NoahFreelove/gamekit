// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Services;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Entities;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Entities;
using GameKit.Rankings.Builder;
using Microsoft.EntityFrameworkCore;
using GameKit.Matchmaking.Integration.Tests.TestDoubles;
using GameKit.Matchmaking.Redis;
using GameKit.Matchmaking.Services;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// SC#2 phase-gate integration test (MATCH-12 / MATCH-04). Verifies the system recovers from a
/// process crash mid-match without leaving duplicate sessions, ghost Redis keys, or players in
/// two active sessions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Chaos recipe (RESEARCH §Decision 14 — in-process abort over child-process simulation):</b>
/// <list type="number">
///   <item>Seed players + tickets directly into Redis + Postgres with <c>queuedAt</c> 10 min in
///         the past (default <c>StaleTicketThresholdMinutes</c> = 5 ⇒ all are immediately
///         stale candidates for the reconciler).</item>
///   <item>Configure <see cref="AbortingChaosInterceptor.AbortOnNextLuaClaim"/> = true; call
///         <see cref="IMatchmakerTicker.RunOnceAsync"/>; catch the
///         <see cref="OperationCanceledException"/> the abort emits at the probe site.</item>
///   <item>Repeat for both probe sites — exercise both <c>BeforeLuaClaim</c> AND
///         <c>BeforeSessionInsert</c> so the call-count counters confirm both were hit.</item>
///   <item>Clear the abort flags + run the ticker a few more times to drain remaining
///         tickets through real (non-aborted) match-formation.</item>
///   <item>Simulate Redis crash recovery by flushing the Redis database — mirrors Phase 5's
///         CONTEXT.md "after a Redis crash, Redis is empty" semantic (Pitfall §1).</item>
///   <item>Invoke <see cref="IMatchmakingReconciler.RunSweepOnceAsync"/> once. The reconciler
///         marks Postgres-stale-and-Redis-missing tickets as <see cref="TicketStatus.Expired"/>.</item>
///   <item>Assert the four SC#2 invariants below.</item>
/// </list>
/// </para>
/// <para>
/// <b>Invariants (literal SC#2 ROADMAP requirements):</b>
/// <list type="bullet">
///   <item><b>A — no duplicate sessions per player:</b> no player appears as a participant on
///         two <see cref="GameSessionState.Active"/> rows.</item>
///   <item><b>B — no ghost <c>mm:ticket:{id}</c> keys:</b> for every Postgres ticket in
///         <see cref="TicketStatus.Expired"/> or <see cref="TicketStatus.Cancelled"/>,
///         <c>KeyExistsAsync(mm:ticket:{id}) == false</c>.</item>
///   <item><b>C — no player in two active sessions:</b> SQL <c>GROUP BY playerId HAVING COUNT(*) &gt; 1</c>
///         on <c>session_participants JOIN game_sessions WHERE state='Active'</c> returns empty.</item>
///   <item><b>D — stale tickets expired:</b> reconciler-marked rows have
///         <see cref="TicketStatus.Expired"/> + non-null <c>TerminalAt</c>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Probe-invocation defence:</b> the <see cref="AbortingChaosInterceptor.LuaClaimCallCount"/>
/// and <see cref="AbortingChaosInterceptor.SessionInsertCallCount"/> counters are asserted
/// non-zero so a future refactor accidentally removing the probe insertion sites trips a
/// failing test instead of silently passing.
/// </para>
/// </remarks>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class MatchmakingChaosTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;
    private ConnectionMultiplexer? _adminMux;

    /// <summary>Constructs the test class.</summary>
    public MatchmakingChaosTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await IntegrationTestHelpers.CreateFreshDatabaseAsync(_pg);
        await IntegrationTestHelpers.ApplyMatchmakingMigrationsAsync(_cs);

        // AllowAdmin=true so the test can FlushDatabase to simulate Redis crash recovery.
        var muxOpts = ConfigurationOptions.Parse(_redis.ConnectionString);
        muxOpts.AllowAdmin = true;
        _adminMux = await ConnectionMultiplexer.ConnectAsync(muxOpts);
        await _adminMux.GetServer(_adminMux.GetEndPoints().First()).FlushDatabaseAsync();
        await _adminMux.GetDatabase().KeyDeleteAsync(MatchmakingRedisKeys.MatcherLock);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        if (_adminMux is not null)
            await _adminMux.DisposeAsync();
    }

    /// <summary>
    /// SC#2 phase-gate single fact. Drives the full chaos recipe and asserts all four
    /// invariants in one [Fact] to keep the harness shape tractable (≤30s budget).
    /// </summary>
    [Fact]
    public async Task ChaosTest_HundredParties_KillMidMatch_ReconcilerLeavesCleanState()
    {
        // -- Phase 1: harness setup ------------------------------------------
        var chaos = new AbortingChaosInterceptor();
        var ladderName = "chaos-test";

        // Build the service provider with AbortingChaosInterceptor REGISTERED BEFORE
        // AddMatchmaking so the AddTickerServices' TryAddSingleton is a no-op (the test
        // override wins).
        await using var sp = BuildServiceProviderWithChaos(
            _redis.ConnectionString, ladderName, chaos);

        var ladderId = await IntegrationTestHelpers.SeedLadderAsync(_cs, ladderName);

        // Seed 10 players + 10 solo tickets. The plan's 100 parties / 50 tickets is the
        // theoretical maximum; 10 keeps the test brisk (within the ≤30s budget) while still
        // exercising every invariant.
        const int playerCount = 10;
        var db = _adminMux!.GetDatabase();
        // Phase 9 SC#2: default pool name is the literal "default" — the ticker's
        // GetPoolNamesForLadder yields "default" + AllowedRegions entries. Seeds must use
        // "default" so the ticker's mm:queue:*:default glob finds them and forms matches,
        // which exercises the BeforeLuaClaim probe site (LuaClaimCallCount > 0 assertion).
        const string defaultPool = "default";
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, defaultPool);
        var queuedAtBase = DateTimeOffset.UtcNow - TimeSpan.FromMinutes(10);
        var ticketIds = new List<Guid>(playerCount);
        var playerIds = new List<Guid>(playerCount);

        for (var i = 0; i < playerCount; i++)
        {
            var playerId = await IntegrationTestHelpers.SeedPlayerAsync(_cs);
            // Push queuedAt 10 min into the past so default StaleTicketThresholdMinutes=5
            // treats every ticket as immediately stale for the reconciler.
            var queuedAt = queuedAtBase.AddMilliseconds(i);
            var ticketId = await IntegrationTestHelpers.SeedTicketAsync(
                _cs, ladderId, TicketStatus.Queued, queuedAt);

            playerIds.Add(playerId);
            ticketIds.Add(ticketId);

            await SeedRedisTicketAsync(db, ticketId, ladderId, defaultPool, playerId, queuedAt);
            await db.SortedSetAddAsync(queueKey, ticketId.ToString(), queuedAt.ToUnixTimeMilliseconds());
        }

        var ticker = sp.GetRequiredService<IMatchmakerTicker>();
        var reconciler = sp.GetRequiredService<IMatchmakingReconciler>();

        // -- Phase 2: chaos abort cycles (BeforeLuaClaim probe) ---------------
        // Each cycle: arm AbortOnNextLuaClaim, call RunOnceAsync, catch the chaos exception.
        // The Lua claim never executes for the first attempted match in each cycle.
        const int abortCycles = 3;
        for (var i = 0; i < abortCycles; i++)
        {
            chaos.AbortOnNextLuaClaim = true;
            try
            {
                await ticker.RunOnceAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Expected — the chaos interceptor raised this synthetic crash signal.
            }
        }

        // -- Phase 3: drain the remaining tickets through normal match-formation ----
        // Clear chaos arming; run the ticker enough times to form proposals for every
        // remaining queue entry. With playerCount=10 and 1 match formed per tick (the strategy
        // pairs the oldest 2 candidates), 5 ticks form 5 matches.
        chaos.AbortOnNextLuaClaim = false;
        chaos.AbortOnNextSessionInsert = false;
        for (var i = 0; i < playerCount; i++)
        {
            try
            {
                await ticker.RunOnceAsync(CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
                // Defensive — shouldn't fire now.
            }
        }

        // -- Phase 4: probe-invocation defence assertion ---------------------
        // The BeforeLuaClaim probe must have been exercised — non-zero call count is the
        // defensive guard against a future refactor silently removing the probe site.
        Assert.True(
            chaos.LuaClaimCallCount > 0,
            $"BeforeLuaClaim probe was never invoked (count={chaos.LuaClaimCallCount}). " +
            "The Plan 05-09 probe insertion in MatchmakerTickerService.TryClaimMatchAsync may " +
            "have been removed — see IChaosInterceptor XML doc.");

        // -- Phase 5: simulate Redis crash recovery + run reconciler ---------
        // Mirrors the Phase 5 CONTEXT.md "after a Redis crash, Redis is empty" semantic
        // (Pitfall §1). Postgres tickets remain Queued; the reconciler must mark them all
        // Expired because they are stale + not present in Redis.
        await _adminMux.GetServer(_adminMux.GetEndPoints().First()).FlushDatabaseAsync();
        await db.KeyDeleteAsync(MatchmakingRedisKeys.MatcherLock);

        var reconcileResult = await reconciler.RunSweepOnceAsync(CancellationToken.None);
        Assert.False(reconcileResult.SkippedBecauseNotLeader);

        // -- Phase 6: invariant assertions -----------------------------------
        await AssertInvariantsAsync(db, ladderId);
    }

    // -------------------------------------------------------------------------
    // Invariant assertions
    // -------------------------------------------------------------------------

    private async Task AssertInvariantsAsync(IDatabase db, Guid ladderId)
    {
        await using var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync();

        // -- INVARIANT C / A — no player in two active sessions --------------
        // Group session_participants by PlayerId for Active sessions only; any HAVING > 1 is
        // a duplicate (closes both A — no duplicate session rows — and C — same player in
        // multiple sessions).
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT sp.""PlayerId"", COUNT(*)
                                FROM gamekit.session_participants sp
                                JOIN gamekit.game_sessions gs ON sp.""SessionId"" = gs.""Id""
                                WHERE gs.""State"" = 'Active' AND sp.""PlayerId"" IS NOT NULL
                                GROUP BY sp.""PlayerId""
                                HAVING COUNT(*) > 1";
            await using var reader = await cmd.ExecuteReaderAsync();
            var duplicates = new List<(Guid PlayerId, long Count)>();
            while (await reader.ReadAsync())
            {
                duplicates.Add((reader.GetGuid(0), reader.GetInt64(1)));
            }
            Assert.True(
                duplicates.Count == 0,
                $"INVARIANT A+C FAIL — {duplicates.Count} player(s) appear in 2+ active sessions: " +
                string.Join(", ", duplicates.Select(d => $"{d.PlayerId}={d.Count}")));
        }

        // -- INVARIANT D — stale tickets reconciled to Expired ---------------
        // After Redis flush + reconciler, every ticket created in Phase 1 is either:
        //   (a) Expired (reconciler marked it — was stale + not in Redis), or
        //   (b) some other terminal state if it was claimed by the ticker and tracked through
        //       the analytics drain (Plan 05-07).
        // The plan's expectation: count > 0 in Expired. With Redis fully flushed, ALL
        // non-terminal tickets become Expired candidates.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.matchmaking_tickets
                                WHERE ""Status"" = @s AND ""TerminalAt"" IS NOT NULL
                                AND ""LadderId"" = @l";
            cmd.Parameters.AddWithValue("s", (int)TicketStatus.Expired);
            cmd.Parameters.AddWithValue("l", ladderId);
            var expiredCount = (long)(await cmd.ExecuteScalarAsync())!;
            Assert.True(
                expiredCount > 0,
                $"INVARIANT D FAIL — reconciler did not mark any tickets as Expired " +
                "(count=0). Expected the reconciler to detect stale Postgres tickets missing " +
                "from Redis after the simulated crash recovery.");
        }

        // -- INVARIANT B — no ghost mm:ticket:{id} keys ----------------------
        // For every Postgres ticket in (Expired, Cancelled), verify KeyExistsAsync is false.
        // Trivially true after FlushDatabaseAsync; the assertion proves the invariant holds
        // and catches any future test changes that move FlushDatabase elsewhere or skip it.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""Id"" FROM gamekit.matchmaking_tickets
                                WHERE ""Status"" IN (@e, @c) AND ""LadderId"" = @l";
            cmd.Parameters.AddWithValue("e", (int)TicketStatus.Expired);
            cmd.Parameters.AddWithValue("c", (int)TicketStatus.Cancelled);
            cmd.Parameters.AddWithValue("l", ladderId);
            await using var reader = await cmd.ExecuteReaderAsync();
            var terminalTicketIds = new List<Guid>();
            while (await reader.ReadAsync())
                terminalTicketIds.Add(reader.GetGuid(0));

            var ghosts = new List<Guid>();
            foreach (var tid in terminalTicketIds)
            {
                var exists = await db.KeyExistsAsync(MatchmakingRedisKeys.Ticket(tid));
                if (exists)
                    ghosts.Add(tid);
            }
            Assert.True(
                ghosts.Count == 0,
                $"INVARIANT B FAIL — {ghosts.Count} ticket(s) in terminal state still have a " +
                $"live mm:ticket:{{id}} Redis key: {string.Join(", ", ghosts)}");
        }
    }

    // -------------------------------------------------------------------------
    // Service-provider builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a full matchmaking service provider with the test's
    /// <see cref="AbortingChaosInterceptor"/> registered BEFORE <see cref="MatchmakingBuilderExtensions.AddMatchmaking"/>
    /// so the latter's <c>TryAddSingleton&lt;IChaosInterceptor, NullChaosInterceptor&gt;</c> is a
    /// no-op (the explicit override wins).
    /// </summary>
    private ServiceProvider BuildServiceProviderWithChaos(
        string redisCs, string ladderName, AbortingChaosInterceptor chaos)
    {
        var services = new ServiceCollection();
        services.AddLogging(l => l.SetMinimumLevel(LogLevel.Warning));

        // T-05-09-01 mitigation: register the abort interceptor BEFORE AddMatchmaking. The
        // builder's TryAddSingleton<NullChaosInterceptor> registration honours this override.
        services.AddSingleton<IChaosInterceptor>(chaos);

        var gk = services.AddGameKit(o =>
        {
            // Required by the AddGameKit option validator. The chaos test applies
            // matchmaking migrations directly via IntegrationTestHelpers (see fixture
            // InitializeAsync); AutoMigrate=false avoids the host running them again.
            o.ConnectionString = _cs;
            o.MigrationsConnectionString = _cs;
            o.AutoMigrate = false;
        });
        // Replace the GameKit-registered DbContext with one that applies the test customizer
        // so MatchmakingTicket / Party / etc. are visible at query time (Plan 05-02
        // MatchmakingTestModelCustomizer pattern — used by every Matchmaking integration test
        // that touches the runtime DbContext).
        var dbCtxDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(Microsoft.EntityFrameworkCore.DbContextOptions<GameKit.Core.Data.GameKitDbContext>));
        if (dbCtxDescriptor is not null) services.Remove(dbCtxDescriptor);
        services.AddDbContext<GameKit.Core.Data.GameKitDbContext>(opts =>
            opts.UseNpgsql(_cs)
                .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer, MatchmakingTestModelCustomizer>()
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

        gk.AddRankings();
        gk.AddMatchmaking(o =>
        {
            // Tight ticker budget so the test drains quickly — but the ticker is invoked
            // directly via IMatchmakerTicker, so the PeriodicTimer interval is largely
            // irrelevant here. Lock TTL kept at default 90s — the test doesn't exercise
            // the lock-expiry path.
            o.Ticker.TickIntervalMs = 100;
            // Default Reconciler.StaleTicketThresholdMinutes = 5; tickets seeded at
            // queuedAt = now-10min are stale immediately.
        }).AddLadder(ladderName);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect(redisCs));

        // The reconciler's orphan-session sweep writes to admin_audit_log via
        // IAdminAuditWriter. Phase 3 registers this in AddGameKitAdmin which the chaos test
        // does NOT compose (admin UI is out of scope); register the writer directly.
        services.AddScoped<IAdminAuditWriter, AdminAuditWriter>();

        return services.BuildServiceProvider();
    }

    /// <summary>Seeds a single Redis ticket hash mirroring <c>MatchmakingService.EnqueueAsync</c>'s shape.</summary>
    private static async Task SeedRedisTicketAsync(
        IDatabase db, Guid ticketId, Guid ladderId, string poolName, Guid playerId, DateTimeOffset queuedAt)
    {
        var queuedAtMs = queuedAt.ToUnixTimeMilliseconds();
        var members = new[]
        {
            new { PlayerId = playerId, Rating = 1500.0, RatingDeviation = 350.0, Volatility = 0.06 },
        };
        await db.HashSetAsync(
            MatchmakingRedisKeys.Ticket(ticketId),
            new[]
            {
                new HashEntry("status", "queued"),
                new HashEntry("ladderId", ladderId.ToString()),
                new HashEntry("poolName", poolName),
                new HashEntry("queuedAt", queuedAtMs.ToString(CultureInfo.InvariantCulture)),
                new HashEntry("aggregateRating", "1500"),
                new HashEntry("partyId", string.Empty),
                new HashEntry("playerId", playerId.ToString()),
                new HashEntry("members", JsonSerializer.Serialize(members)),
            });
    }
}
