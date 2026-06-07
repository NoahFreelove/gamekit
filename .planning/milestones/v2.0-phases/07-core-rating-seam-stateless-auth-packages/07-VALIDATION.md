---
phase: 7
slug: core-rating-seam-stateless-auth-packages
status: planned
nyquist_compliant: true
wave_0_complete: false
created: 2026-06-05
updated: 2026-06-05
---

# Phase 7 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11 (Postgres, integration only) + Moq + WireMock.Net 2.2.0 |
| **Config file** | none — new test projects added per plan (`tests/GameKit.Auth.Argon2.Tests`, `.Google.Tests`, `.Apple.Tests`, `.Epic.Tests`; Core seam tests in existing `tests/GameKit.Core.Tests`; rehash integration test in existing `tests/GameKit.Auth.Integration.Tests`) |
| **Quick run command** | `dotnet test GameKit.sln --filter "Category=Unit" --nologo` (or per-project `dotnet test <proj> --nologo`) |
| **Full suite command** | `dotnet test GameKit.sln --nologo` |
| **Estimated runtime** | unit < 30s per project; integration (Testcontainers Postgres + Docker) ~1-3 min |

---

## Sampling Rate

- **After every task commit:** Run the quick (unit) command for the affected project
- **After every plan wave:** Run the full suite command
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** ~30s (unit) / minutes (integration)

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 07-01-T1 | 07-01 | 1 | CORE-18 | T-07-01-01 | IPlayerRatingProvider compiles, fully XML-doc'd | Build | `dotnet build src/GameKit.Core/GameKit.Core.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-01-T2 | 07-01 | 1 | CORE-18 | T-07-01-01 | Null-object registered via TryAddSingleton (never throws on Core-only) | Build | `dotnet build src/GameKit.Core/GameKit.Core.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-01-T3 | 07-01 | 1 | CORE-18 | T-07-01-01 | Null-object returns empty dict; singleton registration | Unit | `dotnet test tests/GameKit.Core.Tests/GameKit.Core.Tests.csproj --filter "FullyQualifiedName~IPlayerRatingProviderTests" --nologo` | ❌ W0 | ⬜ pending |
| 07-02-T1 | 07-02 | 1 | AUTH-17, AUTH-18 | T-07-02-SC | CPM pins + sln entries for all 5 new packages (single-owner) | Grep gate | `grep -q 'Isopoh.Cryptography.Argon2' Directory.Packages.props && grep -q 'GameKit.Auth.Epic.csproj' GameKit.sln` | ❌ W0 | ⬜ pending |
| 07-02-T2 | 07-02 | 1 | AUTH-18 | — | IPasswordHasher.NeedsRehash added; BCrypt returns false | Build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-02-T3 | 07-02 | 1 | AUTH-17 | T-07-02-SC | Argon2 package builds; no NU1109 diamond downgrade | Build | `dotnet build src/GameKit.Auth.Argon2/GameKit.Auth.Argon2.csproj --nologo` (assert no NU1109) | ❌ W0 | ⬜ pending |
| 07-02-T4 | 07-02 | 1 | AUTH-17, AUTH-18 | T-07-02-01, T-07-02-02 | Hash $argon2id$ prefix; round-trip (Isopoh sig); BCrypt-compat verify; NeedsRehash discriminator; params ≥ OWASP | Unit (Wave 0) | `dotnet test tests/GameKit.Auth.Argon2.Tests/GameKit.Auth.Argon2.Tests.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-03-T1 | 07-03 | 2 | AUTH-19 | T-07-03-01 | GoogleOAuthProvider upserts (provider="google", external_id=sub) | Build | `dotnet build src/GameKit.Auth.Google/GameKit.Auth.Google.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-03-T2 | 07-03 | 2 | AUTH-19, AUTH-22 | T-07-03-01, T-07-03-03 | Self-register provider; conditional Google scheme; sub via NameIdentifier | Build | `dotnet build src/GameKit.Auth.Google/GameKit.Auth.Google.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-03-T3 | 07-03 | 2 | AUTH-19, AUTH-22 | T-07-03-03, T-07-03-04 | DI-smoke (Provider=="google", Scoped); conditional-scheme guard | Unit | `dotnet test tests/GameKit.Auth.Google.Tests/GameKit.Auth.Google.Tests.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-04-T1 | 07-04 | 2 | AUTH-20 | T-07-04-02 | AppleOAuthProvider (provider="apple", external_id=sub); first-login Metadata; no NU1109 | Build | `dotnet build src/GameKit.Auth.Apple/GameKit.Auth.Apple.csproj --nologo` (assert no NU1109) | ❌ W0 | ⬜ pending |
| 07-04-T2 | 07-04 | 2 | AUTH-20, AUTH-22 | T-07-04-01, T-07-04-02, T-07-04-03 | GenerateClientSecret=true; UsePrivateKey ES256; FindFirst("sub") | Build | `dotnet build src/GameKit.Auth.Apple/GameKit.Auth.Apple.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-04-T3 | 07-04 | 2 | AUTH-20, AUTH-22 | T-07-04-01, T-07-04-04, T-07-04-05 | ClientSecretExpiresAfter<180d; DI-smoke; conditional-scheme | Unit | `dotnet test tests/GameKit.Auth.Apple.Tests/GameKit.Auth.Apple.Tests.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-04-T4 | 07-04 | 2 | AUTH-20 | T-07-04-01, T-07-04-02 | Live Apple Sign-In: sub→external_id, first-login Metadata | Human-verify (external creds) | manual — `gate="blocking-human"` | ❌ W0 | ⬜ pending |
| 07-05-T1 | 07-05 | 2 | AUTH-21 | T-07-05-01 | Custom OAuthHandler; Basic-auth ExchangeCodeAsync; zero new NuGet dep | Build | `dotnet build src/GameKit.Auth.Epic/GameKit.Auth.Epic.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-05-T2 | 07-05 | 2 | AUTH-21, AUTH-22 | T-07-05-02, T-07-05-03 | EpicOAuthProvider (provider="epic", external_id=account_id); self-register; conditional scheme | Build | `dotnet build src/GameKit.Auth.Epic/GameKit.Auth.Epic.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-05-T3 | 07-05 | 2 | AUTH-21, AUTH-22 | T-07-05-01, T-07-05-04 | DI-smoke; conditional-scheme; Basic-auth token exchange vs WireMock | Unit (WireMock) | `dotnet test tests/GameKit.Auth.Epic.Tests/GameKit.Auth.Epic.Tests.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-05-T4 | 07-05 | 2 | AUTH-21 | T-07-05-05 | Live EOS exchange confirms Basic-auth method (form-body fallback documented) | Human-verify (external creds) | manual — `gate="blocking-human"` | ❌ W0 | ⬜ pending |
| 07-06-T1 | 07-06 | 2 | AUTH-18 | T-07-06-01, T-07-06-02 | Rehash-on-verify wired only on successful Verify; same-scope SaveChanges | Build | `dotnet build src/GameKit.Auth/GameKit.Auth.csproj --nologo` | ❌ W0 | ⬜ pending |
| 07-06-T2 | 07-06 | 2 | AUTH-18 | T-07-06-01, T-07-06-03 | BCrypt hash migrates to $argon2id$ on login (durable, re-read); BCrypt control unchanged | Integration (Testcontainers) | `dotnet test tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj --filter "FullyQualifiedName~ArgonRehashOnVerifyTests" --nologo` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Argon2 round-trip unit test (07-02-T4) — resolves RESEARCH open question A3 (`Argon2.Verify` signature/arg order for Isopoh 2.0.0: encoded hash is the first argument)
- [ ] `NeedsRehash` discriminator unit tests (07-02-T4) — BCrypt prefix ⇒ true on Argon2 hasher; Argon2 prefix ⇒ false; BCrypt hasher always false
- [ ] Epic token-endpoint auth-method handler shape unit-tested against a WireMock stub (07-05-T3) — Basic-auth wire format proven before the live EOS round-trip (open question A2)
- [ ] New test projects + Testcontainers/WireMock fixtures stood up for the new auth provider packages (07-02-T4, 07-03-T3, 07-04-T3, 07-05-T3, 07-06-T2)

*Existing v1 Testcontainers fixtures (AuthCollection / Postgres) are the reuse base for the rehash integration test (07-06-T2).*

---

## Manual-Only Verifications

| Behavior | Requirement | Task | Why Manual | Test Instructions |
|----------|-------------|------|------------|-------------------|
| Apple Sign-In live round-trip (real ES256 `.p8` client secret + `sub`→external_id) | AUTH-20 | 07-04-T4 | Needs a real Apple developer `.p8` key + Service ID (external credentials) | Configure Apple key/Service ID, perform sign-in, assert `player_identities` row keyed by `sub` with first-login-only Metadata |
| Epic OAuth live round-trip (token-endpoint auth method) | AUTH-21 | 07-05-T4 | Needs Epic EOS sandbox credentials; confirms Basic-vs-form-body (open question A2) | Configure EOS client, perform sign-in, confirm identity row + that the live endpoint accepts the Basic-auth header (else switch to form-body fallback) |

*Provider crypto/handler shape, conditional scheme registration, DI wiring, and identity-linking logic ARE automated (mocked principals / WireMock backchannel / handler-shape tests). Only live end-to-end provider round-trips require external credentials, and both are marked `autonomous: false` + `gate="blocking-human"` so the executor surfaces them rather than fabricating a pass.*

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or are explicit human-verify gates (07-04-T4, 07-05-T4)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify (the two human-verify tasks are the final task of their plan and follow automated tasks)
- [x] Wave 0 covers all MISSING references (all new test files created in their plans; Isopoh signature + Epic Basic-auth resolved by unit tests before downstream reliance)
- [x] No watch-mode flags
- [x] Feedback latency < 30s (unit)
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** planner-approved 2026-06-05 (pending checker review)
