// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Envelope returned by <c>/admin/api/players/search</c>. Wraps a
/// <see cref="PaginatedResult{T}"/> of <see cref="PlayerRow"/> with a debug-friendly
/// <see cref="Origin"/> hint indicating which branch of <c>PlayerSearchService.ClassifyInput</c>
/// fired (D-11: <c>id</c>, <c>identity</c>, or <c>displayname</c>).
/// </summary>
/// <param name="Result">The paginated row set.</param>
/// <param name="Origin">Which search branch produced the result: <c>id</c> / <c>identity</c> / <c>displayname</c> / <c>none</c>.</param>
public sealed record PlayerSearchResult(
    PaginatedResult<PlayerRow> Result,
    string Origin);
