// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Text.Json;

namespace GameKit.Auth.Entities;

/// <summary>
/// One row per external identity linked to a GameKit <c>Player</c>. A player may have multiple
/// identities (one per provider: steam, discord, ...) — and the UNIQUE(provider, external_id)
/// constraint is the database-level guard that serializes the concurrent guest-upgrade race
/// (CONTEXT D-14, AUTH-13, ROADMAP success criterion #4).
/// </summary>
public sealed class PlayerIdentity
{
    /// <summary>Identity row id — UUIDv7 assigned by <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <c>players.id</c>. ON DELETE CASCADE — identities have no meaning without their player.</summary>
    public Guid PlayerId { get; set; }

    /// <summary>Provider discriminator — stable string: <c>steam</c>, <c>discord</c>. Never a user-supplied value.</summary>
    public required string Provider { get; set; }

    /// <summary>External id returned by the provider (Steam64 decimal string / Discord snowflake). Opaque to GameKit.</summary>
    public required string ExternalId { get; set; }

    /// <summary>Provider-reported display name. Not authoritative; <c>Player.DisplayName</c> is.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Provider-reported avatar URL. Not fetched or cached by GameKit.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>Sparse JSONB metadata (e.g. raw provider claims). Infrequently-written per CORE-17 constraint.</summary>
    public JsonDocument? Metadata { get; set; }

    /// <summary>UTC timestamp at which this identity row was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the most recent provider-refresh of <c>DisplayName</c> / <c>AvatarUrl</c> / <c>Metadata</c>.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
