// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Query-string parameters for <c>GET /admin/api/players/search</c>. Binds via
/// <c>[AsParameters]</c> because the endpoint is read-only GET — no JSON body (W8: D-16
/// antiforgery applies to mutations only). <see cref="Validators.PlayerSearchRequestValidator"/>
/// still runs through <c>ValidationEndpointFilter&lt;PlayerSearchRequest&gt;</c>.
/// </summary>
/// <param name="Query">Unified search input (UUID / <c>provider:external_id</c> / display-name prefix).</param>
/// <param name="AfterId">Keyset pagination cursor — caller passes the last row's id from the previous page.</param>
/// <param name="PageSize">Desired page size (clamped to [1, 50]).</param>
public sealed record PlayerSearchRequest(string Query, Guid? AfterId, int PageSize = 50);
