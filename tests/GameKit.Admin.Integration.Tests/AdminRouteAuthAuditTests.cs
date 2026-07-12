// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Threading.Tasks;
using GameKit.Admin.Integration.Tests.Mocks;
using GameKit.Admin.UI.Authorization;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// SEC-02 — Route-enumeration audit: every <c>/admin/*</c> endpoint registered in the admin
/// package must either (a) be in the known-anonymous allowlist (login / logout) or (b) carry an
/// <see cref="IAuthorizeData"/> referencing <see cref="AdminPolicies.Admin"/> or
/// <see cref="AdminPolicies.Superadmin"/>. These policies pin the <c>GameKitAdmin</c> cookie
/// scheme via <c>AddAuthenticationSchemes</c>, so a player JWT presented as a Bearer header can
/// never satisfy them.
///
/// <para>
/// The structural enumeration test is a forward-protection gate: any future <c>/admin/*</c>
/// endpoint added WITHOUT the required policy will fail CI immediately, preventing a new
/// unprotected surface from shipping.
/// </para>
/// <para>
/// The behavioral <see cref="PlayerJwt_IsRejected_OnExistingAdminRoute"/> [Fact] first asserts
/// that the target admin endpoint EXISTS in <see cref="EndpointDataSource"/> (guarding against a
/// vacuous 404 from a missing route), then asserts a player JWT presented as Bearer is rejected
/// — never returning 200.
/// </para>
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class AdminRouteAuthAuditTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    /// <summary>
    /// Known-anonymous allowlist — only these admin route raw-text values may omit
    /// <see cref="IAuthorizeData"/>. Any other <c>admin/*</c> or <c>/admin/*</c> endpoint that
    /// is not on this list and lacks auth metadata causes the structural test to fail CI.
    ///
    /// <para>
    /// The list deliberately contains both the JSON-API endpoints (under <c>admin/api/*</c>)
    /// and the Blazor/form endpoints (under <c>admin/*</c>) that require anonymous access:
    /// login so unauthenticated operators can sign in, and logout so the cookie can be cleared
    /// even after the session expires (RFC-7009 semantics).
    /// </para>
    /// </summary>
    private static readonly HashSet<string> KnownAnonymousRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        // JSON-API login / logout (AdminEndpoints.Map):
        "admin/api/login",
        "admin/api/logout",
        // Blazor/form login / logout (AdminFormEndpoints):
        "admin/login",
        "admin/logout",
        // Static-SSR form POST handler (AdminFormEndpoints.MapAdminFormEndpoints).
        // The Blazor Login.razor page submits its form to /admin/login/submit; this is
        // AllowAnonymous + rate-limited (same bucket as /admin/api/login) so an unauthenticated
        // browser can complete the cookie-based sign-in flow (D-02 / AdminFormEndpoints.cs).
        "admin/login/submit",
    };

    public AdminRouteAuthAuditTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    // =====================================================================================
    // Structural enumeration — dynamic walk of IEndpointDataSource
    // =====================================================================================

    /// <summary>
    /// SEC-02 / T-18-04-01 — Every <c>/admin/*</c> endpoint that is NOT in the
    /// known-anonymous allowlist must carry an <see cref="IAuthorizeData"/> with policy
    /// <see cref="AdminPolicies.Admin"/> or <see cref="AdminPolicies.Superadmin"/>.
    ///
    /// <para>
    /// The test is deliberately dynamic: it walks the real <see cref="EndpointDataSource"/>
    /// after the host starts, so any future endpoint added to <c>AdminEndpoints</c> without
    /// a policy will fail CI. Do NOT replace this with a hardcoded 14-endpoint list —
    /// that approach gives false safety as the surface grows.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "SEC-02: Every non-anonymous /admin/* route requires AdminPolicies.Admin or .Superadmin")]
    public async Task AllAdminRoutes_Either_AreAnonymousAllowlisted_Or_HaveAdminPolicy()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("sec02-admin", "hunter2hunter2", AdminRoles.Superadmin));

        // Pitfall 5: IEndpointDataSource is populated after app.Build() + first request.
        // Trigger one request to guarantee the endpoint table is fully constructed.
        await host.Client.GetAsync("/");

        // Resolve the data source from the host's root service provider.
        var dataSource = host.Server.Services.GetRequiredService<EndpointDataSource>();

        var adminEndpoints = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e =>
            {
                var raw = e.RoutePattern.RawText ?? string.Empty;
                return raw.StartsWith("admin/", StringComparison.OrdinalIgnoreCase)
                    || raw.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "admin", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(raw, "/admin", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        // There must be at least one admin endpoint for the test to be non-vacuous.
        Assert.True(adminEndpoints.Count > 0,
            "No /admin/* endpoints found in EndpointDataSource — AdminEndpoints.Map() was not called or the host did not start correctly.");

        var violations = new List<string>();

        foreach (var ep in adminEndpoints)
        {
            var raw = ep.RoutePattern.RawText ?? string.Empty;

            // Normalize to bare "admin/api/login" form for allowlist lookup.
            var normalized = raw.TrimStart('/');

            if (KnownAnonymousRoutes.Contains(normalized))
            {
                // Anonymous is correct for this route — no further assertions needed.
                continue;
            }

            // Every non-anonymous admin endpoint MUST have IAuthorizeData.
            var authMetaList = ep.Metadata.OfType<IAuthorizeData>().ToList();
            if (authMetaList.Count == 0)
            {
                violations.Add(
                    $"Endpoint '{raw}' has NO IAuthorizeData metadata and is NOT in the anonymous allowlist.");
                continue;
            }

            // The policy must be AdminPolicies.Admin or AdminPolicies.Superadmin.
            // Both policies are defined in AdminBuilderExtensions with AddAuthenticationSchemes("GameKitAdmin"),
            // which is what scheme-pins the endpoint to the cookie scheme and rejects player JWTs.
            var policyNames = authMetaList
                .Select(a => a.Policy)
                .Where(p => p is not null)
                .ToHashSet(StringComparer.Ordinal);

            var hasAdminPolicy = policyNames.Contains(AdminPolicies.Admin)
                || policyNames.Contains(AdminPolicies.Superadmin);

            if (!hasAdminPolicy)
            {
                violations.Add(
                    $"Endpoint '{raw}' has IAuthorizeData but its policy names ({string.Join(", ", policyNames.Select(p => $"'{p}'"))}) "
                    + $"are not '{AdminPolicies.Admin}' or '{AdminPolicies.Superadmin}'. "
                    + "The endpoint is NOT scheme-pinned to the GameKitAdmin cookie scheme.");
            }
        }

        Assert.True(violations.Count == 0,
            $"SEC-02 enumeration found {violations.Count} unprotected admin endpoint(s):\n"
            + string.Join("\n", violations.Select((v, i) => $"  [{i + 1}] {v}")));
    }

    // =====================================================================================
    // Behavioral: player JWT is rejected on an existing protected admin route
    // =====================================================================================

    /// <summary>
    /// SEC-02 / T-18-04-02 — A player JWT presented as a Bearer header is rejected (never 200)
    /// on a protected admin route that is first asserted to EXIST in <see cref="EndpointDataSource"/>.
    ///
    /// <para>
    /// The existence guard prevents a vacuous 404 pass: if the targeted route were absent from
    /// the endpoint data source, a request to it would 404 because ASP.NET Core could not match
    /// it — not because the admin policy blocked a player JWT. The guard distinguishes the
    /// "route not found" 404 from the "cookie challenge suppression" 404 documented below.
    /// </para>
    /// <para>
    /// Accepted status codes:
    /// <list type="bullet">
    ///   <item>
    ///     <c>401 Unauthorized</c> — cookie challenge in non-Production; player Bearer is not
    ///     in the admin policy's scheme list so the challenge handler fires.
    ///   </item>
    ///   <item>
    ///     <c>403 Forbidden</c> — scheme-matched but policy denied.
    ///   </item>
    ///   <item>
    ///     <c>404 Not Found</c> — SPECIFICALLY the <see cref="GameKit.Admin.UI.Authentication.AdminCookieEvents"/>
    ///     cookie-challenge-suppression behavior in Production: the admin cookie scheme fires a
    ///     challenge when the player Bearer cannot satisfy the admin policy, and
    ///     <c>AdminCookieEvents.RedirectToLogin</c> converts that challenge to a 404 so
    ///     unauthenticated clients cannot enumerate admin surface (D-04). This 404 is for an
    ///     endpoint that EXISTS (guaranteed by the existence guard above) — NOT a generic
    ///     route-not-found catch-all.
    ///   </item>
    /// </list>
    /// <c>200 OK</c> is NEVER acceptable — a player JWT must not authenticate into the admin scheme.
    /// </para>
    /// </summary>
    [Fact(DisplayName = "SEC-02: Player JWT in Bearer header is rejected (not 200) on a protected admin route — existence-guarded")]
    public async Task PlayerJwt_IsRejected_OnExistingAdminRoute()
    {
        await using var host = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("sec02-jwt-admin", "hunter2hunter2", AdminRoles.Superadmin));

        // Pitfall 5: trigger one request so IEndpointDataSource is fully populated.
        await host.Client.GetAsync("/");

        // ---- EXISTENCE GUARD ----
        // Before issuing the player-JWT request, assert the targeted route is registered.
        // Without this guard, a missing route would 404 for "no route matched" — vacuously
        // passing the "not 200" assertion without exercising the admin scheme at all.
        const string TargetRouteRaw = "admin/api/audit";

        var dataSource = host.Server.Services.GetRequiredService<EndpointDataSource>();
        var targetEndpoint = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .FirstOrDefault(e =>
                string.Equals(e.RoutePattern.RawText?.TrimStart('/'), TargetRouteRaw, StringComparison.OrdinalIgnoreCase));

        Assert.True(targetEndpoint is not null,
            $"SEC-02 existence guard FAILED: the target route '{TargetRouteRaw}' was NOT found in EndpointDataSource. "
            + "Either the route was removed or its raw-text pattern changed. "
            + "Update TargetRouteRaw in this test to match the current endpoint pattern.");

        // ---- BEHAVIORAL ASSERTION ----
        // Issue a player JWT via FakePlayerJwtIssuer — the JWT is well-formed and correctly
        // signed, but with a throwaway key that is NOT the admin cookie's signing authority.
        // The admin authorization policy pins AddAuthenticationSchemes("GameKitAdmin"), which
        // means the JwtBearer handler is not consulted at all for /admin/* routes.
        using var issuer = new FakePlayerJwtIssuer();
        var jwt = issuer.IssueValidPlayerJwt(Guid.NewGuid(), Guid.NewGuid());

        var req = new HttpRequestMessage(HttpMethod.Get, $"/{TargetRouteRaw}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        var resp = await host.Client.SendAsync(req);

        // 200 OK is NEVER acceptable.
        Assert.NotEqual(HttpStatusCode.OK, resp.StatusCode);

        // Accepted non-200 responses (see XML doc above):
        //   401 — bearer challenge (non-Production, or if challenge suppression is off)
        //   403 — scheme matched but policy denied
        //   404 — AdminCookieEvents cookie-challenge suppression in Production (D-04):
        //         the admin cookie scheme fires a redirect challenge in response to the
        //         unauthenticated player Bearer; AdminCookieEvents converts that to 404 so
        //         admin surface is indistinguishable from non-mounted surface to an attacker.
        //         This 404 is for a route that EXISTS — confirmed by the existence guard above.
        var acceptedCodes = new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.NotFound };
        Assert.Contains(resp.StatusCode, acceptedCodes);
    }
}
