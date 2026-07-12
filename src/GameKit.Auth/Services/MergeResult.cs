// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Auth.Services;

/// <summary>Outcome discriminator of <c>IAccountMergeService.MergeAsync</c>.</summary>
public enum MergeResultKind
{
    /// <summary>The merge completed — source player has been tombstoned and all FKs re-pointed.</summary>
    Merged = 0,

    /// <summary>
    /// The source player was already merged into the target player — idempotent no-op.
    /// An existing <c>account_merges</c> row with <c>Status &gt;= Committed</c> was found.
    /// </summary>
    AlreadyMerged = 1,
}

/// <summary>
/// The outcome of an <c>IAccountMergeService.MergeAsync</c> call.
/// Always carries the surviving <see cref="TargetPlayerId"/> so the HTTP endpoint can return it
/// in the response body without exposing the source player id (SC#5).
/// </summary>
public sealed class MergeResult
{
    /// <summary>The outcome discriminator.</summary>
    public MergeResultKind Kind { get; }

    /// <summary>The surviving (target) player's id. Never the source player id (SC#5).</summary>
    public Guid TargetPlayerId { get; }

    private MergeResult(MergeResultKind kind, Guid targetPlayerId)
    {
        Kind = kind;
        TargetPlayerId = targetPlayerId;
    }

    /// <summary>Convenience factory for the successful merge outcome.</summary>
    /// <param name="targetPlayerId">The surviving player's id.</param>
    public static MergeResult Merged(Guid targetPlayerId) =>
        new(MergeResultKind.Merged, targetPlayerId);

    /// <summary>Convenience factory for the idempotent "already merged" outcome.</summary>
    /// <param name="targetPlayerId">The surviving player's id.</param>
    public static MergeResult AlreadyMerged(Guid targetPlayerId) =>
        new(MergeResultKind.AlreadyMerged, targetPlayerId);
}
