// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGameKit(opts =>
{
    opts.ConnectionString = builder.Configuration.GetConnectionString("GameKit")
        ?? throw new InvalidOperationException("Missing ConnectionStrings:GameKit");
    opts.MigrationsConnectionString = builder.Configuration.GetConnectionString("GameKitMigrations");
    opts.RedisConnectionString = builder.Configuration.GetConnectionString("Redis");
});

var app = builder.Build();

app.UseGameKit();
app.MapGameKit();

app.Run();
