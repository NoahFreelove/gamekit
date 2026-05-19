// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Rankings.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Rankings.Http;

/// <summary>
/// Maps player-facing Rankings endpoints (RANK-13 / D-15 / D-16).
/// </summary>
/// <remarks>
/// Called by the consumer's pipeline configuration. Requires <c>UseGameKitAuth</c> to have
/// run in the middleware pipeline so the JWT Bearer scheme is active for authorization.
/// </remarks>
public static class RankingsPlayerEndpoints
{
    /// <summary>
    /// Maps the player-facing Rankings endpoints onto the provided route builder.
    /// Adds:
    /// <list type="bullet">
    ///   <item><c>GET /api/players/{id}/export</c> — requires player JWT; sub claim must match {id} (D-16).</item>
    /// </list>
    /// </summary>
    /// <param name="routes">The endpoint route builder to register routes on.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapRankingsPlayer(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        // GET /api/players/{id}/export — player-JWT scheme, sub claim must match route {id}.
        routes.MapGet("/api/players/{id:guid}/export", PlayerGdprExportAsync)
            .RequireAuthorization(); // default JWT-Bearer scheme from Phase 2

        return routes;
    }

    // ---- handlers ----

    private static async Task<IResult> PlayerGdprExportAsync(
        Guid id,
        HttpContext http,
        IGdprExportService svc,
        CancellationToken ct)
    {
        // D-16: sub claim must match route {id}. Mismatch → 403 Forbidden.
        var subClaim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? http.User.FindFirst("sub")?.Value;

        if (subClaim is null || !Guid.TryParse(subClaim, out var subId) || subId != id)
            return Results.Forbid();

        try
        {
            var response = await svc.ExportAsync(id, ct).ConfigureAwait(false);
            return response is null
                ? Results.NotFound(new { error = "player_not_found", playerId = id })
                : Results.Ok(response);
        }
        catch (GdprExportPayloadTooLargeException ex)
        {
            return Results.Problem(
                title: "Export payload too large",
                detail: ex.Message,
                statusCode: StatusCodes.Status413RequestEntityTooLarge);
        }
    }
}
