// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using GameKit.LoadTests.Infrastructure;
using GameKit.Matchmaking.Services;

namespace GameKit.LoadTests.Benchmarks;

/// <summary>
/// Benchmarks the matchmaking-ticket Redis round-trip:
/// <see cref="IMatchmakingService.EnqueueAsync"/> against a live Testcontainers Redis instance.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Critical (19-RESEARCH.md Pitfall §3):</strong> The Testcontainers Redis container is
/// started ONCE in <see cref="SetupAsync"/>, NOT inside each benchmark iteration. Container boot
/// time (~1-3 s) must not contaminate the per-iteration measurement.
/// The <c>[Benchmark]</c> method measures only the <c>EnqueueAsync</c> I/O path.
/// </para>
/// <para>
/// <see cref="MinIterationCountAttribute"/> is set to 15 (19-RESEARCH.md §How to Stabilize)
/// to dampen Docker-bridge network jitter and ensure a statistically stable mean despite the
/// inherent latency variability of Testcontainers networking on Linux.
/// </para>
/// <para>
/// The full suite is intentionally slow (~15 iterations × ~5ms/enqueue = ~75ms per BDN job).
/// Run with <c>--job short --filter '*Ticket*'</c> for quick smoke-validation;
/// the committed baseline capture in Plan 19-04 runs the full suite.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[MinIterationCount(15)]
public class MatchmakingTicketBenchmarks : IAsyncDisposable
{
    private MatchmakingBenchmarkHost _host = null!;
    private IMatchmakingService _svc = null!;
    private Guid _ladderId;
    private Guid _playerId;

    /// <summary>
    /// Starts Testcontainers Redis + Postgres (once), applies migrations, seeds a ladder and
    /// player, and resolves <see cref="IMatchmakingService"/>. Container boot cost (~1-3 s) is
    /// paid here and excluded from the <see cref="TicketEnqueueAsync"/> measurement.
    /// </summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _host = new MatchmakingBenchmarkHost();
        await _host.InitializeAsync();

        _svc      = _host.MatchmakingService;
        _ladderId = _host.TestLadderId;
        _playerId = _host.TestPlayerId;
    }

    /// <summary>
    /// Measures a single <see cref="IMatchmakingService.EnqueueAsync"/> call — the Redis
    /// write path: HSETNX ticket hash + ZADD to the ladder sorted set + Channel publish.
    /// Returns the <see cref="EnqueueResult"/> so BenchmarkDotNet cannot elide the call.
    /// </summary>
    /// <remarks>
    /// The same player is re-enqueued on each iteration. After a successful enqueue the player
    /// holds a queued ticket; subsequent iterations may return
    /// <see cref="EnqueueOutcome.AlreadyEnqueued"/>. That is intentional — the benchmark
    /// exercises the full fast-path decision (Redis HSETNX + ZADD check), not just the
    /// "happy path". The returned <see cref="EnqueueResult"/> captures whichever outcome fired.
    /// </remarks>
    /// <returns>The <see cref="EnqueueResult"/> from the Redis enqueue operation.</returns>
    [Benchmark]
    public async Task<EnqueueResult> TicketEnqueueAsync()
        => await _svc.EnqueueAsync(
            playerId:  _playerId,
            ladderId:  _ladderId,
            poolName:  null,
            partyId:   null,
            ct:        default);

    /// <summary>Disposes the Testcontainers Redis + Postgres containers and the host.</summary>
    [GlobalCleanup]
    public async Task CleanupAsync()
        => await _host.DisposeAsync();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
        => await _host.DisposeAsync();
}
