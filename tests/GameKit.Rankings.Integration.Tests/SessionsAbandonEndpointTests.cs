// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Core.Entities;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// Integration tests for <c>POST /api/sessions/{id}/abandon</c> (Phase 6 — PRES-05, D-20).
/// Anchors:
/// <list type="bullet">
///   <item><see cref="Abandon_Anonymous_Returns_401_Or_403"/> — service-token-required gate.</item>
///   <item><see cref="Abandon_NonExistent_Session_Returns_404"/> — unknown id maps to 404.</item>
///   <item><see cref="Abandon_PendingSession_Returns_409_InvalidState"/> — state-machine guard.</item>
///   <item><see cref="Abandon_ActiveSession_Returns_200_And_Transitions_To_Abandoned"/> — happy path.</item>
/// </list>
/// </summary>
[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class SessionsAbandonEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    public SessionsAbandonEndpointTests(PostgresFixture pg, RedisFixture redis)
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
    public async Task Abandon_Anonymous_Returns_401_Or_403()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "abandon-ladder-anon", _redis.ConnectionString);
        using var client = server.CreateClient();

        var sessionId = Guid.NewGuid();
        await SessionLifecycleTestHelpers.SeedSessionWithStateAsync(
            _cs, "abandon-ladder-anon", sessionId, GameSessionState.Active);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/abandon");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 401 or 403, got {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Abandon_NonExistent_Session_Returns_404()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "abandon-ladder-404", _redis.ConnectionString);
        using var client = server.CreateClient();

        var (rawToken, _) = await SessionLifecycleTestHelpers.IssueTokenAsync(server, "game-server-abandon-404");
        var missing = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{missing}/abandon");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Abandon_PendingSession_Returns_409_InvalidState()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "abandon-ladder-409", _redis.ConnectionString);
        using var client = server.CreateClient();

        var (rawToken, _) = await SessionLifecycleTestHelpers.IssueTokenAsync(server, "game-server-abandon-409");

        // /abandon requires Active state — Pending must be rejected.
        var sessionId = await SessionLifecycleTestHelpers.SeedPendingSessionAsync(_cs, "abandon-ladder-409");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/abandon");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_session_state", body);
        Assert.Contains("Pending", body);
    }

    [Fact]
    public async Task Abandon_ActiveSession_Returns_200_And_Transitions_To_Abandoned()
    {
        await using var server = await SessionLifecycleTestServer.CreateAsync(_cs, "abandon-ladder-ok", _redis.ConnectionString);
        using var client = server.CreateClient();

        var (rawToken, _) = await SessionLifecycleTestHelpers.IssueTokenAsync(server, "game-server-abandon-ok");

        var sessionId = Guid.NewGuid();
        await SessionLifecycleTestHelpers.SeedSessionWithStateAsync(
            _cs, "abandon-ladder-ok", sessionId, GameSessionState.Active);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/abandon");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bodyText = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(bodyText);
        var stateText = doc.RootElement.GetProperty("state").GetString();
        Assert.Equal(nameof(GameSessionState.Abandoned), stateText);

        var dbState = await SessionLifecycleTestHelpers.QueryScalarStringAsync(
            _cs, $"SELECT \"State\" FROM gamekit.game_sessions WHERE \"Id\" = '{sessionId}'");
        Assert.Equal(nameof(GameSessionState.Abandoned), dbState);

        var completedAt = await SessionLifecycleTestHelpers.QueryScalarStringAsync(
            _cs, $"SELECT \"CompletedAt\" FROM gamekit.game_sessions WHERE \"Id\" = '{sessionId}'");
        Assert.NotNull(completedAt);
    }
}
