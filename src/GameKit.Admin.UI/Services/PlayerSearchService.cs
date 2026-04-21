// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Admin.UI.Services;

/// <summary>Search-mode discriminator for <see cref="PlayerSearchService.ClassifyInput(string?)"/>.</summary>
public enum SearchMode
{
    /// <summary>Blank / whitespace input — no search performed.</summary>
    None,
    /// <summary>Input parsed as a UUID (with or without hyphens) — id lookup.</summary>
    Id,
    /// <summary><c>provider:external_id</c> shape — identity lookup in <c>player_identities</c>.</summary>
    Identity,
    /// <summary>Free-text input — case-insensitive prefix match on <c>display_name</c>.</summary>
    DisplayName,
}

/// <summary>
/// Classification output for <see cref="PlayerSearchService.ClassifyInput(string?)"/>. Only the
/// fields corresponding to <see cref="Mode"/> are meaningful; the others default to empty.
/// </summary>
/// <param name="Mode">Which branch matched.</param>
/// <param name="Id">Parsed UUID when <see cref="Mode"/> is <see cref="SearchMode.Id"/>.</param>
/// <param name="Provider">Provider discriminator when <see cref="Mode"/> is <see cref="SearchMode.Identity"/>.</param>
/// <param name="ExternalId">External id when <see cref="Mode"/> is <see cref="SearchMode.Identity"/>.</param>
/// <param name="DisplayName">Prefix query when <see cref="Mode"/> is <see cref="SearchMode.DisplayName"/>.</param>
public readonly record struct PlayerSearchClassification(
    SearchMode Mode,
    Guid Id,
    string Provider,
    string ExternalId,
    string DisplayName);

/// <summary>
/// Default <see cref="IPlayerSearchService"/>. Input classification is a pure static helper
/// (<see cref="ClassifyInput(string?)"/>) so unit tests can exercise the branch logic without
/// standing up a DbContext. The query path uses <c>AsNoTracking</c> + projection directly into
/// <see cref="PlayerRow"/> per Phase-1 <c>PlayerEndpoints</c> pattern.
/// </summary>
public sealed class PlayerSearchService : IPlayerSearchService
{
    private readonly GameKitDbContext _ctx;

    /// <summary>Constructs the service.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    public PlayerSearchService(GameKitDbContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    /// <summary>
    /// Classifies <paramref name="raw"/> into a <see cref="SearchMode"/>. Public static so the
    /// unit test suite can verify each branch without spinning up a DbContext; production call
    /// sites go through <see cref="SearchAsync"/>.
    /// </summary>
    /// <param name="raw">Raw user input from the admin search box.</param>
    /// <returns>Classification describing which branch (if any) matched.</returns>
    public static PlayerSearchClassification ClassifyInput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new PlayerSearchClassification(SearchMode.None, default, string.Empty, string.Empty, string.Empty);

        var q = raw.Trim();

        // UUID — with or without hyphens. Guid.TryParse handles both "N" and "D" formats.
        if (Guid.TryParse(q, out var id))
            return new PlayerSearchClassification(SearchMode.Id, id, string.Empty, string.Empty, string.Empty);

        // provider:external_id shape (e.g. "steam:76561...", "discord:1234567890")
        var colon = q.IndexOf(':');
        if (colon > 0 && colon < q.Length - 1)
        {
            var p = q[..colon];
            var ext = q[(colon + 1)..];
            if (p.Length is >= 2 and <= 32 && ext.Length is >= 1 and <= 256)
                return new PlayerSearchClassification(SearchMode.Identity, Guid.Empty, p, ext, string.Empty);
        }

        // Fall through to display-name prefix.
        return new PlayerSearchClassification(SearchMode.DisplayName, Guid.Empty, string.Empty, string.Empty, q);
    }

    /// <inheritdoc />
    public async Task<PaginatedResult<PlayerRow>> SearchAsync(
        string query,
        Guid? afterId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var classification = ClassifyInput(query);
        pageSize = Math.Clamp(pageSize, 1, 50);

        switch (classification.Mode)
        {
            case SearchMode.None:
                return PaginatedResult<PlayerRow>.Empty;

            case SearchMode.Id:
            {
                var player = await _ctx.Set<Player>()
                    .AsNoTracking()
                    .Where(p => p.Id == classification.Id)
                    .Select(p => new PlayerRow(p.Id, p.DisplayName, p.CreatedAt, p.IsBanned))
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                return player is null
                    ? PaginatedResult<PlayerRow>.Empty
                    : new PaginatedResult<PlayerRow>(new[] { player }, null, false);
            }

            case SearchMode.Identity:
            {
                var row = await (
                    from i in _ctx.Set<PlayerIdentity>().AsNoTracking()
                    where i.Provider == classification.Provider && i.ExternalId == classification.ExternalId
                    join p in _ctx.Set<Player>().AsNoTracking() on i.PlayerId equals p.Id
                    select new PlayerRow(p.Id, p.DisplayName, p.CreatedAt, p.IsBanned))
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
                return row is null
                    ? PaginatedResult<PlayerRow>.Empty
                    : new PaginatedResult<PlayerRow>(new[] { row }, null, false);
            }

            default: // DisplayName — keyset prefix
            {
                var q = _ctx.Set<Player>().AsNoTracking()
                    .Where(p => EF.Functions.ILike(p.DisplayName, classification.DisplayName + "%"));
                if (afterId is not null)
                    q = q.Where(p => p.Id < afterId.Value);

                var rows = await q
                    .OrderByDescending(p => p.Id)
                    .Take(pageSize + 1)
                    .Select(p => new PlayerRow(p.Id, p.DisplayName, p.CreatedAt, p.IsBanned))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var hasMore = rows.Count > pageSize;
                if (hasMore) rows.RemoveAt(pageSize);
                return new PaginatedResult<PlayerRow>(
                    rows,
                    hasMore ? rows[^1].Id.ToString() : null,
                    hasMore);
            }
        }
    }
}
