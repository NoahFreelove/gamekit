// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Threading;
using GameKit.Admin.UI.Services;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Lobby.Builder;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using GameKit.Presence.Builder;
using GameKit.Rankings.Algorithms;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Entities;  // SeasonResetPolicy
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Platformer3D.Algorithms;
using Platformer3D.GameServer;
using Platformer3D.Strategy;

namespace GameKit.Platformer3D.Integration.Tests;

/// <summary>
/// In-process ASP.NET Core test host that mirrors the <c>samples/Platformer3D/Program.cs</c>
/// composition: custom <see cref="BestTimeMatchmakingStrategy"/> (via <c>services.Replace</c>
/// after <c>AddMatchmaking()</c>), custom <see cref="TimeMarginRankingAlgorithm"/>, embedded
/// <see cref="PlatformerGameServerService"/>, admin console, and the full endpoint surface.
/// Used by all Platformer3D integration tests (R5 / R7 / R8 / R9 / R10).
/// </summary>
/// <remarks>
/// Mirrors <c>tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs</c>. The ephemeral RSA
/// keypair is generated at construction; all JWTs minted via <see cref="MintPlayerJwt"/>
/// validate against the in-process JwtBearer middleware.
/// </remarks>
internal sealed class PlatformerTestApp : IAsyncDisposable
{
    private readonly string _keyDir;
    private readonly string _privPath;
    private readonly string _pubPath;
    private readonly RSA _signingRsa;
    private IHost? _host;

    /// <summary>HTTP client bound to the in-memory test server.</summary>
    public HttpClient Client { get; private set; } = default!;

    /// <summary>Connection string for the fresh per-host database.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>JWT issuer (audience matches).</summary>
    public string Issuer { get; } = "gk-platformer3d-test";

    /// <summary>JWT audience.</summary>
    public string Audience { get; } = "gk-platformer3d-test";

    /// <summary>The in-process test server — exposes <c>CreateHandler()</c> for SignalR hub connections.</summary>
    public TestServer Server => _host!.GetTestServer();

    /// <summary>Seeded "platformer" ladder id — usable for enqueue / lobby creation.</summary>
    public Guid PlatformerLadderId { get; private set; }

    /// <summary>
    /// Constructs the test app — generates an ephemeral RSA PEM keypair under a temp directory.
    /// </summary>
    public PlatformerTestApp()
    {
        _keyDir = Path.Combine(Path.GetTempPath(), $"gk-platformer-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_keyDir);
        _privPath = Path.Combine(_keyDir, "priv.pem");
        _pubPath = Path.Combine(_keyDir, "pub.pem");
        _signingRsa = RSA.Create(2048);
        File.WriteAllText(_privPath, _signingRsa.ExportRSAPrivateKeyPem());
        File.WriteAllText(_pubPath, _signingRsa.ExportRSAPublicKeyPem());
    }

    /// <summary>
    /// Builds and starts the host against a fresh per-host database. Replaces the registered
    /// <c>IConnectionMultiplexer</c> with one connected to <paramref name="redis"/>'s
    /// connection string.
    /// </summary>
    public async Task StartAsync(PostgresFixture pg, RedisFixture redis,
        Action<IServiceCollection>? serviceOverrides = null)
    {
        ConnectionString = await PlatformerIntegrationFixture.CreateFreshDatabaseAsync(pg);
        await PlatformerIntegrationFixture.ApplyPlatformerMigrationsAsync(ConnectionString);

        // Seed the "platformer" ladder row so enqueue / lobby FK constraints are satisfied.
        PlatformerLadderId = await SeedPlatformerLadderAsync(ConnectionString);

        _host = await Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    // Redis multiplexer — required by Matchmaking and Lobby.
                    services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
                        StackExchange.Redis.ConnectionMultiplexer.Connect(redis.ConnectionString));

                    // Custom ranking algorithm (registered BEFORE AddRankings, no shadowing risk).
                    services.AddSingleton<IRankingAlgorithm, TimeMarginRankingAlgorithm>();

                    var gkBuilder = services.AddGameKit(o =>
                    {
                        o.ConnectionString = ConnectionString;
                        o.AutoMigrate = false;
                    });

                    gkBuilder.AddAuth(auth =>
                    {
                        auth.Jwt.Issuer = Issuer;
                        auth.Jwt.Audience = Audience;
                        auth.Jwt.PrivateKeyPemPath = _privPath;
                        auth.Jwt.PublicKeyPemPath = _pubPath;
                        auth.Jwt.Kid = "test-kid";
                        // Steam / Discord not needed — guest is the onramp.
                        auth.Steam.Realm = string.Empty;
                        auth.Steam.CallbackPath = "/auth/callback/steam";
                        auth.Discord.ClientId = string.Empty;
                        auth.Discord.ClientSecret = string.Empty;
                        auth.Discord.CallbackPath = "/auth/callback/discord";
                    });

                    gkBuilder.AddRankings().AddLadder("platformer", c =>
                    {
                        c.Algorithm = "time-margin";
                        c.DefaultRating = 1000;
                        c.DefaultRd = 350;
                        c.DefaultVolatility = 0.06;
                        c.RatingPeriod = TimeSpan.FromMinutes(1);
                        c.ResetPolicy = SeasonResetPolicy.SoftRegress;
                    });

                    gkBuilder.AddMatchmaking(opts =>
                    {
                        opts.Ticker.TickIntervalMs = 200; // Fast tick for tests
                    }).AddLadder("platformer", ladder =>
                    {
                        ladder.BracketStart = 0;
                        ladder.BracketEnd = 60_000;
                        ladder.BracketRampSeconds = 60;
                        ladder.PartyRatingAggregator = PartyRatingAggregator.Mean;
                    });

                    // A3 LOCKED: Replace AFTER AddMatchmaking so BestTimeMatchmakingStrategy
                    // is the sole IMatchmakingStrategy resolved by MatchmakerTickerService.
                    services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>());

                    gkBuilder.AddPresence();
                    gkBuilder.AddLobby();

                    // Note: AddGameKitAdmin is intentionally omitted from the test host.
                    // Admin.UI's SuperadminGateHostedService queries admin_users at startup,
                    // but the test migration chain (Core→Auth→Rankings→Matchmaking→Lobby)
                    // does not include the Admin migration. Admin console is verified by the
                    // human-verify checkpoint (Task 3) against the real docker-compose stack.
                    //
                    // However, GameKit.Matchmaking's RedisMatchmakingControlService +
                    // MatchmakingReconcilerService both depend on IAdminAuditWriter (for admin
                    // audit trails). Without Admin.UI, no implementation is registered.
                    // Register a no-op implementation so DI validation passes and the ticker runs.
                    services.AddScoped<IAdminAuditWriter, NullAdminAuditWriter>();

                    gkBuilder.AddGameKitHealthChecks();

                    // GameServer in-process (D-13).
                    services.AddHttpClient("platformer.web-api");
                    services.AddSingleton<PlatformerGameServerService>();
                    services.AddHostedService(sp => sp.GetRequiredService<PlatformerGameServerService>());

                    // Runtime DbContext with all five packages' entities visible.
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts.UseNpgsql(ConnectionString)
                              .ReplaceService<IModelCustomizer, PlatformerTestModelCustomizer>());

                    serviceOverrides?.Invoke(services);
                });

                web.Configure(app =>
                {
                    // UseWebSockets BEFORE UseRouting so TestServer WebSocket transport works.
                    app.UseWebSockets();
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseGameKitAuth();
                    app.UseGameKit();
                    // Note: UseGameKitAdmin / MapGameKitAdmin intentionally omitted — see AddGameKitAdmin note above.
                    app.UseEndpoints(e =>
                    {
                        e.MapAuth();
                        e.MapGameKit();
                        e.MapRankings();
                        e.MapMatchmaking();
                        e.MapLobby();
                        e.MapPresence();
                        e.MapGameKitHealth();
                    });
                });
            })
            .StartAsync()
            .ConfigureAwait(false);

        Client = _host.GetTestClient();
    }

    // ─── JWT helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Mints a valid player JWT signed with the host's RSA private key, accepted by
    /// the host's JwtBearer middleware. Sets <c>sub</c> and <c>NameIdentifier</c> to
    /// <paramref name="playerId"/>; <c>is_guest</c> is set to "true" (guest player).
    /// </summary>
    public string MintPlayerJwt(Guid playerId, bool isGuest = true)
    {
        var creds = new SigningCredentials(
            new RsaSecurityKey(_signingRsa),
            SecurityAlgorithms.RsaSha256)
        {
            CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
        };
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim("sub", playerId.ToString()),
                new Claim(ClaimTypes.NameIdentifier, playerId.ToString()),
                new Claim("is_guest", isGuest ? "true" : "false"),
                new Claim("provider", isGuest ? "guest" : "test"),
            },
            notBefore: now.AddMinutes(-1),
            expires: now.AddHours(1),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Returns an <see cref="HttpClient"/> with the Bearer token pre-attached.
    /// Idempotently upserts the player row to satisfy FK constraints.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(Guid playerId, bool isGuest = true)
    {
        EnsurePlayerRow(playerId);
        var client = _host!.GetTestClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", MintPlayerJwt(playerId, isGuest));
        return client;
    }

    /// <summary>
    /// Builds a <see cref="HubConnection"/> to <c>/hubs/lobby</c> routed through the
    /// in-process test server. The JWT for <paramref name="playerId"/> is supplied via
    /// <c>AccessTokenProvider</c> so the JwtBearer <c>OnMessageReceived</c> hook picks it up.
    /// </summary>
    public HubConnection ConnectLobbyHub(Guid playerId)
    {
        var jwt = MintPlayerJwt(playerId);
        return new HubConnectionBuilder()
            .WithUrl("http://localhost/hubs/lobby", o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.AccessTokenProvider = () => Task.FromResult<string?>(jwt);
            })
            .Build();
    }

    // ─── DB helpers ──────────────────────────────────────────────────────────

    /// <summary>Idempotent INSERT of a player row so FK constraints succeed.</summary>
    public void EnsurePlayerRow(Guid playerId, string? displayName = null)
    {
        using var conn = new NpgsqlConnection(ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.players
            (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)
            ON CONFLICT (""Id"") DO NOTHING";
        cmd.Parameters.AddWithValue("id", playerId);
        cmd.Parameters.AddWithValue("name", displayName ?? "P_" + playerId.ToString("N")[..8]);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Counts <c>player_identities</c> rows for <paramref name="playerId"/>.
    /// Returns 0 for a no-PII guest player.
    /// </summary>
    public async Task<int> CountPlayerIdentitiesAsync(Guid playerId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.player_identities WHERE ""PlayerId"" = @id";
        cmd.Parameters.AddWithValue("id", playerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Counts <c>player_credentials</c> rows for <paramref name="playerId"/>.
    /// Returns 0 for a no-PII guest player.
    /// </summary>
    public async Task<int> CountPlayerCredentialsAsync(Guid playerId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.player_credentials WHERE ""PlayerId"" = @id";
        cmd.Parameters.AddWithValue("id", playerId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Returns the <c>DisplayName</c> for <paramref name="playerId"/> from the database.
    /// </summary>
    public async Task<string?> GetPlayerDisplayNameAsync(Guid playerId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""DisplayName"" FROM gamekit.players WHERE ""Id"" = @id";
        cmd.Parameters.AddWithValue("id", playerId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    /// <summary>
    /// Counts <c>matchmaking_tickets</c> rows that are currently in the queue (Status = 0 = Queued)
    /// for the given <paramref name="ladderId"/>.
    /// </summary>
    public async Task<int> CountQueuedTicketsAsync(Guid ladderId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.matchmaking_tickets
            WHERE ""LadderId"" = @lid AND ""Status"" = 0";
        cmd.Parameters.AddWithValue("lid", ladderId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Returns the count of <c>game_sessions</c> outcome rows for the given
    /// <paramref name="sessionId"/> (should be exactly 1 after an idempotent completion).
    /// </summary>
    public async Task<int> CountGameSessionOutcomesAsync(Guid sessionId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM gamekit.game_sessions WHERE ""Id"" = @id";
        cmd.Parameters.AddWithValue("id", sessionId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Returns the player's current rating from <c>player_ranks</c> for the platformer ladder,
    /// or <see langword="null"/> when no rank row exists yet.
    /// </summary>
    public async Task<double?> GetPlayerRatingAsync(Guid playerId)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"SELECT ""Rating"" FROM gamekit.player_ranks
            WHERE ""PlayerId"" = @pid AND ""LadderId"" = @lid LIMIT 1";
        cmd.Parameters.AddWithValue("pid", playerId);
        cmd.Parameters.AddWithValue("lid", PlatformerLadderId);
        return (double?)await cmd.ExecuteScalarAsync();
    }

    // ─── IAsyncDisposable ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            try { await _host.StopAsync().ConfigureAwait(false); } catch { /* best-effort */ }
            _host.Dispose();
        }
        Client?.Dispose();
        _signingRsa.Dispose();
        if (Directory.Exists(_keyDir))
        {
            try { Directory.Delete(_keyDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    // ─── Nested types ────────────────────────────────────────────────────────

    /// <summary>
    /// No-op <see cref="IAdminAuditWriter"/> used in the Platformer3D test host.
    /// <para>
    /// <c>GameKit.Matchmaking</c>'s <c>RedisMatchmakingControlService</c> and
    /// <c>MatchmakingReconcilerService</c> both depend on <see cref="IAdminAuditWriter"/> for
    /// admin audit trails. The test host omits <c>AddGameKitAdmin</c> (Admin migration not in
    /// the test chain), so no real implementation is registered. This no-op satisfies the DI
    /// graph so the matchmaker ticker can acquire the lease and form matches. Admin audit
    /// correctness is verified by the human-verify checkpoint (Task 3) against the full
    /// docker-compose stack.
    /// </para>
    /// </summary>
    private sealed class NullAdminAuditWriter : IAdminAuditWriter
    {
        public Task WriteAsync(
            string action,
            string targetType,
            Guid? targetId,
            Guid actorId,
            object? before,
            object? after,
            string? reason,
            CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // ─── Static helpers ──────────────────────────────────────────────────────

    private static async Task<Guid> SeedPlatformerLadderAsync(string cs)
    {
        var ladderId = Guid.NewGuid();
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, 'platformer', 'time-margin', true, NOW(), '{}'::jsonb)
            ON CONFLICT DO NOTHING";
        cmd.Parameters.AddWithValue("id", ladderId);
        await cmd.ExecuteNonQueryAsync();
        return ladderId;
    }
}
