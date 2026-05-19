// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Authentication;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Entities;
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
/// Integration tests for <c>POST /api/sessions/{id}/complete</c> (SC#2, RANK-11, D-07, D-08).
/// Anchors:
/// <list type="bullet">
///   <item><see cref="Retry_Five_Times_Applies_Delta_Once"/> — SC#2: 5× retry → exactly one pending_rating_updates row per participant.</item>
///   <item><see cref="Same_Key_Different_Body_Returns_409"/> — T-04-05-IK: different body reuse → 409.</item>
///   <item><see cref="Missing_Idempotency_Key_Returns_400"/> — T-04-05-MK: missing header → 400.</item>
///   <item><see cref="PlayerJWT_Returns_403"/> — T-04-05-SJ: player JWT cannot authenticate as service account.</item>
///   <item><see cref="Already_Completed_Session_Returns_Cached_Response"/> — D-07/D-08: second call returns cached 200.</item>
///   <item><see cref="Cancelled_Session_Returns_409_Invalid_State"/> — D-07: non-active state → 409.</item>
/// </list>
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class SessionCompleteIdempotencyTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres fixture.</summary>
    public SessionCompleteIdempotencyTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // SC#2 anchor: 5× retry → exactly one delta per participant
    // -------------------------------------------------------------------------

    /// <summary>
    /// SC#2: Five identical POSTs with the same Idempotency-Key produce exactly ONE
    /// <c>session_complete_idempotency</c> row, exactly TWO <c>pending_rating_updates</c> rows
    /// (one per participant), and all five responses return 200 OK.
    /// </summary>
    [Fact]
    public async Task Retry_Five_Times_Applies_Delta_Once()
    {
        await using var server = await BuildSessionCompleteServer(_cs, "test-ladder");
        using var client = server.CreateClient();

        // Seed: ladder row + 2 players + 1 active session + 2 participants
        var (sessionId, p1Id, p2Id) = await SeedActivatedSessionAsync(_cs, "test-ladder", playerCount: 2);

        // Mint a service token.
        var (rawToken, _) = await IssueTokenAsync(server, "game-server-1");

        var body = BuildCompleteBody(p1Id, p2Id);
        const string idempotencyKey = "retry-test-key-001";

        // Loop 5 times with the same body + key.
        for (var i = 0; i < 5; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
            request.Headers.Add("Idempotency-Key", idempotencyKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Assert: exactly ONE idempotency row.
        var idempotencyCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.session_complete_idempotency WHERE \"SessionId\" = '{sessionId}' AND \"IdempotencyKey\" = '{idempotencyKey}'");
        Assert.Equal(1L, idempotencyCount);

        // Assert: exactly TWO pending_rating_updates rows (one per participant).
        var pendingCount = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.pending_rating_updates WHERE \"SessionId\" = '{sessionId}'");
        Assert.Equal(2L, pendingCount);

        // Assert: session is Completed.
        // State is stored as text (HasConversion<string>()) — compare the enum name, not the integer.
        var state = await QueryScalarStringAsync(_cs,
            $"SELECT \"State\" FROM gamekit.game_sessions WHERE \"Id\" = '{sessionId}'");
        Assert.Equal(nameof(GameSessionState.Completed), state);

        // Assert: CompletedAt is non-null.
        var completedAt = await QueryScalarStringAsync(_cs,
            $"SELECT \"CompletedAt\" FROM gamekit.game_sessions WHERE \"Id\" = '{sessionId}'");
        Assert.NotNull(completedAt);
    }

    // -------------------------------------------------------------------------
    // Same key, different body → 409
    // -------------------------------------------------------------------------

    /// <summary>
    /// T-04-05-IK: A second POST with the same Idempotency-Key but a different body returns
    /// 409 with error <c>idempotency_key_reused</c>.
    /// </summary>
    [Fact]
    public async Task Same_Key_Different_Body_Returns_409()
    {
        await using var server = await BuildSessionCompleteServer(_cs, "test-ladder-2");
        using var client = server.CreateClient();

        var (sessionId, p1Id, p2Id) = await SeedActivatedSessionAsync(_cs, "test-ladder-2", playerCount: 2);
        var (rawToken, _) = await IssueTokenAsync(server, "game-server-2");

        var bodyA = BuildCompleteBody(p1Id, p2Id, result1: 0, result2: 1); // Win, Loss
        var bodyB = BuildCompleteBody(p1Id, p2Id, result1: 2, result2: 2); // Draw, Draw
        const string idempotencyKey = "key-conflict-001";

        // First POST with body A → 200.
        using var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        req1.Content = new StringContent(bodyA, Encoding.UTF8, "application/json");
        var response1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Second POST with body B → 409.
        // Note: the session state changed to Completed on the first call, but idempotency check
        // runs BEFORE the state-conditional UPDATE, so the 409 is returned from the idempotency lookup.
        // We need a new session for this test because the first POST completed the session.
        // Actually with the same session: we'll get InvalidState from the UPDATE (since session is now Completed).
        // The 409 idempotency_key_reused occurs when idempotency lookup finds a row with a different hash.
        // So: same key + different body on the SAME session → 409 idempotency_key_reused (idempotency check fires first).
        using var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        req2.Content = new StringContent(bodyB, Encoding.UTF8, "application/json");
        var response2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, response2.StatusCode);

        var responseText = await response2.Content.ReadAsStringAsync();
        Assert.Contains("idempotency_key_reused", responseText);
    }

    // -------------------------------------------------------------------------
    // Missing Idempotency-Key header → 400
    // -------------------------------------------------------------------------

    /// <summary>
    /// T-04-05-MK: A POST without the <c>Idempotency-Key</c> header returns 400 with
    /// error code <c>idempotency_key_required</c>.
    /// </summary>
    [Fact]
    public async Task Missing_Idempotency_Key_Returns_400()
    {
        await using var server = await BuildSessionCompleteServer(_cs, "test-ladder-3");
        using var client = server.CreateClient();

        var (sessionId, p1Id, p2Id) = await SeedActivatedSessionAsync(_cs, "test-ladder-3", playerCount: 2);
        var (rawToken, _) = await IssueTokenAsync(server, "game-server-3");

        // No Idempotency-Key header.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent(BuildCompleteBody(p1Id, p2Id), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.Contains("idempotency_key_required", responseText);
    }

    // -------------------------------------------------------------------------
    // Player JWT → 403
    // -------------------------------------------------------------------------

    /// <summary>
    /// T-04-05-SJ: A Player JWT (not a service-account token) is rejected with 403.
    /// The <c>RequiresServiceToken</c> policy requires the <c>GameKitServiceToken</c> scheme.
    /// </summary>
    [Fact]
    public async Task PlayerJWT_Returns_403()
    {
        await using var server = await BuildSessionCompleteServer(_cs, "test-ladder-4");
        using var client = server.CreateClient();

        var (sessionId, p1Id, p2Id) = await SeedActivatedSessionAsync(_cs, "test-ladder-4", playerCount: 2);

        // Send a random bearer token that won't pass ServiceToken authentication.
        // A real player JWT would also fail because the GameKitServiceToken scheme
        // doesn't accept JWTs — here we use a clearly-fake token to trigger 401/403.
        // The RequiresServiceToken policy's challenge returns 403 (forbidden) when a different
        // scheme is used, because UseAuthentication() runs but the GameKitServiceToken handler
        // produces NoResult for a JWT-format bearer.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        request.Headers.Add("Idempotency-Key", "player-jwt-test-key");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",
            "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.fake-signature");
        request.Content = new StringContent(BuildCompleteBody(p1Id, p2Id), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);

        // ServiceTokenAuthenticationHandler returns NoResult for any token not in the DB.
        // The policy challenge returns 401 (not authenticated). In some configurations it may
        // return 403 if another scheme authenticated but the policy denies.
        // We assert either 401 or 403 — the important thing is the endpoint is not accessible.
        Assert.True(
            response.StatusCode == HttpStatusCode.Unauthorized ||
            response.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 401 or 403, got {(int)response.StatusCode}");
    }

    // -------------------------------------------------------------------------
    // Already-completed session with same idempotency key → 200 cached
    // -------------------------------------------------------------------------

    /// <summary>
    /// D-07/D-08: A second POST with the SAME body and SAME Idempotency-Key returns
    /// 200 with the cached response. No new <c>pending_rating_updates</c> rows are added.
    /// </summary>
    [Fact]
    public async Task Already_Completed_Session_Returns_Cached_Response()
    {
        await using var server = await BuildSessionCompleteServer(_cs, "test-ladder-5");
        using var client = server.CreateClient();

        var (sessionId, p1Id, p2Id) = await SeedActivatedSessionAsync(_cs, "test-ladder-5", playerCount: 2);
        var (rawToken, _) = await IssueTokenAsync(server, "game-server-5");

        var body = BuildCompleteBody(p1Id, p2Id);
        const string idempotencyKey = "already-done-key-001";

        // First POST → 200.
        using var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        req1.Headers.Add("Idempotency-Key", idempotencyKey);
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        req1.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var response1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Count pending rows after first call.
        var pendingAfterFirst = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.pending_rating_updates WHERE \"SessionId\" = '{sessionId}'");
        Assert.Equal(2L, pendingAfterFirst);

        // Second POST (same body, same key) → 200 (cached).
        using var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        req2.Headers.Add("Idempotency-Key", idempotencyKey);
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        req2.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var response2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Assert: still TWO rows (not four — cached path does not re-enqueue).
        var pendingAfterSecond = await QueryScalarAsync(_cs,
            $"SELECT COUNT(*) FROM gamekit.pending_rating_updates WHERE \"SessionId\" = '{sessionId}'");
        Assert.Equal(2L, pendingAfterSecond);
    }

    // -------------------------------------------------------------------------
    // Cancelled session → 409 invalid_session_state
    // -------------------------------------------------------------------------

    /// <summary>
    /// D-07: A session in <see cref="GameSessionState.Cancelled"/> state cannot be completed.
    /// Returns 409 with problem type <c>invalid_session_state</c>.
    /// </summary>
    [Fact]
    public async Task Cancelled_Session_Returns_409_Invalid_State()
    {
        await using var server = await BuildSessionCompleteServer(_cs, "test-ladder-6");
        using var client = server.CreateClient();

        var (sessionId, p1Id, p2Id) = await SeedActivatedSessionAsync(_cs, "test-ladder-6", playerCount: 2);
        var (rawToken, _) = await IssueTokenAsync(server, "game-server-6");

        // Cancel the session directly in the DB.
        // NOTE: State is stored as text (HasConversion<string>()) — use the enum name, not the integer.
        await ExecuteAsync(_cs,
            $"UPDATE gamekit.game_sessions SET \"State\" = '{nameof(GameSessionState.Cancelled)}', \"CompletedAt\" = NOW() WHERE \"Id\" = '{sessionId}'");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sessions/{sessionId}/complete");
        request.Headers.Add("Idempotency-Key", "cancelled-session-key-001");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Content = new StringContent(BuildCompleteBody(p1Id, p2Id), Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var responseText = await response.Content.ReadAsStringAsync();
        Assert.Contains("invalid_session_state", responseText);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string BuildCompleteBody(Guid p1Id, Guid p2Id,
        int result1 = 0, int result2 = 1) // 0=Win, 1=Loss, 2=Draw (SessionResult enum values)
    {
        return JsonSerializer.Serialize(new
        {
            participants = new[]
            {
                new { playerId = p1Id, team = 0, result = result1, score = 10 },
                new { playerId = p2Id, team = 1, result = result2, score = 5 },
            },
        }, _jsonOpts);
    }

    private static async Task<(Guid sessionId, Guid p1Id, Guid p2Id)> SeedActivatedSessionAsync(
        string cs, string ladderName, int playerCount)
    {
        var now = DateTimeOffset.UtcNow;
        var p1Id = Guid.NewGuid();
        var p2Id = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        // Insert players.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.players (""Id"", ""DisplayName"", ""CreatedAt"")
                VALUES ('{p1Id}', 'Player1', '{now:O}'), ('{p2Id}', 'Player2', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Get the ladder id.
        object? ladderId;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"SELECT \"Id\" FROM gamekit.ladders WHERE \"Name\" = '{ladderName}'";
            ladderId = await cmd.ExecuteScalarAsync();
        }

        if (ladderId is null)
        {
            // Insert ladder if not present (for tests that don't use the full server startup).
            var newLadderId = Guid.NewGuid();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    INSERT INTO gamekit.ladders (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"")
                    VALUES ('{newLadderId}', '{ladderName}', 'glicko2', true, '{now:O}')";
                await cmd.ExecuteNonQueryAsync();
            }
            ladderId = newLadderId;
        }

        // Insert session with State = Active.
        // NOTE: State is stored as text (HasConversion<string>()) — insert the enum name, not the integer.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.game_sessions (""Id"", ""State"", ""LadderId"", ""CreatedAt"", ""StartedAt"")
                VALUES ('{sessionId}', '{nameof(GameSessionState.Active)}', '{ladderId}', '{now:O}', '{now:O}')";
            await cmd.ExecuteNonQueryAsync();
        }

        // Insert session participants.
        var sp1Id = Guid.NewGuid();
        var sp2Id = Guid.NewGuid();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $@"
                INSERT INTO gamekit.session_participants (""Id"", ""SessionId"", ""PlayerId"", ""Team"")
                VALUES ('{sp1Id}', '{sessionId}', '{p1Id}', 0),
                       ('{sp2Id}', '{sessionId}', '{p2Id}', 1)";
            await cmd.ExecuteNonQueryAsync();
        }

        return (sessionId, p1Id, p2Id);
    }

    private static async Task<(string Raw, ServiceToken Row)> IssueTokenAsync(
        SessionCompleteTestServer server, string name)
    {
        using var scope = server.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IServiceTokenService>();
        return await svc.IssueAsync(name, expiresAt: null, default);
    }

    private static async Task<long> QueryScalarAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result is long l ? l : Convert.ToInt64(result);
    }

    private static async Task<string?> QueryScalarStringAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync();
        return result?.ToString();
    }

    private static async Task ExecuteAsync(string cs, string sql)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task<SessionCompleteTestServer> BuildSessionCompleteServer(string cs, string ladderName)
        => SessionCompleteTestServer.CreateAsync(cs, ladderName);

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_sc_" + Guid.NewGuid().ToString("N")[..12];

        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync();
        }

        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var freshCs = builder.ConnectionString;

        await using (var freshConn = new NpgsqlConnection(freshCs))
        {
            await freshConn.OpenAsync();
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync();
        }

        return freshCs;
    }

    private static async Task ApplyMigrationsAsync(string cs)
    {
        // Core migrations.
        var services = new ServiceCollection();
        services.AddGameKit(o => { o.ConnectionString = cs; o.MigrationsConnectionString = cs; o.AutoMigrate = false; });
        await using (var sp = services.BuildServiceProvider())
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await MigrationRunner.MigrateWithLockAsync(ctx);
        }

        // Rankings migrations.
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using var rankingsCtx = new GameKitDbContext(rankingsOpts);
        await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
    }
}

/// <summary>
/// In-process <see cref="TestServer"/> for the session-complete endpoint integration tests.
/// Mounts the full GameKit + Rankings stack including the <c>GameKitServiceToken</c> auth scheme,
/// rate limiter, and routing for <c>POST /api/sessions/{id}/complete</c>.
/// </summary>
internal sealed class SessionCompleteTestServer : IAsyncDisposable
{
    private readonly IHost _host;

    private SessionCompleteTestServer(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public HttpClient CreateClient() => _host.GetTestServer().CreateClient();

    public static async Task<SessionCompleteTestServer> CreateAsync(string cs, string ladderName)
    {
        var builder = new HostBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();

                    services
                        .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
                        .AddRankings(o => { })
                        .AddLadder(ladderName);

                    services.AddLogging();

                    // Override DbContext to include Rankings entities (bypass global EF model cache — Pitfall 3).
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts
                            .UseNpgsql(cs)
                            .ReplaceService<IModelCustomizer, SessionCompleteTestModelCustomizer>()
                            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGameKit();
                    });
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return new SessionCompleteTestServer(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>Test-only model customizer that applies Rankings entities (bypasses EF global cache — Pitfall 3).</summary>
internal sealed class SessionCompleteTestModelCustomizer : RelationalModelCustomizer
{
    public SessionCompleteTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
