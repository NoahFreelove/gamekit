// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Presence.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Presence.Http;

/// <summary>
/// Maps the player-facing Presence HTTP surface. Currently exposes
/// <c>POST /api/presence/heartbeat</c> (Phase 6 D-02). The endpoint requires the
/// default JWT-Bearer authentication scheme from Phase 2 (the consumer must call
/// <c>UseGameKitAuth</c> upstream); there is intentionally no rate limit (CONTEXT D-05).
/// </summary>
public static class PresenceEndpoints
{
    /// <summary>
    /// Maps the Presence endpoints onto the provided route builder.
    /// <list type="bullet">
    ///   <item><c>POST /api/presence/heartbeat</c> — idempotent heartbeat write (JWT-required, no rate limit, empty body, 204 on success).</item>
    /// </list>
    /// </summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapPresenceEndpoints(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapPost("/api/presence/heartbeat", HeartbeatAsync)
            .RequireAuthorization()
            .WithTags("GameKit.Presence");

        return routes;
    }

    // ---- handlers ----

    private static async Task<IResult> HeartbeatAsync(
        HttpContext http,
        IPresenceWriter writer,
        CancellationToken ct)
    {
        if (!TryGetPlayerId(http, out var playerId))
        {
            // Mirrors the PartyEndpoints / RankingsPlayerEndpoints idiom: a missing or
            // unparseable sub claim is a 403 (the user authenticated but cannot be tied
            // to a player id), not a 401.
            return Results.Forbid();
        }

        await writer.WriteHeartbeatAsync(playerId, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    /// <summary>Extracts and parses the player id from the JWT <c>sub</c> / <c>NameIdentifier</c> claim.</summary>
    private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
    {
        playerId = default;
        var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? http.User.FindFirst("sub")?.Value;
        return sub is not null && Guid.TryParse(sub, out playerId);
    }
}
