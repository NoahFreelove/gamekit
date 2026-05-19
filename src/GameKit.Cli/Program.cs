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

    config.AddBranch("service-token", st =>
    {
        st.SetDescription("Service-account bearer token operations (issue, revoke, list).");
        st.AddCommand<ServiceTokenIssueCommand>("issue")
            .WithDescription("Mint a new service-account bearer token. Raw token printed once — store it securely.");
        st.AddCommand<ServiceTokenRevokeCommand>("revoke")
            .WithDescription("Revoke a service-account bearer token by name.");
        st.AddCommand<ServiceTokenListCommand>("list")
            .WithDescription("List all service-account bearer tokens (names, dates, status — never the hash).");
    });
});
return await app.RunAsync(args);
