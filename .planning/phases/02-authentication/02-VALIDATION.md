---
phase: 02
slug: authentication
status: ready
nyquist_compliant: true
wave_0_complete: false
created: 2026-04-18
updated: 2026-04-18
---

# Phase 02 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution. Populated after planning.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11.0 + Moq 4.20.72 + WireMock.Net 2.2.0 |
| **Config file** | `tests/Directory.Packages.props`, `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs` (via [Collection("Auth")] in CollectionDefinitions.cs) |
| **Quick run command** | `dotnet test tests/GameKit.Auth.Tests --nologo --verbosity quiet` |
| **Full suite command** | `dotnet test tests/GameKit.Auth.Tests tests/GameKit.Auth.Integration.Tests --nologo --verbosity normal` |
| **Estimated runtime** | ~15 s (unit-only), ~150 s (full, incl. Testcontainers cold start + WireMock boot) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter "FullyQualifiedName~<ClassUnderTest>" --nologo --verbosity quiet` (task-scoped)
- **After every plan wave:** Run quick command (unit-only) to catch fast regressions; full Testcontainers suite after Wave 3+
- **Before `/gsd-verify-work`:** Full suite must be green, including all 6 ROADMAP success-criteria integration tests
- **Max feedback latency:** 15 seconds (unit); 150 seconds (full)

---

## Per-Task Verification Map

> Finalized during `gsd-plan-phase` for Phase 2. Every task declares either an `<automated>` verify command OR a Wave 0 dependency that installs its test scaffolding.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|--------|
| 02-01-T1 | 01 | 0 | AUTH-01 (pkg ships) | T-02-01 | Directory.Packages.props pins verified net10.0 TFM | build | `dotnet restore GameKit.sln --nologo` | ⬜ pending |
| 02-01-T2 | 01 | 0 | scaffolding | — | WireMockFixture + stubs compile | build | `dotnet build tests/GameKit.TestFixtures/GameKit.TestFixtures.csproj --nologo` | ⬜ pending |
| 02-01-T3 | 01 | 0 | scaffolding | — | Auth test projects boot + smoke test passes | smoke | `dotnet test tests/GameKit.Auth.Integration.Tests --filter "Category=Smoke" --nologo` | ⬜ pending |
| 02-02-T1 | 02 | 1 | AUTH-02, AUTH-03, AUTH-04 | T-02-01, T-02-03 | entities + configs build with CASCADE/UNIQUE | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-02-T2 | 02 | 1 | AUTH-11 | T-02-09 | AuthAdvisoryLockKey matches live hashtext, distinct from Core | integration | `dotnet test --filter FullyQualifiedName~AuthAdvisoryLockKeyTests` | ⬜ pending |
| 02-02-T3 | 02 | 1 | AUTH-02, AUTH-11 | T-02-01 | migration applies; UNIQUE(provider, external_id) enforced | integration | `dotnet test --filter "FullyQualifiedName~AuthSchemaTests\|FullyQualifiedName~PlayerIdentityUniqueTests"` | ⬜ pending |
| 02-03-T1 | 03 | 1 | AUTH-10 | T-02-04, T-02-05 | Options + DefaultAllowedHosts + EgressViolationException compile | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-03-T2 | 03 | 1 | AUTH-05, AUTH-10 | T-02-04, T-02-15 | EgressAllowListHandler + AddAuth + UseGameKitAuth compile | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-03-T3 | 03 | 1 | AUTH-10 | T-02-04, T-02-05, T-02-06 | Egress handler blocks off-list; AddAuth validates options | unit | `dotnet test --filter "FullyQualifiedName~EgressAllowListHandlerTests\|FullyQualifiedName~AuthBuilderOptionsValidationTests"` | ⬜ pending |
| 02-04-T1 | 04 | 2 | AUTH-09, AUTH-10, AUTH-13, AUTH-16 | T-02-08 | Leaf services + JwtBearer scheme wired | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-04-T2 | 04 | 2 | AUTH-11, AUTH-12 | T-02-07, T-02-11, T-02-12 | RefreshTokenService rotation + grace + family revoke | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-04-T3 | 04 | 2 | AUTH-09, AUTH-10, AUTH-11, AUTH-12, AUTH-16 | T-02-07, T-02-08, T-02-11, T-02-12 | BCrypt round-trip; JWT claims correct; grace-match replay; fingerprint mismatch revokes family; reuse outside grace revokes family | unit + integration | `dotnet test --filter "FullyQualifiedName~BCryptPasswordHasherTests\|FullyQualifiedName~ExternalIdHasherTests\|FullyQualifiedName~JwtIssuerTests\|FullyQualifiedName~IsGuestResolverTests\|FullyQualifiedName~RefreshTokenServiceTests"` | ⬜ pending |
| 02-05-T1 | 05 | 2 | AUTH-05, AUTH-06 | T-02-17 | IOAuthProvider + Steam verifier compile; check_authentication param echo correct | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-05-T2 | 05 | 2 | AUTH-05, AUTH-06, AUTH-07 | T-02-18, T-02-19 | Provider upserts + Discord scope locked + Backchannel override + Scrutor scan | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-05-T3 | 05 | 2 | AUTH-06, AUTH-07 | T-02-17, T-02-18, T-02-20 | Steam check_authentication forgery rejected (Success #2); Scrutor discovers steam+discord; provider upserts correct | unit + integration | `dotnet test --filter "FullyQualifiedName~SteamOpenIdVerifierTests\|FullyQualifiedName~ScrutorProviderDiscoveryTests\|FullyQualifiedName~SteamProviderTests\|FullyQualifiedName~DiscordProviderTests"` | ⬜ pending |
| 02-06-T1 | 06 | 3 | AUTH-08, AUTH-09 | T-02-16 | Guest + Password providers compile; timing mitigation present | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-06-T2 | 06 | 3 | AUTH-13, AUTH-14 | T-02-10, T-02-22, T-02-23, T-02-25 | IdentityLinker + GuestUpgradeService with SERIALIZABLE + 40001 retry + 23505 branch | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-06-T3 | 06 | 3 | AUTH-08, AUTH-09, AUTH-13, AUTH-14 | T-02-10, T-02-16, T-02-22, T-02-23, T-02-25 | Concurrent guest-upgrade race (Success #4); cross-player collision (Success #5); concurrent username-unique collision | integration | `dotnet test --filter "FullyQualifiedName~GuestProviderTests\|FullyQualifiedName~PasswordProviderTests\|FullyQualifiedName~GuestUpgradeServiceTests\|FullyQualifiedName~IdentityLinkerTests"` | ⬜ pending |
| 02-07-T1 | 07 | 3 | AUTH-15 | T-02-26, T-02-27 | Contracts + validators + rate-limit policies compile | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-07-T2 | 07 | 3 | AUTH-14, AUTH-15 | T-02-10, T-02-15, T-02-27, T-02-28 | AuthEndpoints.MapAuthEndpoints registers 10 endpoints; MapAuth wires in | build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ⬜ pending |
| 02-07-T3 | 07 | 3 | AUTH-14, AUTH-15, AUTH-16 | T-02-10, T-02-15, T-02-26, T-02-27 | 4-provider e2e (Success #1); concurrent refresh grace+mismatch e2e (Success #3); cross-player link 409 e2e (Success #5 e2e); rate-limit 429 e2e (Success #6); Steam forgery e2e (Success #2 e2e) | e2e | `dotnet test --filter "FullyQualifiedName~AuthEndpointsE2ETests\|FullyQualifiedName~AuthRateLimitE2ETests"` | ⬜ pending |
| 02-08-T1 | 08 | 4 | AUTH-01 (sample exercises it) | T-02-30 | Sample Program.cs composes AddAuth + UseGameKitAuth; /demo/players/register removed | build | `dotnet build samples/TicTacToeDuel/TicTacToeDuel.csproj --nologo` | ⬜ pending |
| 02-08-T2 | 08 | 4 | sample-app demo | T-02-29 | Client sends X-GameKit-Device + performs 401-refresh-retry; localStorage XSS disclaimer visible | grep | `grep -c 'X-GameKit-Device' samples/TicTacToeDuel/wwwroot/index.html >= 5 && grep -c 'refreshTokens' samples/TicTacToeDuel/wwwroot/index.html >= 1` | ⬜ pending |
| 02-08-T3 | 08 | 4 | sample-app demo | T-02-29, T-02-30, T-02-31, T-02-15 | Human walks guest → upgrade → refresh retry → logout in browser; README auth section complete | manual | Human-verify checkpoint (see Manual-Only) | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Requirements Coverage Matrix

Every AUTH-XX requirement appears in at least one plan's `requirements_addressed`:

| Requirement | Plans | Primary Task |
|-------------|-------|--------------|
| AUTH-01 | 02-01, 02-08 | 02-01-T1 (pin pkg); 02-08-T3 (sample proves it installs) |
| AUTH-02 | 02-02 | 02-02-T3 (schema + unique constraint) |
| AUTH-03 | 02-02 | 02-02-T1 (entity + config) |
| AUTH-04 | 02-02 | 02-02-T1 (entity + config) |
| AUTH-05 | 02-03, 02-05 | 02-05-T3 (Scrutor discovers IOAuthProvider) |
| AUTH-06 | 02-05 | 02-05-T3 (Steam forgery rejection via in-house check_authentication) |
| AUTH-07 | 02-05 | 02-05-T3 (Discord identify-only scope) |
| AUTH-08 | 02-06 | 02-06-T3 (GuestOAuthProvider creates anonymous player) |
| AUTH-09 | 02-04, 02-06 | 02-04-T3 (BCrypt round-trip); 02-06-T3 (password provider integration) |
| AUTH-10 | 02-03, 02-04 | 02-04-T3 (JwtIssuer claims shape) |
| AUTH-11 | 02-02, 02-04 | 02-04-T3 (family revoke on reuse) |
| AUTH-12 | 02-04 | 02-04-T3 (45s grace window + fingerprint gate) |
| AUTH-13 | 02-04, 02-06 | 02-06-T3 (SERIALIZABLE guest upgrade race) |
| AUTH-14 | 02-06, 02-07 | 02-06-T3 + 02-07-T3 (cross-player collision 409 with hash) |
| AUTH-15 | 02-07 | 02-07-T3 (rate-limit burst 429 + Retry-After) |
| AUTH-16 | 02-04, 02-07 | 02-04-T3 (IPasswordHasher abstraction) |

16 / 16 requirements covered. No dropouts.

---

## ROADMAP Success Criteria Coverage

| # | Behavior | Plans | Proof Task IDs |
|---|----------|-------|---------------|
| 1 | 4-provider e2e (Steam, Discord, Guest, Password) | 02-05, 02-06, 02-07 | 02-05-T3 (Steam, Discord integration); 02-06-T3 (Guest, Password integration); 02-07-T3 (all 4 via WebApplicationFactory) |
| 2 | Forged Steam callback rejected | 02-05, 02-07 | 02-05-T3 (`SteamOpenIdVerifierTests.Forged_Assertion_*`, `SteamProviderTests.Forged_Assertion_Rejected_*`); 02-07-T3 (`AuthEndpointsE2ETests.Steam_Callback_Forged_Assertion_Returns_400`) |
| 3 | Concurrent refresh — grace+match vs. mismatch | 02-04, 02-07 | 02-04-T3 (`RefreshTokenServiceTests.RefreshInsideGraceWithMatchingFingerprint_ReturnsChildToken`, `*MismatchedFingerprint_RevokesFamily`); 02-07-T3 (`AuthEndpointsE2ETests.Refresh_Within_Grace_*`) |
| 4 | Concurrent guest-upgrade race (SERIALIZABLE) | 02-06 | 02-06-T3 (`GuestUpgradeServiceTests.ConcurrentGuestLink_Same_Steam_Id_One_Succeeds_One_409`) |
| 5 | Cross-player link collision (no silent merge) | 02-06, 02-07 | 02-06-T3 (`IdentityLinkerTests.CrossPlayer_Collision_Returns_AlreadyLinkedToOtherPlayer_With_Hash`); 02-07-T3 (`AuthEndpointsE2ETests.Link_Cross_Player_Collision_Returns_409_With_Hash`) |
| 6 | Rate-limit 429 on burst | 02-07 | 02-07-T3 (`AuthRateLimitE2ETests.Login_11th_Request_*`, `Register_6th_Request_*`) |

All 6 success criteria proven.

---

## Threat Model Coverage (STRIDE)

Each plan has a `<threat_model>` block; T-02-XX IDs flow across plans. Summary:

| T-ID | Category | First Addressed In | Mitigation |
|------|----------|--------------------|------------|
| T-02-01 | Tampering (cross-player identity hijack) | 02-02 | UNIQUE(provider, external_id) |
| T-02-02 | Spoofing (WireMock stubs safety) | 02-01 | test-only, namespaced paths |
| T-02-03 | Repudiation (orphaned auth rows on player delete) | 02-02 | ON DELETE CASCADE |
| T-02-04 | Information Disclosure (SSRF via OAuth backchannel) | 02-03, 02-05 | EgressAllowListHandler + Discord PostConfigure |
| T-02-05 | Tampering (cleared allow-list) | 02-03 | ValidateAuthOptions throws on empty list |
| T-02-06 | Spoofing (PEM missing at startup) | 02-03 | ValidateAuthOptions file-exists check |
| T-02-07 | Spoofing (refresh-token replay) | 02-04 | SHA-256 hashed storage + single-use rotation |
| T-02-08 | Tampering (JWT alg confusion) | 02-04 | RS256 + RequireSignedTokens |
| T-02-09 | Denial of Service (advisory lock collision) | 02-02 | distinct Auth lock key |
| T-02-10 | Information Disclosure (raw external id in 409) | 02-04, 02-06, 02-07 | ExternalIdHasher + 409 body |
| T-02-11 | Spoofing (refresh hijack from different device) | 02-04 | fingerprint gate + family revoke |
| T-02-12 | Repudiation (silent family revocation) | 02-04 | audit log with reason |
| T-02-13 | Information Disclosure (test data in logs) | 02-01 | synthetic IDs only |
| T-02-14 | Information Disclosure (username case bypass) | 02-02 | citext column |
| T-02-15 | Elevation of Privilege (middleware order) | 02-03, 02-07, 02-08 | UseGameKitAuth extension; e2e test |
| T-02-16 | Information Disclosure (password-verify timing) | 02-06 | dummy BCrypt.Verify on user-not-found |
| T-02-17 | Spoofing (forged Steam OpenID assertion) | 02-05 | server-side check_authentication |
| T-02-18 | Information Disclosure (Discord scope creep) | 02-05 | Scope.Clear + Add("identify") |
| T-02-19 | Tampering (Discord Backchannel override) | 02-05 | IPostConfigureOptions scoped to type |
| T-02-20 | Spoofing (OpenID assertion replay) | 02-05 | accepted — Steam OP tracks nonce reuse |
| T-02-21 | Elevation of Privilege (custom IOAuthProvider bypass) | 02-05 | accepted — customer trust boundary |
| T-02-22 | Tampering (concurrent guest-upgrade race duplicates) | 02-06 | SERIALIZABLE + UNIQUE + 40001 retry |
| T-02-23 | Tampering (concurrent username-register collision) | 02-06 | UNIQUE(username) citext + 23505 surfaced |
| T-02-24 | Elevation of Privilege (guest → admin via credential link) | 02-06 | accepted — admin is Phase 3 separate scheme |
| T-02-25 | Repudiation (silent guest upgrade) | 02-06 | auth.guest.upgraded_password audit row |
| T-02-26 | Denial of Service (burst traffic) | 02-07 | AuthRateLimitRegistrations + composite partition |
| T-02-27 | Tampering (bad request body) | 02-07 | ValidationEndpointFilter + FluentValidation |
| T-02-28 | Repudiation (silent logout) | 02-07 | manual_logout + logout_all audit rows |
| T-02-29 | Information Disclosure (localStorage XSS) | 02-08 | accepted — README + banner disclaimer |
| T-02-30 | Spoofing (dev PEM commit to git) | 02-08 | .gitignore + README warning |
| T-02-31 | Tampering (sample copied to prod without reading) | 02-08 | accepted — GPL + README |

No `high` severity threats unmitigated.

---

## Wave 0 Requirements

- [x] `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` — unit project with xUnit + Moq (plan 02-01 Task 3 authors)
- [x] `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj` — integration project with Testcontainers + WireMock.Net + Mvc.Testing (plan 02-01 Task 3 authors)
- [x] `tests/GameKit.TestFixtures/WireMockFixture.cs` — IAsyncLifetime + Steam `is_valid:true/false` + Discord identify stubs (plan 02-01 Task 2 authors)
- [x] `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs` — composite Postgres + Redis + WireMock (plan 02-01 Task 2 authors)
- [x] `tests/GameKit.TestFixtures/CollectionDefinitions.cs` — `[CollectionDefinition("Auth")]` added (plan 02-01 Task 2 authors)
- [x] `Directory.Packages.props` — pins for `BCrypt.Net-Next 4.1.0`, `Microsoft.Extensions.Http.Resilience 10.5.0`, `AspNet.Security.OAuth.Discord 10.0.0`, `WireMock.Net 2.2.0`, `Microsoft.AspNetCore.Mvc.Testing 10.0.0`, `Microsoft.IdentityModel.Tokens 8.3.0`, `System.IdentityModel.Tokens.Jwt 8.3.0` (plan 02-01 Task 1 authors)
- [x] Smoke test: `dotnet test tests/GameKit.Auth.Integration.Tests --filter "Category=Smoke"` reports 4 passes (plan 02-01 Task 3 authors)

Wave 0 completes when plan 02-01 completes. All downstream plans depend on it.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| TicTacToeDuel sample: full auth flow (guest → password upgrade in place → refresh retry on expired access token → logout) in a real browser | sample-app demo (02-08-T3) | Browser-only localStorage interaction + 401-refresh-retry requires live network timing | 1) `./scripts/gen-test-rsa-pem.sh` 2) `docker compose up -d` 3) `dotnet run --project samples/TicTacToeDuel` 4) Open `http://localhost:5000/` 5) Click "Play as Guest" — confirm localStorage has `gk.access_token`, `gk.refresh_token`, `gk.device_id`; JWT has `is_guest: "true"` 6) Enter username/password, click "Register" — confirm same player_id in `sub` claim but `is_guest: "false"` (D-12 upgrade) 7) Truncate `gk.access_token` in DevTools; invoke an authenticated action; confirm silent refresh + retry 8) Click "Logout" — confirm localStorage cleared |
| README auth section clarity review | doc-05 (02-08-T3) | Human judgment on writing clarity and completeness | Operator reads `samples/TicTacToeDuel/README.md` auth section, confirms: localStorage/XSS disclaimer present, signing-key 0600 guidance present, `AllowedProviderHosts` customization snippet present, endpoint table updated |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references (plan 02-01 is the Wave-0 plan)
- [x] No watch-mode flags
- [x] Feedback latency < 150s (full) / < 15s (quick)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** ready for execution. Every AUTH-XX requirement is mapped to at least one task; every ROADMAP success criterion is mapped to at least one e2e / integration test; every STRIDE threat has a mitigation or documented acceptance.
