// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core.Entities;

/// <summary>The lifecycle state of a <see cref="GameSession"/>.</summary>
public enum GameSessionState
{
    /// <summary>Session has been created but has not started yet (players still being assembled).</summary>
    Pending = 0,

    /// <summary>Session is in progress — participants are playing.</summary>
    Active = 1,

    /// <summary>Session ended normally with recorded results.</summary>
    Completed = 2,

    /// <summary>Session was cancelled before or during play (no results recorded).</summary>
    Cancelled = 3,

    /// <summary>Session was abandoned mid-play (partial results may exist; rating impact per game policy).</summary>
    Abandoned = 4,
}

/// <summary>Valid-transition table for <see cref="GameSessionState"/>.</summary>
internal static class GameSessionStateTransitions
{
    /// <summary>Returns true iff <paramref name="to"/> is a permitted successor of <paramref name="from"/>.</summary>
    public static bool IsValidTransition(GameSessionState from, GameSessionState to) =>
        (from, to) switch
        {
            (GameSessionState.Pending, GameSessionState.Active) => true,
            (GameSessionState.Pending, GameSessionState.Cancelled) => true,
            (GameSessionState.Active, GameSessionState.Completed) => true,
            (GameSessionState.Active, GameSessionState.Cancelled) => true,
            (GameSessionState.Active, GameSessionState.Abandoned) => true,
            _ => false,
        };
}
