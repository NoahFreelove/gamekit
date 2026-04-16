// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("gamekit");
    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Apply pending GameKit migrations against the configured Postgres database.");
    config.AddCommand<AdminCreateCommand>("admin")
        .WithDescription("(Phase 3 — stub) Create the first admin user.");
});
return await app.RunAsync(args);
