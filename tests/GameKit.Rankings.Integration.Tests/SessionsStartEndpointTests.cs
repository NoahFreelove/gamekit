// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Rankings.Authentication;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Services;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests for <c>POST /api/sessions/{id}/start</c> (Phase 6 — PRES-05, D-20).
/// Anchors:
/// <list type="bullet">
///   <item><see cref="Start_Anonymous_Returns_401_Or_403"/> — service-token-required gate.</item>
///   <item><see cref="Start_NonExistent_Session_Returns_404"/> — unknown id maps to 404.</item>
///   <item><see cref="Start_AlreadyActive_Returns_409_InvalidState"/> — state-machine guard.</item>
///   <item><see cref="Start_Pending_Session_Returns_200_And_Transitions_To_Active"/> — happy path; row updated.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Deviation note: the plan frontmatter places these tests in <c>tests/GameKit.Core.Integration.Tests/</c>
/// but that project deliberately has no HTTP / WebApplicationFactory / service-token-auth
/// infrastructure (see <c>GameKit.Core.Integration.Tests.csproj</c> — only Npgsql + StackExchange.Redis +
/// Hosting are referenced). Adding HTTP test infrastructure there would require pulling in Rankings
/// for <c>ServiceTokenAuthenticationHandler</c>, violating the package boundary. The /complete
/// endpoint's existing integration tests live here (<see cref="SessionCompleteIdempotencyTests"/>)
/// for exactly the same reason; we follow that precedent.
/// </para>
/// </remarks>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class SessionsStartEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    public SessionsStartEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _cs = await SessionLifecycleTestHelpers.CreateFreshDatabaseAsync(_pg);
        await SessionLifecycleTestHelpers.ApplyMigrationsAsync(_cs);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Start_Anonymous_Returns_401_Or_403()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "start-ladder-anon", _redis.ConnectionString);
        using var client = server.CreateClient();

        var sessionId = await SessionLifecycleTestHelpers.SeedPendingSessionAsync(_cs, "start-ladder-anon");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/start");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 401 or 403, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Start_NonExistent_Session_Returns_404()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "start-ladder-404", _redis.ConnectionString);
        using var client = server.CreateClient();

        var (rawToken, _) = await SessionLifecycleTestHelpers.IssueTokenAsync(server, "game-server-404");
        var missing = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{missing}/start");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Start_AlreadyActive_Returns_409_InvalidState()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "start-ladder-409", _redis.ConnectionString);
        using var client = server.CreateClient();

        var (rawToken, _) = await SessionLifecycleTestHelpers.IssueTokenAsync(server, "game-server-409");

        // Seed an already-Active session — /start must reject.
        var sessionId = Guid.NewGuid();
        await SessionLifecycleTestHelpers.SeedSessionWithStateAsync(
            _cs, "start-ladder-409", sessionId, GameSessionState.Active);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/start");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_session_state", body);
        Assert.Contains("Active", body);
    }

    [Fact]
    public async Task Start_Pending_Session_Returns_200_And_Transitions_To_Active()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "start-ladder-ok", _redis.ConnectionString);
        using var client = server.CreateClient();

        var (rawToken, _) = await SessionLifecycleTestHelpers.IssueTokenAsync(server, "game-server-ok");
        var sessionId = await SessionLifecycleTestHelpers.SeedPendingSessionAsync(_cs, "start-ladder-ok");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/start");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bodyText = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyText);
        var stateText = doc.RootElement.GetProperty("state").GetString();
        Assert.Equal(nameof(GameSessionState.Active), stateText);

        // DB row reflects the transition.
        var dbState = await SessionLifecycleTestHelpers.QueryScalarStringAsync(
            _cs, $"SELECT \"State\" FROM gamekit.game_sessions WHERE \"Id\" = '{sessionId}'");
        Assert.Equal(nameof(GameSessionState.Active), dbState);

        var startedAt = await SessionLifecycleTestHelpers.QueryScalarStringAsync(
            _cs, $"SELECT \"StartedAt\" FROM gamekit.game_sessions WHERE \"Id\" = '{sessionId}'");
        Assert.NotNull(startedAt);
    }
}
