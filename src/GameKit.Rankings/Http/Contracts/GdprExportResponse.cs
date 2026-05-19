// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GameKit.Rankings.Http.Contracts;

/// <summary>
/// Top-level GDPR export bundle returned by <c>GET /api/players/{id}/export</c> and
/// <c>GET /admin/api/players/{id}/export</c> (RANK-13 / D-15).
/// </summary>
/// <remarks>
/// <para>
/// EVERY property and every nested <c>*Section</c> record carries an explicit
/// <c>[JsonPropertyName("snake_case_key")]</c> attribute. This makes the on-wire shape
/// deterministic and the SC#5 contract test independent of ambient
/// <see cref="System.Text.Json.JsonSerializerOptions"/> — no <c>JsonNamingPolicy</c>,
/// no global registration, no per-endpoint override needed.
/// </para>
/// <para>
/// Sensitive fields are deliberately excluded:
/// <list type="bullet">
///   <item>No <c>password_hash</c> — credentials section exposes metadata only.</item>
///   <item>No raw <c>external_id</c> — identity section exposes only <c>external_id_hash</c>.</item>
///   <item>No refresh-token hashes.</item>
/// </list>
/// </para>
/// </remarks>
public sealed record GdprExportResponse
{
    /// <summary>Core player record fields (D-15).</summary>
    [JsonPropertyName("player")]
    public PlayerSection Player { get; init; } = default!;

    /// <summary>External identity rows — provider + hashed external id only.</summary>
    [JsonPropertyName("identities")]
    public IReadOnlyList<IdentitySection> Identities { get; init; } = Array.Empty<IdentitySection>();

    /// <summary>Password credential metadata — created/updated timestamps only, never the hash.</summary>
    [JsonPropertyName("credentials_metadata")]
    public IReadOnlyList<CredentialMetadataSection> CredentialsMetadata { get; init; } = Array.Empty<CredentialMetadataSection>();

    /// <summary>Session participation history.</summary>
    [JsonPropertyName("sessions")]
    public IReadOnlyList<SessionSection> Sessions { get; init; } = Array.Empty<SessionSection>();

    /// <summary>Rating history from <c>season_rank_archive</c> (seasonal snapshots) and live <c>player_ranks</c>.</summary>
    [JsonPropertyName("rating_history")]
    public IReadOnlyList<RatingHistorySection> RatingHistory { get; init; } = Array.Empty<RatingHistorySection>();

    /// <summary>UTC timestamp at which the export was produced (IClock.UtcNow at export time).</summary>
    [JsonPropertyName("exported_at")]
    public DateTimeOffset ExportedAt { get; init; }
}

/// <summary>Player core-data section of the GDPR export bundle (D-15).</summary>
public sealed record PlayerSection
{
    /// <summary>Player id.</summary>
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    /// <summary>Public display name.</summary>
    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>UTC creation timestamp.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp of the most recent activity. Null until first seen event.</summary>
    [JsonPropertyName("last_seen_at")]
    public DateTimeOffset? LastSeenAt { get; init; }

    /// <summary>Whether the player is currently banned.</summary>
    [JsonPropertyName("is_banned")]
    public bool IsBanned { get; init; }

    /// <summary>UTC timestamp at which the ban was applied. Null when not banned.</summary>
    [JsonPropertyName("banned_at")]
    public DateTimeOffset? BannedAt { get; init; }

    /// <summary>Ban reason text. Null when not banned.</summary>
    [JsonPropertyName("ban_reason")]
    public string? BanReason { get; init; }
}

/// <summary>
/// External identity section of the GDPR export bundle (D-15).
/// Exposes the provider + hashed external id only — raw external id is never exported.
/// </summary>
public sealed record IdentitySection
{
    /// <summary>Provider discriminator (e.g. <c>steam</c>, <c>discord</c>).</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>SHA-256 hash of the raw external id. The raw id is never included.</summary>
    [JsonPropertyName("external_id_hash")]
    public string ExternalIdHash { get; init; } = string.Empty;

    /// <summary>UTC timestamp at which this identity was linked to the player.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Credential metadata section of the GDPR export bundle (D-15).
/// Exposes only timestamps — the <c>password_hash</c> is never exported.
/// </summary>
public sealed record CredentialMetadataSection
{
    /// <summary>UTC timestamp at which the credential was first created.</summary>
    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>UTC timestamp at which the password was last updated.</summary>
    [JsonPropertyName("last_used_at")]
    public DateTimeOffset LastUsedAt { get; init; }
}

/// <summary>Session participation history entry for the GDPR export bundle (D-15).</summary>
public sealed record SessionSection
{
    /// <summary>Session id.</summary>
    [JsonPropertyName("session_id")]
    public Guid SessionId { get; init; }

    /// <summary>Ladder id this session belongs to. Null for unranked sessions.</summary>
    [JsonPropertyName("ladder_id")]
    public Guid? LadderId { get; init; }

    /// <summary>Team number (0-indexed).</summary>
    [JsonPropertyName("team")]
    public int Team { get; init; }

    /// <summary>Session result for this participant.</summary>
    [JsonPropertyName("result")]
    public string? Result { get; init; }

    /// <summary>Rating snapshot at session start.</summary>
    [JsonPropertyName("rating_before")]
    public double? RatingBefore { get; init; }

    /// <summary>Rating snapshot at session end.</summary>
    [JsonPropertyName("rating_after")]
    public double? RatingAfter { get; init; }

    /// <summary>UTC timestamp at which the session reached a terminal state.</summary>
    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }
}

/// <summary>Rating history entry in the GDPR export bundle (D-15).</summary>
/// <remarks>
/// Sourced from <c>season_rank_archive</c> (seasonal snapshots). The live <c>player_ranks</c>
/// row is also included as a snapshot of the current season without an archived <c>season_id</c>.
/// </remarks>
public sealed record RatingHistorySection
{
    /// <summary>Ladder id.</summary>
    [JsonPropertyName("ladder_id")]
    public Guid LadderId { get; init; }

    /// <summary>Season id for archived snapshots. Null for the live current-season snapshot.</summary>
    [JsonPropertyName("season_id")]
    public Guid? SeasonId { get; init; }

    /// <summary>Rating at the snapshot moment.</summary>
    [JsonPropertyName("rating")]
    public double Rating { get; init; }

    /// <summary>Rating deviation at the snapshot moment.</summary>
    [JsonPropertyName("rd")]
    public double Rd { get; init; }

    /// <summary>Volatility at the snapshot moment.</summary>
    [JsonPropertyName("volatility")]
    public double Volatility { get; init; }

    /// <summary>UTC timestamp at which this snapshot was captured.</summary>
    [JsonPropertyName("snapshot_at")]
    public DateTimeOffset SnapshotAt { get; init; }
}
