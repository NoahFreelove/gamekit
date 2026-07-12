// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking;

/// <summary>
/// Escalating decline-cooldown options (Dota-2-style ladder).
/// </summary>
/// <remarks>
/// Default values per CONTEXT D-08: first decline / timeout = 3 min, second within the
/// configurable window = 15 min, third = 30 min. Cooldown bookkeeping persists in the
/// <c>decline_history</c> Postgres entity (Plan 05-02) so the ladder survives app restart.
/// </remarks>
public sealed class GameKitMatchmakingCooldownOptions
{
    /// <summary>
    /// Rolling window in minutes within which prior declines escalate the cooldown step.
    /// Default <c>60</c> minutes.
    /// </summary>
    /// <remarks>Default per CONTEXT D-08.</remarks>
    public int WindowMinutes { get; set; } = 60;

    /// <summary>
    /// Cooldown duration after the first decline / timeout. Default <c>3</c> minutes.
    /// </summary>
    /// <remarks>Default per CONTEXT D-08.</remarks>
    public int Step1Minutes { get; set; } = 3;

    /// <summary>
    /// Cooldown duration after the second decline / timeout within <see cref="WindowMinutes"/>.
    /// Default <c>15</c> minutes.
    /// </summary>
    /// <remarks>Default per CONTEXT D-08.</remarks>
    public int Step2Minutes { get; set; } = 15;

    /// <summary>
    /// Cooldown duration after the third (or later) decline / timeout within
    /// <see cref="WindowMinutes"/>. Default <c>30</c> minutes.
    /// </summary>
    /// <remarks>Default per CONTEXT D-08.</remarks>
    public int Step3Minutes { get; set; } = 30;
}
