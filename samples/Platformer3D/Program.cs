// SPDX-License-Identifier: Apache-2.0
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
using Platformer3D;
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
    // Live-demo responsiveness: drain pending rating updates every few seconds so the
    // after-match rating change and the leaderboard update visibly within seconds rather
    // than at the production-default 60s cadence. (Demo-only — production should keep the
    // default.) Combined with the short RatingPeriod below, a completed 1v1 applies its
    // rating delta in ~5s, which the browser results screen polls for and displays.
    opts.Ticker.TickIntervalSeconds = 3;

    // Demo: minimal placement so a player is "ranked" almost immediately. (Production default is
    // 10 — a longer provisional period before a rating is publicly shown.) The /demo/leaderboard
    // endpoint shows raw ratings regardless of placement, but this also clears the menu's
    // "placement matches" note quickly.
    opts.Decay.PlacementMatchCount = 1;
})
.AddLadder("platformer", c =>
{
    c.Algorithm         = "time-margin";           // R6/D-12: matches TimeMarginRankingAlgorithm.Name
    c.DefaultRating     = 1000;
    c.DefaultRd         = 350;
    c.DefaultVolatility = 0.06;
    c.RatingPeriod      = System.TimeSpan.FromSeconds(5);  // Short period for snappy live-demo rating updates
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
builder.Services.Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>());

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

// Demo admin seeder — runs at startup after Admin migration creates admin_users.
// Seeds a single superadmin when admin_users is empty AND Platformer:DemoAdmin:Enabled=true.
// SECURITY: no-ops in Production; only active in Staging/Development (DEMO ONLY).
// Registered AFTER AddGameKitAdmin (which registers AdminMigrationHostedService first)
// so admin_users exists when the seeder runs.
builder.Services.AddHostedService<DemoAdminSeederHostedService>();

// D-13: HttpClient named "platformer.web-api" for the embedded game server's loopback
// POST /api/sessions/{id}/complete call. BaseAddress is set at runtime by the game server
// service from configuration (Platformer:WebApiBaseUrl).
builder.Services.AddHttpClient("platformer.web-api");

// D-13: PlatformerGameServerService as both a named singleton (so the /ws/game endpoint
// handler can resolve the SAME instance that holds the in-process service token) AND
// as an IHostedService (so ASP.NET Core calls StartAsync at startup).
builder.Services.AddSingleton<PlatformerGameServerService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PlatformerGameServerService>());

// Browsers cannot set an Authorization header on a WebSocket upgrade, so the demo client passes
// the JWT as ?access_token=<JWT> on the /ws/game/{id} URL. GameKit.Lobby already adds this
// query-string token extraction for /hubs/lobby (LobbyJwtBearerPostConfigure); mirror it here
// for the embedded GameServer's /ws/game path so authenticated run-summary submissions work
// from the browser — without it the run WS is anonymous, the run is never recorded, and a 1v1
// match never completes. Chains the existing OnMessageReceived so the lobby-hub extraction is
// preserved. (D-15: host-only; scoped to the /ws/game path so ordinary HTTP requests are unaffected.)
builder.Services.PostConfigureAll<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(options =>
{
    var existingHandler = options.Events?.OnMessageReceived;
    options.Events ??= new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents();
    options.Events.OnMessageReceived = async context =>
    {
        if (existingHandler is not null)
            await existingHandler(context);

        if (string.IsNullOrEmpty(context.Token))
        {
            var accessToken = context.Request.Query["access_token"].ToString();
            if (!string.IsNullOrEmpty(accessToken) &&
                context.HttpContext.Request.Path.StartsWithSegments("/ws/game"))
            {
                context.Token = accessToken;
            }
        }
    };
});

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

// Demo helper — returns the calling player's active matchmaking ticket id (if any).
// Used by the browser client after receiving the InGame lobby broadcast to discover
// the ticket id needed for the poll loop (GET /api/mm/queue/{ticketId}/status).
// D-15 compliant: lives entirely within samples/Platformer3D/Program.cs.
app.MapGet("/demo/my-ticket", async (
    Microsoft.AspNetCore.Http.HttpContext ctx,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? ctx.User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var playerId))
        return Results.Unauthorized();

    // Find the caller's most recent active ticket (Queued=0 or Proposed=1).
    // GameKitDbContext has all sets registered when AddMatchmaking() is called.
    // Uses static-method form of FirstOrDefaultAsync to avoid a `using` import.
    var row = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(
            db.Set<GameKit.Matchmaking.Entities.MatchmakingTicket>()
              .Join(db.Set<GameKit.Matchmaking.Entities.PartyMember>(),
                    t => t.PartyId,
                    pm => pm.PartyId,
                    (t, pm) => new { Ticket = t, Member = pm })
              .Where(x => x.Member.PlayerId == playerId
                       && ((int)x.Ticket.Status == 0 || (int)x.Ticket.Status == 1))
              .OrderByDescending(x => x.Ticket.QueuedAt)
              .Select(x => new { ticketId = x.Ticket.Id }),
            ct);

    return row is null
        ? Results.NotFound(new { error = "no_active_ticket" })
        : Results.Ok(row);
}).RequireAuthorization();

// Demo helper — dissolve the caller's active matchmaking parties. The browser calls this
// before creating/joining a friend party so a lingering party from a previous match doesn't
// trip PartyConflictException inside the lobby's ready-check. (D-15: host-only.)
app.MapPost("/demo/leave-party", async (
    Microsoft.AspNetCore.Http.HttpContext ctx,
    GameKit.Matchmaking.Services.IPartyService partyService,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? ctx.User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var playerId))
        return Results.Unauthorized();

    var dissolved = await DissolveActivePartiesAsync(db, partyService, playerId, ct);
    return Results.Ok(new { dissolved });
}).RequireAuthorization();

// Local helper shared by /demo/quick-match and /demo/leave-party. Dissolves every active
// party (state Open/Queueing/InMatch) the player belongs to, using each party's real OwnerId
// as the actor so member-only players are cleaned up too. Best-effort: per-party failures are
// swallowed so one stuck party never blocks the rest.
static async Task<int> DissolveActivePartiesAsync(
    GameKit.Core.Data.GameKitDbContext db,
    GameKit.Matchmaking.Services.IPartyService partyService,
    Guid playerId,
    System.Threading.CancellationToken ct)
{
    var parties = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .ToListAsync(
            db.Set<GameKit.Matchmaking.Entities.PartyMember>()
              .Where(pm => pm.PlayerId == playerId)
              .Join(db.Set<GameKit.Matchmaking.Entities.Party>(),
                    pm => pm.PartyId, p => p.Id, (pm, p) => new { p.Id, p.OwnerPlayerId, p.State })
              .Where(x => (int)x.State == 0 || (int)x.State == 1 || (int)x.State == 2),
            ct);

    var dissolved = 0;
    foreach (var p in parties.GroupBy(x => x.Id).Select(g => g.First()))
    {
        try { await partyService.DissolveAsync(p.Id, p.OwnerPlayerId, ct); dissolved++; }
        catch { /* best-effort cleanup */ }
    }
    return dissolved;
}

// True when the exception chain contains a Postgres serialization_failure (40001) or
// deadlock_detected (40P01) — both are safe to retry.
static bool IsTransientSerializationFailure(Exception ex)
{
    for (var e = ex; e is not null; e = e.InnerException)
    {
        if (e is Npgsql.PostgresException pg && (pg.SqlState == "40001" || pg.SqlState == "40P01"))
            return true;
    }
    return false;
}

// Demo helper — RANKED quick-match. Creates a solo party for the caller and enqueues it on
// the platformer ladder's default pool, returning the ticket id the browser then polls via
// GET /api/mm/queue/{ticketId}/status. Two different players who both hit this endpoint are
// paired into a ranked 1v1 by the matchmaker. Idempotent: if the caller already has an active
// ticket it is returned as-is (avoids orphan parties on a double-click).
// D-15 compliant: lives entirely within samples/Platformer3D/Program.cs.
app.MapPost("/demo/quick-match", async (
    Microsoft.AspNetCore.Http.HttpContext ctx,
    GameKit.Matchmaking.Services.IPartyService partyService,
    GameKit.Matchmaking.Services.IMatchmakingService matchmaking,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? ctx.User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var playerId))
        return Results.Unauthorized();

    // Reuse any active ticket (Queued=0 / Proposed=1) the caller already holds.
    var existing = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(
            db.Set<GameKit.Matchmaking.Entities.MatchmakingTicket>()
              .Join(db.Set<GameKit.Matchmaking.Entities.PartyMember>(),
                    t => t.PartyId, pm => pm.PartyId, (t, pm) => new { Ticket = t, Member = pm })
              .Where(x => x.Member.PlayerId == playerId
                       && ((int)x.Ticket.Status == 0 || (int)x.Ticket.Status == 1))
              .OrderByDescending(x => x.Ticket.QueuedAt)
              .Select(x => new { ticketId = x.Ticket.Id }),
            ct);
    if (existing is not null)
        return Results.Ok(new { ticketId = existing.ticketId, reused = true });

    var ladder = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(db.Set<GameKit.Rankings.Entities.Ladder>().Where(l => l.Name == "platformer"), ct);
    if (ladder is null)
        return Results.NotFound(new { error = "ladder_not_found" });

    // Retry the dissolve → create-party → enqueue sequence on transient Postgres serialization
    // failures (40001): two players clicking Ranked at the same moment collide on the parties
    // tables under SERIALIZABLE isolation. Dissolving first on every attempt cleans up any party
    // a previous failed attempt left behind, so retries never orphan a party.
    for (var attempt = 0; ; attempt++)
    {
        try
        {
            await DissolveActivePartiesAsync(db, partyService, playerId, ct);
            var party = await partyService.CreateAsync(playerId, ct);
            var result = await matchmaking.EnqueueAsync(playerId, ladder.Id, "default", party.Id, ct);
            if (result.Outcome == GameKit.Matchmaking.Services.EnqueueOutcome.Queued && result.TicketId is not null)
                return Results.Ok(new { ticketId = result.TicketId.Value, reused = false });

            return Results.Json(
                new { error = "enqueue_failed", outcome = result.Outcome.ToString(), detail = result.Detail },
                statusCode: 409);
        }
        catch (Exception ex) when (attempt < 5 && IsTransientSerializationFailure(ex))
        {
            await Task.Delay(60 * (attempt + 1), ct);
        }
    }
}).RequireAuthorization();

// Demo helper — the caller's current rank on the platformer ladder (rating + W/L), used by the
// browser to render the menu header and the after-match rating delta. Returns hasRank=false for
// a player who has not completed a ranked match yet (no PlayerRank row).
// D-15 compliant: lives entirely within samples/Platformer3D/Program.cs.
app.MapGet("/demo/my-rank", async (
    Microsoft.AspNetCore.Http.HttpContext ctx,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? ctx.User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out var playerId))
        return Results.Unauthorized();

    var ladder = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(db.Set<GameKit.Rankings.Entities.Ladder>().Where(l => l.Name == "platformer"), ct);
    if (ladder is null)
        return Results.NotFound(new { error = "ladder_not_found" });

    var rank = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(
            db.Set<GameKit.Rankings.Entities.PlayerRank>()
              .Where(r => r.PlayerId == playerId && r.LadderId == ladder.Id),
            ct);

    if (rank is null)
        return Results.Ok(new { hasRank = false, rating = (double?)null, deviation = (double?)null, wins = 0, losses = 0, draws = 0, isInPlacement = true });

    return Results.Ok(new
    {
        hasRank = true,
        rating = rank.Rating,
        deviation = rank.RatingDeviation,
        wins = rank.Wins,
        losses = rank.Losses,
        draws = rank.Draws,
        isInPlacement = rank.IsInPlacement,
    });
}).RequireAuthorization();

// Demo helper — the result of a session once it has been completed by the GameServer, so the
// browser can render the post-match screen (your result + opponent's time, who won). Returns
// completed=false while the session is still Active (the client polls until both runs are in).
// D-15 compliant: lives entirely within samples/Platformer3D/Program.cs.
app.MapGet("/demo/session-result/{sessionId:guid}", async (
    Guid sessionId,
    Microsoft.AspNetCore.Http.HttpContext ctx,
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var sub = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
              ?? ctx.User.FindFirst("sub")?.Value;
    if (sub is null || !Guid.TryParse(sub, out _))
        return Results.Unauthorized();

    var session = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(db.Set<GameKit.Core.Entities.GameSession>().Where(s => s.Id == sessionId), ct);
    if (session is null)
        return Results.NotFound(new { error = "session_not_found" });

    var raw = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .ToListAsync(
            db.Set<GameKit.Core.Entities.SessionParticipant>()
              .Where(p => p.SessionId == sessionId)
              .Select(p => new { p.PlayerId, p.Team, p.Result, p.Score }),
            ct);

    var participants = raw.Select(p => new
    {
        playerId = p.PlayerId,
        team = p.Team,
        result = p.Result.HasValue ? p.Result.Value.ToString() : null,
        timeMs = p.Score,
    });

    return Results.Ok(new
    {
        sessionId,
        state = session.State.ToString(),
        completed = session.State == GameKit.Core.Entities.GameSessionState.Completed,
        ranked = session.LadderId != null,
        participants,
    });
}).RequireAuthorization();

// Demo leaderboard — read-only, anonymous. Returns top-20 players on the platformer ladder
// with their ACTUAL ratings. Deliberately does NOT use ILeaderboardService: that service hides
// the rating (returns null) while a player is in their placement matches (RANK-16), which in a
// short demo means every player shows a 0 rating. Here we read PlayerRank directly so the
// leaderboard is consistent with /demo/my-rank and the after-match results screen.
// D-15 compliant: lives entirely within samples/Platformer3D/Program.cs.
app.MapGet("/demo/leaderboard", async (
    GameKit.Core.Data.GameKitDbContext db,
    System.Threading.CancellationToken ct) =>
{
    var ladder = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .FirstOrDefaultAsync(
            db.Set<GameKit.Rankings.Entities.Ladder>()
              .Where(l => l.Name == "platformer"),
            ct);
    if (ladder is null)
        return Results.NotFound(new { error = "ladder_not_found" });

    var ranked = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
        .ToListAsync(
            db.Set<GameKit.Rankings.Entities.PlayerRank>()
              .Where(r => r.LadderId == ladder.Id)
              .OrderByDescending(r => r.Rating)
              .Take(20)
              .Join(db.Set<GameKit.Core.Entities.Player>(),
                    r => r.PlayerId, p => p.Id,
                    (r, p) => new { r.PlayerId, p.DisplayName, r.Rating, r.Wins, r.Losses }),
            ct);

    var rows = ranked.Select((x, i) => new
    {
        rank = i + 1,
        playerId = x.PlayerId,
        displayName = x.DisplayName,
        rating = x.Rating,
        wins = x.Wins,
        losses = x.Losses,
    });
    return Results.Ok(rows);
});
// Anonymous — read-only demo leaderboard, no auth required.

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
