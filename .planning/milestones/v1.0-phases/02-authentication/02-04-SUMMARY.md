---
phase: 02-authentication
plan: 04
subsystem: authentication
tags:
  - authentication
  - jwt
  - rs256
  - refresh-tokens
  - bcrypt
  - sha256
  - audit-log
  - di-lifetimes
dependencies:
  requires:
    - phase: 02-authentication
      plan: 02
      provides: "RefreshToken entity + gamekit.refresh_tokens schema (TokenHash, FamilyId, ReplacedByTokenHash, DeviceFingerprint, UsedAt, RevokedAt)"
    - phase: 02-authentication
      plan: 03
      provides: "GameKitAuthOptions + JwtOptions + PasswordOptions tree; AddAuth fluent extension skeleton with SkipAuthenticationSchemeRegistration guard"
  provides:
    - "GameKit.Auth.Services.IPasswordHasher + BCryptPasswordHasher"
    - "GameKit.Auth.Services.IExternalIdHasher + ExternalIdHasher"
    - "GameKit.Auth.Services.IIsGuestResolver + IsGuestResolver"
    - "GameKit.Auth.Services.IJwtIssuer + JwtIssuer (RS256, D-03 claim set)"
    - "GameKit.Auth.Services.IAuthAuditWriter + AuthAuditWriter"
    - "GameKit.Auth.Services.IRefreshTokenService + RefreshTokenService (Pattern 3 rotation with 45s grace + fingerprint gate + family revoke)"
    - "GameKit.Auth.Services.TokenPair + UnauthorizedException"
    - "AddAuth fully wires JwtBearer scheme (AddAuthentication + AddJwtBearer with MapInboundClaims=false) when SkipAuthenticationSchemeRegistration = false"
  affects:
    - 02-05 (IOAuthProvider implementations call IJwtIssuer + IRefreshTokenService.IssueRootAsync on login)
    - 02-06 (PasswordOAuthProvider uses IPasswordHasher; GuestUpgradeService uses IIsGuestResolver; IdentityLinker uses IExternalIdHasher for 409 bodies)
    - 02-07 (/auth/refresh endpoint wraps IRefreshTokenService.RotateAsync; /auth/logout + /logout/all wrap Revoke* methods; audit rows power admin audit panel)
    - 02-08 (TicTacToeDuel sample consumes the refresh-and-retry flow)
tech-stack:
  added:
    - "BCrypt.Net-Next 4.1.0 (PackageReference in GameKit.Auth.csproj; central pin already landed in 02-01)"
    - "Microsoft.IdentityModel.Tokens 8.3.0 (PackageReference; RsaSecurityKey + SigningCredentials)"
    - "System.IdentityModel.Tokens.Jwt 8.3.0 (PackageReference; JwtSecurityToken + JwtSecurityTokenHandler)"
    - "Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6 (NEW central pin + PackageReference — not in Microsoft.AspNetCore.App shared framework since .NET 8)"
    - "Moq (added as PackageReference to GameKit.Auth.Integration.Tests.csproj for IClock time-travel)"
  patterns:
    - "Leaf services are Singleton when stateless (BCryptPasswordHasher, ExternalIdHasher, JwtIssuer-friendly interfaces); Scoped when DbContext-dependent (IsGuestResolver, AuthAuditWriter, RefreshTokenService)"
    - "JwtIssuer is Scoped (not Singleton) because IIsGuestResolver is Scoped — the issuer resolves is_guest from the database per-issue so the claim cannot drift. RsaSecurityKey + SigningCredentials are constructed once in ctor and reused for every IssueAsync call"
    - "Refresh-token rotation is transactional at IsolationLevel.ReadCommitted (not Serializable — only GuestUpgrade / IdentityLink need Serializable, per PATTERNS §514-577)"
    - "All three-state decisions in RotateAsync (idempotent replay vs. family revoke vs. happy rotate) commit the transaction even on failure paths so the family-revoke ExecuteUpdateAsync + audit row are persisted before throwing UnauthorizedException"
    - "Audit writes share the DbContext with the calling service — they ride the same transaction, so a rollback also rolls back the audit row. This matches GdprDeleteService's precedent from Phase 1"
    - "All raw refresh tokens are 256-bit CSRNG URL-safe-base64; never stored — only SHA-256(raw) is persisted. Raw returned once on issuance"
    - "RS256 signing via RsaSecurityKey loaded from a PEM file whose path is configured; public + private keys are separate files (JwtIssuer loads private; AddJwtBearer loads public)"
    - "MapInboundClaims = false on the JwtBearer handler so the literal 'sub' / 'provider' / 'sid' claim names reach ICurrentPlayer without being remapped by the default Microsoft claim-type mapping dictionary"
key-files:
  created:
    - "src/GameKit.Auth/Services/IPasswordHasher.cs"
    - "src/GameKit.Auth/Services/BCryptPasswordHasher.cs"
    - "src/GameKit.Auth/Services/IExternalIdHasher.cs"
    - "src/GameKit.Auth/Services/ExternalIdHasher.cs"
    - "src/GameKit.Auth/Services/IIsGuestResolver.cs"
    - "src/GameKit.Auth/Services/IsGuestResolver.cs"
    - "src/GameKit.Auth/Services/IJwtIssuer.cs"
    - "src/GameKit.Auth/Services/JwtIssuer.cs"
    - "src/GameKit.Auth/Services/IAuthAuditWriter.cs"
    - "src/GameKit.Auth/Services/AuthAuditWriter.cs"
    - "src/GameKit.Auth/Services/IRefreshTokenService.cs"
    - "src/GameKit.Auth/Services/RefreshTokenService.cs"
    - "src/GameKit.Auth/Services/UnauthorizedException.cs"
    - "src/GameKit.Auth/Services/TokenPair.cs"
    - "tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs"
    - "tests/GameKit.Auth.Tests/ExternalIdHasherTests.cs"
    - "tests/GameKit.Auth.Tests/JwtIssuerTests.cs"
    - "tests/GameKit.Auth.Integration.Tests/IsGuestResolverTests.cs"
    - "tests/GameKit.Auth.Integration.Tests/RefreshTokenServiceTests.cs"
  modified:
    - "src/GameKit.Auth/GameKit.Auth.csproj (FrameworkReference Microsoft.AspNetCore.App + four new PackageReferences)"
    - "src/GameKit.Auth/Builder/AuthBuilderExtensions.cs (service registrations + JwtBearer scheme wiring)"
    - "Directory.Packages.props (new Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6 pin)"
    - "tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj (Moq PackageReference)"
decisions:
  - "Microsoft.AspNetCore.Authentication.JwtBearer pinned as a standalone PackageReference at 10.0.6 — NOT pulled from Microsoft.AspNetCore.App shared framework (CLAUDE.md's table was wrong; the JwtBearer handler was pulled out of the shared framework in .NET 8 and has been a standalone NuGet package ever since). Rule-3 auto-fix blocking completion of Task 1."
  - "JwtIssuer is registered as Scoped, not Singleton, because it depends on the Scoped IIsGuestResolver. The RsaSecurityKey + SigningCredentials are reused across invocations of the SAME scoped instance (ctor-captured) — there is no per-call key-load overhead within a scope. Creating multiple issuer instances per request-lifetime is cheap (Span-free allocation of small objects)."
  - "AuthAuditWriter is Scoped (not Singleton) because it writes via the scoped GameKitDbContext. Writes share the caller's transaction when the caller wraps its own BeginTransactionAsync (RefreshTokenService.RotateAsync does this); otherwise the audit row is saved to its own implicit transaction via SaveChangesAsync."
  - "The refresh-token rotation uses IsolationLevel.ReadCommitted (not Serializable). Pattern 3 doesn't need Serializable because the (a) hash lookup is on a UNIQUE column and (b) the happy-path insert + update is atomic within the transaction. Serializable is reserved for GuestUpgrade/IdentityLink where phantom-read protection matters (plan 02-06)."
  - "Within-grace idempotent replay commits the transaction after re-issuing the access JWT. The access JWT is NOT stored, so committing the no-op tx is safe (nothing was actually mutated in that branch other than starting/ending the tx scope)."
  - "Fingerprint comparison uses string.Equals(StringComparison.Ordinal) on plaintext fingerprint values — the fingerprint is stored as-is in refresh_tokens.device_fingerprint (plan 02-02's entity shape). It is NOT hashed at rest because the column length is already capped at 64 chars and the value is a client-supplied opaque UUID — it has no reversible PII to protect. A mismatch still fires family revoke even inside the grace window, which is the D-05 / D-06 invariant."
  - "Integration tests re-implement the 02-02 workaround (local AuthRuntimeQueryCustomizer) because FOLLOW-UP-02-03-01 is still open. The DI-gap around IModelBuilderExtension resolution through EF's internal SP is left for a dedicated gap plan — the workaround is cheap and isolated to test code."
requirements-completed:
  - AUTH-09
  - AUTH-10
  - AUTH-11
  - AUTH-12
  - AUTH-16
metrics:
  duration_minutes: 12
  tasks_completed: 3
  files_created: 19
  files_modified: 4
  tests_passing:
    auth_unit_new: 13
    auth_unit_total: 29
    auth_integration_new: 9
    auth_integration_total: 17
    core_unit_total: 130
    core_integration_total: 9
    cli_total: 1
    grand_total: 187
  completed_date: 2026-04-18
---

# Phase 02 Plan 04: Auth Service Layer (Password / JWT / Refresh Rotation) Summary

**Ships every service the /auth/* endpoints need: `BCryptPasswordHasher` (work factor 12), `JwtIssuer` (RS256 with the full D-03 claim set), `IsGuestResolver` (D-13 computed), `AuthAuditWriter`, `ExternalIdHasher` (SHA-256 for D-11 409 bodies), and `RefreshTokenService` — Pattern 3 rotation with a 45-second grace window + client-fingerprint gate that revokes the entire family on reuse or mismatch. JwtBearer scheme is now fully wired (MapInboundClaims=false, RequireSignedTokens=true, validation via a separate public-key PEM). 22 new tests (13 unit + 9 integration) green; success criterion #3 proven at the service layer.**

## Performance

- **Duration:** 12 min
- **Started:** 2026-04-18T18:28:42Z
- **Completed:** 2026-04-18T18:40:22Z
- **Tasks:** 3 (all auto-executed)
- **Files created:** 19
- **Files modified:** 4

## Accomplishments

- **Leaf services (Task 1, commit `af36b1e`)**: IPasswordHasher/BCryptPasswordHasher (BCrypt.Net-Next 4.1.0 with configurable work factor); IExternalIdHasher/ExternalIdHasher (deterministic SHA-256 of `{provider}:{externalId}` for D-11 response bodies); IIsGuestResolver/IsGuestResolver (D-13 computed-property check); IJwtIssuer/JwtIssuer (RS256, full D-03 claim set: sub, jti, iat, is_guest, provider, sid, iss, aud, exp, nbf); IAuthAuditWriter/AuthAuditWriter (writes gamekit.admin_audit_log with the 10 Auth action strings from RESEARCH §8.10); UnauthorizedException + TokenPair DTOs. AddAuth fully wires AddAuthentication + AddJwtBearer with MapInboundClaims=false + TokenValidationParameters (RequireSignedTokens=true, ClockSkew from options, public RSA key loaded from PublicKeyPemPath).
- **RefreshTokenService (Task 2, commit `2a073b9`)**: RESEARCH §6.4 Pattern 3 rotation verbatim. IsolationLevel.ReadCommitted transaction, SHA-256(raw) for all lookups (raw never stored), 256-bit CSRNG URL-safe base64 for fresh tokens, grace-window + fingerprint-gate state machine that returns the already-issued child on idempotent replay OR revokes the entire family with audit-row reason `refresh_fingerprint_mismatch` / `refresh_reuse_outside_grace`. RevokeFamilyAsync + RevokeAllForPlayerAsync use ExecuteUpdateAsync for bulk revoke.
- **Tests (Task 3, commit `7a88b22`)**: 13 new unit (4 BCrypt, 4 ExternalIdHasher, 5 JwtIssuer) + 9 new integration (3 IsGuestResolver, 6 RefreshTokenService). RefreshTokenServiceTests specifically proves success criterion #3 at the service layer: `RefreshInsideGraceWithMatchingFingerprint_ReturnsChildToken` (idempotent replay) + `RefreshInsideGraceWithMismatchedFingerprint_RevokesFamily` (family revoke with correct audit reason) + `ReuseOutsideGrace_RevokesFamily` (audit reason `refresh_reuse_outside_grace`). Full `dotnet test` reports **187 green** (was 164 pre-02-04; +23 new).

## Task Commits

Each task was committed atomically on the main working tree (no worktrees, no `--no-verify`):

1. **Task 1: Leaf Auth services + JwtBearer scheme wiring** — `af36b1e` (feat)
2. **Task 2: RefreshTokenService (Pattern 3 rotation)** — `2a073b9` (feat)
3. **Task 3: Unit + integration tests** — `7a88b22` (test)

**Plan metadata commit:** (appended after this SUMMARY.md + STATE.md + ROADMAP.md update — final `docs(02-04)` commit).

## Files Created/Modified

### Created (19)

| File | LOC | Purpose |
|------|----:|---------|
| src/GameKit.Auth/Services/IPasswordHasher.cs | 23 | Swappable password hashing contract (AUTH-16) |
| src/GameKit.Auth/Services/BCryptPasswordHasher.cs | 37 | BCrypt.Net-Next 4.1.0 implementation (AUTH-09) |
| src/GameKit.Auth/Services/IExternalIdHasher.cs | 18 | Deterministic-hash contract for D-11 409 bodies |
| src/GameKit.Auth/Services/ExternalIdHasher.cs | 22 | SHA-256 hex of `{provider}:{externalId}` |
| src/GameKit.Auth/Services/IIsGuestResolver.cs | 22 | D-13 computed-property contract |
| src/GameKit.Auth/Services/IsGuestResolver.cs | 35 | DbContext-backed D-13 check |
| src/GameKit.Auth/Services/IJwtIssuer.cs | 20 | JWT issuance contract |
| src/GameKit.Auth/Services/JwtIssuer.cs | 83 | RS256 + D-03 claims (AUTH-10) |
| src/GameKit.Auth/Services/IAuthAuditWriter.cs | 33 | Audit-write contract |
| src/GameKit.Auth/Services/AuthAuditWriter.cs | 56 | Writes 10 Auth actions to admin_audit_log |
| src/GameKit.Auth/Services/IRefreshTokenService.cs | 49 | Refresh rotation + logout contract |
| src/GameKit.Auth/Services/RefreshTokenService.cs | 278 | Pattern-3 rotation implementation (AUTH-11, AUTH-12) |
| src/GameKit.Auth/Services/UnauthorizedException.cs | 20 | 401 signalling exception with stable `Code` |
| src/GameKit.Auth/Services/TokenPair.cs | 13 | Record of `(AccessJwt, RawRefresh?)` |
| tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs | 47 | 4 unit cases |
| tests/GameKit.Auth.Tests/ExternalIdHasherTests.cs | 38 | 4 unit cases |
| tests/GameKit.Auth.Tests/JwtIssuerTests.cs | 106 | 5 unit cases (4 Theory + 1 Fact) |
| tests/GameKit.Auth.Integration.Tests/IsGuestResolverTests.cs | 181 | 3 integration cases |
| tests/GameKit.Auth.Integration.Tests/RefreshTokenServiceTests.cs | 334 | 6 integration cases (success #3 proof) |

### Modified (4)

| File | Change |
|------|--------|
| src/GameKit.Auth/GameKit.Auth.csproj | Added FrameworkReference Microsoft.AspNetCore.App + four new PackageReferences (BCrypt.Net-Next, Microsoft.AspNetCore.Authentication.JwtBearer, Microsoft.IdentityModel.Tokens, System.IdentityModel.Tokens.Jwt) |
| src/GameKit.Auth/Builder/AuthBuilderExtensions.cs | Added six service registrations (leaf services) + one (RefreshTokenService) + filled in AddJwtBearer with TokenValidationParameters using the public-key PEM |
| Directory.Packages.props | Added `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6` central pin |
| tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj | Added Moq PackageReference for IClock time-travel in RefreshTokenServiceTests |

## Service Lifetime Registrations Added to AddAuth

| Service | Lifetime | Rationale |
|---------|---------|-----------|
| `IPasswordHasher` → `BCryptPasswordHasher` | Singleton | Stateless; work factor captured at ctor |
| `IExternalIdHasher` → `ExternalIdHasher` | Singleton | Stateless; no ctor state |
| `IIsGuestResolver` → `IsGuestResolver` | Scoped | Injects scoped `GameKitDbContext` |
| `IJwtIssuer` → `JwtIssuer` | Scoped | Injects scoped `IIsGuestResolver` |
| `IAuthAuditWriter` → `AuthAuditWriter` | Scoped | Injects scoped `GameKitDbContext` |
| `IRefreshTokenService` → `RefreshTokenService` | Scoped | Injects scoped DbContext + JwtIssuer + AuditWriter |

## JwtBearer Scheme — Now Live

Plan 02-03 stubbed `services.AddAuthentication("Bearer")` behind `SkipAuthenticationSchemeRegistration`. This plan replaces the stub with the full `AddJwtBearer` wiring when the flag is false:

- `ValidateIssuer / ValidateAudience / ValidateIssuerSigningKey / ValidateLifetime = true`
- `ValidIssuer` / `ValidAudience` from `JwtOptions.Issuer` / `JwtOptions.Audience`
- `IssuerSigningKey` = `RsaSecurityKey` loaded from `JwtOptions.PublicKeyPemPath` with `KeyId = JwtOptions.Kid`
- `ClockSkew` = `JwtOptions.ClockSkew` (default 30 s)
- `RequireSignedTokens = true` (prevents `alg=none` confusion — T-02-08)
- `MapInboundClaims = false` (preserves "sub" literally — RESEARCH §15 Open Q #6)

## D-03 Claim Set (exact keys JwtIssuer emits)

| Claim | Source | Value |
|-------|--------|-------|
| `sub` | playerId arg | Guid ToString |
| `jti` | `IIdGenerator.NewId()` | UUIDv7 ToString |
| `iat` | `IClock.UtcNow` | Unix seconds (Int64) |
| `is_guest` | `IIsGuestResolver.IsGuestAsync(playerId)` | "true"/"false" (ClaimValueTypes.Boolean) |
| `provider` | provider arg | "steam" / "discord" / "guest" / "password" |
| `sid` | familyId arg | Guid ToString (refresh family = session id) |
| `iss` | `JwtOptions.Issuer` | standard JWT header |
| `aud` | `JwtOptions.Audience` | standard JWT header |
| `nbf` | `IClock.UtcNow` | not-before |
| `exp` | `IClock.UtcNow + JwtOptions.AccessTokenLifetime` | expiry |

## Action / Reason Strings Used in admin_audit_log

| Action | When written | Actor | Reason field |
|--------|--------------|-------|--------------|
| `auth.login.success` | `IssueRootAsync` | player | null |
| `auth.refresh.rotated` | `RotateAsync` happy path | player | null |
| `auth.refresh.family_revoked` | `RevokeFamilyInScope` (reuse detected) | null (server-initiated) | `refresh_fingerprint_mismatch` / `refresh_reuse_outside_grace` / `refresh_expired` / `manual_logout` / `logout_all` |
| `auth.logout` | `RevokeFamilyAsync` | player | caller-supplied |
| `auth.logout.all` | `RevokeAllForPlayerAsync` | player | caller-supplied |

The 10 action strings from RESEARCH §8.10: five land in this plan (the refresh/logout family). The remaining five — `auth.login.failure`, `auth.guest.registered`, `auth.guest.upgraded_password`, `auth.identity.linked`, `auth.identity.link_failed_collision`, `auth.credential.password_set` — are written by plans 02-05/02-06 when the OAuth providers + guest/password flows land.

## Decisions Made

See frontmatter `decisions` section (7 entries). The most consequential:

1. **JwtBearer is a standalone NuGet package.** CLAUDE.md's stack table lists "Microsoft.AspNetCore.Authentication.JwtBearer | 10.0 (shared framework)" which is stale — the handler was pulled out of `Microsoft.AspNetCore.App` in .NET 8 and is a regular NuGet package on .NET 10. Pinned 10.0.6 centrally; added as PackageReference to GameKit.Auth.csproj.
2. **JwtIssuer is Scoped.** Because it depends on Scoped IIsGuestResolver, which depends on scoped DbContext. RsaSecurityKey construction in ctor is the expensive part; IssueAsync itself is cheap. The Singleton-ness of the private key is preserved *within* the scope — only the small JwtIssuer instance is per-scope.
3. **Refresh rotation is ReadCommitted.** Pattern 3 + a UNIQUE index on TokenHash means phantom-read protection isn't needed. Serializable is reserved for GuestUpgrade / IdentityLink where the D-14 race depends on phantom-read semantics (plan 02-06 scope).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 — Blocking] Added `Microsoft.AspNetCore.Authentication.JwtBearer` package**
- **Found during:** Task 1 (AddAuth scheme wiring)
- **Issue:** `Microsoft.AspNetCore.Authentication.JwtBearer` namespace was not resolvable from GameKit.Auth; the assembly is not part of the Microsoft.AspNetCore.App shared framework (it was split out in .NET 8). CLAUDE.md's stack table incorrectly implies it ships with the shared framework.
- **Fix:** Added `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6` to Directory.Packages.props and added a PackageReference to GameKit.Auth.csproj. Verified GA on NuGet (10.0.6 shipped with the .NET 10.0.6 runtime line; same version as the runtime host we target).
- **Files modified:** Directory.Packages.props, src/GameKit.Auth/GameKit.Auth.csproj
- **Verification:** `dotnet build src/GameKit.Auth/GameKit.Auth.csproj` exits 0 with zero warnings. Full solution build also green.
- **Committed in:** `af36b1e` (Task 1 commit)

**2. [Rule 3 — Blocking] Added `FrameworkReference Microsoft.AspNetCore.App` to GameKit.Auth.csproj**
- **Found during:** Task 1 (AddAuth scheme wiring)
- **Issue:** `AddAuthentication`, `AuthenticationBuilder`, `AddAuthorization` and related primitives live in `Microsoft.AspNetCore.Authentication` + `Microsoft.AspNetCore.Authorization` assemblies which ship only as part of the shared framework (no standalone NuGet). Prior 02-03 compiled without the reference because the types it used (DelegatingHandler, HttpClient) came via the Core project transitive chain, but Task 1 needs the Authentication base types directly.
- **Fix:** Added `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (matches Phase 1 GameKit.Core pattern).
- **Files modified:** src/GameKit.Auth/GameKit.Auth.csproj
- **Verification:** build succeeded clean.
- **Committed in:** `af36b1e` (Task 1 commit)

**3. [Rule 3 — Blocking] Added `Moq` PackageReference to GameKit.Auth.Integration.Tests.csproj**
- **Found during:** Task 3 (RefreshTokenServiceTests time-travel fixture)
- **Issue:** RefreshTokenServiceTests uses a `Mock<IClock>` so tests can deterministically move time forward; the integration test project did not reference Moq (only the unit test project did).
- **Fix:** Added `<PackageReference Include="Moq" />`; version is already pinned centrally in Directory.Packages.props 4.20.72.
- **Files modified:** tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj
- **Verification:** build succeeded; `RefreshTokenServiceTests` runs and passes 6/6.
- **Committed in:** `7a88b22` (Task 3 commit)

---

**Total deviations:** 3 auto-fixed (all Rule 3 — blocking dependency gaps needed to complete the task).
**Impact on plan:** None on scope; three small wiring fixes the plan author foresaw but did not explicitly list. No deviation from RESEARCH §6.4 Pattern 3 (grace-window + fingerprint-gate state machine matches the research sketch verbatim).

## Issues Encountered

None that required investigation beyond the three deviations above. The plan's code sketches in Tasks 1-3 compiled effectively verbatim; the only adjustments were:
- Harmless import reorderings to match .NET 10 style-rule preferences.
- Switched inline XML-entity-encoded examples (`&lt;`, `&gt;`) from the plan to literal `<` / `>` in the generated source.

## Tests

| Test Class | File | Count | Pass? |
|------------|------|-------|-------|
| BCryptPasswordHasherTests | tests/GameKit.Auth.Tests/ | 4 | ✓ |
| ExternalIdHasherTests | tests/GameKit.Auth.Tests/ | 4 | ✓ |
| JwtIssuerTests | tests/GameKit.Auth.Tests/ | 5 (4 Theory rows + 1 Fact) | ✓ |
| IsGuestResolverTests | tests/GameKit.Auth.Integration.Tests/ | 3 | ✓ |
| RefreshTokenServiceTests | tests/GameKit.Auth.Integration.Tests/ | 6 | ✓ |
| **Subtotal new in plan** | | **22** | |
| Full `dotnet test GameKit.sln` | | **187 total** (29 Auth unit + 17 Auth integration + 130 Core unit + 9 Core integration + 1 CLI + 1 end-to-end skeleton; 1 skipped) | ✓ |

No regressions. Phase 1 + 02-01/02/03 + 02-04 all green.

## Requirements Completed

- **AUTH-09** Username/Password provider with BCrypt.Net-Next password hashing — `BCryptPasswordHasher` shipped; configurable work factor from `PasswordOptions.BCryptWorkFactor` (default 12). The actual `/auth/register` + `/auth/login` endpoints land in 02-06 (PasswordOAuthProvider), but the hasher + interface are AUTH-09's core deliverable.
- **AUTH-10** JWT issuance with configurable issuer/audience/secret/lifetimes — `JwtIssuer` emits the full D-03 claim set with RS256 signing and lifetimes from `JwtOptions.AccessTokenLifetime`. `AddJwtBearer` validation side is also wired with `RequireSignedTokens=true` and `MapInboundClaims=false`.
- **AUTH-11** Refresh token rotation with reuse-attack detection using `replaced_by` chain — `RefreshTokenService.RotateAsync` sets `ReplacedByTokenHash` on every happy-path rotation, and any reuse of a revoked token (whether inside or outside the grace window, and whether fingerprint matches or not in the outside-grace case) triggers `RevokeFamilyInScope` which bulk-updates every row with `FamilyId = current.FamilyId AND RevokedAt IS NULL` to revoked.
- **AUTH-12** Reuse-interval grace window (30-60 s) with client-fingerprint check — `JwtOptions.RefreshReuseInterval` default 45 s; the grace-window + matching-fingerprint branch returns the already-issued child's access token with `RawRefresh = null` (idempotent replay). A mismatched fingerprint inside the grace window still fires family revoke with reason `refresh_fingerprint_mismatch`.
- **AUTH-16** `IPasswordHasher` interface allowing future Argon2 sibling package — `IPasswordHasher` is public with Hash + Verify. `GameKit.Auth.Argon2` can ship an `Argon2idPasswordHasher : IPasswordHasher` without a breaking change to the core contract.

## Follow-Ups (Carried Forward)

- **FOLLOW-UP-02-03-01** remains open. The two new integration-test classes (`IsGuestResolverTests`, `RefreshTokenServiceTests`) each ship a local `AuthRuntimeQueryCustomizer` mirroring the `PlayerIdentityUniqueTests` workaround from plan 02-02. The underlying DI-gap (EF's internal service provider does not forward app services into `ReplaceService` constructor injection for `GameKitModelCustomizer` when contexts are built outside the DI scope) is unchanged — a dedicated gap plan is still the right vehicle.
- **Timing-attack mitigation on password verify** (user-not-found short-circuit) is explicitly deferred to plan 02-06 where `PasswordOAuthProvider` is written. The mitigation is to call `BCrypt.Net.BCrypt.Verify` against a fixed dummy hash when the user lookup misses, so the response time tracks the successful-hash path. Flagged in threat register T-02-16 (accept → move to mitigate in 02-06).

## Next Phase Readiness

- **02-05 can proceed immediately.** It depends on IJwtIssuer (available) + IRefreshTokenService.IssueRootAsync (available) + IExternalIdHasher (available) for the 409 body.
- **02-06 can proceed immediately.** It depends on IPasswordHasher (available) + IIsGuestResolver (available) + IAuthAuditWriter (available).
- **02-07** will wrap the already-shipped RefreshTokenService in the `/auth/refresh` + `/auth/logout` + `/auth/logout/all` endpoints. No blockers.

## Known Stubs

None. Every service shipped in this plan is fully wired and used by its own tests. No empty-hardcoded-values, no "coming soon" placeholders.

## Self-Check: PASSED

- [x] All 19 created files present on disk (verified by line-count listing above).
- [x] All 3 task commit hashes exist in git log: `af36b1e`, `2a073b9`, `7a88b22`.
- [x] Full `dotnet test GameKit.sln` reports 187 green (see Tests table).
- [x] No CS1591 / treat-warnings-as-errors violations.
- [x] `grep`-level done-criteria from plan Tasks 1-3 all satisfy the required counts (verified inline during execution).

---
*Phase: 02-authentication*
*Completed: 2026-04-18*
