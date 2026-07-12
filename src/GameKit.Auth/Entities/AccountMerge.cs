// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Text.Json;

namespace GameKit.Auth.Entities;

/// <summary>
/// Current state of an account merge operation. Stored as an integer column (no string conversion)
/// per the project's mandatory integer-enum convention (STATE.md Phase 5, PITFALLS #13).
/// </summary>
public enum MergeStatus
{
    /// <summary>The merge has been initiated but the FK-surgery transaction has not yet committed.</summary>
    Pending = 0,

    /// <summary>The FK-surgery transaction committed — all foreign keys now point at the target player.</summary>
    Committed = 1,

    /// <summary>Post-commit Redis presence/matchmaking keys for the source player have been cleaned up.</summary>
    RedisCleaned = 2,
}

/// <summary>
/// Crash-resume checkpoint row for an account merge operation (AUTH-24, SC#1). One row per merge
/// request: created in <see cref="MergeStatus.Pending"/> at the start of the SERIALIZABLE
/// transaction, advanced to <see cref="MergeStatus.Committed"/> after the FK-surgery commits, and
/// finally advanced to <see cref="MergeStatus.RedisCleaned"/> after Redis key cleanup.
/// </summary>
/// <remarks>
/// The UNIQUE index on <see cref="SourcePlayerId"/> (in <c>account_merges</c>) enforces that a
/// source player can only be merged once (SC#1 double-merge prevention). A second concurrent
/// attempt to insert a row with the same <c>SourcePlayerId</c> will receive a 23505 unique-violation
/// and the service layer returns <see cref="GameKit.Auth.Services.MergeResultKind.AlreadyMerged"/>.
/// </remarks>
public sealed class AccountMerge
{
    /// <summary>Row id — UUIDv7 assigned by <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The player being absorbed into <see cref="TargetPlayerId"/>. After the merge this player
    /// is soft-deleted (tombstoned) with <c>Player.MergedIntoPlayerId</c> set to
    /// <see cref="TargetPlayerId"/>. No FK constraint — the source row may be hard-deleted later
    /// by GDPR erasure without affecting this row (bare UUID column).
    /// </summary>
    public Guid SourcePlayerId { get; set; }

    /// <summary>
    /// The surviving player. All foreign keys that referenced <see cref="SourcePlayerId"/> are
    /// re-pointed here during the merge transaction. FK → <c>players.id</c> ON DELETE RESTRICT —
    /// the target cannot be GDPR-deleted while this merge record exists (T-10-02-02).
    /// </summary>
    public Guid TargetPlayerId { get; set; }

    /// <summary>
    /// Current state of the merge operation. Stored as <c>integer</c> (no string conversion).
    /// Default <see cref="MergeStatus.Pending"/> (= 0).
    /// </summary>
    public MergeStatus Status { get; set; }

    /// <summary>
    /// Id of the admin user who initiated the merge. Nullable — system-initiated merges (future)
    /// will have a null actor.
    /// </summary>
    public Guid? ActorId { get; set; }

    /// <summary>UTC timestamp at which the merge was first requested.</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>UTC timestamp at which <see cref="MergeStatus.Committed"/> was recorded. Null until then.</summary>
    public DateTimeOffset? CommittedAt { get; set; }

    /// <summary>UTC timestamp at which <see cref="MergeStatus.RedisCleaned"/> was recorded. Null until then.</summary>
    public DateTimeOffset? RedisCleanedAt { get; set; }

    /// <summary>
    /// Sparse JSONB metadata (e.g. operator notes, source snapshot for audit).
    /// Infrequently-written per CORE-17 constraint.
    /// </summary>
    public JsonDocument? Metadata { get; set; }
}
