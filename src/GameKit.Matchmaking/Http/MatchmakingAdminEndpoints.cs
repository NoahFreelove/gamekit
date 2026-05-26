// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Http.EndpointFilters;
using GameKit.Matchmaking.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GameKit.Matchmaking.Http;

/// <summary>
/// Maps Matchmaking-specific admin HTTP endpoints onto a route group (Plan 05-08 / MATCH-14).
/// Two verbs: pause-queue and drain-queue, both per-ladder per RESEARCH §OQ-5.
/// </summary>
/// <remarks>
/// <para>
/// Authorization constants are referenced as string literals because <c>GameKit.Matchmaking</c>
/// does NOT have a runtime API dependency on <c>GameKit.Admin.UI</c> at the policy layer
/// (D-22 invariant; mirrors the Rankings.AdminEndpoints pattern). The audit row is written
/// inside <see cref="IMatchmakingControlService"/> — the cross-package integration point.
/// </para>
/// <para>
/// Phase 5 UAT-2 D1 refactor: handlers now delegate to <see cref="IMatchmakingControlService"/>
/// so the Blazor admin chrome (PauseQueueDialog / DrainQueueDialog) can invoke the same
/// behaviour through DI rather than HTTP loopback.
/// </para>
/// <para>
/// Cookie scheme: <c>GameKitAdmin</c> (<c>AdminAuthenticationSchemeConstants.Scheme</c>).
/// Superadmin policy: <c>gamekit.admin.superadmin</c> (<c>AdminPolicies.Superadmin</c>).
/// </para>
/// </remarks>
public static class MatchmakingAdminEndpoints
{
    // Source of truth: GameKit.Admin.UI.Authorization.AdminPolicies.Superadmin
    private const string SuperadminPolicy = "gamekit.admin.superadmin";

    /// <summary>Maps the matchmaking admin endpoints onto the provided route group.</summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapMatchmakingAdmin(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/admin/api/matchmaking");

        // Antiforgery filter chain matches the Rankings precedent
        // (RankingsAdminEndpoints.cs:83-85). Without this filter, a logged-in admin
        // could be CSRF'd by an attacker page into pausing/draining any ladder —
        // T-05-08-05 (CSRF on superadmin state-changing POST) remediation.
        group.MapPost("/pause-queue", PauseQueueAsync)
            .RequireAuthorization(SuperadminPolicy)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        group.MapPost("/drain-queue", DrainQueueAsync)
            .RequireAuthorization(SuperadminPolicy)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        return routes;
    }

    /// <summary>Request body for pause/drain — carries the ladder scope + reason.</summary>
    public sealed record MatchmakingControlRequest(Guid LadderId, string Reason);

    // ---- handlers ----

    private static async Task<IResult> PauseQueueAsync(
        MatchmakingControlRequest req,
        HttpContext http,
        IMatchmakingControlService control,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);
        await control.PauseAsync(req.LadderId, req.Reason, actorId, ct).ConfigureAwait(false);
        return Results.Ok(new { paused = true, ladderId = req.LadderId });
    }

    private static async Task<IResult> DrainQueueAsync(
        MatchmakingControlRequest req,
        HttpContext http,
        IMatchmakingControlService control,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);
        await control.DrainAsync(req.LadderId, req.Reason, actorId, ct).ConfigureAwait(false);
        return Results.Ok(new { drain = true, ladderId = req.LadderId });
    }

    private static Guid GetAdminId(HttpContext http)
    {
        var nameId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(nameId, out var id)
            ? id
            : throw new UnauthorizedAccessException(
                "Admin id claim is missing or malformed — SignInAsync did not populate NameIdentifier.");
    }
}
