// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth.Services;

/// <summary>
/// The reason a merge was rejected before the SERIALIZABLE transaction opened.
/// Checked synchronously from precondition guards in <c>AccountMergeService</c>.
/// </summary>
public enum MergeConflictReason
{
    /// <summary>Source and target players are members of the same active party — merging would create a self-reference in party_members.</summary>
    PlayersInSameParty = 0,

    /// <summary>The target player is currently banned — merging into a banned account is disallowed.</summary>
    TargetBanned = 1,

    /// <summary>Source and target player ids are identical — a player cannot be merged into themselves.</summary>
    SelfMerge = 2,

    /// <summary>
    /// The source player has already been merged (its <c>account_merges</c> row is
    /// <see cref="GameKit.Auth.Entities.MergeStatus.Committed"/> or
    /// <see cref="GameKit.Auth.Entities.MergeStatus.RedisCleaned"/>) into a DIFFERENT target
    /// than the one requested.
    /// </summary>
    SourceAlreadyMerged = 3,
}

/// <summary>
/// Thrown by <c>AccountMergeService.MergeAsync</c> when a merge precondition check fails before
/// the crash-resume transaction opens. The <see cref="Reason"/> property allows the HTTP layer to
/// map the conflict to a structured 409 response body.
/// </summary>
public sealed class MergeConflictException : Exception
{
    /// <summary>The specific reason the merge was rejected.</summary>
    public MergeConflictReason Reason { get; }

    /// <summary>
    /// Constructs a new <see cref="MergeConflictException"/> with a reason and descriptive message.
    /// </summary>
    /// <param name="reason">The conflict reason enum value.</param>
    /// <param name="message">Human-readable message (not exposed to callers; used for logging).</param>
    public MergeConflictException(MergeConflictReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }
}
