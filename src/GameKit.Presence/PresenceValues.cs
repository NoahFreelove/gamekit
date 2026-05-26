// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Presence;

/// <summary>
/// Canonical Redis value constants for player presence state. Kept separate from
/// <see cref="PresenceRedisKeys"/> (key formatter) for separation of concerns — the
/// heartbeat Lua script + <c>IPresenceWriter</c> implementations + the read-path
/// status-string parser all reference these constants instead of inlining string
/// literals.
/// </summary>
/// <remarks>
/// <para>
/// These string values appear inside the embedded Lua script (<c>'online'</c>,
/// <c>'in_match'</c>) and at every Redis write site. If a value ever changes, this
/// class is the single source of truth — update here and ensure the Lua literal
/// stays in sync (the script is asserted character-for-character in unit tests).
/// </para>
/// <para>
/// PATTERNS warning #6: <see cref="InMatch"/> is reserved for game-server-authoritative
/// writes via <c>IPresenceWriter.WriteInMatchAsync</c>. The player-facing heartbeat
/// endpoint NEVER writes this value; the Lua precedence script ensures heartbeats
/// cannot downgrade an in-match key to online (CONTEXT D-03).
/// </para>
/// </remarks>
public static class PresenceValues
{
    /// <summary>Value stored at <c>presence:{playerId}</c> when the player is Online.</summary>
    public const string Online = "online";

    /// <summary>Value stored at <c>presence:{playerId}</c> when the player is currently in a game session.</summary>
    public const string InMatch = "in_match";
}
