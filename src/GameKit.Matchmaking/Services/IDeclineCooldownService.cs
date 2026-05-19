// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Application service that evaluates the escalating decline cooldown defined by CONTEXT
/// D-08. Wires the enqueue endpoint (Plan 05-08) — a player who has recently declined or
/// timed out a proposal is locked out of the queue for an escalating duration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ladder (CONTEXT D-08):</b> within the rolling <c>WindowMinutes</c> window —
/// 1 prior decline ⇒ <c>Step1Minutes</c> (default 3); 2 ⇒ <c>Step2Minutes</c> (default 15);
/// 3 or more ⇒ <c>Step3Minutes</c> (default 30). The cooldown clock starts at the
/// <em>most recent</em> decline's timestamp; a player who declined 5 minutes ago with one
/// prior decline within the window is locked for <c>Step2 − 5</c> more minutes.
/// </para>
/// <para>
/// <b>Time source:</b> every time comparison uses the explicit <c>now</c> argument so the
/// service is deterministic against <see cref="GameKit.Core.Services.IClock"/> (Pitfall §4 —
/// never <see cref="DateTime"/>.<c>Now</c>). The HTTP layer passes <c>IClock.UtcNow</c>.
/// </para>
/// </remarks>
public interface IDeclineCooldownService
{
    /// <summary>
    /// Compute the current cooldown for <paramref name="playerId"/>.
    /// </summary>
    /// <param name="playerId">Canonical player id.</param>
    /// <param name="now">Authoritative UTC clock reading from <see cref="GameKit.Core.Services.IClock"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="CooldownStatus"/> indicating whether the player is currently locked and,
    /// if so, the remaining time before they may re-enqueue.
    /// </returns>
    Task<CooldownStatus> GetCurrentCooldownAsync(Guid playerId, DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Record a decline / timeout for <paramref name="playerId"/>. Appends a row to
    /// <c>decline_history</c>. Subsequent <see cref="GetCurrentCooldownAsync"/> calls will
    /// observe the new row and may escalate the cooldown step.
    /// </summary>
    /// <param name="playerId">Canonical player id.</param>
    /// <param name="proposalId">The proposal id the player declined or timed out on.</param>
    /// <param name="declinedAt">UTC timestamp at which the decline occurred.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Awaitable.</returns>
    Task RecordDeclineAsync(Guid playerId, Guid proposalId, DateTimeOffset declinedAt, CancellationToken ct = default);
}

/// <summary>
/// Result of <see cref="IDeclineCooldownService.GetCurrentCooldownAsync"/>.
/// </summary>
/// <param name="IsLocked">
/// <see langword="true"/> when the player is currently in a cooldown window and must wait
/// <see cref="RetryAfter"/> before the enqueue endpoint accepts a new ticket. When
/// <see langword="false"/>, <see cref="RetryAfter"/> is <see langword="null"/>.
/// </param>
/// <param name="RetryAfter">
/// Remaining cooldown duration. <see langword="null"/> when <see cref="IsLocked"/> is
/// <see langword="false"/>.
/// </param>
public sealed record CooldownStatus(bool IsLocked, TimeSpan? RetryAfter);
