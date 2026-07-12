// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking;

/// <summary>
/// Options for the matchmaking ticker <c>BackgroundService</c> (MATCH-07 / MATCH-08).
/// </summary>
/// <remarks>
/// Mirrors <c>GameKit.Rankings.GameKitRankingsTickerOptions</c> in shape; the matchmaking
/// ticker runs ~10× more frequently (500ms vs 60s) because the matcher must drain Redis
/// sorted sets at sub-second cadence for the SC#3 1k-concurrent-ticket load target.
/// </remarks>
public sealed class GameKitMatchmakingTickerOptions
{
    /// <summary>
    /// Ticker interval in milliseconds. Default <c>500</c> ms.
    /// </summary>
    /// <remarks>Default per RESEARCH §Architecture diagram (matcher tick = ~500 ms).</remarks>
    public int TickIntervalMs { get; set; } = 500;

    /// <summary>
    /// Redis distributed-lock TTL in seconds. Default <c>90</c>.
    /// </summary>
    /// <remarks>
    /// Mirrors the Rankings ticker lock TTL — allows one missed self-renewal before a standby
    /// instance picks up leadership (MATCH-08).
    /// </remarks>
    public int LockTtlSeconds { get; set; } = 90;

    /// <summary>
    /// Redis key for the matchmaker leader-election lock. Default
    /// <c>"gamekit:matchmaking:matcher:lock"</c>.
    /// </summary>
    /// <remarks>
    /// Default per RESEARCH §Decision 11 (live state surface). The literal string is also
    /// pinned as <see cref="GameKit.Matchmaking.Redis.MatchmakingRedisKeys.MatcherLock"/>;
    /// operators overriding this value MUST update both surfaces consistently.
    /// </remarks>
    public string LockKey { get; set; } = "gamekit:matchmaking:matcher:lock";

    /// <summary>
    /// Maximum milliseconds the ticker may spend inside a single iteration before yielding.
    /// Default <c>50</c> ms.
    /// </summary>
    /// <remarks>
    /// Default per RESEARCH §Decision 13 (load test). The matcher will bail out of the current
    /// tick once <c>Stopwatch.ElapsedMilliseconds</c> exceeds this budget so the 500ms cadence
    /// is preserved under load.
    /// </remarks>
    public int MaxIterationBudgetMs { get; set; } = 50;
}
