// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Rankings.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/players/{id}/rank-adjust</c> (D-19 / RANK-12).
/// Validated by <c>RankAdjustRequestValidator</c> (FluentValidation).
/// </summary>
/// <param name="LadderId">The ladder whose rating should be adjusted (required, non-empty).</param>
/// <param name="NewRating">New rating value (finite double; bounded to [MinRating, MaxRating]).</param>
/// <param name="Reason">Operator reason (3–512 characters, stored verbatim in the audit log).</param>
public sealed record RankAdjustRequest(Guid LadderId, double NewRating, string Reason);
