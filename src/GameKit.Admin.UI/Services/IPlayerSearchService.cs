// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Unified player-search service (D-11). A single query string is auto-classified by
/// <see cref="PlayerSearchService.ClassifyInput(string?)"/> into one of four modes
/// (None / Id / Identity / DisplayName) and dispatched to the appropriate query.
/// </summary>
public interface IPlayerSearchService
{
    /// <summary>
    /// Searches players by the unified query. UUID → id lookup; <c>provider:external_id</c> →
    /// identity lookup; otherwise prefix (case-insensitive) display-name match with keyset
    /// pagination (D-12).
    /// </summary>
    /// <param name="query">Unified search input (id / identity / display-name prefix).</param>
    /// <param name="afterId">Optional keyset cursor — return rows with id &lt; afterId (DESC order).</param>
    /// <param name="pageSize">Desired page size (clamped to 1..50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated player rows; empty envelope when the input is blank.</returns>
    Task<PaginatedResult<PlayerRow>> SearchAsync(
        string query,
        Guid? afterId,
        int pageSize,
        CancellationToken cancellationToken);
}
