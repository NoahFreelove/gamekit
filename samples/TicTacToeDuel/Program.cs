// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Builder;
using TicTacToeDuel.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
});

var app = builder.Build();

// Serve wwwroot/index.html at "/" — must come before UseGameKit / MapGameKit so the
// static handler runs before any endpoint matching.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseGameKit();
app.MapGameKit();   // /api/players (RequireAuthorization — 401 in Phase 1)
app.MapDemo();      // /demo/* (anonymous — demo only)

app.Run();
