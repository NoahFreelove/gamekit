// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Xunit;

namespace GameKit.Matchmaking.Tests;

/// <summary>
/// xUnit collection definition that serialises all
/// <see cref="System.Diagnostics.Metrics.MeterListener"/>-based tests for
/// <c>GameKit.Matchmaking</c> so they never run concurrently.
/// </summary>
/// <remarks>
/// <para>
/// <c>MatchmakingMeter</c> instruments are static singletons. When multiple test classes
/// subscribe concurrent <see cref="System.Diagnostics.Metrics.MeterListener"/> instances
/// to the same instrument, measurement callbacks fire on the <c>Add</c> caller's thread —
/// writing to <see cref="System.Collections.Generic.List{T}"/> instances that belong to
/// other tests. Since <c>List&lt;T&gt;</c> is not thread-safe, concurrent writes cause
/// data loss and false <c>Assert.Contains</c> failures.
/// </para>
/// <para>
/// Placing all meter-based tests in this collection ensures xUnit runs them sequentially
/// (no cross-test listener contamination) without changing the test logic.
/// </para>
/// </remarks>
[CollectionDefinition("MatchmakingMeterTests", DisableParallelization = true)]
public sealed class MatchmakingMeterCollection
{
    // Marker class — no fixture state. The [CollectionDefinition] attribute is the sole purpose.
}
