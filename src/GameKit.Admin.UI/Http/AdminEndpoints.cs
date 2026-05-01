// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authentication;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Entities;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Http.EndpointFilters;
using GameKit.Admin.UI.Http.RateLimiting;
using GameKit.Admin.UI.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Admin.UI.Http;

/// <summary>
/// Maps the <c>/admin/api/*</c> minimal-API surface onto a <see cref="RouteGroupBuilder"/>.
/// Called by <see cref="Builder.AdminApplicationBuilderExtensions.MapGameKitAdmin"/>. 13 endpoints
/// total, each composed from the authorization policies + endpoint filters registered by
/// <see cref="Builder.AdminBuilderExtensions.AddGameKitAdmin"/>:
/// <list type="bullet">
///   <item><c>POST /login</c> — anonymous + rate-limited + validator.</item>
///   <item><c>POST /logout</c> — anonymous (cookie presence is the auth factor).</item>
///   <item><c>GET /players/search</c> — admin, validator; read-only GET, no antiforgery (W8).</item>
///   <item><c>POST /players/{id}/ban</c> — admin + antiforgery + validator.</item>
///   <item><c>POST /players/{id}/unban</c> — admin + antiforgery.</item>
///   <item><c>POST /players/{id}/gdpr-delete</c> — superadmin + antiforgery.</item>
///   <item><c>GET /admins</c> — superadmin.</item>
///   <item><c>POST /admins</c> — superadmin + antiforgery + validator.</item>
///   <item><c>DELETE /admins/{id}</c> — superadmin + antiforgery.</item>
///   <item><c>GET /audit</c> — admin (keyset-paginated).</item>
///   <item><c>GET /match-history</c> — admin.</item>
///   <item><c>GET /health</c> — admin.</item>
///   <item><c>GET /commands</c> — admin (Plan 03.1-04 verb-engine palette feed).</item>
/// </list>
/// </summary>
public static class AdminEndpoints
{
    /// <summary>
    /// Maps the full admin HTTP-API surface onto <paramref name="group"/>. Plan 03-07 Task 2:
    /// 12 endpoints with per-endpoint authorization policy + antiforgery / validator / rate-limit
    /// filter chains per D-16 / D-18.
    /// </summary>
    /// <param name="group">The admin-API route group (typically <c>/admin/api</c>).</param>
    /// <returns><paramref name="group"/> for chaining.</returns>
    public static RouteGroupBuilder Map(RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        // POST /login — anonymous, rate-limited (D-18: 5/min/IP sliding window), validator.
        group.MapPost("/login", LoginAsync)
            .RequireRateLimiting(AdminRateLimitRegistrations.AdminLoginPolicy)
            .AddEndpointFilter<ValidationEndpointFilter<LoginRequest>>()
            .AllowAnonymous();

        // POST /logout — clears the admin cookie. Anonymous: the cookie presence IS the revocation
        // capability (RFC 7009-style semantics); requiring auth would break logout when the cookie
        // has already expired in the browser. Cast to Delegate so ASP.NET binds the IResult return
        // value as the response body instead of treating the single-HttpContext parameter as a
        // plain RequestDelegate (ASP0016).
        group.MapPost("/logout", (Delegate)LogoutAsync).AllowAnonymous();

        // GET /players/search — admin, validator. Read-only GET → NO antiforgery filter per W8
        // (D-16 antiforgery applies to MUTATIONS only; search is idempotent and side-effect-free).
        // Query-string parameters bind via [AsParameters] into PlayerSearchRequest.
        group.MapGet("/players/search", SearchPlayersAsync)
            .RequireAuthorization(AdminPolicies.Admin)
            .AddEndpointFilter<ValidationEndpointFilter<PlayerSearchRequest>>();

        // POST /players/{id}/ban — admin + antiforgery + validator. Order: antiforgery BEFORE
        // validator so the CSRF check short-circuits before body deserialization.
        group.MapPost("/players/{id:guid}/ban", BanPlayerAsync)
            .RequireAuthorization(AdminPolicies.Admin)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .AddEndpointFilter<ValidationEndpointFilter<BanPlayerRequest>>();

        // POST /players/{id}/unban — admin + antiforgery. Reason is optional (no validator).
        group.MapPost("/players/{id:guid}/unban", UnbanPlayerAsync)
            .RequireAuthorization(AdminPolicies.Admin)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        // POST /players/{id}/gdpr-delete — superadmin + antiforgery (T-03-07-07).
        group.MapPost("/players/{id:guid}/gdpr-delete", GdprDeletePlayerAsync)
            .RequireAuthorization(AdminPolicies.Superadmin)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        // GET /admins — superadmin list.
        group.MapGet("/admins", ListAdminsAsync)
            .RequireAuthorization(AdminPolicies.Superadmin);

        // POST /admins — superadmin + antiforgery + validator.
        group.MapPost("/admins", CreateAdminAsync)
            .RequireAuthorization(AdminPolicies.Superadmin)
            .AddEndpointFilter<AntiforgeryValidationFilter>()
            .AddEndpointFilter<ValidationEndpointFilter<CreateAdminRequest>>();

        // DELETE /admins/{id} — superadmin + antiforgery.
        group.MapDelete("/admins/{id:guid}", DeleteAdminAsync)
            .RequireAuthorization(AdminPolicies.Superadmin)
            .AddEndpointFilter<AntiforgeryValidationFilter>();

        // GET /audit — admin, keyset-paginated.
        group.MapGet("/audit", GetAuditAsync).RequireAuthorization(AdminPolicies.Admin);

        // GET /match-history — admin.
        group.MapGet("/match-history", GetMatchHistoryAsync).RequireAuthorization(AdminPolicies.Admin);

        // GET /health — admin (3-probe Postgres + Redis + error-rate report).
        group.MapGet("/health", GetHealthAsync).RequireAuthorization(AdminPolicies.Admin);

        // GET /commands — admin. Phase 03.1 D-09 verb-engine palette feed; rows whose
        // RequiresSuperadmin=true are filtered out for non-superadmin operators (D-11).
        // Cast to Delegate (mirrors LogoutAsync) so ASP.NET binds the IResult return value
        // as the response body instead of treating the single-HttpContext parameter as a
        // plain RequestDelegate (ASP0016).
        group.MapGet("/commands", (Delegate)GetCommandsAsync).RequireAuthorization(AdminPolicies.Admin);

        return group;
    }

    // ---- handlers ----

    // ARCHITECTURE NOTE — admin auth surface split:
    //
    //   POST /admin/api/login     (this file, JSON)         → for SPA / programmatic clients
    //                                                         that fetch from the BROWSER side.
    //   POST /admin/login         (AdminFormEndpoints, form) → for the static-SSR Blazor login
    //                                                         page; browser submits the form,
    //                                                         server returns 302.
    //
    // Both share SignInCoreAsync below so the cookie-issuance logic is single-sourced.
    //
    // RULE FOR FUTURE CONTRIBUTORS: never call a cookie-mutating endpoint (login, logout, change
    // password, role refresh, etc.) from a Blazor INTERACTIVE circuit via HttpClient. The
    // Set-Cookie header lands on the server-side HttpClient, not on the browser, so the cookie
    // never propagates. Cookie-mutating actions must be reached by the browser directly — via
    // a static-SSR HTML form submission, or via a SPA fetch from the browser. Interactive
    // Blazor pages access domain logic through DI services (IPlayerBanService, IAdminUserService,
    // …), NOT through HTTP loopback to /admin/api/*.
    private static async Task<IResult> LoginAsync(
        LoginRequest req,
        HttpContext http,
        GameKitAdminOptions opts,
        IAdminAuthService authSvc,
        CancellationToken ct)
    {
        var ok = await SignInCoreAsync(http, opts, authSvc, req, ct).ConfigureAwait(false);
        return ok
            ? Results.Ok(new { success = true, redirectUrl = "/admin" })
            : Results.Unauthorized();
    }

    /// <summary>
    /// Verifies admin credentials and, on success, writes the admin auth cookie via
    /// <c>HttpContext.SignInAsync</c>. Shared by the JSON <c>POST /admin/api/login</c> handler
    /// and the static-SSR form <c>POST /admin/login</c> handler so the cookie-issuance logic
    /// lives in exactly one place.
    /// </summary>
    /// <param name="http">The current HTTP context — Set-Cookie is written on its response.</param>
    /// <param name="opts">Admin options (consulted for <see cref="AdminCookieOptions.RememberMeDuration"/>).</param>
    /// <param name="authSvc">Credential verifier (timing-parity dummy on miss; null on bad creds).</param>
    /// <param name="req">The login request (username + password + remember-me flag).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> on successful sign-in (cookie written); <c>false</c> on bad credentials.</returns>
    internal static async Task<bool> SignInCoreAsync(
        HttpContext http,
        GameKitAdminOptions opts,
        IAdminAuthService authSvc,
        LoginRequest req,
        CancellationToken ct)
    {
        var result = await authSvc.VerifyPasswordAsync(req.Username, req.Password, ct).ConfigureAwait(false);
        if (result is null) return false;
        var (adminId, role) = result.Value;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, adminId.ToString()),
            new(ClaimTypes.Name, req.Username),
            new(ClaimTypes.Role, role),
        };
        var identity = new ClaimsIdentity(claims, AdminAuthenticationSchemeConstants.Scheme);
        var principal = new ClaimsPrincipal(identity);

        var authProps = new AuthenticationProperties
        {
            IsPersistent = req.RememberMe,
            ExpiresUtc = req.RememberMe
                ? DateTimeOffset.UtcNow.Add(opts.Cookie.RememberMeDuration)
                : null,
        };
        await http.SignInAsync(AdminAuthenticationSchemeConstants.Scheme, principal, authProps)
            .ConfigureAwait(false);
        return true;
    }

    private static async Task<IResult> LogoutAsync(HttpContext http)
    {
        await http.SignOutAsync(AdminAuthenticationSchemeConstants.Scheme).ConfigureAwait(false);
        return Results.Ok(new { success = true, redirectUrl = "/admin/login" });
    }

    private static async Task<IResult> SearchPlayersAsync(
        [AsParameters] PlayerSearchRequest req,
        IPlayerSearchService svc,
        CancellationToken ct)
    {
        var result = await svc
            .SearchAsync(req.Query, req.AfterId, req.PageSize, ct)
            .ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> BanPlayerAsync(
        Guid id,
        BanPlayerRequest req,
        HttpContext http,
        IPlayerBanService bans,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);
        await bans.BanAsync(id, actorId, req.Reason, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> UnbanPlayerAsync(
        Guid id,
        UnbanPlayerRequest req,
        HttpContext http,
        IPlayerBanService bans,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);
        await bans.UnbanAsync(id, actorId, req.Reason, ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> GdprDeletePlayerAsync(
        Guid id,
        GdprDeleteRequest req,
        HttpContext http,
        IGdprDeleteService gdpr,
        IAdminAuditWriter audit,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);
        var reason = string.IsNullOrWhiteSpace(req.Reason) ? "gdpr_request" : req.Reason;
        try
        {
            await gdpr.DeletePlayerAsync(id, actorId, reason, ct).ConfigureAwait(false);
        }
        catch (PlayerNotFoundException)
        {
            return Results.NotFound(new { error = "player_not_found", playerId = id });
        }

        // Write an admin-scoped audit row in addition to the Core GDPR audit row so the admin-UI
        // audit viewer (plan 03-09) surfaces the action under the admin.player.gdpr_delete action.
        await audit.WriteAsync(
            action: AdminAuditActions.PlayerGdprDelete,
            targetType: "player",
            targetId: id,
            actorId: actorId,
            before: null,
            after: new { deleted = true, confirm_username = req.ConfirmUsername },
            reason: reason,
            cancellationToken: ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListAdminsAsync(
        IAdminUserService svc,
        CancellationToken ct)
    {
        var rows = await svc.ListAsync(ct).ConfigureAwait(false);
        // Project to a hash-free DTO before returning — defense-in-depth (the service already
        // returns the raw entity but PasswordHash must never cross the wire).
        var projected = rows.Select(a => new
        {
            id = a.Id,
            username = a.Username,
            role = a.Role,
            createdAt = a.CreatedAt,
            lastLoginAt = a.LastLoginAt,
        }).ToArray();
        return Results.Ok(projected);
    }

    private static async Task<IResult> CreateAdminAsync(
        CreateAdminRequest req,
        HttpContext http,
        IAdminUserService svc,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);
        try
        {
            var id = await svc.CreateAsync(req.Username, req.Password, req.Role, actorId, ct)
                .ConfigureAwait(false);
            return Results.Ok(new { id });
        }
        catch (AdminUsernameAlreadyTakenException ex)
        {
            return Results.Conflict(new { error = "username_taken", username = ex.Username });
        }
    }

    private static async Task<IResult> DeleteAdminAsync(
        Guid id,
        HttpContext http,
        IAdminUserService svc,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);
        try
        {
            await svc.DeleteAsync(id, actorId, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound(new { error = "admin_not_found", adminId = id });
        }
        catch (LastSuperadminException)
        {
            return Results.Conflict(new { error = "last_superadmin", adminId = id });
        }
        return Results.NoContent();
    }

    private static async Task<IResult> GetAuditAsync(
        [AsParameters] AuditQuery q,
        GameKitDbContext ctx,
        CancellationToken ct)
    {
        // Keyset pagination on (CreatedAt DESC, Id DESC) — callers pass the cursor from the
        // previous page's tail row. Page size clamped to [1, 100].
        var page = Math.Clamp(q.PageSize ?? 50, 1, 100);
        var qq = ctx.AdminAuditLog.AsNoTracking();
        if (q.AfterCreatedAt is { } ts && q.AfterId is { } aid)
        {
            qq = qq.Where(r => r.CreatedAt < ts || (r.CreatedAt == ts && r.Id < aid));
        }
        if (!string.IsNullOrEmpty(q.Action))
        {
            qq = qq.Where(r => r.Action == q.Action);
        }
        var rows = await qq
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .Take(page + 1)
            .Select(r => new AuditRow(
                r.Id, r.ActorId, r.Action, r.TargetType, r.TargetId, r.Reason, r.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var hasMore = rows.Count > page;
        if (hasMore) rows.RemoveAt(page);
        return Results.Ok(new { items = rows, hasMore });
    }

    private static async Task<IResult> GetMatchHistoryAsync(
        Guid playerId,
        int? pageSize,
        GameKitDbContext ctx,
        CancellationToken ct)
    {
        var size = Math.Clamp(pageSize ?? 50, 1, 50);
        // Direct join against GameSessions (no nav property on SessionParticipant by
        // SessionParticipantConfiguration — FK defined via HasOne<GameSession>().WithMany()).
        var rows = await (
            from p in ctx.Set<SessionParticipant>().AsNoTracking()
            join s in ctx.Set<GameSession>().AsNoTracking() on p.SessionId equals s.Id
            where p.PlayerId == playerId && s.State == GameSessionState.Completed
            orderby s.CompletedAt descending
            select new MatchHistoryRow(
                p.SessionId,
                s.LadderId,
                p.Team,
                p.Result == null ? null : p.Result.ToString(),
                p.RatingBefore,
                p.RatingAfter,
                p.RatingDelta,
                s.CompletedAt))
            .Take(size)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return Results.Ok(rows);
    }

    private static async Task<IResult> GetHealthAsync(
        IHealthProbeService svc,
        CancellationToken ct)
    {
        var report = await svc.ProbeAsync(ct).ConfigureAwait(false);
        return Results.Ok(report);
    }

    /// <summary>
    /// Returns the role-filtered command list consumed by the Phase 03.1 Cmd+K palette
    /// (D-09 verb-engine). Per CONTEXT D-11 the server discards rows whose
    /// <see cref="AdminCommand.RequiresSuperadmin"/> is <c>true</c> when the operator is not
    /// a superadmin — never grayed, always absent. The DTO excludes the
    /// <c>RequiresSuperadmin</c> flag so admin operators cannot infer the existence of
    /// superadmin-only commands. Cookie-authenticated; <c>AdminPolicies.Admin</c> gate is
    /// applied at the route level.
    /// </summary>
    /// <param name="http">The current HTTP context — supplies the role claim from the admin cookie principal.</param>
    /// <returns>HTTP 200 with a JSON array of <see cref="AdminCommandDto"/>.</returns>
    private static Task<IResult> GetCommandsAsync(HttpContext http)
    {
        var role = http.User.FindFirst(ClaimTypes.Role)?.Value ?? AdminRoles.Admin;
        var isSuper = string.Equals(role, AdminRoles.Superadmin, StringComparison.Ordinal);
        var visible = AdminCommandRegistry.AllCommands
            .Where(c => isSuper || !c.RequiresSuperadmin)
            .Select(c => new AdminCommandDto(c.Id, c.Label, c.Category, c.RequiresTarget))
            .ToArray();
        return Task.FromResult<IResult>(Results.Ok(visible));
    }

    /// <summary>
    /// Extracts the <see cref="ClaimTypes.NameIdentifier"/> claim from the admin cookie principal.
    /// Throws when absent — every admin-policy-gated endpoint has a populated principal by the
    /// time a handler runs, so a missing claim indicates a programming error (T-03-07-06).
    /// </summary>
    private static Guid GetAdminId(HttpContext http)
    {
        var nameId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(nameId, out var id)
            ? id
            : throw new UnauthorizedAccessException(
                "Admin id claim is missing or malformed — SignInAsync did not populate NameIdentifier.");
    }

    /// <summary>Query-string shape for <c>GET /audit</c> (bound via <c>[AsParameters]</c>).</summary>
    /// <param name="AfterCreatedAt">Keyset cursor — tail row's CreatedAt.</param>
    /// <param name="AfterId">Keyset cursor — tail row's Id.</param>
    /// <param name="Action">Optional action-name filter (e.g. <c>admin.player.ban</c>).</param>
    /// <param name="PageSize">Desired page size (clamped to [1, 100]).</param>
    public sealed record AuditQuery(
        DateTimeOffset? AfterCreatedAt,
        Guid? AfterId,
        string? Action,
        int? PageSize);

    /// <summary>Projection row for <c>GET /audit</c> (PasswordHash never projected).</summary>
    /// <param name="Id">Audit row id.</param>
    /// <param name="ActorId">Acting admin id (nullable for system-originated rows).</param>
    /// <param name="Action">Stable action verb.</param>
    /// <param name="TargetType">Target entity type.</param>
    /// <param name="TargetId">Target entity id, if applicable.</param>
    /// <param name="Reason">Free-text reason.</param>
    /// <param name="CreatedAt">UTC timestamp.</param>
    public sealed record AuditRow(
        Guid Id,
        Guid? ActorId,
        string Action,
        string TargetType,
        Guid? TargetId,
        string? Reason,
        DateTimeOffset CreatedAt);

    /// <summary>Projection row for <c>GET /match-history</c>.</summary>
    /// <param name="SessionId">Owning session id.</param>
    /// <param name="LadderId">Optional ladder id.</param>
    /// <param name="Team">Team number.</param>
    /// <param name="Result">Terminal session result as a string.</param>
    /// <param name="RatingBefore">Rating snapshot at session start.</param>
    /// <param name="RatingAfter">Rating snapshot at session end.</param>
    /// <param name="Delta">Rating delta (RatingAfter - RatingBefore).</param>
    /// <param name="CompletedAt">UTC timestamp of completion.</param>
    public sealed record MatchHistoryRow(
        Guid SessionId,
        Guid? LadderId,
        int Team,
        string? Result,
        double? RatingBefore,
        double? RatingAfter,
        double? Delta,
        DateTimeOffset? CompletedAt);
}
