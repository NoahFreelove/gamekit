// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using TicTacToeDuel.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
})
.AddAuth(auth =>
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

var app = builder.Build();

// Serve wwwroot/index.html at "/" — must come before UseGameKit / MapGameKit so the
// static handler runs before any endpoint matching.
app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware order is strict: UseRouting → UseRateLimiter → UseGameKitAuth (UseAuthentication) →
// UseGameKit (UseAuthorization + AutoMigrate) → endpoints. Deviating causes authenticated
// endpoints (/auth/me, /auth/link, /api/players) to 401 even with a valid Bearer token
// (RESEARCH §8.12 #6).
app.UseRouting();
app.UseRateLimiter();
app.UseGameKitAuth();
app.UseGameKit();

app.MapGameKit();   // /api/players (RequireAuthorization — Bearer JWT now enforced)
app.MapAuth();      // /auth/* — Phase 2
app.MapDemo();      // /demo/games (the /demo/players/register endpoint is REMOVED in Phase 2)

app.Run();
