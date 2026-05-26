// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Presence.Configuration;

/// <summary>
/// Root options for <c>GameKit.Presence</c>. Populated via
/// <c>services.AddGameKit(...).AddPresence(opts =&gt; ...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// Default values are pinned per CONTEXT D-01 (Phase 6 — Presence + OpenAPI + Distribution):
/// 30-second TTL with a 10-second client cadence gives a 3× safety factor — a player
/// must miss three consecutive heartbeats before they expire to <c>Offline</c>.
/// </para>
/// <para>
/// Validation is performed by <see cref="PresenceOptionsValidator"/> at host startup via
/// <c>OptionsBuilder.ValidateOnStart()</c>. Operators who tune these values MUST preserve
/// the <c>HeartbeatIntervalSeconds * 3 &lt;= TtlSeconds</c> invariant; otherwise the
/// validator fails fast at startup.
/// </para>
/// </remarks>
public sealed class GameKitPresenceOptions
{
    /// <summary>
    /// Time-to-live (seconds) applied to the per-player Redis presence key on every heartbeat
    /// write. Default <c>30</c> seconds (CONTEXT D-01).
    /// </summary>
    /// <remarks>
    /// When the TTL expires, the player transitions to <see cref="GameKit.Core.Services.PresenceStatus.Offline"/>
    /// implicitly (the key simply disappears from Redis). The default aligns with the documented
    /// arena-style cadence — operators running slow-tick simulation games may raise this.
    /// </remarks>
    public int TtlSeconds { get; set; } = 30;

    /// <summary>
    /// Expected client heartbeat cadence (seconds). Default <c>10</c> seconds (CONTEXT D-01).
    /// </summary>
    /// <remarks>
    /// <para>
    /// This field is advisory — the server does not enforce a minimum cadence; it is published
    /// as the recommended cadence for SDK consumers. The Presence subsystem tolerates up to
    /// three consecutive missed pings before declaring the player offline (3× safety factor).
    /// </para>
    /// <para>
    /// <see cref="PresenceOptionsValidator"/> enforces
    /// <c>HeartbeatIntervalSeconds * 3 &lt;= TtlSeconds</c>; pushing the interval above one-third
    /// of the TTL collapses the safety margin and triggers a startup-time validation failure.
    /// </para>
    /// </remarks>
    public int HeartbeatIntervalSeconds { get; set; } = 10;
}
