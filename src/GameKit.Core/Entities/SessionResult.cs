// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core.Entities;

/// <summary>The outcome for a single <see cref="SessionParticipant"/> once the session completes.</summary>
public enum SessionResult
{
    /// <summary>Participant won.</summary>
    Win = 0,

    /// <summary>Participant lost.</summary>
    Loss = 1,

    /// <summary>Participant drew / tied.</summary>
    Draw = 2,

    /// <summary>Participant abandoned the session before it completed (may incur rating penalty per game policy).</summary>
    Abandoned = 3,
}
