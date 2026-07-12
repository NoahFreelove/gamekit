// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/players/{id}/ban</c>. The reason field is required
/// (D-09) and validated by <see cref="Validators.BanPlayerRequestValidator"/> to be 3-512
/// characters — short-circuits BEFORE the SERIALIZABLE ban transaction opens.
/// </summary>
/// <param name="Reason">Free-text ban reason (3-512 chars). Emitted into the audit row verbatim.</param>
public sealed record BanPlayerRequest(string Reason);
