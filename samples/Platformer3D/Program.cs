// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Builder;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using GameKit.Lobby.Builder;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using GameKit.OpenApi.Builder;
using GameKit.Presence.Builder;
using GameKit.Rankings.Algorithms;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Entities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Platformer3D.Algorithms;
using Platformer3D.GameServer;
using Platformer3D.Strategy;

var builder = WebApplication.CreateBuilder(args);

// Register the Redis multiplexer up-front — both GameKit.Rankings (ticker) and
// GameKit.Matchmaking (ticker / proposal service / observability) take an
// IConnectionMultiplexer dependency. The package builders intentionally do NOT
// auto-register the multiplexer because operators want to wire ConfigurationOptions
// (TLS, AllowAdmin, AbortOnConnectFail, etc.) manually.
var redisCs = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis");
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisCs));

// D-09/D-12: Register the custom ranking algorithm BEFORE AddRankings() so it is
// available during DI service discovery. Selection by ladder.Algorithm = "time-margin"
// (string Name match) — no shadowing concern here unlike IMatchmakingStrategy.
builder.Services.AddSingleton<IRankingAlgorithm, TimeMarginRankingAlgorithm>();

// Capture the IGameKitBuilder so we can chain .AddAuth(), .AddRankings(),
// .AddMatchmaking(), etc. on it without losing the reference.
var gameKitBuilder = builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
});

gameKitBuilder.AddAuth(auth =>
{
    // JWT issuance/validation — RSA PEM paths resolve relative to Content Root.
    // Run scripts/gen-test-rsa-pem.sh to generate the dev key pair.
    auth.Jwt.Issuer            = builder.Configuration["GameKit:Auth:Jwt:Issuer"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:Issuer");
    auth.Jwt.Audience          = builder.Configuration["GameKit:Auth:Jwt:Audience"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:Audience");
    auth.Jwt.PrivateKeyPemPath = builder.Configuration["GameKit:Auth:Jwt:PrivateKeyPemPath"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:PrivateKeyPemPath");
    auth.Jwt.PublicKeyPemPath  = builder.Configuration["GameKit:Auth:Jwt:PublicKeyPemPath"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:PublicKeyPemPath");
    auth.Jwt.Kid               = builder.Configuration["GameKit:Auth:Jwt:Kid"] ?? auth.Jwt.Kid;

    // Steam and Discord are optional for this demo. Guest is the required onramp.
    // Leave Steam.ApiKey null — display-name metadata unavailable; OpenID assertion still verified.
    auth.Steam.Realm           = builder.Configuration["GameKit:Auth:Steam:Realm"] ?? string.Empty;
    auth.Steam.CallbackPath    = builder.Configuration["GameKit:Auth:Steam:CallbackPath"] ?? auth.Steam.CallbackPath;
    auth.Steam.ApiKey          = builder.Configuration["GameKit:Auth:Steam:ApiKey"];

    // Discord OAuth2 — skips registration when ClientId/ClientSecret are placeholders.
    auth.Discord.ClientId      = builder.Configuration["GameKit:Auth:Discord:ClientId"] ?? string.Empty;
    auth.Discord.ClientSecret  = builder.Configuration["GameKit:Auth:Discord:ClientSecret"] ?? string.Empty;
    auth.Discord.CallbackPath  = builder.Configuration["GameKit:Auth:Discord:CallbackPath"] ?? auth.Discord.CallbackPath;
});

// R6/D-12: Rankings ladder "platformer" with the custom time-margin algorithm.
// RatingPeriod = 1 minute keeps the live demo leaderboard visibly active (Pitfall 8).
// IGameKitRankingsBuilder does not extend IGameKitBuilder, so AddGameKitAdmin is chained
// from gameKitBuilder below, not from AddLadder's return value.
gameKitBuilder.AddRankings(opts =>
{
    // Default ranking options are production-safe. No overrides needed for the demo.
})
.AddLadder("platformer", c =>
{
    c.Algorithm         = "time-margin";           // R6/D-12: matches TimeMarginRankingAlgorithm.Name
    c.DefaultRating     = 1000;
    c.DefaultRd         = 350;
    c.DefaultVolatility = 0.06;
    c.RatingPeriod      = System.TimeSpan.FromMinutes(1);  // Short period for live demo (Pitfall 8)
    c.ResetPolicy       = SeasonResetPolicy.SoftRegress;
});

// Matchmaking ladder "platformer" — BestTimeMatchmakingStrategy is registered via
// services.Replace(...) AFTER this call (A3 LOCKED — see comment below).
gameKitBuilder.AddMatchmaking(opts =>
{
    opts.Ticker.TickIntervalMs = 500;   // 500ms tick for low perceived latency in demo
})
.AddLadder("platformer", ladder =>
{
    ladder.BracketStart          = 0;
    ladder.BracketEnd            = 60_000;   // Wide upper bound; strategy uses its own window
    ladder.BracketRampSeconds    = 60;
    ladder.PartyRatingAggregator = PartyRatingAggregator.Mean;
});

// *** A3 LOCKED — CRITICAL WIRING ***
// MatchmakerTickerService injects a SINGLE IMatchmakingStrategy (ctor parameter,
// NOT IEnumerable, NOT keyed). AddMatchmaking() calls AddStrategyServices() which
// Scrutor-scans FromAssemblyOf<EloRangeMatchmakingStrategy>() and registers
// EloRangeMatchmakingStrategy as IMatchmakingStrategy. MS.DI returns the LAST-registered
// descriptor, so a naive AddSingleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>()
// placed BEFORE AddMatchmaking() is SHADOWED by EloRange.
//
// services.Replace(...) AFTER AddMatchmaking() removes the scanned EloRange descriptor
// and leaves exactly ONE strategy (the custom one). This is the ONLY correct form.
// The R5 resolution test in 21-06 (GetRequiredService<IMatchmakingStrategy>() is
// BestTimeMatchmakingStrategy) is the gate that proves this.
builder.Services.Replace(
    ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>());

// Presence — Redis TTL-keyed heartbeat + in-match state marker.
gameKitBuilder.AddPresence();

// Lobby — SignalR hub at /hubs/lobby + REST /api/lobbies. Redis backplane required.
gameKitBuilder.AddLobby();

// Phase 15 OTel — opt-in observability. Skips the OTLP exporter when endpoint is absent.
gameKitBuilder.AddGameKitObservability(otel =>
{
    otel.OtlpEndpoint = builder.Configuration["GameKit:Observability:OtlpEndpoint"];
});

// ASP.NET Core HTTP server-span instrumentation for cross-service trace propagation.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation());

// Health & readiness endpoints (D-02/D-03).
gameKitBuilder.AddGameKitHealthChecks();

// OpenAPI document covering all player-facing endpoints.
builder.Services.AddGameKitOpenApi();

// R4: Admin console — surfaces live demo players/matches/sessions.
gameKitBuilder.AddGameKitAdmin(admin =>
{
    admin.MountPath = "/admin";
});

// D-13: HttpClient named "platformer.web-api" for the embedded game server's loopback
// POST /api/sessions/{id}/complete call. BaseAddress is set at runtime by the game server
// service from configuration (Platformer:WebApiBaseUrl).
builder.Services.AddHttpClient("platformer.web-api");

// D-13: PlatformerGameServerService as both a named singleton (so the /ws/game endpoint
// handler can resolve the SAME instance that holds the in-process service token) AND
// as an IHostedService (so ASP.NET Core calls StartAsync at startup).
builder.Services.AddSingleton<PlatformerGameServerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlatformerGameServerService>());

var app = builder.Build();

// Serve wwwroot/index.html at "/" — must come before any endpoint matching.
app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware order is strict (mirrors TicTacToeDuel exact order):
// UseRouting → UseRateLimiter → UseGameKitAuth (UseAuthentication) →
// UseGameKit (UseAuthorization + AutoMigrate) → UseGameKitAdmin (CSP nonce + antiforgery)
// Deviating causes authenticated endpoints to 401 even with a valid Bearer token,
// or admin CSP/antiforgery to misfire on non-admin paths.
app.UseRouting();
app.UseRateLimiter();
app.UseGameKitAuth();
app.UseGameKit();
app.UseGameKitAdmin();   // Admin CSP nonce + antiforgery; scoped to /admin/*

// UseWebSockets is placed AFTER UseGameKitAdmin and BEFORE the Map calls.
// This ensures the auth middleware has already run (ctx.User is populated)
// when the /ws/game endpoint handler executes (D-01).
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = System.TimeSpan.FromSeconds(30),
});

// .NET 8+ static-asset pipeline — composes RCL assets (Admin.UI/MudBlazor JS/CSS,
// Blazor framework _framework/blazor.web.js). Required by MapGameKitAdmin's
// MapRazorComponents<App>().WithStaticAssets().
app.MapStaticAssets();

app.MapGameKitHealth();           // /health/live + /health/ready — anonymous (D-02/D-03)
app.MapGameKit();                 // /api/players — Bearer JWT required
app.MapAuth();                    // /auth/* — guest + Steam/Discord + JWT refresh
app.MapRankings();                // /api/players/{id}/export + admin rank-adjust
app.MapMatchmaking();             // /api/parties/* + /api/mm/*
app.MapLobby();                   // /api/lobbies REST + /hubs/lobby SignalR hub
app.MapPresence();                // POST /api/presence/heartbeat
app.MapGameKitOpenApi();          // GET /openapi/v1.json — anonymous
app.MapGameKitAdmin("/admin");    // /admin/api/* HTTP + /admin/* Blazor console (R4)

// Demo helper — resolves a ladder name to its Guid so the browser client can
// POST /api/mm/queue without hard-coding the ladder id (generated on first startup).
app.MapGet("/demo/ladder-id/{name}", async (
    string name,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var ladder = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(db.Set<GameKit.Rankings.Entities.Ladder>()
            .Where(l => l.Name == name), ct);
    return ladder is null
        ? Results.NotFound(new { error = "unknown_ladder", name })
        : Results.Ok(new { id = ladder.Id, name = ladder.Name });
});

// D-01/D-13: WebSocket run-summary endpoint.
// Placed AFTER the auth middleware (UseGameKitAuth ran above) so ctx.User is the
// authenticated player's principal. The endpoint itself delegates all game logic to the
// PlatformerGameServerService (the embedded IHostedService that holds the in-process token).
app.Map("/ws/game/{matchId:guid}", async (
    System.Net.WebSockets.WebSocket? _unused,
    Microsoft.AspNetCore.Http.HttpContext ctx,
    System.Guid matchId) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }

    var gameServer = ctx.RequestServices.GetRequiredService<PlatformerGameServerService>();
    var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    await gameServer.HandleConnectionAsync(ws, matchId, ctx.User, ctx.RequestAborted);
});

app.Run();
