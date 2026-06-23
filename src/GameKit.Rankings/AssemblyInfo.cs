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
// Phase 5 Plan 05-01: MatchmakingTestModelCustomizer applies RankingsModelBuilderExtension
// directly so cross-package integration tests (Matchmaking reads player_ranks via
// EloRangeMatchmakingStrategy) can include Rankings entities in their EF model without
// re-implementing the seven entity configurations. Mirrors the GameKit.Auth → Admin
// InternalsVisibleTo grant established in plan 03-06 (CoreInternalsVisibleTo precedent).
[assembly: InternalsVisibleTo("GameKit.Matchmaking.Integration.Tests")]
// Phase 6 Plan 06-05: SessionsLifecycleObserverTests in GameKit.Presence.Integration.Tests
// applies RankingsModelBuilderExtension + RankingsMigrationModelCustomizer to build a hybrid
// Core + Rankings + Presence test host that empirically validates the cross-package
// ISessionLifecycleObserver wire-up (game-server-authoritative POST /api/sessions/{id}/start
// sets Redis presence:{playerId}=in_match via PresenceSessionObserver). Test-only coupling —
// Presence runtime still does NOT depend on Rankings.
[assembly: InternalsVisibleTo("GameKit.Presence.Integration.Tests")]
// Phase 6 Plan 06-06: OpenApi contract tests apply RankingsMigrationModelCustomizer to the
// runtime DbContext so MapRankings's endpoints register cleanly (the test host composes the
// full sample's package set so the D-09 EndpointDataSource enumeration covers every player-
// facing endpoint). Same precedent as the Presence.Integration.Tests grant above.
[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]
// Plan 10-02: AccountMerge integration tests apply RankingsMigrationModelCustomizer directly
// to apply the Rankings migration and build a hybrid test host covering all four packages
// (Core + Auth + Rankings + Matchmaking) for the cross-package FK-surgery tests (AUTH-24).
[assembly: InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")]
// Plan 11-04: LobbyTestModelCustomizer in GameKit.Lobby.Integration.Tests applies
// RankingsModelBuilderExtension directly so the runtime DbContext sees the ladders entity
// needed for the two-TestServer Lobby integration harness (lobbies.LadderId FK targets ladders).
[assembly: InternalsVisibleTo("GameKit.Lobby.Integration.Tests")]
// Plan 12-02: RankAdjustServiceTests in GameKit.Admin.Integration.Tests applies
// RankingsModelBuilderExtension + RankingsMigrationModelCustomizer to boot a hybrid
// Core+Auth+Admin+Rankings test host and prove SC#3: IRankAdjustService.AdjustAsync
// writes an admin_audit_log row with action "admin.player.rank_adjust".
[assembly: InternalsVisibleTo("GameKit.Admin.Integration.Tests")]
// Plan 21-06: Platformer3D integration tests compose the full five-package stack and apply
// RankingsModelBuilderExtension in PlatformerTestModelCustomizer. Mirrors the
// Matchmaking.Integration.Tests + Lobby.Integration.Tests grants above.
[assembly: InternalsVisibleTo("GameKit.Platformer3D.Integration.Tests")]
