// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Telemetry;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace GameKit.Matchmaking.LoadTests;

/// <summary>
/// SC#3 phase-gate load test (MATCH-13). Sustains ~1k concurrent queued tickets for
/// 10 minutes against a single Testcontainer Postgres + Redis pair and asserts:
/// <list type="bullet">
///   <item>Every ticker iteration (per-tick <see cref="MatchmakingActivitySource"/> span)
///         stays within <see cref="GameKitMatchmakingTickerOptions.MaxIterationBudgetMs"/>
///         (default 50 ms; tightened from RESEARCH §Decision 13).</item>
///   <item>The Npgsql pool is not exhausted (Pitfall §8 mitigation — pool capped at 25;
///         a sustained pool wait or timeout indicates the drain/reconciler/retention
///         services are holding connections across Polly retry sleeps).</item>
///   <item>The bounded analytics channel does not drop events
///         (<c>matchmaking.analytics.dropped_events == 0</c>) — D-15 design margin
///         assertion at the SC#3 throughput level.</item>
///   <item>At least 1000 <c>matchmaking_tickets</c> rows reach <see cref="TicketStatus.Matched"/>
///         over the run (sanity check: 10 min × ~100 matches/min minimum).</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Opt-in via <see cref="TraitAttribute"/>.</b> The test is decorated with
/// <c>[Trait("Category", "LoadTest")]</c> so CI/dev rapid loops on
/// <c>dotnet test</c> against the full solution skip it by default. Invoke explicitly:
/// <code>
/// dotnet test tests/GameKit.Matchmaking.LoadTests --filter Category=LoadTest --logger "console;verbosity=detailed"
/// </code>
/// The project also sets <c>&lt;IsPackable&gt;false&lt;/IsPackable&gt;</c> so it is excluded
/// from any solution-level <c>dotnet pack</c> sweeps.
/// </para>
/// <para>
/// <b>Runtime upper bound.</b> The <see cref="FactAttribute.Timeout"/> is 15 minutes
/// (10-minute sustain + ~2-minute warm-up + assertions). If the test hangs past this it
/// is a deadlock — typically Pitfall §5 long-poll subscription leakage or Pitfall §8 pool
/// exhaustion preventing the drain from making forward progress.
/// </para>
/// <para>
/// <b>OQ-4 implicit verification (Reconciler + Retention coexistence under load).</b>
/// The fixture does NOT pause the reconciler (30 s tick) or retention sweep (startup +
/// daily) during the run. If either service's Postgres queries contend with the drain
/// for the 25-connection pool, the ticker iteration time will exceed budget OR the pool
/// observer will fire exhaustion events. Either failure mode is caught and surfaced with
/// a descriptive error.
/// </para>
/// <para>
/// <b>Dropped-event detection.</b> A <see cref="MeterListener"/> subscribes to the
/// <c>"GameKit.Matchmaking"</c> meter and accumulates increments to the
/// <c>matchmaking.analytics.dropped_events</c> counter. Production zero-state is asserted
/// after the run completes.
/// </para>
/// </remarks>
[Trait("Category", "LoadTest")]
public sealed class MatchmakingLoadTests : IClassFixture<LoadTestFixture>, IAsyncLifetime
{
    private readonly LoadTestFixture _fx;
    private readonly ITestOutputHelper _output;
    private MeterListener? _meterListener;
    private long _droppedEventsObserved;

    /// <summary>Number of concurrent queued tickets sustained over the 10-minute run.</summary>
    private const int ConcurrentTickets = 1000;

    /// <summary>Total wall-clock duration of the sustain phase (10 minutes).</summary>
    private static readonly TimeSpan SustainDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Status-poll period for the re-enqueue feedback loop. Bounded so the test driver
    /// itself does not consume excessive cycles.
    /// </summary>
    private static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(10);

    public MatchmakingLoadTests(LoadTestFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _output = output;
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        // Subscribe a MeterListener to the GameKit.Matchmaking meter so we can detect any
        // matchmaking.analytics.dropped_events increments during the run (D-15 / D-16).
        _meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "GameKit.Matchmaking"
                    && instrument.Name == "matchmaking.analytics.dropped_events")
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            },
        };
        _meterListener.SetMeasurementEventCallback<long>((_, measurement, _, _) =>
            Interlocked.Add(ref _droppedEventsObserved, measurement));
        _meterListener.Start();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _meterListener?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// SC#3 phase-gate test. Run via
    /// <c>dotnet test tests/GameKit.Matchmaking.LoadTests --filter Category=LoadTest</c>.
    /// </summary>
    [Fact(Timeout = 15 * 60 * 1000)]
    public async Task SustainedThousandTicketLoad_HoldsBudget()
    {
        _output.WriteLine($"[{Stamp()}] Load test starting — {ConcurrentTickets} tickets, " +
                          $"{SustainDuration.TotalMinutes:F0} min sustain, " +
                          $"pool=Maximum Pool Size=25, ticker=500ms, budget=50ms.");

        var testStart = DateTimeOffset.UtcNow;

        // ----- Seed -----
        // 1) Bulk-insert 1000 player rows (single Postgres round-trip per row via Npgsql
        // bulk command; pool size is 25 so we stay way under the cap during seeding).
        _output.WriteLine($"[{Stamp()}] Seeding {ConcurrentTickets} player rows...");
        var playerIds = new Guid[ConcurrentTickets];
        for (var i = 0; i < ConcurrentTickets; i++) playerIds[i] = Guid.NewGuid();
        _fx.BulkInsertPlayers(playerIds);
        _output.WriteLine($"[{Stamp()}] Player seeding complete.");

        // 2) Mint a JWT per player so we don't pay the signing cost inside the parallel
        // enqueue burst. JWT signing is CPU-bound; pre-computing 1000 tokens keeps the
        // burst HTTP-bound (the actual test surface).
        _output.WriteLine($"[{Stamp()}] Pre-minting {ConcurrentTickets} JWTs...");
        var jwts = new string[ConcurrentTickets];
        Parallel.For(0, ConcurrentTickets, i => jwts[i] = _fx.MintPlayerJwt(playerIds[i]));
        _output.WriteLine($"[{Stamp()}] JWT mint complete.");

        // ----- Initial burst: 1000 concurrent enqueues -----
        _output.WriteLine($"[{Stamp()}] Firing initial {ConcurrentTickets} concurrent enqueues...");
        var ticketIds = new ConcurrentDictionary<int, Guid>();
        var enqueueErrors = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, ConcurrentTickets),
            new ParallelOptions { MaxDegreeOfParallelism = 100 },
            async (i, ct) =>
            {
                // Re-use the shared HttpClient (TestServer is thread-safe). The fixture owns
                // its lifetime; `using` would dispose the shared instance on first scope-exit
                // and abort every other in-flight parallel request mid-response.
                var client = _fx.Client;
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/mm/queue")
                {
                    Content = JsonContent.Create(new EnqueueRequest(_fx.TestLadderId, _fx.TestLadderName, null)),
                };
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwts[i]);
                try
                {
                    using var resp = await client.SendAsync(req, ct);
                    if (resp.StatusCode != HttpStatusCode.OK)
                    {
                        enqueueErrors.Add($"player[{i}] HTTP {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync(ct)}");
                        return;
                    }
                    var body = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
                    if (body.TryGetProperty("ticketId", out var tidEl) && tidEl.TryGetGuid(out var tid))
                    {
                        ticketIds[i] = tid;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    enqueueErrors.Add($"player[{i}] exception: {ex.Message}");
                    _fx.Pool.RecordExceptionFallback(ex.ToString());
                }
            });

        _output.WriteLine($"[{Stamp()}] Initial burst complete. " +
                          $"Tickets queued: {ticketIds.Count}/{ConcurrentTickets}; " +
                          $"errors: {enqueueErrors.Count}.");
        if (enqueueErrors.Count > 0)
        {
            // Show up to 5 sample errors; do NOT fail here — we want the full run to capture
            // the budget/pool/dropped-event picture even if some enqueues failed.
            foreach (var e in enqueueErrors.Take(5))
                _output.WriteLine($"  [enqueue-error] {e}");
        }

        // Mark the warmup cutoff so the budget assertion only considers steady-state ticks.
        // The 1000-concurrent enqueue burst saturates the shared StackExchange.Redis
        // multiplexer's send-queue; the next 1-2 ticker passes pay queue-wait latency that
        // is not a matcher cost. 5 seconds is enough to flush the burst's outstanding work.
        _fx.Budget.WarmupCutoff = DateTimeOffset.UtcNow.AddSeconds(5);
        _output.WriteLine($"[{Stamp()}] Budget warmup cutoff set to {_fx.Budget.WarmupCutoff:O}.");

        // Start the auto-acceptor — drives proposals to Matched terminal state. Without it,
        // proposals time out and tickets cycle Queued → Proposed → TimedOut → Queued without
        // ever reaching Matched. The SC#3 throughput assertion (>= 1000 matched) requires
        // a complete proposal acceptance flow.
        var playerToJwt = new Dictionary<Guid, string>(ConcurrentTickets);
        for (var i = 0; i < ConcurrentTickets; i++) playerToJwt[playerIds[i]] = jwts[i];
        using var acceptorCts = new CancellationTokenSource();
        var acceptorTask = Task.Run(() => AutoAcceptProposalsAsync(playerToJwt, acceptorCts.Token));

        // ----- Sustain phase: 10 minutes -----
        _output.WriteLine($"[{Stamp()}] Entering sustain phase ({SustainDuration.TotalMinutes:F0} min)...");

        var sustainStart = Stopwatch.StartNew();
        var halfwayReported = false;
        var rng = new Random(42);

        // Use a CancellationTokenSource to break the loop at the sustain budget — bounded
        // independently of the [Fact(Timeout=...)] safety net.
        using var sustainCts = new CancellationTokenSource(SustainDuration + TimeSpan.FromSeconds(30));

        // Re-enqueue loop: every PollPeriod, sample a fraction of the seeded players and
        // re-enqueue them. This sustains roughly ConcurrentTickets concurrent queued
        // tickets — the ticker drains matched tickets continuously so we top up the same
        // depth. We do NOT poll each individual ticket's status (which would consume HTTP
        // capacity and JWT validation cycles); the matchmaker's natural drain + this
        // periodic re-enqueue keep the queue at the target depth.
        while (!sustainCts.IsCancellationRequested && sustainStart.Elapsed < SustainDuration)
        {
            // Halfway-point progress report (~5 min mark)
            if (!halfwayReported && sustainStart.Elapsed >= SustainDuration / 2)
            {
                halfwayReported = true;
                var hist = _fx.Budget.IterationMsHistogram;
                _output.WriteLine($"[{Stamp()}] [halfway] " +
                    $"TicksObserved={_fx.Budget.TicksObserved} " +
                    $"MaxIterationMs={_fx.Budget.MaxIterationMs} " +
                    $"p99={(hist.Count > 0 ? hist[Math.Min(hist.Count - 1, (int)(0.99 * hist.Count))] : 0):F2} " +
                    $"PoolExhaustionEvents={_fx.Pool.PoolExhaustionEvents} " +
                    $"PoolWaitEvents={_fx.Pool.PoolWaitEvents} " +
                    $"DroppedEvents={Interlocked.Read(ref _droppedEventsObserved)}");

                try
                {
                    var matched = await _fx.CountMatchedTicketsAsync();
                    _output.WriteLine($"[{Stamp()}] [halfway] matchmaking_tickets.Status=Matched count: {matched}");
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"[{Stamp()}] [halfway] matched-count query failed: {ex.Message}");
                }
            }

            // Sleep one poll-period before the next re-enqueue wave.
            try
            {
                await Task.Delay(PollPeriod, sustainCts.Token);
            }
            catch (TaskCanceledException) { break; }

            // Re-enqueue a random sample of 100 players. This mirrors the steady-state
            // pattern: tickets that get matched (or expired) come back into the queue as
            // the ticker drains. The drain rate at the target depth is ~tens per second
            // under design budget; 100 per PollPeriod (10 s) ≈ 10/s keeps depth roughly
            // constant without overwhelming the pool.
            var reenqBatch = Enumerable.Range(0, 100)
                .Select(_ => rng.Next(ConcurrentTickets))
                .ToArray();
            await Parallel.ForEachAsync(
                reenqBatch,
                new ParallelOptions { MaxDegreeOfParallelism = 25, CancellationToken = sustainCts.Token },
                async (i, ct) =>
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/mm/queue")
                        {
                            Content = JsonContent.Create(new EnqueueRequest(_fx.TestLadderId, _fx.TestLadderName, null)),
                        };
                        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwts[i]);
                        using var resp = await _fx.Client.SendAsync(req, ct);
                        // We tolerate 429 (rate-limited) and 400 (queue depth or other
                        // soft-failure modes that don't reflect a load-test invariant
                        // failure). Pool exhaustion would surface via the observer.
                        if (resp.StatusCode != HttpStatusCode.OK
                            && resp.StatusCode != (HttpStatusCode)429
                            && resp.StatusCode != HttpStatusCode.BadRequest)
                        {
                            // Record as a non-fatal warning — the budget+pool observers are
                            // the SC#3 truth source.
                            _output.WriteLine($"[{Stamp()}] [re-enqueue] player[{i}] HTTP {(int)resp.StatusCode}");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _fx.Pool.RecordExceptionFallback(ex.ToString());
                    }
                });
        }

        sustainStart.Stop();
        _output.WriteLine($"[{Stamp()}] Sustain phase complete after {sustainStart.Elapsed}. " +
                          $"Stopping acceptor + 30s tail for drain/sweep to settle...");
        acceptorCts.Cancel();
        try { await acceptorTask; } catch (OperationCanceledException) { }

        // Brief tail wait so the drain + sweeper finalize any in-flight events.
        await Task.Delay(TimeSpan.FromSeconds(30));

        // ----- Final stats + assertions -----
        var finalHist = _fx.Budget.IterationMsHistogram;
        var p50 = Pct(finalHist, 50);
        var p90 = Pct(finalHist, 90);
        var p99 = Pct(finalHist, 99);
        var maxMs = _fx.Budget.MaxIterationMs;
        var ticks = _fx.Budget.TicksObserved;
        var poolEx = _fx.Pool.PoolExhaustionEvents;
        var poolWait = _fx.Pool.PoolWaitEvents;
        var dropped = Interlocked.Read(ref _droppedEventsObserved);

        long matchedCount = 0;
        try { matchedCount = await _fx.CountMatchedTicketsAsync(); }
        catch (Exception ex) { _output.WriteLine($"[{Stamp()}] final matched-count query failed: {ex.Message}"); }

        _output.WriteLine($"[{Stamp()}] ===== SC#3 FINAL =====");
        _output.WriteLine($"  Test duration:       {DateTimeOffset.UtcNow - testStart}");
        _output.WriteLine($"  Tick observations:   {ticks}");
        _output.WriteLine($"  MaxIterationMs:      {maxMs} (budget 50)");
        _output.WriteLine($"  p50 / p90 / p99 ms:  {p50:F2} / {p90:F2} / {p99:F2}");
        _output.WriteLine($"  Pool exhaustion:     {poolEx}");
        _output.WriteLine($"  Pool waits >100ms:   {poolWait}");
        _output.WriteLine($"  Dropped events:      {dropped}");
        _output.WriteLine($"  Matched tickets:     {matchedCount}");
        _output.WriteLine($"  Enqueue errors:      {enqueueErrors.Count}");

        // SC#3 phase-gate assertions — order matters: the budget assertion is most
        // operator-actionable (Lua perf / strategy iteration regression), so it surfaces first.
        _fx.Budget.AssertBudgetHeld(maxBudgetMs: 50);
        _fx.Pool.AssertNoPoolExhaustion();
        Assert.True(dropped == 0,
            $"matchmaking.analytics.dropped_events = {dropped}; expected 0. " +
            $"The bounded channel capacity (10000) was insufficient for sustained {ConcurrentTickets}-concurrent load.");
        Assert.True(matchedCount >= 1000,
            $"Expected >= 1000 matched tickets; got {matchedCount}. " +
            $"Indicates the matcher did not maintain throughput across the run.");
    }

    private static double Pct(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var i = Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[i];
    }

    private static string Stamp() => DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>
    /// Polls Redis for live <c>mm:proposal:*</c> keys and POSTs accept for every participating
    /// ticket. Drives the lifecycle Proposed → Matched so the SC#3 matched-count assertion is
    /// physically achievable without a client-side long-poll subscriber. Polls every 250 ms;
    /// AcceptTimeoutSeconds default is 10 s so we have generous margin before the sweeper
    /// reaps the proposal.
    /// </summary>
    private async Task AutoAcceptProposalsAsync(Dictionary<Guid, string> playerToJwt, CancellationToken ct)
    {
        var processed = new HashSet<Guid>();
        using var redis = await ConnectionMultiplexer.ConnectAsync(_fx.RedisConnectionString);
        var db = redis.GetDatabase();
        var endpoints = redis.GetEndPoints();
        var server = redis.GetServer(endpoints[0]);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                foreach (var key in server.Keys(pattern: "mm:proposal:*", pageSize: 100))
                {
                    if (ct.IsCancellationRequested) break;
                    var keyStr = key.ToString();
                    if (keyStr.EndsWith(":accepts")) continue;

                    var idIdx = keyStr.LastIndexOf(':') + 1;
                    if (!Guid.TryParse(keyStr.AsSpan(idIdx), out var proposalId)) continue;
                    if (!processed.Add(proposalId)) continue;

                    var ticketsField = await db.HashGetAsync(key, "tickets");
                    if (!ticketsField.HasValue) continue;

                    var ticketIdStrs = ticketsField.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var tidStr in ticketIdStrs)
                    {
                        if (ct.IsCancellationRequested) break;
                        if (!Guid.TryParse(tidStr.Trim(), out var tid)) continue;

                        var ticketHash = await db.HashGetAllAsync($"mm:ticket:{tid}");
                        Guid? playerId = null;
                        foreach (var e in ticketHash)
                        {
                            if (e.Name.ToString() == "playerId" && Guid.TryParse(e.Value.ToString(), out var pid))
                            {
                                playerId = pid;
                                break;
                            }
                        }
                        if (playerId is null || !playerToJwt.TryGetValue(playerId.Value, out var jwt)) continue;

                        using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/mm/proposal/{proposalId}/accept")
                        {
                            Content = JsonContent.Create(new AcceptDeclineRequest(tid)),
                        };
                        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", jwt);
                        try
                        {
                            using var resp = await _fx.Client.SendAsync(req, ct);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { /* best-effort */ }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _output.WriteLine($"[{Stamp()}] [acceptor] {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromMilliseconds(250), ct); }
            catch (TaskCanceledException) { break; }
        }
    }
}
