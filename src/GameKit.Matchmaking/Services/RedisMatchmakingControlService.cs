// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Services;
using GameKit.Matchmaking.Redis;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Default <see cref="IMatchmakingControlService"/> implementation. Writes the per-ladder
/// pause / drain flag into Redis and records the matching audit row via
/// <see cref="IAdminAuditWriter"/>. Registered as scoped in
/// <c>MatchmakingBuilderExtensions.AddHttpServices</c>.
/// </summary>
/// <remarks>
/// Defense-in-depth: every public method re-verifies the caller against
/// <see cref="AdminPolicies.Superadmin"/> before writing Redis or audit rows. The HTTP
/// endpoint chain in <see cref="GameKit.Matchmaking.Http.MatchmakingAdminEndpoints"/> already
/// gates entry with <c>.RequireAuthorization(AdminPolicies.Superadmin)</c> + antiforgery,
/// but the Blazor dialog path resolves the service from DI directly. Without the in-service
/// check, a non-superadmin admin with circuit access could call <c>PauseAsync</c>/<c>DrainAsync</c>
/// bypassing the HTTP-layer policy entirely (Plan 05 security audit OPEN-NEW-05-A).
/// </remarks>
public sealed class RedisMatchmakingControlService : IMatchmakingControlService
{
    // Audit action constants — mirror AdminAuditActions in GameKit.Admin.UI.
    private const string AuditActionPauseQueue = "admin.matchmaking.pause_queue";
    private const string AuditActionDrainQueue = "admin.matchmaking.drain_queue";

    private readonly IConnectionMultiplexer _redis;
    private readonly IAdminAuditWriter _audit;
    private readonly IAuthorizationService _authz;
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>Constructs the service.</summary>
    /// <param name="redis">Connection multiplexer for the Redis SET on the per-ladder flag key.</param>
    /// <param name="audit">Admin audit writer for the action row.</param>
    /// <param name="authz">Authorization service used to re-verify the caller is in the Superadmin policy.</param>
    /// <param name="httpContextAccessor">Carries the cookie-authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/> from the live request/circuit.</param>
    public RedisMatchmakingControlService(
        IConnectionMultiplexer redis,
        IAdminAuditWriter audit,
        IAuthorizationService authz,
        IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(authz);
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _redis = redis;
        _audit = audit;
        _authz = authz;
        _httpContextAccessor = httpContextAccessor;
    }

    private async Task EnsureSuperadminAsync()
    {
        var http = _httpContextAccessor.HttpContext
            ?? throw new UnauthorizedAccessException("matchmaking control invoked outside a live HTTP/circuit context");
        var result = await _authz.AuthorizeAsync(http.User, AdminPolicies.Superadmin).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new UnauthorizedAccessException("matchmaking control requires the superadmin policy");
        }
    }

    /// <inheritdoc />
    public async Task PauseAsync(Guid ladderId, string reason, Guid actorId, CancellationToken ct)
    {
        await EnsureSuperadminAsync().ConfigureAwait(false);

        var safeReason = string.IsNullOrWhiteSpace(reason) ? "(no reason)" : reason;
        var db = _redis.GetDatabase();
        await db.StringSetAsync(ControlPausedKeyForLadder(ladderId), safeReason).ConfigureAwait(false);

        await _audit.WriteAsync(
            action: AuditActionPauseQueue,
            targetType: "ladder",
            targetId: ladderId,
            actorId: actorId,
            before: null,
            after: new { paused = true, reason = safeReason },
            reason: safeReason,
            cancellationToken: ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DrainAsync(Guid ladderId, string reason, Guid actorId, CancellationToken ct)
    {
        await EnsureSuperadminAsync().ConfigureAwait(false);

        var safeReason = string.IsNullOrWhiteSpace(reason) ? "(no reason)" : reason;
        var db = _redis.GetDatabase();
        await db.StringSetAsync(ControlDrainKeyForLadder(ladderId), safeReason).ConfigureAwait(false);

        await _audit.WriteAsync(
            action: AuditActionDrainQueue,
            targetType: "ladder",
            targetId: ladderId,
            actorId: actorId,
            before: null,
            after: new { drain = true, reason = safeReason },
            reason: safeReason,
            cancellationToken: ct).ConfigureAwait(false);
    }

    internal static string ControlPausedKeyForLadder(Guid ladderId)
        => $"{MatchmakingRedisKeys.ControlPaused}:{ladderId}";

    internal static string ControlDrainKeyForLadder(Guid ladderId)
        => $"{MatchmakingRedisKeys.ControlDrain}:{ladderId}";
}
