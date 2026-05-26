// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Presence;

/// <summary>
/// Centralised Redis key constants + formatters for <c>GameKit.Presence</c>. Every Redis
/// key written by the package flows through this class so that key-layout changes are made
/// in exactly one place. Mirrors the per-package convention from
/// <c>GameKit.Matchmaking.Redis.MatchmakingRedisKeys</c>.
/// </summary>
/// <remarks>
/// <para>
/// CONTEXT D-04 — Phase 6 ships a single Redis key per player (<c>presence:{playerId}</c>)
/// with last-write-wins semantics across multiple devices. Per-device aggregation is
/// deferred to v2; for v1 any device's heartbeat keeps the player Online and whichever
/// device heartbeats most recently wins.
/// </para>
/// <para>
/// The <see cref="PrefixOnline"/> / <see cref="PrefixInMatch"/> constants are the *values*
/// written to the key, not key prefixes. They are exposed here so the Lua precedence script
/// in <c>RedisPresenceProvider</c> and the SCAN-based <see cref="GameKit.Core.Services.IPresenceProvider.GetStatusAsync"/>
/// reader can reference a single source of truth.
/// </para>
/// </remarks>
public static class PresenceRedisKeys
{
    /// <summary>
    /// Value written to the player presence key when the player is <c>Online</c> (heartbeat
    /// fresh, not currently in a game session). Read by
    /// <see cref="GameKit.Core.Services.IPresenceProvider.GetStatusAsync"/> and written by the
    /// heartbeat Lua script when no <see cref="PrefixInMatch"/> marker is present.
    /// </summary>
    public const string PrefixOnline = "online";

    /// <summary>
    /// Value written to the player presence key when the player is currently inside a
    /// game session. Set by the <see cref="GameKit.Core.Services.ISessionLifecycleObserver"/>
    /// implementation when <c>POST /api/sessions/{id}/start</c> fires (Phase 6 / D-03).
    /// </summary>
    /// <remarks>
    /// Per PATTERNS warning #6, the heartbeat Lua script MUST NOT downgrade an
    /// <see cref="PrefixInMatch"/> value to <see cref="PrefixOnline"/> — it may only refresh
    /// the TTL on an in-match key. The game-server is authoritative; player JWTs cannot
    /// write this value via the heartbeat endpoint.
    /// </remarks>
    public const string PrefixInMatch = "in_match";

    /// <summary>
    /// SCAN pattern used by <c>RedisPresenceProvider.GetOnlinePlayerIdsAsync</c> to enumerate
    /// every player presence key. Must be paired with <see cref="StackExchange.Redis.IServer.KeysAsync"/>
    /// (SCAN-based) NOT the synchronous <c>Keys()</c> primitive — see PATTERNS warning #6 and
    /// RESEARCH §Pitfall anti-pattern line 872.
    /// </summary>
    public const string ScanPattern = "presence:*";

    /// <summary>Per-player presence key.</summary>
    /// <param name="playerId">The player identifier.</param>
    /// <returns>The fully-qualified Redis key <c>presence:{playerId}</c>.</returns>
    public static string Player(Guid playerId) => $"presence:{playerId}";
}
