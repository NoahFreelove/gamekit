// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;

namespace GameKit.Cli.Commands;

/// <summary>CLI command: <c>gamekit admin create</c>. Stubbed in Phase 1; full implementation in Phase 3 (ADMIN-11).</summary>
internal sealed class AdminCreateCommand : AsyncCommand
{
    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context)
    {
        AnsiConsole.MarkupLine("[yellow]'gamekit admin create' is not yet implemented — Phase 3 deliverable (ADMIN-11).[/]");
        return Task.FromResult(2);
    }
}
