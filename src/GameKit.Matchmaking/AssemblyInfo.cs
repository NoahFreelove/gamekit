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
// Phase 6 Plan 06-06: OpenApi contract tests apply MatchmakingMigrationModelCustomizer to
// the runtime DbContext so MapMatchmaking's endpoints register cleanly inside the hybrid
// host that asserts the D-09 EndpointDataSource enumeration vs document.paths contract.
[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]
// Plan 10-02: AccountMerge integration tests apply MatchmakingMigrationModelCustomizer
// directly to apply the Matchmaking migration as part of the cross-package ApplyMigrations
// scaffold (Core + Auth + Rankings + Matchmaking). Mirrors the Rankings grant above.
[assembly: InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")]
// Plan 11-03: GameKit.Lobby reuses SerializationFailureRetry.Build() for the MarkReadyAsync
// SERIALIZABLE transaction + 40001 retry pipeline (RESEARCH §SERIALIZABLE Pattern).
[assembly: InternalsVisibleTo("GameKit.Lobby")]
// Plan 11-04: LobbyTestModelCustomizer in GameKit.Lobby.Integration.Tests applies
// MatchmakingModelBuilderExtension directly so the runtime DbContext sees matchmaking entities
// (parties, matchmaking_tickets, etc.) needed for the two-TestServer integration harness.
[assembly: InternalsVisibleTo("GameKit.Lobby.Integration.Tests")]
// Plan 21-06: Platformer3D integration tests apply MatchmakingModelBuilderExtension in
// PlatformerTestModelCustomizer to build the full five-package runtime DbContext.
// Mirrors the Matchmaking.Integration.Tests + Lobby.Integration.Tests grants above.
[assembly: InternalsVisibleTo("GameKit.Platformer3D.Integration.Tests")]
