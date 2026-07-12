// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace Platformer3D.GameServer;

/// <summary>
/// Result of a <see cref="RunSummaryValidator.Validate"/> call.
/// </summary>
public enum RunSummaryValidationResult
{
    /// <summary>The run-summary passed all D-03 sanity checks.</summary>
    Ok,

    /// <summary>
    /// Checkpoint timestamps are not strictly ascending (non-monotonic), or a checkpoint
    /// timestamp falls outside the [StartMs, FinishMs] window.
    /// </summary>
    NonMonotonic,

    /// <summary>
    /// The total run duration (FinishMs − StartMs) is outside the plausible window
    /// [<see cref="RunSummaryValidator.MinPlausibleMs"/>, <see cref="RunSummaryValidator.MaxPlausibleMs"/>].
    /// </summary>
    Implausible,

    /// <summary>
    /// A second <c>run_finish</c> frame arrived for a connection that already completed a run.
    /// Enforced by the per-connection state machine in <see cref="WebSocketGameSession"/>.
    /// </summary>
    DuplicateFinish,
}

/// <summary>
/// Pure D-03 sanity validator for <see cref="RunSummary"/> instances (no I/O, no DI dependencies).
/// Validates monotonic ordered checkpoints, plausible time bounds, and structural integrity.
/// Does NOT perform full re-simulation (D-03 is sanity-level only).
/// </summary>
public static class RunSummaryValidator
{
    /// <summary>Minimum plausible run duration (5 seconds) in milliseconds.</summary>
    public const long MinPlausibleMs = 5_000L;

    /// <summary>Maximum plausible run duration (5 minutes) in milliseconds.</summary>
    public const long MaxPlausibleMs = 300_000L;

    /// <summary>
    /// Validates the <paramref name="summary"/> against all D-03 sanity constraints.
    /// </summary>
    /// <param name="summary">The run-summary to validate.</param>
    /// <returns>
    /// <see cref="RunSummaryValidationResult.Ok"/> when all checks pass; otherwise the
    /// first failing constraint's discriminator.
    /// </returns>
    public static RunSummaryValidationResult Validate(RunSummary summary)
    {
        var totalMs = summary.FinishMs - summary.StartMs;

        // Plausible duration check (D-03).
        if (totalMs < MinPlausibleMs || totalMs > MaxPlausibleMs)
            return RunSummaryValidationResult.Implausible;

        // Monotonic start check: StartMs must be strictly less than the first checkpoint.
        var checkpoints = summary.CheckpointTimesMs;
        if (checkpoints.Count > 0 && summary.StartMs >= checkpoints[0])
            return RunSummaryValidationResult.NonMonotonic;

        // Strictly ascending checkpoints (D-03): each must be greater than the previous.
        for (var i = 1; i < checkpoints.Count; i++)
        {
            if (checkpoints[i] <= checkpoints[i - 1])
                return RunSummaryValidationResult.NonMonotonic;
        }

        // Last checkpoint must be before FinishMs.
        if (checkpoints.Count > 0 && checkpoints[checkpoints.Count - 1] >= summary.FinishMs)
            return RunSummaryValidationResult.NonMonotonic;

        return RunSummaryValidationResult.Ok;
    }
}
