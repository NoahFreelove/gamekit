---
phase: 07-core-rating-seam-stateless-auth-packages
verified: 2026-06-05T23:30:00Z
status: human_needed
score: 4/5 must-haves verified
overrides_applied: 0
human_verification:
  - test: "Apple Sign-In live round-trip — confirm sub stored as external_id"
    expected: "POST /auth/callback/apple completes, player_identities row has provider='apple' and external_id equal to Apple sub (opaque string, NOT email address); relay email + name present in Metadata JSONB on first login only; Metadata NOT overwritten on second login"
    why_human: "Requires real Apple Developer .p8 key + Service ID + Team ID + Key ID; Apple token endpoint must accept GenerateClientSecret=true ES256 JWT; cannot be exercised without credentials"
  - test: "Epic EOS live round-trip — confirm Basic-auth token exchange and account_id stored as external_id"
    expected: "POST /auth/callback/epic completes without 400 invalid_client; player_identities row has provider='epic' and external_id equal to Epic account_id; if Epic live endpoint rejects Basic auth, document fallback to form-body auth"
    why_human: "Requires real Epic EOS sandbox credentials (Client ID/Secret, redirect URI configured); live Epic token endpoint behavior for Authorization: Basic header cannot be verified against WireMock stub alone"
gaps: []
deferred:
  - truth: "Real Glicko-2 ratings flow into EloRangeMatchmakingStrategy bracket comparisons without additional configuration (SC#2)"
    addressed_in: "Phase 8"
    evidence: "Phase 8 goal: 'real ratings flow into the matchmaking bracket'; Phase 8 SC#3: 'A developer calling .WithRatingsFrom<RankingsRatingSource>() gets real Glicko-2 ratings injected into the matchmaking queue at enqueue time'; ROADMAP.md Phase 7 note explicitly defers MATCH-16 consumption wiring to Phase 8"
---

# Phase 7: Core Rating Seam + Stateless Auth Packages Verification Report

**Phase Goal:** The codebase gains the rating-provider seam and four new auth packages; every rating-aware feature is unblocked; no database migrations are needed.
**Verified:** 2026-06-05T23:30:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths

| # | Truth | Status | Evidence |
|---|-------|--------|---------|
| 1 | A developer who installs only GameKit.Matchmaking (without Rankings) gets v1 zero-rating fallback unchanged — no compile errors, no runtime exceptions. | VERIFIED | `IPlayerRatingProvider` seam + `NullPlayerRatingProvider` registered via `TryAddSingleton` in `GameKitServiceCollectionExtensions.AddGameKit()` (line 110). `MatchmakingService` still hardcodes `Rating: 0, RatingDeviation: 0, Volatility: 0` (MatchmakingService.cs:203, ProposalService.cs:303) — v1 path unchanged. Build 0 errors, 0 warnings. |
| 2 | (SC#2) Real Glicko-2 ratings flow into EloRangeMatchmakingStrategy bracket comparisons without additional configuration. | DEFERRED | Per ROADMAP.md Phase 7 note: "SC#2 requires the Phase 8 consumption wiring in MatchmakingService.EnqueueAsync. Phase 7 ships only the IPlayerRatingProvider seam + null-object default." `IPlayerRatingProvider` seam is present; Phase 8 (MATCH-16, RANK-17) delivers the real wiring. Not a gap — correctly deferred. |
| 3 | A developer installing GameKit.Auth.Argon2 and calling AddAuth().UseArgon2() can log in; existing BCrypt-hashed passwords are transparently rehashed to Argon2id on successful login; no password reset required. | VERIFIED | `Argon2idPasswordHasher.NeedsRehash()` returns true for `$2a$`/`$2b$` prefixes. `PasswordOAuthProvider.CompleteLoginAsync` wires the rehash-on-verify block (lines 140–152). `ArgonRehashOnVerifyTests` (Testcontainers Postgres): 2/2 PASS — BCrypt→Argon2id migration verified durable by re-reading from a fresh DbContext scope. `player_credentials.password_hash` extended to varchar(512) via `20260418100000_AuthPasswordHashLength` Auth migration. |
| 4 | A developer can install any of GameKit.Auth.Google, GameKit.Auth.Apple, GameKit.Auth.Epic as standalone packages; each registers its IOAuthProvider via unconditional AddScoped and creates a player_identities row on first login using (provider, external_id) uniqueness. | VERIFIED | All three packages call `builder.Services.AddScoped<IOAuthProvider, XxxOAuthProvider>()` unconditionally before any conditional scheme block. Provider discriminators: "google", "apple", "epic". `CompleteLoginAsync` in each provider upserts via `Provider == Provider && ExternalId == externalId` (UNIQUE constraint). Unit tests: Google 3/3 PASS, Apple 4/4 PASS, Epic 4/4 PASS. External IDs: Google sub (ClaimTypes.NameIdentifier), Apple sub (FindFirst("sub")), Epic account_id (ClaimTypes.NameIdentifier). |
| 5 | The Apple provider generates a fresh ES256 client secret per token exchange (GenerateClientSecret = true); an integration test asserts the Apple sub (not email) is stored as external_id. | UNCERTAIN (human_needed) | `GenerateClientSecret = true` is set unconditionally in `AppleBuilderExtensions.cs` (line 94). `ClientSecretExpiresAfter = 170d` (< 180d cap) is asserted by unit test `ClientSecretOptions_DefaultExpiry_IsLessThan180Days`. `FindFirst("sub")` extracts sub (not email) in `OnCreatingTicket`. However, the "integration test asserts Apple sub stored as external_id" clause requires a live Apple token round-trip (real .p8 key + Service ID) — this is documented as the human-verify gate (07-04-T4, VALIDATION.md). DI, options-shape, sub extraction, and conditional-scheme are all unit-tested. |

**Score:** 4/5 truths verified (SC#2 deferred, SC#5 human_needed)

### Deferred Items

Items not yet met but explicitly addressed in later milestone phases.

| # | Item | Addressed In | Evidence |
|---|------|-------------|---------|
| 1 | Real Glicko-2 ratings flow into EloRangeMatchmakingStrategy without additional configuration (SC#2) | Phase 8 | Phase 8 SC#3: ".WithRatingsFrom<RankingsRatingSource>() gets real Glicko-2 ratings injected into matchmaking queue at enqueue time"; MATCH-16 in Phase 8 requirements |

---

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `src/GameKit.Core/Services/IPlayerRatingProvider.cs` | IPlayerRatingProvider interface + PlayerRatingValue record | VERIFIED | 49 lines; full XML docs; `ValueTask<IReadOnlyDictionary<Guid, PlayerRatingValue>> GetRatingsAsync(...)` |
| `src/GameKit.Core/Services/NullPlayerRatingProvider.cs` | Null-object returning ImmutableDictionary.Empty | VERIFIED | `internal sealed`; returns `ImmutableDictionary<Guid, PlayerRatingValue>.Empty`; zero allocation |
| `src/GameKit.Auth/Services/IPasswordHasher.cs` | IPasswordHasher with NeedsRehash method | VERIFIED | `bool NeedsRehash(string hash)` present with full XML docs |
| `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` | NeedsRehash always returns false | VERIFIED | `public bool NeedsRehash(string hash) => false;` at line 42 |
| `src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs` | Argon2id hasher with prefix dispatch | VERIFIED | `$2a$`/`$2b$` → BCrypt.Verify for migration; otherwise Argon2.Verify; NeedsRehash returns true for BCrypt prefixes |
| `src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs` | UseArgon2() removes BCrypt, adds Argon2 | VERIFIED | `RemoveAll<IPasswordHasher>()` + `AddSingleton<IPasswordHasher, Argon2idPasswordHasher>()` |
| `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` | Rehash-on-verify wiring | VERIFIED | Lines 140–152: `NeedsRehash` guard after `Verify` success, before `BannedCheckHelper`; reloads tracked entity by PK; `SaveChangesAsync` in same scope |
| `src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.cs` | Auth migration extending varchar(72)→varchar(512) | VERIFIED | `AlterColumn<string>` on `player_credentials.PasswordHash` to `character varying(512)`; Auth schema only, no Core table modification |
| `src/GameKit.Auth.Google/Providers/Google/GoogleOAuthProvider.cs` | Google provider, sub as external_id | VERIFIED | `Provider => "google"`; upserts via `(Provider, ExternalId)` where ExternalId = sub from ClaimTypes.NameIdentifier |
| `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs` | Self-registration + conditional scheme | VERIFIED | Unconditional `AddScoped<IOAuthProvider, GoogleOAuthProvider>()`; conditional Google scheme when ClientId present |
| `src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs` | Apple provider, sub as external_id, first-login Metadata | VERIFIED | `Provider => "apple"`; first-login Metadata JSONB write with relay_email+name; subsequent logins never overwrite Metadata |
| `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs` | GenerateClientSecret=true, ES256, sub extraction | VERIFIED | `apple.GenerateClientSecret = true` (line 94); `PrivateKey` delegate decodes base64 PEM; `FindFirst("sub")` as externalId |
| `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs` | Custom OAuthHandler with Basic-auth ExchangeCodeAsync | VERIFIED | `ExchangeCodeAsync` builds `Authorization: Basic base64(clientId:clientSecret)` header; form body contains only grant_type/code/redirect_uri |
| `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthProvider.cs` | Epic provider, account_id as external_id | VERIFIED | `Provider => "epic"`; upserts via `(Provider, ExternalId)` where ExternalId = account_id |
| `tests/GameKit.Auth.Argon2.Tests/Argon2HasherTests.cs` | Argon2 unit tests | VERIFIED | 12/12 PASS; round-trip, NeedsRehash discriminator, OWASP floor, BCrypt-compat verify |
| `tests/GameKit.Auth.Google.Tests/GoogleProviderTests.cs` | Google unit tests | VERIFIED | 3/3 PASS; DI-smoke, conditional-scheme, sub-not-email |
| `tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs` | Apple unit tests | VERIFIED | 4/4 PASS; DI-smoke, options-shape (expiry<180d), conditional-scheme, discriminator |
| `tests/GameKit.Auth.Epic.Tests/EpicProviderTests.cs` | Epic unit tests incl. WireMock Basic-auth proof | VERIFIED | 4/4 PASS; DI-smoke, conditional-scheme, discriminator, WireMock Basic-auth wire format assertion |
| `tests/GameKit.Auth.Integration.Tests/ArgonRehashOnVerifyTests.cs` | Testcontainers proof of BCrypt→Argon2 rehash | VERIFIED | 2/2 PASS (Testcontainers Postgres); BCrypt hash durably upgraded to `$argon2id$`; BCrypt control unchanged |
| `tests/GameKit.Core.Tests/IPlayerRatingProviderTests.cs` | Core seam unit tests | VERIFIED | 2/2 PASS; null-object returns empty dict; TryAddSingleton registration |

---

### Key Link Verification

| From | To | Via | Status | Details |
|------|----|-----|--------|---------|
| `GameKitServiceCollectionExtensions.AddGameKit()` | `NullPlayerRatingProvider` | `services.TryAddSingleton<IPlayerRatingProvider, NullPlayerRatingProvider>()` | WIRED | Line 110 of GameKitServiceCollectionExtensions.cs confirmed |
| `PasswordOAuthProvider.CompleteLoginAsync` | `IPasswordHasher.NeedsRehash` | Call after successful Verify, before BannedCheck | WIRED | Lines 140–152 of PasswordOAuthProvider.cs confirmed |
| `Argon2BuilderExtensions.UseArgon2()` | `Argon2idPasswordHasher` | `RemoveAll<IPasswordHasher>()` + `AddSingleton` | WIRED | Lines 47–49 of Argon2BuilderExtensions.cs confirmed |
| `GoogleBuilderExtensions.AddGoogle()` | `GoogleOAuthProvider` | `AddScoped<IOAuthProvider, GoogleOAuthProvider>()` | WIRED | Unconditional registration before conditional scheme |
| `AppleBuilderExtensions.AddApple()` | `AppleOAuthProvider` | `AddScoped<IOAuthProvider, AppleOAuthProvider>()` | WIRED | Unconditional registration before conditional scheme |
| `AppleBuilderExtensions.AddApple()` | Apple ES256 via `GenerateClientSecret = true` | `apple.GenerateClientSecret = true` + `PrivateKey` delegate | WIRED | Line 94 + lines 103–111 of AppleBuilderExtensions.cs |
| `EpicBuilderExtensions.AddEpic()` | `EpicOAuthProvider` | `AddScoped<IOAuthProvider, EpicOAuthProvider>()` | WIRED | Unconditional registration before conditional scheme |
| `EpicOAuthHandler.ExchangeCodeAsync` | Basic-auth header | `request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials)` | WIRED | Line 85 of EpicOAuthHandler.cs; WireMock test confirms wire format |

---

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| `NullPlayerRatingProvider.GetRatingsAsync` | `ImmutableDictionary<Guid, PlayerRatingValue>.Empty` | Intentional null-object | Yes (empty is the correct null-object behavior) | FLOWING — zero-rating fallback is the designed output |
| `Argon2idPasswordHasher.Hash` | Argon2id encoded string | `Isopoh.Cryptography.Argon2.Argon2.Hash(...)` with random salt | Yes | FLOWING — round-trip test proves real hash produced |
| `PasswordOAuthProvider` rehash block | Updated `credential.PasswordHash` | Re-loaded tracked entity + `_hasher.Hash(password)` + `SaveChangesAsync` | Yes | FLOWING — Testcontainers test proves durable DB write |
| `GoogleOAuthProvider.CompleteLoginAsync` | `PlayerIdentity.ExternalId` | `externalId` parameter (= Google sub from ClaimTypes.NameIdentifier) | Yes | FLOWING — code path verified; live Google round-trip is human-only |
| `AppleOAuthProvider.CompleteLoginAsync` | `PlayerIdentity.ExternalId`, `PlayerIdentity.Metadata` | `externalId` (= Apple sub), relay email on first login only | Yes (code correct) | FLOWING (code) / UNCERTAIN (live) — live Apple round-trip is human-verify gate |
| `EpicOAuthProvider.CompleteLoginAsync` | `PlayerIdentity.ExternalId` | `externalId` (= Epic account_id from ClaimTypes.NameIdentifier) | Yes (code correct) | FLOWING (code) / UNCERTAIN (live) — live Epic round-trip is human-verify gate |

---

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Solution builds clean | `dotnet build GameKit.sln --nologo` | 0 Warning(s), 0 Error(s) | PASS |
| Argon2 unit tests | `dotnet test tests/GameKit.Auth.Argon2.Tests/...` | 12/12 passed | PASS |
| Google unit tests | `dotnet test tests/GameKit.Auth.Google.Tests/...` | 3/3 passed | PASS |
| Apple unit tests | `dotnet test tests/GameKit.Auth.Apple.Tests/...` | 4/4 passed | PASS |
| Epic unit tests (incl. WireMock) | `dotnet test tests/GameKit.Auth.Epic.Tests/...` | 4/4 passed | PASS |
| Core seam unit tests | `dotnet test tests/GameKit.Core.Tests/... --filter IPlayerRatingProvider` | 2/2 passed | PASS |
| ArgonRehashOnVerify integration tests | `dotnet test tests/GameKit.Auth.Integration.Tests/... --filter ArgonRehashOnVerifyTests` | 2/2 passed (Testcontainers Postgres) | PASS |

---

### Probe Execution

No `scripts/*/tests/probe-*.sh` files found for Phase 7. SKIPPED.

---

### Requirements Coverage

| Requirement | Source Plan | Description | Status | Evidence |
|-------------|------------|-------------|--------|---------|
| CORE-18 | 07-01-PLAN | IPlayerRatingProvider optional-port + NullPlayerRatingProvider null-object + TryAddSingleton | SATISFIED | Interface + record in `GameKit.Core/Services/IPlayerRatingProvider.cs`; null-object in `NullPlayerRatingProvider.cs`; registration in `GameKitServiceCollectionExtensions.cs` line 110 |
| AUTH-17 | 07-02-PLAN | GameKit.Auth.Argon2 package with Argon2idPasswordHasher (Isopoh 2.0.0, m=65536, t=3, p=1) | SATISFIED | Package exists at `src/GameKit.Auth.Argon2/`; OWASP defaults in `GameKitArgon2Options`; 12 unit tests green |
| AUTH-18 | 07-02-PLAN, 07-06-PLAN | NeedsRehash seam + BCrypt→Argon2 rehash-on-verify; no forced password reset | SATISFIED | `IPasswordHasher.NeedsRehash` in interface; `BCryptPasswordHasher.NeedsRehash => false`; `Argon2idPasswordHasher.NeedsRehash` returns true for `$2a$`/`$2b$` prefixes; wired in `PasswordOAuthProvider.CompleteLoginAsync`; `ArgonRehashOnVerifyTests` 2/2 PASS (Testcontainers) |
| AUTH-19 | 07-03-PLAN | GameKit.Auth.Google package, Microsoft.AspNetCore.Authentication.Google 10.0.8, sub as external_id | SATISFIED | Package at `src/GameKit.Auth.Google/`; sub via ClaimTypes.NameIdentifier; 3 unit tests green |
| AUTH-20 | 07-04-PLAN | GameKit.Auth.Apple, ES256 per-exchange client secret, sub as canonical external_id, first-login name/email to Metadata | SATISFIED (automated) / HUMAN_NEEDED (live round-trip) | `GenerateClientSecret=true`, `ClientSecretExpiresAfter=170d`; `FindFirst("sub")` as externalId; first-login Metadata JSONB; 4 unit tests green. Live Apple round-trip requires external credentials (human-verify gate 07-04-T4) |
| AUTH-21 | 07-05-PLAN | GameKit.Auth.Epic, custom OAuthHandler, Basic-auth token exchange, account_id as external_id | SATISFIED (automated) / HUMAN_NEEDED (live round-trip) | `EpicOAuthHandler.ExchangeCodeAsync` sends `Authorization: Basic` header; WireMock test proves wire format; account_id mapped to ClaimTypes.NameIdentifier; 4 unit tests green. Live EOS round-trip requires external credentials (human-verify gate 07-05-T4) |
| AUTH-22 | 07-03-PLAN, 07-04-PLAN, 07-05-PLAN | All new providers integrate with IOAuthProvider + (provider, external_id) uniqueness contract, minimal scopes | SATISFIED | All three providers: unconditional `AddScoped<IOAuthProvider, XxxOAuthProvider>()`; discriminators "google"/"apple"/"epic"; upsert keyed by `(Provider, ExternalId)`; minimal scopes (Google: profile/email; Apple: name/email; Epic: basic_profile) |

---

### Acceptable Deviation: Auth Migration in "Zero-Migration" Phase

Phase 7 was scoped as "zero-migration" in the ROADMAP, but plan 07-06 correctly added `20260418100000_AuthPasswordHashLength` which extends `player_credentials.password_hash` from varchar(72) to varchar(512).

**Assessment: ACCEPTABLE DEVIATION — not a gap.**

Rationale:
1. The migration is strictly within the Auth package's own boundary — it modifies the `player_credentials` table which is owned by `GameKit.Auth`, not a Core table. The CLAUDE.md migration boundary rule ("packages never modify Core tables in their migrations — only add new tables or FK references") is not violated.
2. The existing Auth advisory lock key (`-298890956L`) is reused — no new advisory lock key, no pairwise-distinctness concern.
3. The migration is functionally required: BCrypt hashes are 60 chars, Argon2id encoded strings are 80–120+ chars; varchar(72) rejects the new hasher's output at `SaveChangesAsync` with PostgresException 22001.
4. The deviation was discovered during TDD (RED phase) and auto-fixed per Rule 1.
5. Summary 07-06 documents this explicitly with full justification.

---

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| None found | — | — | — | No TBD/FIXME/XXX/HACK/placeholder markers in any Phase 7 source files. No NotImplementedException, no hollow return null implementations. The NullPlayerRatingProvider returns ImmutableDictionary.Empty which is the intended null-object behavior, not a stub. |

---

### Human Verification Required

#### 1. Apple Sign-In Live Round-Trip (AUTH-20, SC#5)

**Test:** Configure a real Apple Developer app: create a Sign-In-with-Apple Key (.p8), note Team ID + Key ID, create a Services ID. Base64-encode the .p8 file content. Set `GAMEKIT_APPLE_PRIVATEKEY_BASE64` environment variable; configure `ServiceId`/`TeamId`/`KeyId` in `AddApple(...)`. Run the sample host TicTacToeDuel (or any host with `AddApple` configured). Perform Sign-In-with-Apple in a browser.

**Expected:**
- The sign-in completes without error (HTTP 200, tokens returned)
- A `player_identities` row exists with `provider = 'apple'` and `external_id` equal to the Apple `sub` claim — a stable opaque string (NOT an email address, NOT a relay email)
- On the first login, `Metadata` JSONB contains `relay_email` and `name` fields
- On a second login with the same Apple account, `Metadata` is NOT overwritten (still shows first-login values)

**Why human:** Requires a real Apple Developer .p8 key + Services ID. Apple's token endpoint validates the ES256 JWT signature against the registered key; no WireMock stub can simulate the full Apple Sign-In flow. This is documented in VALIDATION.md as task 07-04-T4 with `gate="blocking-human"`.

---

#### 2. Epic EOS Live Round-Trip (AUTH-21)

**Test:** Create a product in the Epic Games Dev Portal. Create an EOS client (Client ID/Secret) with redirect URI set to `<host>/signin-epic`. Configure `AddEpic(o => { o.ClientId = ...; o.ClientSecret = ...; })` in the sample host. Perform an Epic login in a browser.

**Expected:**
- The sign-in completes without `400 invalid_client` error (confirming the live Epic token endpoint accepts `Authorization: Basic` as the handler sends)
- A `player_identities` row exists with `provider = 'epic'` and `external_id` equal to the Epic `account_id` field (NOT email, NOT display_name)

**Fallback if Basic auth rejected:** If Epic's live endpoint returns `400 invalid_client` with Basic auth, the handler's `ExchangeCodeAsync` override must be updated to use form-body client auth (`client_id`/`client_secret` as form fields) and the WireMock test updated accordingly. This is documented in SUMMARY 07-05 as the known fallback path.

**Why human:** Requires real Epic EOS sandbox credentials. The WireMock test (`TokenExchange_UsesBasicAuth_WithWireMockStub`) proves the wire format of the code, but only a live EOS exchange can confirm Epic's token endpoint accepts it. This is documented in VALIDATION.md as task 07-05-T4 with `gate="blocking-human"`.

---

### Gaps Summary

No automatable gaps. All automated checks passed:
- Build: 0 errors, 0 warnings (CS1591 XML-doc enforcement passing)
- Unit tests: Core.Tests 2/2, Auth.Argon2.Tests 12/12, Auth.Google.Tests 3/3, Auth.Apple.Tests 4/4, Auth.Epic.Tests 4/4
- Integration tests: ArgonRehashOnVerifyTests 2/2 (Testcontainers Postgres)
- SC#2 correctly deferred to Phase 8 per ROADMAP note
- Auth migration deviation is acceptable (Auth-owned boundary, functionally required)

Two live-credential round-trips require human verification (Apple live Sign-In, Epic live EOS exchange). These are not fabricated — they are deliberate human-verify gates designed into the validation plan because external developer credentials are unavailable in the CI environment.

---

_Verified: 2026-06-05T23:30:00Z_
_Verifier: Claude (gsd-verifier)_
