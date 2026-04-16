// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace GameKit.Core.Services;

/// <summary>
/// Default <see cref="ICurrentPlayer"/> sourced from the <c>HttpContext</c>'s authenticated user.
/// Reads the custom <c>gamekit_player_id</c> claim first, then falls back to the standard <c>sub</c> / <c>NameIdentifier</c>.
/// </summary>
/// <remarks>
/// In Phase 1 there is no <c>GameKit.Auth</c> package yet — <c>HttpContext.User</c> is never authenticated and this accessor
/// always returns null. Phase 2 populates the claim when JWTs are issued.
/// </remarks>
public sealed class HttpContextCurrentPlayer : ICurrentPlayer
{
    private readonly IHttpContextAccessor _accessor;

    /// <summary>Constructs the accessor.</summary>
    public HttpContextCurrentPlayer(IHttpContextAccessor accessor) => _accessor = accessor;

    /// <inheritdoc />
    public Guid? PlayerId
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user is null || user.Identity?.IsAuthenticated != true) return null;

            var raw = user.FindFirst("gamekit_player_id")?.Value
                      ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            return Guid.TryParse(raw, out var id) ? id : null;
        }
    }
}
