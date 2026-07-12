// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace GameKit.Presence.Configuration;

/// <summary>
/// Fail-fast validator for <see cref="GameKitPresenceOptions"/>. Throws
/// <see cref="OptionsValidationException"/> at host startup when any required invariant is
/// violated — mitigates the misconfiguration class where a too-short TTL combined with a
/// too-slow heartbeat cadence causes all clients to flap between Online and Offline.
/// </summary>
/// <remarks>
/// Mirrors the pure-function validator pattern from <c>MatchmakingOptionsValidator</c>
/// (Plan 05-03): the <see cref="Validate(GameKitPresenceOptions, out IReadOnlyList{string})"/>
/// overload is callable from unit tests without spinning up a host.
/// </remarks>
public sealed class PresenceOptionsValidator : IValidateOptions<GameKitPresenceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GameKitPresenceOptions options)
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
    public static bool Validate(GameKitPresenceOptions options, out IReadOnlyList<string> failures)
    {
        var problems = new List<string>();

        if (options.TtlSeconds < 1)
        {
            problems.Add(
                $"{nameof(GameKitPresenceOptions.TtlSeconds)} must be >= 1 second (got {options.TtlSeconds}).");
        }

        if (options.HeartbeatIntervalSeconds < 1)
        {
            problems.Add(
                $"{nameof(GameKitPresenceOptions.HeartbeatIntervalSeconds)} must be >= 1 second " +
                $"(got {options.HeartbeatIntervalSeconds}).");
        }

        // CONTEXT D-01 — 3× safety factor: the player must be able to lose three consecutive
        // heartbeats before the TTL expires and they transition to Offline. Reject configurations
        // that compress the safety margin below this floor.
        if (options.TtlSeconds >= 1 && options.HeartbeatIntervalSeconds >= 1)
        {
            var requiredTtl = options.HeartbeatIntervalSeconds * 3;
            if (requiredTtl > options.TtlSeconds)
            {
                problems.Add(
                    $"{nameof(GameKitPresenceOptions.HeartbeatIntervalSeconds)} * 3 ({requiredTtl}) " +
                    $"must be <= {nameof(GameKitPresenceOptions.TtlSeconds)} ({options.TtlSeconds}) " +
                    "to preserve the 3× safety factor (CONTEXT D-01).");
            }
        }

        failures = problems;
        return problems.Count == 0;
    }
}
