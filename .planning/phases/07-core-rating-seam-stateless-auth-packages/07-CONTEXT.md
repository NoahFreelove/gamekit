# Phase 7: Core Rating Seam + Stateless Auth Packages - Context

**Gathered:** 2026-06-05
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss; enriched from .planning/research/)

<domain>
## Phase Boundary

Deliver the `IPlayerRatingProvider` rating seam in `GameKit.Core` plus four new stateless opt-in auth sibling packages (Argon2, Google, Apple, Epic). ZERO database migrations in this phase — every new package is stateless (the seam is an interface + null-object; auth packages only add `IPasswordHasher`/`IOAuthProvider` implementations writing to the existing `player_identities` / `player_credentials` tables). Rating-aware matchmaking *consumption* and Rankings' `RankingsRatingSource` implementation are deferred to Phase 8 — this phase only establishes the seam and the optional null-object default.

Requirements: CORE-18, AUTH-17, AUTH-18, AUTH-19, AUTH-20, AUTH-21, AUTH-22.
</domain>

<decisions>
## Implementation Decisions

### Rating Seam (CORE-18)
- `IPlayerRatingProvider` defined in `GameKit.Core` mirroring the existing `IPresenceProvider` optional-port pattern; method shape returns rating + RD for a player/ladder (align with Glicko-2 `double` rating already used).
- A null-object default implementation registered by Core returns the v1 behaviour (rating=0 / default RD) so Matchmaking-without-Rankings is unchanged. The MatchmakingService consumption wiring (reading the provider at `EnqueueAsync` and caching into the Redis ticket hash) is built in Phase 8, NOT here — but the seam + null-object default land here.

### Auth — Argon2 (AUTH-17/18)
- `GameKit.Auth.Argon2` provides `Argon2idPasswordHasher : IPasswordHasher` using `Isopoh.Cryptography.Argon2` 2.0.0 (CC0). Params: m=65536 (64 MiB), t=3, p=1.
- BCrypt→Argon2 migration is rehash-on-verify via hash-format detection (`$2a$`/`$2b$` ⇒ BCrypt verify then re-hash with Argon2; `$argon2id$` ⇒ Argon2 verify). No `player_credentials` schema change — format prefix is sufficient discriminator (no migration).

### Auth — OAuth Providers (AUTH-19/20/21/22)
- Google: `GameKit.Auth.Google` wraps `Microsoft.AspNetCore.Authentication.Google` 10.0.8 (no aspnet-contrib Google exists).
- Apple: `GameKit.Auth.Apple` wraps `AspNet.Security.OAuth.Apple` 10.0.0; `GenerateClientSecret = true` (ES256 client secret regenerated per exchange from a `.p8` private key via BCL `ECDsa.ImportPkcs8PrivateKey`); `sub` is stored as `external_id` (NOT email); name/email captured first-login-only; private-relay email stored as-is.
- Epic: `GameKit.Auth.Epic` is a custom `OAuthHandler<EpicOAuthOptions>` against Epic's standard OAuth2 endpoints — no NuGet dep (no maintained package exists).
- All four register their `IOAuthProvider` via the existing Scrutor scan (`publicOnly:false`) and honour the `(provider, external_id)` uniqueness contract; minimal scopes only (no scope creep). Conditional scheme registration (only when ClientId+Secret supplied) mirrors the v1 Discord pattern so test harnesses don't throw.

### Distribution
- All five new package IDs join the coordinated MinVer release train (same version, exact-pinned `[X.Y.Z]` sibling refs) — formal release-train wiring + version-assertion coverage is closed out in Phase 12 (DIST-07), but new `.csproj`s must follow the existing Directory.Build.props/Directory.Packages.props conventions now.

### Claude's Discretion
Exact interface method signatures, file layout, options-class shape, and test structure are at Claude's discretion — follow existing v1 patterns (`IPresenceProvider`, Discord `IOAuthProvider`, `BCryptPasswordHasher`, per-package csproj conventions). Discuss was skipped per user setting; research basis is `.planning/research/STACK.md`, `FEATURES.md`, `ARCHITECTURE.md`, `PITFALLS.md`, `SUMMARY.md`.
</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets / Patterns to mirror
- `src/GameKit.Core/Abstractions/` — home for `IPlayerRatingProvider`; `IPresenceProvider` is the null-object optional-port precedent.
- `src/GameKit.Auth/` — `IOAuthProvider`, the Discord provider (conditional scheme registration; `DiscordBackchannelPostConfigure`), `BCryptPasswordHasher : IPasswordHasher`, Scrutor registration with `publicOnly:false`.
- `src/GameKit.Matchmaking/MatchmakingService.cs` — `EnqueueAsync` currently hardcodes member rating=0 (the seam's eventual consumer; left for Phase 8).
- Directory.Build.props (CS1591-as-error, MinVer, SourceLink), Directory.Packages.props (CPM pins) — new packages must conform.

### Integration Points
- New auth packages plug into the existing `AddAuth()` builder / Scrutor scan and the existing `player_identities`/`player_credentials` tables (no new tables).
- The rating seam registers in Core's DI; the null-object default must be overridable by Phase 8's `RankingsRatingSource`.

### Pitfalls (from research)
- Apple ES256 client secret must be generated PER exchange (6-month max), not cached; `sub` is the identity key, not email.
- Confirm no `Microsoft.IdentityModel.*` diamond-dependency conflict when adding the Apple package.
- Epic provider correctness needs live EOS credentials to fully integration-test (flag as acceptable blocker if unavailable; unit-test the handler shape regardless).
</code_context>

<specifics>
## Specific Ideas

- Stateless = NO migrations this phase. If planning discovers any feature needs a schema change (e.g. an Argon2 discriminator column), STOP and reconsider — format-prefix detection should make it unnecessary; a migration would also need a new advisory-lock key (defer such a need and flag it).
- Open questions to resolve during planning/execution: (1) Epic = custom `OAuthHandler` vs OpenIddict client (default to custom handler per research); (2) confirm Argon2 needs no `player_credentials` migration (format-prefix detection expected).
</specifics>

<deferred>
## Deferred Ideas

- Rating-aware EloRange consumption + `RankingsRatingSource` implementation + guardrails → Phase 8.
- Formal release-train version-assertion coverage for the 5 new packages → Phase 12 (DIST-07).
</deferred>
