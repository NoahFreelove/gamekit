// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Core.Services;

/// <summary>Accessor for the current authenticated player. Returns null when there is no authenticated player.</summary>
public interface ICurrentPlayer
{
    /// <summary>The current player id, or null when there is no <c>HttpContext</c> or no authenticated user.</summary>
    Guid? PlayerId { get; }
}
