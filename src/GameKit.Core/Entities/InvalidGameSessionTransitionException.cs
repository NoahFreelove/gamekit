// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Core.Entities;

/// <summary>Thrown when a <see cref="GameSession"/> state transition is not permitted by the transition table.</summary>
public sealed class InvalidGameSessionTransitionException : InvalidOperationException
{
    /// <summary>The current state the session was in when the illegal transition was attempted.</summary>
    public GameSessionState From { get; }

    /// <summary>The state the caller attempted to transition to.</summary>
    public GameSessionState To { get; }

    /// <summary>Constructs the exception with the illegal transition pair.</summary>
    public InvalidGameSessionTransitionException(GameSessionState from, GameSessionState to)
        : base($"Invalid GameSession transition: {from} -> {to}")
    {
        From = from;
        To = to;
    }
}
