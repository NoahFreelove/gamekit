// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using Microsoft.AspNetCore.Authentication;

namespace GameKit.Rankings.Authentication;

/// <summary>
/// Options for the <c>GameKitServiceToken</c> authentication scheme. Empty for v1 — no
/// configurable knobs are exposed. A future v2 may add <c>IMemoryCache</c> TTL settings
/// per Pitfall 10 (DB hot-read optimization).
/// </summary>
/// <remarks>
/// TODO(v2): Add <c>CacheTtlSeconds</c> property when the token-lookup IMemoryCache optimization
/// is implemented (Pitfall 10 — DB hot-read accepted for v1; 300 req/min rate limit keeps load
/// tractable per D-10).
/// </remarks>
public sealed class ServiceTokenAuthenticationOptions : AuthenticationSchemeOptions
{
}
