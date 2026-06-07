// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Lobby.Entities;

/// <summary>
/// Lifecycle state of a <see cref="Lobby"/>. Stored as <c>integer</c> at the SQL level
/// (Phase 5 mandatory pattern — <c>HasConversion&lt;string&gt;()</c> is forbidden).
/// </summary>
public enum LobbyState
{
    /// <summary>Lobby exists; accepting new members.</summary>
    Open = 0,

    /// <summary>All-ready check in progress; members must mark ready before matchmaking.</summary>
    ReadyChecking = 1,

    /// <summary>Locked; no new members. Waiting for matchmaking to complete.</summary>
    Closed = 2,

    /// <summary>Matchmaking submitted; terminal state for this session.</summary>
    InGame = 3,
}
