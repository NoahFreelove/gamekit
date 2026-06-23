// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// tests/k6/SpikeHost/Program.cs
//
// Minimal GameKit Lobby host for the PERF-04a k6 SignalR spike.
// Exposes the full Lobby pipeline (Auth + Rankings + Matchmaking + Lobby) over Kestrel
// on http://localhost:5100 so Docker-run k6 can hit it via --network host.
//
// Environment variables:
//   SPIKE_PG_CS           — Postgres connection string (default: see below)
//   SPIKE_REDIS_CS        — Redis connection string (default: localhost:6399)
//   SPIKE_KESTREL_URL     — Kestrel bind URL (default: http://localhost:5100)
//   SPIKE_PRIV_PEM        — RSA private key PEM path (generated in-process if omitted)
//   SPIKE_PUB_PEM         — RSA public key PEM path (generated in-process if omitted)
//
// After startup, the program prints:
//   SPIKE_JWT=<jwt>       — a valid player JWT for the test player
//   SPIKE_LOBBY_ID=<guid> — a seeded lobby the test player is a member of
//   SPIKE_LADDER_ID=<guid>— the seeded ladder id
//
// Run (from repo root):
//   dotnet run --project tests/k6/SpikeHost -- 2>&1 | grep -E "^(SPIKE_|Now listening|Application)"

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using GameKit.Auth.Builder;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Lobby.Builder;
using GameKit.Matchmaking.Builder;
using GameKit.Rankings.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using StackExchange.Redis;

// ---------------------------------------------------------------------------
// Configuration from environment
// ---------------------------------------------------------------------------
var pgCs = Environment.GetEnvironmentVariable("SPIKE_PG_CS")
    ?? "Host=localhost;Port=5499;Database=gamekit_spike;Username=gamekit_owner;Password=gamekit_owner_dev;Search Path=gamekit,public";
var pgAppCs = Environment.GetEnvironmentVariable("SPIKE_PG_APP_CS")
    ?? "Host=localhost;Port=5499;Database=gamekit_spike;Username=gamekit_app;Password=gamekit_app_dev;Search Path=gamekit,public";
var redisCs = Environment.GetEnvironmentVariable("SPIKE_REDIS_CS") ?? "localhost:6399";
var kestrelUrl = Environment.GetEnvironmentVariable("SPIKE_KESTREL_URL") ?? "http://localhost:5100";

const string Issuer   = "gamekit-spike";
const string Audience = "gamekit-spike";

// ---------------------------------------------------------------------------
// Ephemeral RSA keypair for JWT signing
// ---------------------------------------------------------------------------
var keyDir = Path.Combine(Path.GetTempPath(), $"gk-spike-keys-{Guid.NewGuid():N}");
Directory.CreateDirectory(keyDir);
var privPem = Path.Combine(keyDir, "priv.pem");
var pubPem  = Path.Combine(keyDir, "pub.pem");
using var rsa = RSA.Create(2048);
File.WriteAllText(privPem, rsa.ExportRSAPrivateKeyPem());
File.WriteAllText(pubPem, rsa.ExportRSAPublicKeyPem());

// ---------------------------------------------------------------------------
// Apply migrations via owner connection (skip if already applied)
// ---------------------------------------------------------------------------
var skipMigrations = Environment.GetEnvironmentVariable("SPIKE_SKIP_MIGRATIONS") == "1";
if (!skipMigrations)
{
    Console.Error.WriteLine("Applying GameKit migrations...");
    await ApplyMigrationsAsync(pgCs);
    Console.Error.WriteLine("Migrations applied.");
}
else
{
    Console.Error.WriteLine("Skipping migrations (SPIKE_SKIP_MIGRATIONS=1).");
}

// ---------------------------------------------------------------------------
// Seed test data
// ---------------------------------------------------------------------------
var testPlayerId = Guid.NewGuid();
var (ladderId, lobbyId) = await SeedTestDataAsync(pgCs, testPlayerId);
Console.Error.WriteLine($"Seeded player={testPlayerId} ladder={ladderId} lobby={lobbyId}");

// ---------------------------------------------------------------------------
// Mint JWT for the test player
// ---------------------------------------------------------------------------
var jwt = MintJwt(testPlayerId, rsa, Issuer, Audience);

// Print the outputs for the caller to capture
Console.WriteLine($"SPIKE_JWT={jwt}");
Console.WriteLine($"SPIKE_LOBBY_ID={lobbyId}");
Console.WriteLine($"SPIKE_LADDER_ID={ladderId}");
Console.WriteLine($"SPIKE_PLAYER_ID={testPlayerId}");

// ---------------------------------------------------------------------------
// Build and start the host
// ---------------------------------------------------------------------------
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(kestrelUrl);

// Redis multiplexer
builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
    ConnectionMultiplexer.Connect(redisCs));

// GameKit pipeline
var gkBuilder = builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = pgAppCs;
    opts.MigrationsConnectionString = pgCs;
    opts.RedisConnectionString = redisCs;
    opts.AutoMigrate = false; // we applied manually above
});
gkBuilder.AddAuth(auth =>
{
    auth.Jwt.Issuer            = Issuer;
    auth.Jwt.Audience          = Audience;
    auth.Jwt.PrivateKeyPemPath = privPem;
    auth.Jwt.PublicKeyPemPath  = pubPem;
    auth.Jwt.Kid               = "spike-key";
});
gkBuilder.AddRankings();
var mm = gkBuilder.AddMatchmaking();
mm.AddLadder("default");
gkBuilder.AddLobby();

// DbContext with all model customizers for Lobby
builder.Services.AddDbContext<GameKitDbContext>((_, opts) =>
    opts.UseNpgsql(pgAppCs)
        .ReplaceService<IModelCustomizer, SpikeModelCustomizer>());

var app = builder.Build();

app.UseWebSockets();
app.UseRouting();
app.UseGameKitAuth();
app.UseGameKit();
app.MapAuth();
app.MapGameKit();
app.MapMatchmaking();
app.MapLobby();

Console.Error.WriteLine($"Starting Kestrel on {kestrelUrl}...");
await app.RunAsync();

// Cleanup temp keys
try { Directory.Delete(keyDir, recursive: true); } catch { /* best-effort */ }

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static async Task ApplyMigrationsAsync(string ownerCs)
{
    // Apply EF Core migrations directly via a DbContext with SpikeModelCustomizer.
    // We suppress PendingModelChangesWarning because our spike model customizer applies
    // all entity configurations but may differ slightly from the migration snapshot —
    // we still want to run the existing committed migrations.
    var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>();
    optionsBuilder.UseNpgsql(ownerCs)
        .ReplaceService<IModelCustomizer, SpikeModelCustomizer>()
        .ConfigureWarnings(w => w.Ignore(
            Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
    await using var ctx = new GameKitDbContext(optionsBuilder.Options);
    await ctx.Database.MigrateAsync();
}

static async Task<(Guid ladderId, Guid lobbyId)> SeedTestDataAsync(string ownerCs, Guid playerId)
{
    // Use Npgsql directly to seed test data — avoids needing the full EF pipeline for seeding.
    // The app connection is used at runtime; owner connection used for seeding (same perms in spike DB).
    var appCs = ownerCs; // spike DB has no role separation — use same conn for seeding
    await using var conn = new NpgsqlConnection(appCs);
    await conn.OpenAsync();

    var ladderId = Guid.NewGuid();
    var lobbyId  = Guid.NewGuid();

    // Seed player
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"INSERT INTO gamekit.players
            (""Id"", ""DisplayName"", ""CreatedAt"", ""IsBanned"")
            VALUES (@id, @name, NOW(), false)
            ON CONFLICT DO NOTHING";
        cmd.Parameters.AddWithValue("id", playerId);
        cmd.Parameters.AddWithValue("name", "spike-player");
        await cmd.ExecuteNonQueryAsync();
    }

    // Seed ladder (idempotent)
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"INSERT INTO gamekit.ladders
            (""Id"", ""Name"", ""Algorithm"", ""IsActive"", ""CreatedAt"", ""Config"")
            VALUES (@id, @name, 'Glicko2', true, NOW(), '{}'::jsonb)
            ON CONFLICT (""Name"") DO UPDATE SET ""IsActive"" = true
            RETURNING ""Id""";
        cmd.Parameters.AddWithValue("id", ladderId);
        cmd.Parameters.AddWithValue("name", "default");
        var result = await cmd.ExecuteScalarAsync();
        if (result is Guid existingId) ladderId = existingId;
    }

    // Seed lobby in Open state (0)
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"INSERT INTO gamekit.lobbies
            (""Id"", ""OwnerId"", ""LadderId"", ""State"", ""MaxMembers"", ""CreatedAt"", ""UpdatedAt"")
            VALUES (@id, @ownerId, @ladderId, 0, 8, NOW(), NOW())";
        cmd.Parameters.AddWithValue("id", lobbyId);
        cmd.Parameters.AddWithValue("ownerId", playerId);
        cmd.Parameters.AddWithValue("ladderId", ladderId);
        await cmd.ExecuteNonQueryAsync();
    }

    // Seed lobby member
    await using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = @"INSERT INTO gamekit.lobby_members
            (""Id"", ""LobbyId"", ""PlayerId"", ""Ready"", ""JoinedAt"")
            VALUES (@id, @lobbyId, @playerId, false, NOW())";
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("lobbyId", lobbyId);
        cmd.Parameters.AddWithValue("playerId", playerId);
        await cmd.ExecuteNonQueryAsync();
    }

    return (ladderId, lobbyId);
}

static string MintJwt(Guid playerId, RSA rsa, string issuer, string audience)
{
    var creds = new SigningCredentials(
        new RsaSecurityKey(rsa) { KeyId = "spike-key" },
        SecurityAlgorithms.RsaSha256)
    {
        CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false },
    };
    var now = DateTime.UtcNow;
    var token = new JwtSecurityToken(
        issuer: issuer,
        audience: audience,
        claims: new[]
        {
            new Claim("sub", playerId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, playerId.ToString()),
            new Claim("is_guest", "false"),
            new Claim("provider", "spike"),
        },
        notBefore: now.AddMinutes(-1),
        expires: now.AddHours(1),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

// ---------------------------------------------------------------------------
// Model customizer that includes Lobby + Matchmaking + Rankings entity models
// ---------------------------------------------------------------------------
internal sealed class SpikeModelCustomizer : IModelCustomizer
{
    public void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        // Apply all GameKit entity configurations by scanning assemblies.
        // This mirrors what the package builders do internally.
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(GameKit.Core.Data.GameKitDbContext).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(GameKit.Auth.GameKitAuthOptions).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(GameKit.Rankings.GameKitRankingsOptions).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(GameKit.Matchmaking.GameKitMatchmakingOptions).Assembly);
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(GameKit.Lobby.GameKitLobbyOptions).Assembly);
    }
}
