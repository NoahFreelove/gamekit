// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

// InternalsVisibleTo: test assemblies need access to internal configurations and model
// extensions to build isolated EF model contexts (bypasses global EF Core model cache,
// Pitfall 3). Mirrors the Auth assembly pattern.
[assembly: InternalsVisibleTo("GameKit.Rankings.Tests")]
[assembly: InternalsVisibleTo("GameKit.Rankings.Integration.Tests")]
[assembly: InternalsVisibleTo("GameKit.Cli.Tests")]
// GameKit.Cli accesses RankingsCliModelCustomizer (internal configurations) — mirrors
// the GameKit.Admin.UI → GameKit.Cli InternalsVisibleTo pattern (plan 04-04).
// AssemblyName in GameKit.Cli.csproj is "gamekit" (the tool command name), not "GameKit.Cli".
[assembly: InternalsVisibleTo("gamekit")]
