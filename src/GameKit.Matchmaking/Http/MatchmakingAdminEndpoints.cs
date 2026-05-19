// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Services;
using GameKit.Matchmaking.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Http;

/// <summary>
/// Maps Matchmaking-specific admin HTTP endpoints onto a route group (Plan 05-08 / MATCH-14).
/// Two verbs: pause-queue and drain-queue, both per-ladder per RESEARCH §OQ-5.
/// </summary>
/// <remarks>
/// <para>
/// Authorization constants are referenced as string literals because <c>GameKit.Matchmaking</c>
/// does NOT have a runtime API dependency on <c>GameKit.Admin.UI</c> at the policy layer
/// (D-22 invariant; mirrors the Rankings.AdminEndpoints pattern). The audit-writer
/// <see cref="IAdminAuditWriter"/> IS resolved from DI — the audit row is the cross-package
/// integration point.
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

    // Source of truth: GameKit.Admin.UI.Services.AdminAuditActions.MatchmakingPauseQueue
    private const string AuditActionPauseQueue = "admin.matchmaking.pause_queue";

    // Source of truth: GameKit.Admin.UI.Services.AdminAuditActions.MatchmakingDrainQueue
    private const string AuditActionDrainQueue = "admin.matchmaking.drain_queue";

    /// <summary>Maps the matchmaking admin endpoints onto the provided route group.</summary>
    /// <param name="routes">The endpoint route builder.</param>
    /// <returns><paramref name="routes"/> for chaining.</returns>
    public static IEndpointRouteBuilder MapMatchmakingAdmin(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        var group = routes.MapGroup("/admin/api/matchmaking");

        group.MapPost("/pause-queue", PauseQueueAsync)
            .RequireAuthorization(SuperadminPolicy);

        group.MapPost("/drain-queue", DrainQueueAsync)
            .RequireAuthorization(SuperadminPolicy);

        return routes;
    }

    /// <summary>Request body for pause/drain — carries the ladder scope + reason.</summary>
    public sealed record MatchmakingControlRequest(Guid LadderId, string Reason);

    // ---- handlers ----

    private static async Task<IResult> PauseQueueAsync(
        MatchmakingControlRequest req,
        HttpContext http,
        IConnectionMultiplexer redis,
        IAdminAuditWriter audit,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);

        var db = redis.GetDatabase();
        var key = ControlPausedKeyForLadder(req.LadderId);
        await db.StringSetAsync(key, req.Reason ?? "(no reason)").ConfigureAwait(false);

        await audit.WriteAsync(
            action: AuditActionPauseQueue,
            targetType: "ladder",
            targetId: req.LadderId,
            actorId: actorId,
            before: null,
            after: new { paused = true, reason = req.Reason },
            reason: req.Reason,
            cancellationToken: ct).ConfigureAwait(false);

        return Results.Ok(new { paused = true, ladderId = req.LadderId });
    }

    private static async Task<IResult> DrainQueueAsync(
        MatchmakingControlRequest req,
        HttpContext http,
        IConnectionMultiplexer redis,
        IAdminAuditWriter audit,
        CancellationToken ct)
    {
        var actorId = GetAdminId(http);

        var db = redis.GetDatabase();
        var key = ControlDrainKeyForLadder(req.LadderId);
        await db.StringSetAsync(key, req.Reason ?? "(no reason)").ConfigureAwait(false);

        await audit.WriteAsync(
            action: AuditActionDrainQueue,
            targetType: "ladder",
            targetId: req.LadderId,
            actorId: actorId,
            before: null,
            after: new { drain = true, reason = req.Reason },
            reason: req.Reason,
            cancellationToken: ct).ConfigureAwait(false);

        return Results.Ok(new { drain = true, ladderId = req.LadderId });
    }

    /// <summary>Per-ladder pause key. Mirrors <see cref="MatchmakingRedisKeys.ControlPaused"/> base.</summary>
    private static string ControlPausedKeyForLadder(Guid ladderId)
        => $"{MatchmakingRedisKeys.ControlPaused}:{ladderId}";

    /// <summary>Per-ladder drain key.</summary>
    private static string ControlDrainKeyForLadder(Guid ladderId)
        => $"{MatchmakingRedisKeys.ControlDrain}:{ladderId}";

    private static Guid GetAdminId(HttpContext http)
    {
        var nameId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(nameId, out var id)
            ? id
            : throw new UnauthorizedAccessException(
                "Admin id claim is missing or malformed — SignInAsync did not populate NameIdentifier.");
    }
}
