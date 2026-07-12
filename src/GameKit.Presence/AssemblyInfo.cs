// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

// InternalsVisibleTo: Phase 6 Plan 06-04 RedisPresenceProviderTests +
// PresenceOptionsValidatorTests probe internal types (the Redis-backed
// IPresenceProvider implementation + its options validator). Mirrors the
// GameKit.Matchmaking.AssemblyInfo grant pattern (Phase 5 Plan 05-01).
[assembly: InternalsVisibleTo("GameKit.Presence.Tests")]
[assembly: InternalsVisibleTo("GameKit.Presence.Integration.Tests")]
