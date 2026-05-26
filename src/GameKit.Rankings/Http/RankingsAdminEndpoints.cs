// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Http.EndpointFilters;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using GameKit.Rankings.Http.Contracts;
using GameKit.Rankings.Http.EndpointFilters;
using GameKit.Rankings.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Http;

/// <summary>
/// Maps Rankings-specific admin HTTP endpoints onto a route group.
/// Called by the consumer's pipeline configuration after <c>MapGameKitAdmin</c>.
/// </summary>
/// <remarks>
/// <para>
/// Authorization constants are referenced as string literals in this file because
/// <c>GameKit.Rankings</c> does NOT project-reference <c>GameKit.Admin.UI</c>
/// (D-22 invariant — only Admin.UI references Rankings, not the reverse). Comments
/// below point to the source-of-truth constants in Admin.UI.
/// </para>
/// <para>
/// Cookie scheme constant: <c>GameKit.Admin.UI.Authentication.AdminAuthenticationSchemeConstants.Scheme = "GameKitAdmin"</c>.
/// Superadmin policy constant: <c>GameKit.Admin.UI.Authorization.AdminPolicies.Superadmin = "gamekit.admin.superadmin"</c>.
/// Admin policy constant: <c>GameKit.Admin.UI.Authorization.AdminPolicies.Admin = "gamekit.admin.admin"</c>.
/// </para>
/// </remarks>
public static class RankingsAdminEndpoints
{
    // Source of truth: GameKit.Admin.UI.Authorization.AdminPolicies.Superadmin
    private const string SuperadminPolicy = "gamekit.admin.superadmin";

    // Source of truth: GameKit.Admin.UI.Authorization.AdminPolicies.Admin
    private const string AdminPolicy = "gamekit.admin.admin";

    // Audit action constant — mirrors AdminAuditActions.PlayerGdprExport in Admin.UI.
    // Rankings cannot reference Admin.UI (D-22 invariant), so the literal is duplicated here.
    // Source of truth: GameKit.Admin.UI.Services.AdminAuditActions.PlayerGdprExport
    private const string AuditActionGdprExport = "admin.player.gdpr_export";

    // Audit action constant — mirrors AdminAuditActions.PlayerRankAdjust in Admin.UI.
    // Source of truth: GameKit.Admin.UI.Services.AdminAuditActions.PlayerRankAdjust
    private const string AuditActionRankAdjust = "admin.player.rank_adjust";

    /// <summary>
    /// Maps the rankings admin endpoints onto the provided route group.
    /// Adds:
    /// <list type="bullet">
    ///   <item><c>POST /admin/api/ladders/{id}/end-season</c> — superadmin + antiforgery + validator</item>
    ///   <item><c>GET /admin/api/leaderboard</c> — admin + query params</item>
    ///   <item><c>GET /admin/api/players/{id}/export</c> — superadmin GDPR export + audit row (D-16)</item>
    ///   <item><c>POST /admin/api/players/{id}/rank-adjust</c> — superadmin + antiforgery + validator (D-19)</item>
    /// </list>
    /// </summary>
    /// <param name="routes">The endpoint route builder to register routes on.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapRankingsAdmin(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/admin/api");

        // POST /admin/api/ladders/{id}/end-season
        // Superadmin + antiforgery + validator. Antiforgery BEFORE validator (CSRF short-circuits before body deserialization).
        // The admin cookie scheme is baked into the SuperadminPolicy at DI time (AddGameKitAdmin registers both policies
        // with .AddAuthenticationSchemes(AdminCookieScheme)). No per-endpoint scheme override needed.
        group.MapPost("/ladders/{id:guid}/end-season", EndSeasonAsync)
            .RequireAuthorization(SuperadminPolicy)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .AddEndpointFilter<ValidationEndpointFilter<EndSeasonRequest>>();

        // GET /admin/api/ladders — admin-policy list of all ladders. Phase 5 UAT-2 D1: the
        // command-palette ladder-search subview needs this to populate target picker rows
        // for end-season, pause-queue, and drain-queue verbs. No filter/search complexity
        // for v1 — operators typically run 1-3 ladders, so returning the full set is
        // cheaper than a SCAN-style search index.
        group.MapGet("/ladders", ListLaddersAsync)
            .RequireAuthorization(AdminPolicy);

        // GET /admin/api/leaderboard?ladderId=&limit= — admin-policy authorized (RANK-08 admin path per D-23).
        group.MapGet("/leaderboard", GetLeaderboardAsync)
            .RequireAuthorization(AdminPolicy);

        // GET /admin/api/players/{id}/export — superadmin GDPR export (D-16).
        // Writes admin.player.gdpr_export audit row before responding.
        group.MapGet("/players/{id:guid}/export", AdminGdprExportAsync)
            .RequireAuthorization(SuperadminPolicy);

        // POST /admin/api/players/{id}/rank-adjust — superadmin manual rank override (D-19).
        // Superadmin + antiforgery + validator.
        group.MapPost("/players/{id:guid}/rank-adjust", AdminRankAdjustAsync)
            .RequireAuthorization(SuperadminPolicy)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .AddEndpointFilter<ValidationEndpointFilter<RankAdjustRequest>>();

        return routes;
    }

    // ---- handlers ----

    private static async Task<IResult> EndSeasonAsync(
        Guid id,
        EndSeasonRequest req,
        HttpContext http,
        GameKitDbContext ctx,
        IEndSeasonService svc,
        CancellationToken ct)
    {
        // Resolve actor id from the admin cookie principal.
        var actorId = GetAdminId(http);

        // Load the ladder to validate the name confirmation (D-11 confirm-name gate).
        var ladder = await ctx.Set<GameKit.Rankings.Entities.Ladder>()
            .FindAsync(new object[] { id }, ct)
            .ConfigureAwait(false);

        if (ladder is null)
            return Results.NotFound(new { error = "ladder_not_found", ladderId = id });

        // Case-sensitive name comparison (mirrors GdprDeleteDialog and D-11).
        if (!string.Equals(req.ConfirmLadderName, ladder.Name, StringComparison.Ordinal))
        {
            return Results.BadRequest(new
            {
                error = "confirm_name_mismatch",
                expected = ladder.Name,
                provided = req.ConfirmLadderName,
            });
        }

        try
        {
            var result = await svc.EndAsync(id, actorId, ct).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (System.Collections.Generic.KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = "no_current_season", detail = ex.Message });
        }
    }

    private static async Task<IResult> GetLeaderboardAsync(
        Guid ladderId,
        int? limit,
        Guid? seasonId,
        ILeaderboardService svc,
        CancellationToken ct)
    {
        var rows = await svc.TopAsync(ladderId, limit ?? 100, seasonId, ct).ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ListLaddersAsync(
        GameKitDbContext ctx,
        CancellationToken ct)
    {
        var rows = await ctx.Set<Ladder>()
            .OrderBy(l => l.Name)
            .Select(l => new { id = l.Id, name = l.Name })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static async Task<IResult> AdminGdprExportAsync(
        Guid id,
        HttpContext http,
        IGdprExportService svc,
        GameKitDbContext ctx,
        IClock clock,
        IIdGenerator idGen,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);

        GdprExportResponse? response;
        long byteSize;
        try
        {
            // WR-07: use ExportWithSizeAsync so we get the serialized byte length without
            // re-serializing the response a second time for the audit row.
            (response, byteSize) = await svc.ExportWithSizeAsync(id, ct).ConfigureAwait(false);
        }
        catch (GdprExportPayloadTooLargeException ex)
        {
            return Results.Problem(
                title: "Export payload too large",
                detail: ex.Message,
                statusCode: StatusCodes.Status413RequestEntityTooLarge);
        }

        if (response is null)
            return Results.NotFound(new { error = "player_not_found", playerId = id });

        // Write admin.player.gdpr_export audit row BEFORE returning (D-16 / T-04-08-AT).
        var exportedAt = response.ExportedAt;
        var afterJson = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            exported_at = exportedAt,
            byte_size = byteSize,
        }));

        // CR-06: use IIdGenerator (UUIDv7, time-ordered) — every other audit row writer in
        // the codebase does the same. UUIDv4 here would break the implicit timestamp ordering
        // of admin_audit_log and scatter gdpr_export entries through history.
        var auditRow = new AdminAuditLog
        {
            Id = idGen.NewId(),
            Action = AuditActionGdprExport,
            TargetType = "player",
            TargetId = id,
            ActorId = actorId,
            Before = null,
            After = afterJson,
            Reason = null,
            CreatedAt = clock.UtcNow,
        };
        ctx.Set<AdminAuditLog>().Add(auditRow);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(response);
    }

    private static async Task<IResult> AdminRankAdjustAsync(
        Guid id,
        RankAdjustRequest req,
        HttpContext http,
        IRankAdjustService svc,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);

        try
        {
            var result = await svc.AdjustAsync(id, req.LadderId, req.NewRating, req.Reason, actorId, ct)
                .ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = "not_found", detail = ex.Message });
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = "invalid_rating", detail = ex.Message });
        }
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
