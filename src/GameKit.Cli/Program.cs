// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Cli.Commands;
using Spectre.Console.Cli;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("gamekit");
    config.AddCommand<MigrateCommand>("migrate")
        .WithDescription("Apply GameKit migrations (Core + Auth + Admin) against the configured Postgres.");

    config.AddBranch("admin", admin =>
    {
        admin.SetDescription("Admin operations (superadmin bootstrap, admin CRUD).");
        admin.AddCommand<AdminCreateCommand>("create")
            .WithDescription("Create an admin user (interactive or flag-driven). First admin auto-promoted to superadmin.");
    });
});
return await app.RunAsync(args);
