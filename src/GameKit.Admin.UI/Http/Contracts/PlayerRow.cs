// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>Player row projected for admin list / search views.</summary>
/// <param name="Id">Player id (UUIDv7).</param>
/// <param name="DisplayName">Public display name.</param>
/// <param name="CreatedAt">UTC creation timestamp.</param>
/// <param name="IsBanned">Current ban state.</param>
public sealed record PlayerRow(
    Guid Id,
    string DisplayName,
    DateTimeOffset CreatedAt,
    bool IsBanned);
