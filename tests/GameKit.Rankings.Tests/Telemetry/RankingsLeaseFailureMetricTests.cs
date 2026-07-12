// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Rankings.Algorithms;
using GameKit.Rankings.Services;
using GameKit.Rankings.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Rankings.Tests.Telemetry;

/// <summary>
/// Behavior tests proving <see cref="RankingsMeter.LockAcquisitionFailures"/>
/// (<c>rankings.leader_lock.acquisition_failures</c>) increments by exactly 1 when leader-lease
/// acquisition fails, for BOTH Rankings lease consumers:
/// <list type="bullet">
///   <item><see cref="RankingsTickerService.RunOnceAsync"/> — ticker lease
///         (<c>gamekit:rankings:ticker:lease</c> via <see cref="RankingsTickerLeaseHelper"/>).</item>
///   <item><see cref="RankDecayBackgroundService.RunOnceAsync"/> — decay lease
///         (<c>gamekit:rankings:decay:lease</c> via <see cref="RankDecayLeaseHelper"/>).</item>
/// </list>
/// Mirrors the Matchmaking pattern where <c>MatchmakerTickerService.RunOnceAsync</c> increments
/// <c>matchmaking.leader_lock.acquisition_failures</c> in its acquisition-failed branch.
/// </summary>
/// <remarks>
/// <para>
/// The failed acquisition is simulated with a Moq <see cref="IConnectionMultiplexer"/> whose
/// <see cref="IDatabaseAsync.LockTakeAsync(RedisKey, RedisValue, TimeSpan, CommandFlags)"/>
/// returns <see langword="false"/> (another replica holds the lock) — the acquisition-failed
/// branch, NOT the exception path. No DB, no Redis container.
/// </para>
/// <para>
/// Joined to the <c>RankingsMetrics</c> collection (<c>DisableParallelization = true</c>) so
/// MeterListener callbacks fired by static-instrument <c>Add</c> calls cannot land in the
/// wrong listener when test classes run concurrently.
/// </para>
/// </remarks>
[Trait("Category", "Unit")]
[Collection("RankingsMetrics")]
public sealed class RankingsLeaseFailureMetricTests
{
    /// <summary>
    /// Builds a Moq <see cref="IConnectionMultiplexer"/> whose <c>LockTakeAsync</c> always
    /// returns <see langword="false"/> — simulating another replica holding the leader lock.
    /// </summary>
    private static IConnectionMultiplexer BuildLockTakeFalseMultiplexer()
    {
        var db = new Mock<IDatabase>(MockBehavior.Loose);
        db.Setup(d => d.LockTakeAsync(
                It.IsAny<RedisKey>(),
                It.IsAny<RedisValue>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var mux = new Mock<IConnectionMultiplexer>(MockBehavior.Loose);
        mux.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(db.Object);
        return mux.Object;
    }

    /// <summary>
    /// Subscribes a <see cref="MeterListener"/> to the
    /// <c>rankings.leader_lock.acquisition_failures</c> counter and collects raw measurements.
    /// </summary>
    private static MeterListener StartLockFailureListener(List<long> capturedValues)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instr, l) =>
            {
                if (instr.Meter.Name == RankingsMeter.MeterName &&
                    instr.Name == "rankings.leader_lock.acquisition_failures")
                    l.EnableMeasurementEvents(instr);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) =>
        {
            capturedValues.Add(value);
        });
        listener.Start();
        return listener;
    }

    [Fact]
    public async Task TickerService_LeaseAcquisitionFails_IncrementsLockAcquisitionFailures()
    {
        // Arrange — real lease helper over a mux whose LockTakeAsync returns false.
        var opts = Options.Create(new GameKitRankingsOptions());
        var lease = new RankingsTickerLeaseHelper(
            BuildLockTakeFalseMultiplexer(),
            NullLogger<RankingsTickerLeaseHelper>.Instance,
            opts);

        var ticker = new RankingsTickerService(
            new Mock<IServiceScopeFactory>(MockBehavior.Loose).Object,
            lease,
            new Mock<IRankingAlgorithm>(MockBehavior.Loose).Object,
            new Mock<IClock>(MockBehavior.Loose).Object,
            opts,
            NullLogger<RankingsTickerService>.Instance);

        var capturedValues = new List<long>();
        using var listener = StartLockFailureListener(capturedValues);

        // Act
        var result = await ticker.RunOnceAsync(CancellationToken.None);

        // Assert — acquisition failed AND the counter was incremented by exactly 1.
        Assert.Equal(TickResult.LockNotAcquired, result);
        var single = Assert.Single(capturedValues);
        Assert.Equal(1L, single);
    }

    [Fact]
    public async Task DecayService_LeaseAcquisitionFails_IncrementsLockAcquisitionFailures()
    {
        // Arrange — real decay lease helper over a mux whose LockTakeAsync returns false.
        var opts = Options.Create(new GameKitRankingsOptions());
        var lease = new RankDecayLeaseHelper(
            BuildLockTakeFalseMultiplexer(),
            NullLogger<RankDecayLeaseHelper>.Instance,
            opts);

        var decay = new RankDecayBackgroundService(
            new Mock<IServiceScopeFactory>(MockBehavior.Loose).Object,
            lease,
            new Mock<IClock>(MockBehavior.Loose).Object,
            opts,
            NullLogger<RankDecayBackgroundService>.Instance);

        var capturedValues = new List<long>();
        using var listener = StartLockFailureListener(capturedValues);

        // Act — RunOnceAsync bails out in the acquisition-failed branch before any DB work.
        await decay.RunOnceAsync(CancellationToken.None);

        // Assert — the counter was incremented by exactly 1.
        var single = Assert.Single(capturedValues);
        Assert.Equal(1L, single);
    }
}
