// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using System.Threading;
using GameKit.Core.Services;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Thread-safe rolling-window error counter. Buckets are fixed-width (configurable, default 1s); the
/// total window equals <see cref="AdminPanelOptions.HealthErrorRateWindow"/> (default 5m). Increments
/// are O(1) lock-free (<see cref="Interlocked.Increment(ref int)"/>); reads (<see cref="RecentErrorCount"/>)
/// rotate and sum. Older buckets naturally decay as newer ticks overwrite them. See
/// <c>.planning/phases/03-admin-ui/03-RESEARCH.md</c> §Health panel (lines 894-938).
/// </summary>
public sealed class ErrorRateRingBuffer
{
    private readonly int[] _buckets;          // one counter per bucket
    private readonly long _bucketTicks;       // fixed bucket width (ticks)
    private readonly IClock _clock;
    private long _headBucketStartTicks;       // start tick of the bucket at _headIndex
    private int _headIndex;                   // index of the currently-live bucket
    private readonly object _rotateGate = new();

    /// <summary>Constructs the buffer from admin options + clock.</summary>
    /// <param name="opts">Admin options supplying the window + bucket size.</param>
    /// <param name="clock">Clock abstraction (time source).</param>
    public ErrorRateRingBuffer(GameKitAdminOptions opts, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(clock);
        var window = opts.Panel.HealthErrorRateWindow;
        var bucket = opts.Panel.HealthErrorRateBucketSize;
        if (bucket <= TimeSpan.Zero)
            throw new ArgumentException("bucket size must be > 0", nameof(opts));
        if (window < bucket)
            throw new ArgumentException("window must be >= bucket size", nameof(opts));
        var count = (int)Math.Max(1, window.Ticks / bucket.Ticks);
        _buckets = new int[count];
        _bucketTicks = bucket.Ticks;
        _clock = clock;
        _headBucketStartTicks = NormalizeToBucket(_clock.UtcNow.UtcTicks);
        _headIndex = 0;
    }

    /// <summary>Records one error event at the current time. O(1), lock-free on the hot path.</summary>
    public void IncrementError()
    {
        AdvanceIfNeeded();
        Interlocked.Increment(ref _buckets[_headIndex]);
    }

    /// <summary>Returns the sum of all buckets in the current rolling window.</summary>
    public int RecentErrorCount()
    {
        AdvanceIfNeeded();
        var sum = 0;
        for (var i = 0; i < _buckets.Length; i++)
            sum += Volatile.Read(ref _buckets[i]);
        return sum;
    }

    private long NormalizeToBucket(long ticks) => ticks - (ticks % _bucketTicks);

    private void AdvanceIfNeeded()
    {
        var nowTicks = _clock.UtcNow.UtcTicks;
        var elapsed = nowTicks - _headBucketStartTicks;
        if (elapsed < _bucketTicks) return;  // still in the same bucket — hot path exits here

        lock (_rotateGate)
        {
            // Re-check under lock (double-checked locking pattern).
            elapsed = _clock.UtcNow.UtcTicks - _headBucketStartTicks;
            if (elapsed < _bucketTicks) return;

            var stepsRaw = elapsed / _bucketTicks;
            var steps = (int)Math.Min(stepsRaw, _buckets.Length);
            for (var i = 0; i < steps; i++)
            {
                _headIndex = (_headIndex + 1) % _buckets.Length;
                _buckets[_headIndex] = 0;  // zero the bucket we're rotating onto
            }
            _headBucketStartTicks += stepsRaw * _bucketTicks;
        }
    }
}
