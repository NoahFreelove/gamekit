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
