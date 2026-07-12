// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameKit.Admin.UI.Hubs;

/// <summary>
/// Admin live-event SignalR hub (ADMIN-13).
/// Gated by the <c>GameKitAdmin</c> COOKIE scheme via
/// <see cref="AdminPolicies.Admin"/> — NOT the player JWT Bearer scheme.
/// <c>AdminLiveBroadcastService</c> injects <see cref="IHubContext{AdminEventHub}"/>
/// to broadcast; this hub is receive-only for connected admin clients.
/// </summary>
/// <remarks>
/// <para>
/// The hub is mapped under <see cref="GameKitAdminOptions.MountPath"/> at
/// <c>{MountPath}/hubs/events</c> by <c>MapGameKitAdmin()</c> so the path-based default
/// scheme selector in <c>AddGameKitAdmin</c> routes WebSocket upgrade requests to the
/// <c>GameKitAdmin</c> cookie scheme (Pitfall 2 mitigation). The negotiate endpoint at
/// <c>{MountPath}/hubs/events/negotiate</c> enforces cookie authentication before the
/// WebSocket handshake begins — unauthenticated upgrade attempts return HTTP 401.
/// </para>
/// <para>
/// <strong>Security boundary:</strong> Do NOT add
/// <c>[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]</c>.
/// The <see cref="AdminPolicies.Admin"/> policy already pins the <c>GameKitAdmin</c>
/// cookie scheme via <c>AddAuthenticationSchemes</c>; adding a Bearer scheme attribute
/// would open a cross-scheme bypass (T-12-04-SPOOF).
/// </para>
/// <para>
/// <strong>Anti-pattern note:</strong> Do NOT inject <c>ICurrentPlayer</c> or
/// <c>IHttpContextAccessor</c> here — <c>HttpContext</c> is <see langword="null"/>
/// during hub method invocations (SignalR lifetime model). Admin claims are available
/// via <c>Context.User</c> if needed.
/// </para>
/// <para>
/// Admin event payloads flowing through this hub must not contain PII beyond what
/// the admin role is authorised to see. Future publishers to <c>gamekit:admin:events</c>
/// must scope payloads accordingly (T-12-04-INF).
/// </para>
/// </remarks>
[Authorize(Policy = AdminPolicies.Admin)]
public sealed class AdminEventHub : Hub
{
    // Receive-only: AdminLiveBroadcastService broadcasts via IHubContext<AdminEventHub>.
    // No server-callable methods are defined — the hub exists solely to be a typed
    // destination for IHubContext<AdminEventHub>.Clients.All.SendAsync("ReceiveAdminEvent", ...).
}
