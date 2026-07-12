// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Core.Services;

/// <summary>Thrown when an operation (e.g. GDPR delete) targets a player id that does not exist.</summary>
public sealed class PlayerNotFoundException : Exception
{
    /// <summary>The missing player id.</summary>
    public Guid PlayerId { get; }

    /// <summary>Constructs the exception.</summary>
    public PlayerNotFoundException(Guid playerId) : base($"Player {playerId} not found.")
    {
        PlayerId = playerId;
    }
}
