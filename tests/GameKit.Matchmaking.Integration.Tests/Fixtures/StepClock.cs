// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Integration.Tests.Fixtures;

/// <summary>Adjustable clock for matchmaking integration tests — allows advancing simulated time deterministically.</summary>
/// <remarks>
/// Verbatim port of <c>tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs:420</c>
/// (the Phase-4 <c>StepClock</c>). Reused by Phase 5 plans 05-04 through 05-08 to advance
/// bracket-flex, accept-timeout, and decline-cooldown clocks without sleeping in tests.
/// </remarks>
internal sealed class StepClock : GameKit.Core.Services.IClock
{
    private DateTimeOffset _current;

    /// <summary>Initializes the clock at the given starting time.</summary>
    public StepClock(DateTimeOffset start) => _current = start;

    /// <inheritdoc />
    public DateTimeOffset UtcNow => _current;

    /// <summary>Advances the simulated clock by <paramref name="delta"/>.</summary>
    public void Advance(TimeSpan delta) => _current += delta;
}
