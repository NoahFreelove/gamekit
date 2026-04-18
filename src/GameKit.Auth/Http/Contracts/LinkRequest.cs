// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Http.Contracts;

/// <summary>
/// Request body for <c>POST /auth/link/{provider}</c>. For Steam, carries the already-verified
/// external id (the endpoint runs <see cref="Providers.Steam.SteamOpenIdVerifier.VerifyAsync"/>
/// on the query when <see cref="ExternalId"/> is null). For Discord, the endpoint expects the
/// external id in the body — Discord callback state is not re-plumbed for /auth/link.
/// </summary>
/// <param name="ExternalId">Provider-side external id (Steam64 / Discord snowflake) or null to trigger re-verification.</param>
public sealed record LinkRequest(string? ExternalId);
