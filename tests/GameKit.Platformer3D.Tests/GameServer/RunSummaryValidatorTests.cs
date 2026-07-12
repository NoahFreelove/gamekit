// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using Platformer3D.GameServer;
using Xunit;

namespace GameKit.Platformer3D.Tests.GameServer;

/// <summary>
/// Unit tests for <see cref="RunSummaryValidator"/> — D-03 sanity validation (monotonic
/// ordered checkpoints, plausible time bounds, structural integrity).
/// No I/O, no Testcontainers — pure deterministic unit tests.
/// </summary>
public sealed class RunSummaryValidatorTests
{
    // ─── Helper: build a valid summary with fixed epoch timestamps ─────────────

    /// <summary>
    /// Returns a structurally valid <see cref="RunSummary"/> with:
    /// startMs=1_000_000, three ascending checkpoints, finishMs=startMs+45_000 (45 seconds).
    /// </summary>
    private static RunSummary MakeValid(
        long startMs = 1_000_000L,
        long finishOffset = 45_000L,
        IReadOnlyList<long>? checkpoints = null)
    {
        checkpoints ??= new[] { startMs + 5_000L, startMs + 20_000L, startMs + 35_000L };
        return new RunSummary(
            SessionId: Guid.NewGuid(),
            StartMs: startMs,
            CheckpointTimesMs: checkpoints,
            FinishMs: startMs + finishOffset);
    }

    // ─── Happy path ──────────────────────────────────────────────────────────

    /// <summary>
    /// A well-formed summary with strictly ascending checkpoints and a plausible duration
    /// must validate as <see cref="RunSummaryValidationResult.Ok"/>.
    /// </summary>
    [Fact]
    public void Valid_Summary_ReturnsOk()
    {
        var summary = MakeValid();
        var result = RunSummaryValidator.Validate(summary);
        Assert.Equal(RunSummaryValidationResult.Ok, result);
    }

    /// <summary>
    /// A summary with no checkpoints but a valid duration is also Ok.
    /// </summary>
    [Fact]
    public void NoCheckpoints_ValidDuration_ReturnsOk()
    {
        var summary = new RunSummary(
            SessionId: Guid.NewGuid(),
            StartMs: 0L,
            CheckpointTimesMs: Array.Empty<long>(),
            FinishMs: 30_000L);   // 30 seconds — within [5 000, 300 000]

        Assert.Equal(RunSummaryValidationResult.Ok, RunSummaryValidator.Validate(summary));
    }

    // ─── Plausible bounds (D-03) ─────────────────────────────────────────────

    /// <summary>
    /// A run shorter than 5 seconds is implausible (D-03 lower bound).
    /// </summary>
    [Fact]
    public void Sub5Second_Duration_ReturnsImplausible()
    {
        var summary = MakeValid(
            startMs: 1_000_000L,
            finishOffset: 4_999L,   // 4.999 seconds — below MinPlausibleMs=5_000
            checkpoints: new[] { 1_000_500L });   // single valid checkpoint

        Assert.Equal(RunSummaryValidationResult.Implausible, RunSummaryValidator.Validate(summary));
    }

    /// <summary>
    /// A run longer than 5 minutes is implausible (D-03 upper bound).
    /// </summary>
    [Fact]
    public void Over5Minute_Duration_ReturnsImplausible()
    {
        var startMs = 1_000_000L;
        var finishMs = startMs + 300_001L;   // 300 001 ms = 5 min + 1 ms
        var summary = new RunSummary(
            SessionId: Guid.NewGuid(),
            StartMs: startMs,
            CheckpointTimesMs: new[] { startMs + 1_000L },
            FinishMs: finishMs);

        Assert.Equal(RunSummaryValidationResult.Implausible, RunSummaryValidator.Validate(summary));
    }

    /// <summary>
    /// Boundary: exactly 5 seconds (MinPlausibleMs) is valid.
    /// </summary>
    [Fact]
    public void ExactMinBoundary_ReturnsOk()
    {
        var summary = MakeValid(finishOffset: RunSummaryValidator.MinPlausibleMs,
            checkpoints: new long[] { });   // no checkpoints — not required for duration check
        // Rebuild without checkpoints to avoid monotonic issue
        var s = new RunSummary(Guid.NewGuid(), 0L, Array.Empty<long>(), RunSummaryValidator.MinPlausibleMs);
        Assert.Equal(RunSummaryValidationResult.Ok, RunSummaryValidator.Validate(s));
    }

    /// <summary>
    /// Boundary: exactly 300 000 ms (MaxPlausibleMs) is valid.
    /// </summary>
    [Fact]
    public void ExactMaxBoundary_ReturnsOk()
    {
        var s = new RunSummary(Guid.NewGuid(), 0L, Array.Empty<long>(), RunSummaryValidator.MaxPlausibleMs);
        Assert.Equal(RunSummaryValidationResult.Ok, RunSummaryValidator.Validate(s));
    }

    // ─── Non-monotonic checkpoints (D-03) ────────────────────────────────────

    /// <summary>
    /// Checkpoints with a decreasing timestamp are non-monotonic (D-03).
    /// </summary>
    [Fact]
    public void DecreasingCheckpoints_ReturnsNonMonotonic()
    {
        var startMs = 1_000_000L;
        var summary = new RunSummary(
            SessionId: Guid.NewGuid(),
            StartMs: startMs,
            CheckpointTimesMs: new[]
            {
                startMs + 10_000L,
                startMs + 8_000L,   // DECREASING — non-monotonic
                startMs + 20_000L,
            },
            FinishMs: startMs + 45_000L);

        Assert.Equal(RunSummaryValidationResult.NonMonotonic, RunSummaryValidator.Validate(summary));
    }

    /// <summary>
    /// Two equal checkpoint timestamps are non-monotonic (strict ascending required).
    /// </summary>
    [Fact]
    public void EqualCheckpoints_ReturnsNonMonotonic()
    {
        var startMs = 1_000_000L;
        var summary = new RunSummary(
            SessionId: Guid.NewGuid(),
            StartMs: startMs,
            CheckpointTimesMs: new[]
            {
                startMs + 10_000L,
                startMs + 10_000L,   // EQUAL — not strictly ascending
            },
            FinishMs: startMs + 45_000L);

        Assert.Equal(RunSummaryValidationResult.NonMonotonic, RunSummaryValidator.Validate(summary));
    }

    /// <summary>
    /// A checkpoint timestamp at or before StartMs is non-monotonic.
    /// </summary>
    [Fact]
    public void CheckpointAtOrBeforeStart_ReturnsNonMonotonic()
    {
        var startMs = 1_000_000L;
        var summary = new RunSummary(
            SessionId: Guid.NewGuid(),
            StartMs: startMs,
            CheckpointTimesMs: new[] { startMs },   // exactly at start — not strictly after
            FinishMs: startMs + 30_000L);

        Assert.Equal(RunSummaryValidationResult.NonMonotonic, RunSummaryValidator.Validate(summary));
    }

    /// <summary>
    /// A checkpoint timestamp at or after FinishMs is non-monotonic.
    /// </summary>
    [Fact]
    public void CheckpointAtFinish_ReturnsNonMonotonic()
    {
        var startMs = 1_000_000L;
        var finishMs = startMs + 30_000L;
        var summary = new RunSummary(
            SessionId: Guid.NewGuid(),
            StartMs: startMs,
            CheckpointTimesMs: new[] { startMs + 10_000L, finishMs },   // last = finishMs — not strictly before
            FinishMs: finishMs);

        Assert.Equal(RunSummaryValidationResult.NonMonotonic, RunSummaryValidator.Validate(summary));
    }
}
