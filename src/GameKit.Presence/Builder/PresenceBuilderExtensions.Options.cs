// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Presence.Builder;

/// <summary>
/// Partial-class slot for future options-shaping extensions (e.g. named-options helpers,
/// PostConfigure callbacks). Empty in v1 — the Presence options surface is small
/// (TtlSeconds + HeartbeatIntervalSeconds) so all wiring lives in the base file
/// <c>PresenceBuilderExtensions.cs</c>. Reserved here per the Matchmaking partial-split
/// convention (PATTERNS Block 5) so a v2 multi-device aggregator or per-environment
/// override helper has a natural home without forcing a base-file rewrite.
/// </summary>
public static partial class PresenceBuilderExtensions
{
}
