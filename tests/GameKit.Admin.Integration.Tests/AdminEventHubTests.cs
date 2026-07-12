// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.Integration.Tests.Mocks;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.SignalR.Client;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Admin.Integration.Tests;

/// <summary>
/// SC#2 integration tests for <see cref="GameKit.Admin.UI.Hubs.AdminEventHub"/>:
/// <list type="bullet">
///   <item>An unauthenticated WebSocket upgrade to <c>/admin/hubs/events</c> returns 401.</item>
///   <item>A player JWT (no admin cookie) cannot connect — cookie scheme isolation proven.</item>
///   <item>An admin event published on replica A reaches an admin client on replica B via the shared Redis backplane.</item>
/// </list>
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class AdminEventHubTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private AdminTestHost _hostA = default!;
    private AdminTestHost _hostB = default!;

    /// <summary>Initializes the test with shared Postgres + Redis fixtures.</summary>
    public AdminEventHubTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
        // Reset admin_users so seeds below do not conflict with prior test runs.
        ResetAdminUsers(_pg.OwnerConnectionString);
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // Both hosts share the same RedisFixture → same Redis container → shared SignalR backplane.
        // Use distinct usernames to avoid the UNIQUE(username) constraint on the shared Postgres DB.
        _hostA = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("hub-test-a", "hunter2hunter2", AdminRoles.Superadmin));
        _hostB = await AdminTestHost.StartAsync(
            _pg, _redis, env: "Production",
            seed: h => h.SeedAdminAsync("hub-test-b", "hunter2hunter2", AdminRoles.Superadmin));
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _hostA.DisposeAsync();
        await _hostB.DisposeAsync();
    }

    /// <summary>
    /// SC#2(a): an unauthenticated WebSocket upgrade to /admin/hubs/events returns 401
    /// before the handshake completes. The hub is mapped under MountPath so the path-based
    /// scheme selector routes the negotiate to the GameKitAdmin cookie scheme (Pitfall 2).
    /// </summary>
    [Fact(DisplayName = "SC#2(a): unauthenticated WebSocket upgrade to /admin/hubs/events is rejected (401 or 404 in Production)")]
    public async Task Unauthenticated_Upgrade_Returns_401()
    {
        var conn = new HubConnectionBuilder()
            .WithUrl($"http://localhost{_hostA.MountPath}/hubs/events", o =>
            {
                o.HttpMessageHandlerFactory = _ => _hostA.Server.CreateHandler();
                // No cookie — unauthenticated
            })
            .Build();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => conn.StartAsync());
        // In Production mode, AdminCookieEvents.RedirectToLogin returns 404 (not 401) so
        // admin paths are indistinguishable from non-mounted paths to unauthenticated callers
        // (same behavior proven by CrossSchemeIsolationTests). Accept 401 or 404 here — the
        // contract is "connection refused before WS handshake completes" (T-12-04-SPOOF2).
        Assert.True(
            ex.StatusCode == HttpStatusCode.Unauthorized
            || ex.StatusCode == HttpStatusCode.NotFound
            || (ex.Message != null && (ex.Message.Contains("401") || ex.Message.Contains("404"))),
            $"Expected 401 or 404 (unauthenticated) but got: {ex.StatusCode} / {ex.Message}");

        await conn.DisposeAsync();
    }

    /// <summary>
    /// SC#2(b): a player JWT in the Authorization header cannot connect to AdminEventHub.
    /// The hub is gated by AdminPolicies.Admin which pins the GameKitAdmin cookie scheme —
    /// a Bearer token cannot satisfy it (T-12-04-SPOOF mitigation / cross-scheme isolation).
    /// </summary>
    [Fact(DisplayName = "SC#2(b): player JWT in Authorization header cannot connect to AdminEventHub (cookie scheme required)")]
    public async Task PlayerJwt_CannotConnect_ToAdminHub()
    {
        using var issuer = new FakePlayerJwtIssuer();
        var jwt = issuer.IssueValidPlayerJwt(Guid.NewGuid(), Guid.NewGuid());

        var conn = new HubConnectionBuilder()
            .WithUrl($"http://localhost{_hostA.MountPath}/hubs/events", o =>
            {
                o.HttpMessageHandlerFactory = _ => _hostA.Server.CreateHandler();
                // Supply JWT via AccessTokenProvider (the standard player-hub query-string path).
                // The admin hub does NOT have a JwtBearer OnMessageReceived handler — but even
                // if the token is forwarded in Authorization: Bearer, the AdminPolicies.Admin
                // policy pins the GameKitAdmin scheme. The cookie scheme challenge fires and
                // returns 401/404 before the WS handshake completes.
                o.AccessTokenProvider = () => Task.FromResult<string?>(jwt);
            })
            .Build();

        // Player JWT must never grant access to the admin hub — assert not Connected.
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => conn.StartAsync());

        // The admin hub 401s (or 302/404 in production via AdminCookieEvents) for any
        // unauthenticated or improperly-schemed request.
        Assert.NotNull(ex);
        // Negative-space: never 200 — player JWT must not yield a Connected hub.
        Assert.True(
            ex.StatusCode is not null && ex.StatusCode != HttpStatusCode.OK,
            $"Expected non-200 response to player JWT on admin hub, got: {ex.StatusCode} / {ex.Message}");

        await conn.DisposeAsync();
    }

    /// <summary>
    /// SC#2(c): a message published to "gamekit:admin:events" on replica A reaches
    /// a connected admin client on replica B via the shared Redis backplane.
    /// Proves ADMIN-13: cross-replica admin event delivery via AdminLiveBroadcastService.
    /// </summary>
    [Fact(DisplayName = "SC#2(c): admin event published on host A reaches admin client on host B via Redis backplane")]
    public async Task AdminEvent_Published_On_HostA_Reaches_Client_On_HostB()
    {
        // Login as admin on host B and extract the session cookie value so the
        // HubConnection can forward it in the WebSocket upgrade request.
        var loginResp = await _hostB.Client.PostAsJsonAsync(
            $"{_hostB.MountPath}/api/login",
            new { username = "hub-test-b", password = "hunter2hunter2" });

        if (loginResp.StatusCode != HttpStatusCode.OK)
        {
            var body = await loginResp.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Admin login on host B failed: {loginResp.StatusCode} / {body}");
        }

        // Extract the gk_admin_session cookie from the login response.
        // TestServer stores cookies in the client's cookie container; read them
        // from the Set-Cookie response header for manual forwarding to HubConnection.
        string? sessionCookieHeader = null;
        if (loginResp.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var c in cookies)
            {
                if (c.StartsWith("gk_admin_session=", StringComparison.Ordinal))
                {
                    // Grab just the name=value portion (before the first semicolon).
                    sessionCookieHeader = c.Split(';')[0].Trim();
                    break;
                }
            }
        }

        if (sessionCookieHeader is null)
        {
            throw new InvalidOperationException(
                "Login response did not set gk_admin_session cookie. " +
                "Check that the admin cookie is set on the /admin/api/login endpoint.");
        }

        // Build a HubConnection to host B that carries the admin session cookie.
        // CookieContainer on HttpClientHandler is used so the cookie is included
        // in the negotiate POST and subsequent WebSocket upgrade.
        var cookieContainer = new CookieContainer();
        cookieContainer.Add(new Uri("http://localhost"), new Cookie(
            name: "gk_admin_session",
            value: sessionCookieHeader.Substring("gk_admin_session=".Length)));

        var connB = new HubConnectionBuilder()
            .WithUrl($"http://localhost{_hostB.MountPath}/hubs/events", o =>
            {
                o.HttpMessageHandlerFactory = _ =>
                {
                    // Chain: TestServer in-process handler wraps a SocketsHttpHandler
                    // with the cookie container carrying the admin session.
                    var inner = _hostB.Server.CreateHandler();
                    return new CookieInjectingHandler(sessionCookieHeader, inner);
                };
            })
            .Build();

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        connB.On<string>("ReceiveAdminEvent", payload => tcs.TrySetResult(payload));

        try
        {
            await connB.StartAsync();
            Assert.Equal(HubConnectionState.Connected, connB.State);

            // Publish via host A's IConnectionMultiplexer directly —
            // AdminLiveBroadcastService on host B should relay it as ReceiveAdminEvent.
            var (scopeA, muxA) = _hostA.Resolve<IConnectionMultiplexer>();
            using (scopeA)
            {
                await muxA.GetSubscriber().PublishAsync(
                    RedisChannel.Literal("gamekit:admin:events"),
                    "ping-from-host-a");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var received = await tcs.Task.WaitAsync(cts.Token);
            Assert.Equal("ping-from-host-a", received);
        }
        finally
        {
            await connB.StopAsync();
            await connB.DisposeAsync();
        }
    }

    // ---- helpers ----

    private static void ResetAdminUsers(string connectionString)
    {
        try
        {
            using var conn = new NpgsqlConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "TRUNCATE TABLE gamekit.admin_users";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01")
        {
            // Table does not yet exist — migrations will create it on first AdminTestHost start.
        }
    }

    /// <summary>
    /// Wraps an inner <see cref="HttpMessageHandler"/> and injects a Cookie header on every
    /// request so the admin session cookie is forwarded to the TestServer for the negotiate
    /// and WebSocket upgrade paths that HubConnectionBuilder sends.
    /// </summary>
    private sealed class CookieInjectingHandler : DelegatingHandler
    {
        private readonly string _cookieHeaderValue;

        public CookieInjectingHandler(string cookieHeaderValue, HttpMessageHandler inner)
            : base(inner)
        {
            _cookieHeaderValue = cookieHeaderValue;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("Cookie", _cookieHeaderValue);
            return base.SendAsync(request, cancellationToken);
        }
    }
}

/// <summary>
/// CR-01 regression: a single-instance install (no <see cref="IConnectionMultiplexer"/>
/// registered) must start cleanly AND serve an <see cref="GameKit.Admin.UI.Hubs.AdminEventHub"/>
/// WebSocket connection in-process without throwing
/// <see cref="InvalidOperationException"/> from <c>AdminBackplanePostConfigure</c>.
/// </summary>
[Collection("Admin")]
[Trait("Category", "Integration")]
public sealed class AdminEventHubNoRedisTests
{
    private readonly PostgresFixture _pg;

    /// <summary>Initializes the test with only the Postgres fixture — no Redis required.</summary>
    public AdminEventHubNoRedisTests(PostgresFixture pg, RedisFixture _)
    {
        _pg = pg;
        // Reset admin_users so this test's seed does not collide with other tests.
        try
        {
            using var conn = new NpgsqlConnection(_pg.OwnerConnectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "TRUNCATE TABLE gamekit.admin_users";
            cmd.ExecuteNonQuery();
        }
        catch (PostgresException ex) when (ex.SqlState == "42P01") { }
    }

    /// <summary>
    /// CR-01: a single-instance admin host (no <c>IConnectionMultiplexer</c>) must start
    /// without error AND accept an unauthenticated WebSocket upgrade to <c>AdminEventHub</c>
    /// without throwing <see cref="InvalidOperationException"/> from
    /// <c>AdminBackplanePostConfigure.PostConfigure</c>. The hub still rejects unauthenticated
    /// callers (401/404) — the key assertion is "no crash on first hub connection attempt".
    /// </summary>
    [Fact(DisplayName = "CR-01: single-instance (no-Redis) host starts and AdminEventHub upgrade does not crash the host")]
    public async Task SingleInstance_NoRedis_Host_Starts_And_Hub_DoesNotCrash()
    {
        await using var host = await AdminTestHost.StartNoRedisAsync(
            _pg, env: "Development",
            seed: h => h.SeedAdminAsync("no-redis-test", "hunter2hunter2", AdminRoles.Superadmin));

        // Verify host is healthy: admin login endpoint should respond.
        var resp = await host.Client.GetAsync("/admin/login");
        // In Development the login page redirects or renders; at minimum the host is alive (not 500).
        Assert.NotEqual(System.Net.HttpStatusCode.InternalServerError, resp.StatusCode);

        // Attempt an unauthenticated WebSocket upgrade to AdminEventHub.
        // The hub must reject with 401/404, NOT crash the host with InvalidOperationException
        // from AdminBackplanePostConfigure (the CR-01 bug). If the host crashed, the negotiate
        // would return 500 or the HubConnection would throw an unexpected exception type.
        var conn = new HubConnectionBuilder()
            .WithUrl($"http://localhost{host.MountPath}/hubs/events", o =>
            {
                o.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                // No cookie — unauthenticated, but must NOT trigger a crash.
            })
            .Build();

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => conn.StartAsync());
        // The hub returns 401 or 404 (not 500 — a 500 would indicate the host crashed).
        Assert.True(
            ex.StatusCode == System.Net.HttpStatusCode.Unauthorized
            || ex.StatusCode == System.Net.HttpStatusCode.NotFound
            || ex.StatusCode == System.Net.HttpStatusCode.Redirect
            || (ex.Message != null && (ex.Message.Contains("401") || ex.Message.Contains("404"))),
            $"Expected 401 or 404 (auth rejection) but got: {ex.StatusCode} / {ex.Message}. " +
            "A 500 here indicates CR-01 is NOT fixed (AdminBackplanePostConfigure still crashes).");

        await conn.DisposeAsync();
    }
}
