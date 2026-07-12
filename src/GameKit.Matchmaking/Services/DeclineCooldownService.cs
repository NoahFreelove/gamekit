// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Entities;
using Microsoft.Extensions.Options;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IDeclineCooldownService"/> backed by a queryable
/// <see cref="IDeclineHistoryReader"/> against the Plan 05-02 <c>decline_history</c> table.
/// Implements the CONTEXT D-08 escalating cooldown ladder; all time math uses the explicit
/// <c>now</c> argument supplied by the caller (Pitfall §4 — never <see cref="DateTime"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Count-to-step mapping (CONTEXT D-08):</b>
/// <list type="bullet">
///   <item>0 declines ⇒ not locked.</item>
///   <item>1 decline ⇒ <c>Step1Minutes</c> (default 3).</item>
///   <item>2 declines ⇒ <c>Step2Minutes</c> (default 15).</item>
///   <item>3 or more declines ⇒ <c>Step3Minutes</c> (default 30) — the ladder caps here.</item>
/// </list>
/// Example: a player who declined once 1 min ago is locked for <c>Step1 − 1 ≈ 2</c> minutes;
/// a player whose 3rd decline within the window was 31 min ago is no longer locked (Step3 is
/// 30 min, so <c>30 − 31 &lt; 0</c> ⇒ <see cref="CooldownStatus.IsLocked"/> = <see langword="false"/>).
/// </para>
/// <para>
/// <b>Query shape:</b> the reader selects up to 3 rows where
/// <c>declined_at &gt; (now − WindowMinutes)</c>, ordered most-recent first. We only need
/// the last 3 because the ladder caps at step 3.
/// </para>
/// </remarks>
public sealed class DeclineCooldownService : IDeclineCooldownService
{
    private readonly IDeclineHistoryReader _reader;
    private readonly GameKitMatchmakingCooldownOptions _opts;

    /// <summary>Constructs the service.</summary>
    /// <param name="reader">Decline-history reader (Postgres-backed in production; in-memory in unit tests).</param>
    /// <param name="options">Matchmaking options snapshot (cooldown ladder lives at <c>Cooldown.*</c>).</param>
    public DeclineCooldownService(
        IDeclineHistoryReader reader,
        IOptions<GameKitMatchmakingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);
        _reader = reader;
        _opts = options.Value.Cooldown;
    }

    /// <inheritdoc />
    public async Task<CooldownStatus> GetCurrentCooldownAsync(
        Guid playerId, DateTimeOffset now, CancellationToken ct = default)
    {
        var windowStart = now - TimeSpan.FromMinutes(_opts.WindowMinutes);

        // Only need the last 3 declines — the ladder caps at step 3 (D-08).
        var declines = await _reader.GetRecentDeclinesAsync(playerId, windowStart, take: 3, ct).ConfigureAwait(false);

        if (declines.Count == 0)
            return new CooldownStatus(IsLocked: false, RetryAfter: null);

        // Reader contract: ordered most-recent first.
        var latest = declines[0].DeclinedAt;

        // Count-to-step mapping — switch expression (XML doc literal example above).
        var stepDuration = declines.Count switch
        {
            1 => TimeSpan.FromMinutes(_opts.Step1Minutes),
            2 => TimeSpan.FromMinutes(_opts.Step2Minutes),
            _ => TimeSpan.FromMinutes(_opts.Step3Minutes),
        };

        var retryAfter = (latest + stepDuration) - now;
        if (retryAfter <= TimeSpan.Zero)
            return new CooldownStatus(IsLocked: false, RetryAfter: null);

        return new CooldownStatus(IsLocked: true, RetryAfter: retryAfter);
    }

    /// <inheritdoc />
    public Task RecordDeclineAsync(
        Guid playerId, Guid proposalId, DateTimeOffset declinedAt, CancellationToken ct = default)
        => _reader.RecordDeclineAsync(playerId, proposalId, declinedAt, ct);
}

/// <summary>
/// Storage seam consumed by <see cref="DeclineCooldownService"/>. The default implementation
/// (registered in <c>MatchmakingBuilderExtensions.Accept.cs</c>) wraps a scoped
/// <c>GameKitDbContext</c> and queries <c>decline_history</c>; unit tests pass an in-memory
/// fake that drives the cooldown arithmetic without spinning up Postgres.
/// </summary>
public interface IDeclineHistoryReader
{
    /// <summary>
    /// Return up to <paramref name="take"/> rows from <c>decline_history</c> for
    /// <paramref name="playerId"/> with <see cref="DeclineHistory.DeclinedAt"/> strictly
    /// greater than <paramref name="since"/>, ordered most-recent first.
    /// </summary>
    /// <param name="playerId">Canonical player id.</param>
    /// <param name="since">Exclusive lower bound on <c>declined_at</c>.</param>
    /// <param name="take">Maximum rows to return (callers pass 3 — ladder cap).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Most-recent-first list of matching rows.</returns>
    Task<IReadOnlyList<DeclineHistory>> GetRecentDeclinesAsync(
        Guid playerId, DateTimeOffset since, int take, CancellationToken ct);

    /// <summary>
    /// Append a row to <c>decline_history</c>.
    /// </summary>
    /// <param name="playerId">Canonical player id.</param>
    /// <param name="proposalId">Proposal id the player declined or timed out on.</param>
    /// <param name="declinedAt">UTC timestamp at which the decline occurred.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Awaitable.</returns>
    Task RecordDeclineAsync(Guid playerId, Guid proposalId, DateTimeOffset declinedAt, CancellationToken ct);
}
