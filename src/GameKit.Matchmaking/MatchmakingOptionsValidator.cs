// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace GameKit.Matchmaking;

/// <summary>
/// Fail-fast validator for <see cref="GameKitMatchmakingOptions"/>. Throws
/// <see cref="OptionsValidationException"/> at host startup when any required invariant is
/// violated; mitigates T-05-03-01 (misconfigured matchmaker causes runtime divide-by-zero
/// or infinite loop).
/// </summary>
/// <remarks>
/// Per-ladder invariants (<c>BracketRampSeconds &gt; 0</c>, <c>BracketEnd &gt;= BracketStart</c>,
/// <c>MaxPartyRatingSpread is null or &gt; 0</c>) are enforced eagerly at builder time inside
/// <c>GameKitMatchmakingBuilder.AddLadder</c> (Task 2) — the registration-time fail-fast is
/// strictly stronger than IValidateOptions because the ladder list is not part of the
/// options tree.
/// </remarks>
public sealed class MatchmakingOptionsValidator : IValidateOptions<GameKitMatchmakingOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GameKitMatchmakingOptions options)
    {
        return Validate(options, out var failures)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Pure-function validation helper used by both the <see cref="IValidateOptions{T}"/>
    /// surface and the unit-level test harness (avoids a hosting dependency in tests).
    /// </summary>
    /// <param name="options">The options to validate.</param>
    /// <param name="failures">Populated with one diagnostic per rule that failed.</param>
    /// <returns><c>true</c> when validation passes; <c>false</c> otherwise.</returns>
    public static bool Validate(GameKitMatchmakingOptions options, out IReadOnlyList<string> failures)
    {
        var problems = new List<string>();

        // Top-level invariants
        if (options.AcceptTimeoutSeconds < 1)
            problems.Add($"{nameof(GameKitMatchmakingOptions.AcceptTimeoutSeconds)} must be >= 1 second (got {options.AcceptTimeoutSeconds}).");

        if (options.MatchmakingEnqueueRatePerMinute < 1)
            problems.Add($"{nameof(GameKitMatchmakingOptions.MatchmakingEnqueueRatePerMinute)} must be >= 1 (got {options.MatchmakingEnqueueRatePerMinute}).");

        if (options.TicketRetentionDays < 1)
            problems.Add($"{nameof(GameKitMatchmakingOptions.TicketRetentionDays)} must be >= 1 day (got {options.TicketRetentionDays}).");

        // Ticker invariants
        if (options.Ticker.TickIntervalMs < 1)
            problems.Add($"{nameof(GameKitMatchmakingTickerOptions.TickIntervalMs)} must be >= 1 ms (got {options.Ticker.TickIntervalMs}).");

        if (options.Ticker.LockTtlSeconds < 1)
            problems.Add($"{nameof(GameKitMatchmakingTickerOptions.LockTtlSeconds)} must be >= 1 second (got {options.Ticker.LockTtlSeconds}).");

        if (options.Ticker.MaxIterationBudgetMs < 1)
            problems.Add($"{nameof(GameKitMatchmakingTickerOptions.MaxIterationBudgetMs)} must be >= 1 ms (got {options.Ticker.MaxIterationBudgetMs}).");

        if (string.IsNullOrWhiteSpace(options.Ticker.LockKey))
            problems.Add($"{nameof(GameKitMatchmakingTickerOptions.LockKey)} must be non-empty.");

        // Cooldown invariants
        if (options.Cooldown.WindowMinutes < 1)
            problems.Add($"{nameof(GameKitMatchmakingCooldownOptions.WindowMinutes)} must be >= 1 minute (got {options.Cooldown.WindowMinutes}).");

        if (options.Cooldown.Step1Minutes < 0)
            problems.Add($"{nameof(GameKitMatchmakingCooldownOptions.Step1Minutes)} must be >= 0 (got {options.Cooldown.Step1Minutes}).");

        if (options.Cooldown.Step2Minutes < 0)
            problems.Add($"{nameof(GameKitMatchmakingCooldownOptions.Step2Minutes)} must be >= 0 (got {options.Cooldown.Step2Minutes}).");

        if (options.Cooldown.Step3Minutes < 0)
            problems.Add($"{nameof(GameKitMatchmakingCooldownOptions.Step3Minutes)} must be >= 0 (got {options.Cooldown.Step3Minutes}).");

        // Analytics invariants
        if (options.Analytics.ChannelCapacity < 100)
            problems.Add($"{nameof(GameKitMatchmakingAnalyticsOptions.ChannelCapacity)} must be >= 100 (got {options.Analytics.ChannelCapacity}).");

        if (options.Analytics.DrainBatchSize < 1)
            problems.Add($"{nameof(GameKitMatchmakingAnalyticsOptions.DrainBatchSize)} must be >= 1 (got {options.Analytics.DrainBatchSize}).");

        if (options.Analytics.DrainIntervalSeconds < 1)
            problems.Add($"{nameof(GameKitMatchmakingAnalyticsOptions.DrainIntervalSeconds)} must be >= 1 second (got {options.Analytics.DrainIntervalSeconds}).");

        if (options.Analytics.PollyMaxRetryAttempts < 0)
            problems.Add($"{nameof(GameKitMatchmakingAnalyticsOptions.PollyMaxRetryAttempts)} must be >= 0 (got {options.Analytics.PollyMaxRetryAttempts}).");

        if (options.Analytics.PollyBaseDelayMs < 1)
            problems.Add($"{nameof(GameKitMatchmakingAnalyticsOptions.PollyBaseDelayMs)} must be >= 1 ms (got {options.Analytics.PollyBaseDelayMs}).");

        if (options.Analytics.PollyTimeoutSeconds < 1)
            problems.Add($"{nameof(GameKitMatchmakingAnalyticsOptions.PollyTimeoutSeconds)} must be >= 1 second (got {options.Analytics.PollyTimeoutSeconds}).");

        // Reconciler invariants
        if (options.Reconciler.SweepIntervalSeconds < 5)
            problems.Add($"{nameof(GameKitMatchmakingReconcilerOptions.SweepIntervalSeconds)} must be >= 5 seconds (got {options.Reconciler.SweepIntervalSeconds}).");

        if (options.Reconciler.StaleTicketThresholdMinutes < 1)
            problems.Add($"{nameof(GameKitMatchmakingReconcilerOptions.StaleTicketThresholdMinutes)} must be >= 1 minute (got {options.Reconciler.StaleTicketThresholdMinutes}).");

        if (options.Reconciler.OrphanSessionThresholdMinutes < 1)
            problems.Add($"{nameof(GameKitMatchmakingReconcilerOptions.OrphanSessionThresholdMinutes)} must be >= 1 minute (got {options.Reconciler.OrphanSessionThresholdMinutes}).");

        failures = problems;
        return problems.Count == 0;
    }
}
