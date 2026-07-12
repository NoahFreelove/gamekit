// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Builder;
#if (!skipAuth)
using GameKit.Auth.Builder;
#endif
using GameKit.Core.Builder;
#if (!skipMatchmaking)
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
#endif
using GameKit.OpenApi.Builder;
#if (!skipPresence)
using GameKit.Presence.Builder;
#endif
#if (!skipRankings)
using GameKit.Rankings.Builder;
using GameKit.Rankings.Entities;
#endif
using GameKit.SampleGame.Http;

var builder = WebApplication.CreateBuilder(args);

// Register the Redis multiplexer up-front — GameKit.Rankings (ticker) and GameKit.Matchmaking
// (ticker + proposal service) take an IConnectionMultiplexer dependency. The package builders
// intentionally do NOT auto-register the multiplexer because (a) it's a singleton with
// operator-owned lifecycle and (b) production deployments often want to wire
// ConfigurationOptions (TLS, AllowAdmin, AbortOnConnectFail, etc.) manually.
var redisCs = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis");
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisCs));

// Capture the IGameKitBuilder so we can call .AddAuth() / .AddRankings() / .AddMatchmaking() /
// .AddPresence() / .AddGameKitAdmin() on it.
var gameKitBuilder = builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
});

#if (!skipAuth)
gameKitBuilder.AddAuth(auth =>
{
    // JWT issuance/validation — RSA PEM paths resolve relative to Content Root.
    // Run ./scripts/gen-test-rsa-pem.sh (template post-action) to generate the dev keys.
    auth.Jwt.Issuer            = builder.Configuration["GameKit:Auth:Jwt:Issuer"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:Issuer");
    auth.Jwt.Audience          = builder.Configuration["GameKit:Auth:Jwt:Audience"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:Audience");
    auth.Jwt.PrivateKeyPemPath = builder.Configuration["GameKit:Auth:Jwt:PrivateKeyPemPath"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:PrivateKeyPemPath");
    auth.Jwt.PublicKeyPemPath  = builder.Configuration["GameKit:Auth:Jwt:PublicKeyPemPath"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:PublicKeyPemPath");
    auth.Jwt.Kid               = builder.Configuration["GameKit:Auth:Jwt:Kid"] ?? auth.Jwt.Kid;

    // Steam OpenID 2.0 — Realm is the base URL the game reports to Steam.
    auth.Steam.Realm           = builder.Configuration["GameKit:Auth:Steam:Realm"] ?? string.Empty;
    auth.Steam.CallbackPath    = builder.Configuration["GameKit:Auth:Steam:CallbackPath"] ?? auth.Steam.CallbackPath;
    auth.Steam.ApiKey          = builder.Configuration["GameKit:Auth:Steam:ApiKey"];

    // Discord OAuth2 — identify scope only. With placeholders, the Discord scheme is skipped at
    // startup so /auth/login/discord returns 400 `unknown_provider` instead of throwing.
    auth.Discord.ClientId      = builder.Configuration["GameKit:Auth:Discord:ClientId"] ?? string.Empty;
    auth.Discord.ClientSecret  = builder.Configuration["GameKit:Auth:Discord:ClientSecret"] ?? string.Empty;
    auth.Discord.CallbackPath  = builder.Configuration["GameKit:Auth:Discord:CallbackPath"] ?? auth.Discord.CallbackPath;
});
#endif

#if (!skipRankings)
// Rankings — IGameKitRankingsBuilder does not extend IGameKitBuilder, so .AddGameKitAdmin
// is chained from gameKitBuilder (below), not from AddLadder's return value.
gameKitBuilder.AddRankings(opts =>
{
    // All ranking options are optional — defaults are production-safe.
})
.AddLadder("main", c =>
{
    c.DefaultRating     = 1500;
    c.DefaultRd         = 350;
    c.DefaultVolatility = 0.06;
    c.RatingPeriod      = System.TimeSpan.FromHours(1);
    c.ResetPolicy       = SeasonResetPolicy.SoftRegress;
})
.AddLadder("tictactoe", c =>
{
    // Matchmaking ladder. Sharing the same name space as Rankings — both packages join
    // on Ladder.Name. Glicko-2 defaults match the "main" ladder.
    c.DefaultRating     = 1500;
    c.DefaultRd         = 350;
    c.DefaultVolatility = 0.06;
    c.RatingPeriod      = System.TimeSpan.FromHours(1);
    c.ResetPolicy       = SeasonResetPolicy.SoftRegress;
});
#endif

#if (!skipMatchmaking)
gameKitBuilder.AddMatchmaking(opts =>
{
    // 500 ms tick keeps perceived latency low for the 1v1 demo. Production deployments
    // often raise this to 1-2 s under heavier load.
    opts.Ticker.TickIntervalMs = 500;
})
.AddLadder("tictactoe", ladder =>
{
    // Linear bracket ramp 100 → 500 over 40 s.
    ladder.BracketStart            = 100;
    ladder.BracketEnd              = 500;
    ladder.BracketRampSeconds      = 40;
    ladder.PartyRatingAggregator   = PartyRatingAggregator.Mean;
});
#endif

#if (!skipPresence)
// Presence — Redis TTL-keyed heartbeat (30s default) + in-match precedence Lua script.
// PresenceSessionObserver observes the /api/sessions/{id}/start + /complete + /abandon
// endpoints to flip the in_match marker. No options override needed — defaults
// (TtlSeconds=30, HeartbeatIntervalSeconds=10) match the 3× safety factor.
gameKitBuilder.AddPresence();
#endif

// OpenApi — single combined /openapi/v1.json document covering every player-facing GameKit
// HTTP endpoint (auth, sessions, matchmaking, parties, presence). The builder wires the
// OpenApiOptions.ShouldInclude lambda that filters out admin endpoints + the
// GameKitInfoTransformer (info.Version from the MinVer-derived GameKitMarker const) + the
// GameKitBearerSchemeTransformer (bearerAuth security scheme applied globally when the
// JwtBearer scheme is registered). AddGameKitOpenApi is an IServiceCollection extension
// (orthogonal to the IGameKitBuilder chain).
builder.Services.AddGameKitOpenApi();

gameKitBuilder.AddGameKitAdmin(admin =>
{
    admin.MountPath = "/admin";
});

var app = builder.Build();

// Serve wwwroot/index.html at "/" — must come before UseGameKit / MapGameKit so the
// static handler runs before any endpoint matching.
app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware order is strict: UseRouting → UseRateLimiter → UseGameKitAuth (UseAuthentication) →
// UseGameKit (UseAuthorization + AutoMigrate) → UseGameKitAdmin (CSP nonce + antiforgery) →
// endpoints. Deviating causes authenticated endpoints (/auth/me, /auth/link, /api/players) to
// 401 even with a valid Bearer token, or admin CSP/antiforgery to misfire on non-admin paths.
app.UseRouting();
app.UseRateLimiter();
#if (!skipAuth)
app.UseGameKitAuth();
#endif
app.UseGameKit();
app.UseGameKitAdmin();  // admin CSP nonce + antiforgery; scoped to /admin/*.

// MapStaticAssets composes static web assets from referenced Razor Class Libraries
// (here: GameKit.Admin.UI's MudBlazor JS/CSS, gamekit-admin.css, and Blazor's
// _framework/blazor.web.js). MapRazorComponents<App>().WithStaticAssets() inside
// MapGameKitAdmin depends on this being mounted.
app.MapStaticAssets();

app.MapGameKit();                   // /api/players (RequireAuthorization — Bearer JWT enforced when Auth is present)
#if (!skipAuth)
app.MapAuth();                      // /auth/*
#endif
app.MapDemo();                      // /demo/games
#if (!skipRankings)
app.MapRankings();                  // /api/players/{id}/export + /admin/api/players/{id}/{export,rank-adjust} + /api/sessions/{id}/{start,complete,abandon}
#endif
#if (!skipMatchmaking)
app.MapMatchmaking();               // /api/parties/* + /api/mm/*
#endif
#if (!skipPresence)
app.MapPresence();                  // POST /api/presence/heartbeat — JWT-bearer required
#endif
app.MapGameKitOpenApi();            // GET /openapi/v1.json — anonymous; admin paths excluded
app.MapGameKitAdmin("/admin");      // /admin/api/* HTTP surface + /admin/* Blazor console.

#if (!skipRankings && !skipMatchmaking)
// Sample-only helper: resolves a ladder name to its Guid so the matchmaking.html client can
// POST /api/mm/queue without hard-coding the ladder id (the id is generated on first startup
// by the Rankings StartupLadderUpserter).
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
#endif

app.Run();

/// <summary>
/// Marker partial — exposed as <c>public</c> so the integration-test
/// <c>WebApplicationFactory&lt;Program&gt;</c> pattern can locate the entry assembly.
/// </summary>
public partial class Program;
