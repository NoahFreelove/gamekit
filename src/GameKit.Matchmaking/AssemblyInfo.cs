// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

// InternalsVisibleTo: the Wave-0 MatchmakingTestModelCustomizer (Plan 05-01) instantiates
// the internal MatchmakingModelBuilderExtension directly so cross-package integration
// tests can apply both Matchmaking and Rankings entity configurations in one EF model
// (bypasses the global EF Core model cache, Pitfall §3). Mirrors the Rankings assembly
// pattern (GameKit.Rankings.AssemblyInfo grants InternalsVisibleTo to its own integration
// test assembly).
[assembly: InternalsVisibleTo("GameKit.Matchmaking.Tests")]
[assembly: InternalsVisibleTo("GameKit.Matchmaking.Integration.Tests")]
[assembly: InternalsVisibleTo("GameKit.Matchmaking.LoadTests")]
