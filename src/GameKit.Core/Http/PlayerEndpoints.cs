// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using GameKit.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Core.Http;

/// <summary>Phase 1 player-list endpoint group. Maps <c>GET /api/players</c> as a paginated list.</summary>
public static class PlayerEndpoints
{
    /// <summary>Adds the GameKit Core player endpoints to the route builder.</summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns>The group builder so callers can further customize (e.g. add rate limits).</returns>
    public static RouteGroupBuilder MapPlayers(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/players").WithTags("GameKit.Core");

        group.MapGet("/", async (GameKitDbContext db, int skip, int take, CancellationToken ct) =>
        {
            var clampedTake = take <= 0 ? 50 : take > 200 ? 200 : take;
            var clampedSkip = skip < 0 ? 0 : skip;

            var rows = await db.Players
                .AsNoTracking()
                .OrderBy(p => p.CreatedAt)
                .ThenBy(p => p.Id)
                .Skip(clampedSkip)
                .Take(clampedTake)
                .Select(p => new
                {
                    id = p.Id,
                    displayName = p.DisplayName,
                    createdAt = p.CreatedAt,
                    lastSeenAt = p.LastSeenAt,
                    isBanned = p.IsBanned,
                })
                .ToListAsync(ct);

            return Results.Ok(rows);
        })
        // Require an authenticated principal. Phase 1 does not ship an authentication handler,
        // so — with the default-deny policy set up by UseGameKit() — this endpoint returns 401
        // until Phase 2 wires GameKit.Auth. Exposing player ids + ban status publicly was a
        // pre-auth security concern flagged in review (WR-05).
        .RequireAuthorization();

        return group;
    }
}
