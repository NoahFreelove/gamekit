// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core.RateLimiting;

/// <summary>
/// Named rate-limit policy identifiers shared across GameKit packages. Sibling packages register
/// concrete rate-limiters under these names via <c>services.AddRateLimiter(o =&gt; o.AddFixedWindowLimiter(name, ...))</c>
/// and endpoints reference them via <c>[EnableRateLimiting(IGameKitRateLimitPolicies.AuthLogin)]</c>.
/// </summary>
/// <remarks>
/// Phase 1 (Core) defines the constants; concrete policy registration lands alongside the endpoints
/// in Auth (AUTH-15), Matchmaking (MATCH-11), and Presence (PRES-03) phases. This interface gives
/// sibling packages a stable attribute value to reference without requiring a runtime dependency.
/// </remarks>
public interface IGameKitRateLimitPolicies
{
    /// <summary><c>POST /auth/login</c> rate-limit policy name.</summary>
    string AuthLogin { get; }

    /// <summary><c>POST /auth/refresh</c> rate-limit policy name.</summary>
    string AuthRefresh { get; }

    /// <summary><c>POST /auth/register</c> rate-limit policy name.</summary>
    string AuthRegister { get; }

    /// <summary><c>POST /mm/queue</c> rate-limit policy name.</summary>
    string MmEnqueue { get; }

    /// <summary><c>POST /presence/heartbeat</c> rate-limit policy name.</summary>
    string PresenceHeartbeat { get; }

    /// <summary>Rate-limit policy name for <c>POST /api/sessions/{id}/complete</c>.</summary>
    string SessionsComplete { get; }

    /// <summary>Rate-limit policy name for <c>POST /api/sessions/{id}/start</c> (Phase 6 — PRES-05, D-20).</summary>
    string SessionsStart { get; }

    /// <summary>Rate-limit policy name for <c>POST /api/sessions/{id}/abandon</c> (Phase 6 — PRES-05, D-20).</summary>
    string SessionsAbandon { get; }
}
