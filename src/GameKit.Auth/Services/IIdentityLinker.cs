// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;

/// <summary>
/// Links a <c>(provider, external_id)</c> tuple to a GameKit <c>Player</c> (AUTH-14). This is the
/// only production writer of <c>player_identities</c> during Phase-2 guest-upgrade and OAuth-link
/// flows. Uses a Postgres SERIALIZABLE transaction so concurrent link attempts for the same
/// external identity either serialize cleanly or one loses on the UNIQUE(provider, external_id)
/// constraint (CONTEXT D-14 race anchor; ROADMAP success criterion #4 at the service layer).
/// </summary>
/// <remarks>
/// Error-code mapping (RESEARCH §8.5):
/// <list type="bullet">
///   <item><c>23505</c> unique_violation → <see cref="LinkResultKind.AlreadyLinkedToOtherPlayer"/>.</item>
///   <item><c>40001</c> serialization_failure → retried up to 3 times before re-throwing.</item>
/// </list>
/// Audit actions (RESEARCH §8.10): <c>auth.identity.linked</c> on success;
/// <c>auth.identity.link_failed_collision</c> with <c>reason="cross_player_collision"</c> on 23505.
/// </remarks>
public interface IIdentityLinker
{
    /// <summary>
    /// Attempts to link <paramref name="provider"/> + <paramref name="externalId"/> to
    /// <paramref name="playerId"/>. Returns a <see cref="LinkResult"/> whose <see cref="LinkResult.Kind"/>
    /// discriminates the three possible outcomes — caller maps to HTTP codes.
    /// </summary>
    /// <param name="playerId">The player who wants the identity bound to them.</param>
    /// <param name="provider">Provider discriminator (<c>steam</c>, <c>discord</c>, etc.).</param>
    /// <param name="externalId">Provider external id (Steam64 / Discord snowflake / etc.).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="LinkResult"/> carrying the outcome.</returns>
    Task<LinkResult> LinkAsync(
        Guid playerId,
        string provider,
        string externalId,
        CancellationToken cancellationToken = default);
}
