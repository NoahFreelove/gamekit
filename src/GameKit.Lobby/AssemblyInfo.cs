// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

// InternalsVisibleTo: the Wave-0 LobbyMigrationModelCustomizer and other internal types
// need to be accessible to both unit and integration test assemblies.
[assembly: InternalsVisibleTo("GameKit.Lobby.Tests")]
[assembly: InternalsVisibleTo("GameKit.Lobby.Integration.Tests")]
// Phase 11: OpenApi contract tests may apply LobbyMigrationModelCustomizer to the
// runtime DbContext — mirrors the Matchmaking assembly grant pattern.
[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]
// Plan 21-06: Platformer3D integration tests apply LobbyModelBuilderExtension in
// PlatformerTestModelCustomizer for the full five-package runtime DbContext.
// Mirrors the Lobby.Integration.Tests + OpenApi.Integration.Tests grants above.
[assembly: InternalsVisibleTo("GameKit.Platformer3D.Integration.Tests")]
