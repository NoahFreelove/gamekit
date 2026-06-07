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
using GameKit.Rankings.Builder;
using GameKit.Rankings.Entities;
using TicTacToeDuel.Http;

var builder = WebApplication.CreateBuilder(args);

// Register the Redis multiplexer up-front — both GameKit.Rankings (Phase 4 ticker) and
// GameKit.Matchmaking (Phase 5 ticker / proposal service / observability) take an
// IConnectionMultiplexer dependency. The package builders intentionally do NOT auto-register
// the multiplexer because (a) it's a singleton with operator-owned lifecycle, and (b)
// production deployments often want to wire ConfigurationOptions (TLS, AllowAdmin,
// AbortOnConnectFail, etc.) manually.
var redisCs = builder.Configuration.GetConnectionString("Redis")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Redis");
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(_ =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisCs));

// Capture the IGameKitBuilder so we can call both .AddAuth() and .AddRankings() on it,
// then chain .AddGameKitAdmin() on the core builder (not on the rankings builder, which
// returns IGameKitRankingsBuilder and does not extend IGameKitBuilder).
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
    // Run ./scripts/gen-test-rsa-pem.sh to generate the dev key pair.
    auth.Jwt.Issuer            = builder.Configuration["GameKit:Auth:Jwt:Issuer"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:Issuer");
    auth.Jwt.Audience          = builder.Configuration["GameKit:Auth:Jwt:Audience"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:Audience");
    auth.Jwt.PrivateKeyPemPath = builder.Configuration["GameKit:Auth:Jwt:PrivateKeyPemPath"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:PrivateKeyPemPath");
    auth.Jwt.PublicKeyPemPath  = builder.Configuration["GameKit:Auth:Jwt:PublicKeyPemPath"]
        ?? throw new InvalidOperationException("Missing GameKit:Auth:Jwt:PublicKeyPemPath");
    auth.Jwt.Kid               = builder.Configuration["GameKit:Auth:Jwt:Kid"] ?? auth.Jwt.Kid;

    // Steam OpenID 2.0 — Realm is the base URL the game reports to Steam. ApiKey is optional
    // (without it, we cannot resolve Steam display-name metadata, but the OpenID assertion is
    // still verified server-side by SteamOpenIdVerifier). Leave ApiKey null for offline demos.
    auth.Steam.Realm           = builder.Configuration["GameKit:Auth:Steam:Realm"] ?? string.Empty;
    auth.Steam.CallbackPath    = builder.Configuration["GameKit:Auth:Steam:CallbackPath"] ?? auth.Steam.CallbackPath;
    auth.Steam.ApiKey          = builder.Configuration["GameKit:Auth:Steam:ApiKey"];

    // Discord OAuth2 — identify scope only (AUTH-07 / D-10). When ClientId or ClientSecret
    // are the placeholder strings, the Discord authentication scheme skips registration at
    // startup, so /auth/login/discord returns 400 `unknown_provider` instead of throwing.
    auth.Discord.ClientId      = builder.Configuration["GameKit:Auth:Discord:ClientId"] ?? string.Empty;
    auth.Discord.ClientSecret  = builder.Configuration["GameKit:Auth:Discord:ClientSecret"] ?? string.Empty;
    auth.Discord.CallbackPath  = builder.Configuration["GameKit:Auth:Discord:CallbackPath"] ?? auth.Discord.CallbackPath;

    // Operator-customizable egress allow-list — defaults cover Steam + Discord. Production
    // apps proxying OAuth through another host append here, e.g.:
    //   auth.AllowedProviderHosts.Add("id.internal.example.com");
});

// Rankings — IGameKitRankingsBuilder does not extend IGameKitBuilder, so AddGameKitAdmin
// is chained from gameKitBuilder (below), not from AddLadder's return value.
gameKitBuilder.AddRankings(opts =>
{
    // All ranking options are optional — defaults are production-safe.
    // MinRating/MaxRating guard manual rank-adjust; ticker drains every RatingPeriod.
})
.AddLadder("main", c =>
{
    // Default Glicko-2 ladder. One ladder per game mode; add more via chained AddLadder calls.
    c.DefaultRating     = 1500;
    c.DefaultRd         = 350;
    c.DefaultVolatility = 0.06;
    c.RatingPeriod      = System.TimeSpan.FromHours(1);
    c.ResetPolicy       = SeasonResetPolicy.SoftRegress;
})
.AddLadder("tictactoe", c =>
{
    // Tic-tac-toe matchmaking ladder. Sharing the same name space as Rankings — both packages
    // join on Ladder.Name (CONTEXT.md D-12). The Glicko-2 defaults match the "main" ladder so
    // any player's rating carries cleanly between modes.
    c.DefaultRating     = 1500;
    c.DefaultRd         = 350;
    c.DefaultVolatility = 0.06;
    c.RatingPeriod      = System.TimeSpan.FromHours(1);
    c.ResetPolicy       = SeasonResetPolicy.SoftRegress;
});

// Plan 05-09 — Matchmaking (Phase 5). Wires the Redis-backed live queue + accept-step
// proposal flow on top of the Rankings ladder. The "tictactoe" matchmaking ladder shares its
// name with the Rankings-side AddLadder above; both packages join on Ladder.Name (D-12).
gameKitBuilder.AddMatchmaking(opts =>
{
    // 500 ms tick keeps perceived latency low for the 1v1 demo. Production deployments often
    // raise this to 1-2 s under heavier load.
    opts.Ticker.TickIntervalMs = 500;
})
.AddLadder("tictactoe", ladder =>
{
    // CONTEXT.md D-11 — linear bracket ramp 100 → 500 over 40 s.
    ladder.BracketStart            = 100;
    ladder.BracketEnd              = 500;
    ladder.BracketRampSeconds      = 40;
    ladder.PartyRatingAggregator   = PartyRatingAggregator.Mean;
});

// Plan 06-04 — Presence (Phase 6). Redis TTL-keyed heartbeat (30s default per CONTEXT D-01) +
// in-match precedence Lua script. The PresenceSessionObserver registered here observes the
// /api/sessions/{id}/start + /complete + /abandon endpoints (wired in Plan 06-05) to flip the
// in_match marker per CONTEXT D-03. No options override needed — defaults (TtlSeconds=30,
// HeartbeatIntervalSeconds=10) match the 3× safety factor.
gameKitBuilder.AddPresence();

// Lobby (v2.0) — SignalR hub at /hubs/lobby + REST /api/lobbies. Requires the
// IConnectionMultiplexer registered above (LOBBY-06 mandates the Redis backplane). No options
// override needed — defaults are production-safe. The Lobby EF migration hosted service creates
// gamekit.lobbies + gamekit.lobby_members at startup (AutoMigrate is on).
gameKitBuilder.AddLobby();

// Plan 06-06 — OpenApi (Phase 6). Single combined /openapi/v1.json document covering every
// player-facing GameKit HTTP endpoint (auth, sessions, matchmaking, parties, presence). The
// builder wires the inline OpenApiOptions.ShouldInclude lambda that filters out admin
// endpoints (D-19; PATTERNS Critical Misuse Warning #1) + the GameKitInfoTransformer
// (info.Version from the MinVer-derived GameKitMarker const) + the GameKitBearerSchemeTransformer
// (bearerAuth security scheme applied globally when the JwtBearer scheme is registered, D-08).
// AddGameKitOpenApi is an IServiceCollection extension (orthogonal to the IGameKitBuilder chain).
builder.Services.AddGameKitOpenApi();

gameKitBuilder.AddGameKitAdmin(admin =>
{
    admin.MountPath = "/admin";
    // Default cookie/panel/CSP options are production-safe. See GameKitAdminOptions.cs for knobs
    // (cookie name + expiry, panel refresh interval, CSP report-only toggle).
});

var app = builder.Build();

// Serve wwwroot/index.html at "/" — must come before UseGameKit / MapGameKit so the
// static handler runs before any endpoint matching.
app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware order is strict: UseRouting → UseRateLimiter → UseGameKitAuth (UseAuthentication) →
// UseGameKit (UseAuthorization + AutoMigrate) → UseGameKitAdmin (CSP nonce + antiforgery) →
// endpoints. Deviating causes authenticated endpoints (/auth/me, /auth/link, /api/players) to
// 401 even with a valid Bearer token (RESEARCH §8.12 #6) or admin CSP/antiforgery to misfire
// on non-admin paths (plan 03-12 RESEARCH §Middleware pipeline contract, line 431).
app.UseRouting();
app.UseRateLimiter();
app.UseGameKitAuth();
app.UseGameKit();
app.UseGameKitAdmin();  // Plan 03-12 — admin CSP nonce + antiforgery; scoped to /admin/*.

// MapStaticAssets is the .NET 8+ static-asset endpoint pipeline. It composes static web assets
// from referenced Razor Class Libraries (here: GameKit.Admin.UI's MudBlazor JS/CSS, gamekit-admin.css,
// and the Blazor framework's _framework/blazor.web.js). MapRazorComponents<App>().WithStaticAssets()
// inside MapGameKitAdmin depends on this being mounted.
app.MapStaticAssets();

app.MapGameKit();                   // /api/players (RequireAuthorization — Bearer JWT now enforced)
app.MapAuth();                      // /auth/* — Phase 2
app.MapDemo();                      // /demo/games (the /demo/players/register endpoint is REMOVED in Phase 2)
app.MapRankings();                  // /api/players/{id}/export + /admin/api/players/{id}/export + /admin/api/players/{id}/rank-adjust — Phase 4
app.MapMatchmaking();               // /api/parties/* + /api/mm/* — Phase 5 (Plan 05-08 surface, Plan 05-09 wiring)
app.MapLobby();                     // /api/lobbies REST + /hubs/lobby SignalR hub — v2.0 Lobby
app.MapPresence();                  // POST /api/presence/heartbeat — Phase 6 (Plan 06-04 — JWT-bearer required, no rate limit per D-05)
app.MapGameKitOpenApi();            // GET /openapi/v1.json — Phase 6 (Plan 06-06 — anonymous; admin paths excluded per D-19)
app.MapGameKitAdmin("/admin");      // Plan 03-12 — /admin/api/* HTTP surface + /admin/* Blazor console.

// Sample-only helper: resolves a ladder name to its Guid so the matchmaking.html client can
// POST /api/mm/queue without hard-coding the ladder id (the id is generated on first startup
// by the Rankings StartupLadderUpserter). Authorization-free because the ladder name is part
// of the public ladder catalogue; in production an OpenAPI surface or a tweak page would
// expose this.
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

app.Run();
