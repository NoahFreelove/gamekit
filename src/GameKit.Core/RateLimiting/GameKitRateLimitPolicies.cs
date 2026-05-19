// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core.RateLimiting;

/// <summary>Default <see cref="IGameKitRateLimitPolicies"/> implementation. Names are stable identifiers.</summary>
public sealed class GameKitRateLimitPolicies : IGameKitRateLimitPolicies
{
    /// <summary>Canonical policy name: <c>gamekit:auth:login</c>.</summary>
    public const string AuthLoginPolicy = "gamekit:auth:login";

    /// <summary>Canonical policy name: <c>gamekit:auth:refresh</c>.</summary>
    public const string AuthRefreshPolicy = "gamekit:auth:refresh";

    /// <summary>Canonical policy name: <c>gamekit:auth:register</c>.</summary>
    public const string AuthRegisterPolicy = "gamekit:auth:register";

    /// <summary>Canonical policy name: <c>gamekit:mm:enqueue</c>.</summary>
    public const string MmEnqueuePolicy = "gamekit:mm:enqueue";

    /// <summary>Canonical policy name: <c>gamekit:presence:heartbeat</c>.</summary>
    public const string PresenceHeartbeatPolicy = "gamekit:presence:heartbeat";

    /// <summary>Canonical policy name: <c>gamekit:sessions:complete</c>.</summary>
    public const string SessionsCompletePolicy = "gamekit:sessions:complete";

    /// <inheritdoc />
    public string AuthLogin => AuthLoginPolicy;

    /// <inheritdoc />
    public string AuthRefresh => AuthRefreshPolicy;

    /// <inheritdoc />
    public string AuthRegister => AuthRegisterPolicy;

    /// <inheritdoc />
    public string MmEnqueue => MmEnqueuePolicy;

    /// <inheritdoc />
    public string PresenceHeartbeat => PresenceHeartbeatPolicy;

    /// <inheritdoc />
    public string SessionsComplete => SessionsCompletePolicy;
}
