// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Generic paginated result envelope. Uses keyset / cursor pagination (D-12): callers pass the
/// returned <see cref="NextCursor"/> back on the next request to fetch the following page. No
/// offset/limit semantics.
/// </summary>
/// <typeparam name="T">Row shape.</typeparam>
/// <param name="Items">Items in this page.</param>
/// <param name="NextCursor">Opaque cursor string for the next page, or <c>null</c> when no more rows.</param>
/// <param name="HasMore">True when a subsequent page exists (<see cref="NextCursor"/> will be non-null).</param>
public sealed record PaginatedResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,
    bool HasMore)
{
    /// <summary>The empty page — no items, no cursor, no more rows.</summary>
    public static readonly PaginatedResult<T> Empty = new(Array.Empty<T>(), null, false);
}
