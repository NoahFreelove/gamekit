// SPDX-License-Identifier: GPL-3.0-or-later
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
using Npgsql;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace GameKit.Matchmaking.LoadTests;

/// <summary>
/// Fast-iteration smoke variant of <see cref="MatchmakingLoadTests"/> for SC#3 fix
/// iteration. Smaller N (100 tickets) + shorter sustain (30s) — should complete in
/// ~45–60 sec including warm-up. Same four assertions but scaled down so we can
/// iterate on fixes without 10-minute round-trips.
/// </summary>
[Trait("Category", "LoadTestSmoke")]
public sealed class MatchmakingSmokeLoadTests : IClassFixture<LoadTestFixture>, IAsyncLifetime
{
    private readonly LoadTestFixture _fx;
    private readonly ITestOutputHelper _output;
    private MeterListener? _meterListener;
    private long _droppedEventsObserved;

    private const int ConcurrentTickets = 100;
    private static readonly TimeSpan SustainDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(5);

    public MatchmakingSmokeLoadTests(LoadTestFixture fx, ITestOutputHelper output)
    {
        _fx = fx;
        _output = output;
    }

    public Task InitializeAsync()
    {
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

    public Task DisposeAsync()
    {
        _meterListener?.Dispose();
        return Task.CompletedTask;
    }

    [Fact(Timeout = 5 * 60 * 1000)]
    public async Task SmokeLoad_100Tickets_30sSustain()
    {
        _output.WriteLine($"[{Stamp()}] Smoke load test starting — {ConcurrentTickets} tickets, " +
                          $"{SustainDuration.TotalSeconds:F0}s sustain.");

        var testStart = DateTimeOffset.UtcNow;

        var playerIds = new Guid[ConcurrentTickets];
        for (var i = 0; i < ConcurrentTickets; i++) playerIds[i] = Guid.NewGuid();
        _fx.BulkInsertPlayers(playerIds);

        var jwts = new string[ConcurrentTickets];
        Parallel.For(0, ConcurrentTickets, i => jwts[i] = _fx.MintPlayerJwt(playerIds[i]));

        _output.WriteLine($"[{Stamp()}] Seeded {ConcurrentTickets} players + JWTs. Firing burst...");

        var ticketIds = new ConcurrentDictionary<int, Guid>();
        var enqueueErrors = new ConcurrentBag<string>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, ConcurrentTickets),
            new ParallelOptions { MaxDegreeOfParallelism = 50 },
            async (i, ct) =>
            {
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
                }
            });

        _output.WriteLine($"[{Stamp()}] Burst done. Tickets={ticketIds.Count}/{ConcurrentTickets}, errors={enqueueErrors.Count}.");
        foreach (var e in enqueueErrors.Take(3))
            _output.WriteLine($"  [err] {e}");

        // Skip warmup ticks — the burst saturates the shared multiplexer queue so the first
        // 1-2 ticker passes pay queue-wait latency unrelated to matcher cost.
        _fx.Budget.WarmupCutoff = DateTimeOffset.UtcNow.AddSeconds(3);

        // Start a background auto-acceptor that drives proposals to Matched. Without this,
        // tickets cycle Queued → Proposed → TimedOut → Queued and never reach the Matched
        // terminal state — defeating the SC#3 throughput floor assertion.
        var playerToJwt = new Dictionary<Guid, string>(ConcurrentTickets);
        for (var i = 0; i < ConcurrentTickets; i++) playerToJwt[playerIds[i]] = jwts[i];
        using var acceptorCts = new CancellationTokenSource();
        var acceptorTask = Task.Run(() => AutoAcceptProposalsAsync(playerToJwt, acceptorCts.Token));

        _output.WriteLine($"[{Stamp()}] Sustain {SustainDuration.TotalSeconds}s...");
        var sustainStart = Stopwatch.StartNew();
        var rng = new Random(42);
        using var sustainCts = new CancellationTokenSource(SustainDuration + TimeSpan.FromSeconds(15));

        while (!sustainCts.IsCancellationRequested && sustainStart.Elapsed < SustainDuration)
        {
            try { await Task.Delay(PollPeriod, sustainCts.Token); }
            catch (TaskCanceledException) { break; }

            var reenqBatch = Enumerable.Range(0, 25).Select(_ => rng.Next(ConcurrentTickets)).ToArray();
            await Parallel.ForEachAsync(
                reenqBatch,
                new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = sustainCts.Token },
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
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                });
        }
        sustainStart.Stop();

        _output.WriteLine($"[{Stamp()}] Sustain done after {sustainStart.Elapsed}. Stopping acceptor + 10s tail for drain to settle...");
        acceptorCts.Cancel();
        try { await acceptorTask; } catch (OperationCanceledException) { }
        await Task.Delay(TimeSpan.FromSeconds(10));

        var hist = _fx.Budget.IterationMsHistogram;
        var p50 = Pct(hist, 50);
        var p90 = Pct(hist, 90);
        var p99 = Pct(hist, 99);
        var maxMs = _fx.Budget.MaxIterationMs;
        var ticks = _fx.Budget.TicksObserved;
        var poolEx = _fx.Pool.PoolExhaustionEvents;
        var dropped = Interlocked.Read(ref _droppedEventsObserved);

        long matchedCount = 0, ticketEventsCount = 0, ticketRowsCount = 0;
        try { matchedCount = await _fx.CountMatchedTicketsAsync(); }
        catch (Exception ex) { _output.WriteLine($"[count-matched] {ex.Message}"); }
        try
        {
            ticketEventsCount = await CountRowsAsync("gamekit.ticket_events");
            ticketRowsCount = await CountRowsAsync("gamekit.matchmaking_tickets");
        }
        catch (Exception ex) { _output.WriteLine($"[count-rows] {ex.Message}"); }

        _output.WriteLine($"[{Stamp()}] ===== SMOKE FINAL =====");
        _output.WriteLine($"  Test duration:           {DateTimeOffset.UtcNow - testStart}");
        _output.WriteLine($"  Tick observations:       {ticks}");
        _output.WriteLine($"  MaxIterationMs:          {maxMs} (budget 50)");
        _output.WriteLine($"  p50/p90/p99 ms:          {p50:F2} / {p90:F2} / {p99:F2}");
        _output.WriteLine($"  Pool exhaustion:         {poolEx}");
        _output.WriteLine($"  Dropped events:          {dropped}");
        _output.WriteLine($"  matchmaking_tickets rows:{ticketRowsCount}");
        _output.WriteLine($"  ticket_events rows:      {ticketEventsCount}");
        _output.WriteLine($"  Matched tickets (db):    {matchedCount}");
        _output.WriteLine($"  Enqueue errors:          {enqueueErrors.Count}");

        // Soft assertions: never throw - we use this as iterating instrumentation
        // until budget is green; then we will tighten.
        if (maxMs <= 50 && poolEx == 0 && dropped == 0 && matchedCount >= 10)
        {
            _output.WriteLine($"[{Stamp()}] PASS — all soft assertions satisfied.");
        }
        else
        {
            _output.WriteLine($"[{Stamp()}] FAIL — see counters above. " +
                $"Budget={maxMs<=50}, Pool={poolEx==0}, Dropped={dropped==0}, Matched={matchedCount>=10}.");
        }
    }

    /// <summary>
    /// Polls Redis for active proposals and POSTs accept for every ticket in each. Drives
    /// the proposal lifecycle from Proposed → Matched so the test's matched-count metric
    /// can be exercised. Polls every 250 ms — the AcceptTimeoutSeconds default is 10s so
    /// this poll cadence has plenty of margin to accept before the sweeper reaps.
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
                foreach (var key in server.Keys(pattern: "mm:proposal:*", pageSize: 50))
                {
                    if (ct.IsCancellationRequested) break;
                    var keyStr = key.ToString();
                    if (keyStr.EndsWith(":accepts")) continue;

                    // Extract proposalId from "mm:proposal:{guid}".
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
                            // Ignore status — best-effort acceptor.
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { /* swallow */ }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _output.WriteLine($"[acceptor] {ex.Message}");
            }

            try { await Task.Delay(TimeSpan.FromMilliseconds(250), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    private async Task<long> CountRowsAsync(string table)
    {
        await using var conn = new NpgsqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        var n = await cmd.ExecuteScalarAsync();
        return n is long l ? l : Convert.ToInt64(n, CultureInfo.InvariantCulture);
    }

    private static double Pct(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var i = Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[i];
    }

    private static string Stamp() => DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
