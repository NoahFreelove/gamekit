---
phase: 02-authentication
plan: 06
subsystem: authentication
tags: [authentication, guest, password, serializable, upgrade, link, collision]
requires:
  - 02-02-SUMMARY (PlayerIdentity + PlayerCredential entities + AuthInitial migration)
  - 02-04-SUMMARY (IPasswordHasher, IExternalIdHasher, IIsGuestResolver, IAuthAuditWriter, IRefreshTokenService)
  - 02-05-SUMMARY (IOAuthProvider contract, Scrutor assembly scan with publicOnly:false)
provides:
  - GuestOAuthProvider (AUTH-08 — guest login creates Player with no identities/credentials)
  - PasswordOAuthProvider (AUTH-09 — BCrypt-verified login + dummy-hash timing mitigation + RegisterAsync)
  - IIdentityLinker + IdentityLinker (AUTH-14 — SERIALIZABLE tx + 23505/40001 handling; cross-player collision → hash-bearing 409)
  - LinkResult + LinkResultKind (Linked / AlreadyLinkedToSelf / AlreadyLinkedToOtherPlayer)
  - IGuestUpgradeService + GuestUpgradeService (AUTH-13 — in-place guest→password upgrade; delegates to IdentityLinker for OAuth upgrade)
  - UsernameAlreadyTakenException (RESEARCH §15 open question #3 resolution)
affects:
  - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs (AddAuth wires IIdentityLinker + IGuestUpgradeService as Scoped)
  - tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs (expected IOAuthProvider count 2→4)
tech-stack:
  added: []
  patterns:
    - Postgres SERIALIZABLE isolation + UNIQUE constraint for the D-14 guest-upgrade race
    - Npgsql exception unwrap via TryFindPostgresException (chains through InvalidOperationException ← DbUpdateException ← PostgresException)
    - Timing-attack mitigation: dummy BCrypt.Verify on user-not-found (T-02-16)
    - IExternalIdHasher-based 409 response bodies (raw external_id never disclosed — T-02-10)
key-files:
  created:
    - src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs (92 lines)
    - src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs (238 lines)
    - src/GameKit.Auth/Services/LinkResult.cs (45 lines)
    - src/GameKit.Auth/Services/IIdentityLinker.cs (43 lines)
    - src/GameKit.Auth/Services/IdentityLinker.cs (196 lines)
    - src/GameKit.Auth/Services/IGuestUpgradeService.cs (73 lines)
    - src/GameKit.Auth/Services/GuestUpgradeService.cs (159 lines)
    - tests/GameKit.Auth.Integration.Tests/TestHelpers.cs (168 lines)
    - tests/GameKit.Auth.Integration.Tests/GuestProviderTests.cs (64 lines)
    - tests/GameKit.Auth.Integration.Tests/PasswordProviderTests.cs (106 lines)
    - tests/GameKit.Auth.Integration.Tests/GuestUpgradeServiceTests.cs (172 lines)
    - tests/GameKit.Auth.Integration.Tests/IdentityLinkerTests.cs (106 lines)
  modified:
    - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs (247 lines; added AddScoped<IIdentityLinker,…> + AddScoped<IGuestUpgradeService,…>)
    - tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs (expected provider count 2→4, added Guest + Password assertions)
decisions:
  - Npgsql exception unwrap uses a bounded InnerException-walker (TryFindPostgresException) rather than disabling the default retry execution strategy — keeps consumer-facing EF behavior intact
  - Guest externalId parameter is ignored (a guest has no provider-side id); each CompleteLoginAsync call mints a fresh Player
  - PasswordOAuthProvider reuses the IOAuthProvider contract's string params for login: externalId=username, displayName=password (enforced by /auth/login/password endpoint in plan 02-07)
  - DummyHash literal ("$2a$12$abcdefghijklmnopqrstuu…") is deliberately a valid BCrypt format so Verify runs its full work-factor-12 comparison (timing parity, T-02-16)
  - UsernameAlreadyTakenException surface is public (endpoints must catch it) — not a LinkResult-style enum because the password-upgrade and OAuth-link paths have asymmetric failure surfaces (one throws, the other returns)
metrics:
  duration: 10min
  tasks_completed: 3
  files_changed: 14
  tests_added: 9
  tests_total_green: 205
---

# Phase 2 Plan 6: Guest + Password providers, IdentityLinker, GuestUpgradeService Summary

## One-liner

Close the Phase 2 provider roster with Guest + Password providers (AUTH-08/09), add SERIALIZABLE-transactional IdentityLinker + GuestUpgradeService (AUTH-13/14), and prove ROADMAP success criteria #4 (concurrent guest-upgrade race) and #5 (cross-player identity collision) at the service-integration layer with 9 new tests against live Postgres.

## What Shipped

### Production code (7 files)

- **`GuestOAuthProvider`** (AUTH-08): creates a fresh `Player` row with no identities/credentials; `IIsGuestResolver` → `true`; issued JWT carries `is_guest=true` (D-13).
- **`PasswordOAuthProvider`** (AUTH-09):
  - `CompleteLoginAsync`: BCrypt verify on hit; dummy BCrypt verify against a canned hash on miss (T-02-16 timing-attack mitigation — closes the follow-up flagged in plan 02-04).
  - `RegisterAsync`: inserts Player + PlayerCredential; 23505 on UNIQUE(Username) returns `OAuthResult.Fail("username_taken")`.
- **`IIdentityLinker` + `IdentityLinker`** (AUTH-14): SERIALIZABLE-tx link with 3-attempt retry on 40001 and 23505 → `AlreadyLinkedToOtherPlayer` with SHA-256 hash (no raw external id in response body — T-02-10). Audit: `auth.identity.linked` + `auth.identity.link_failed_collision` (reason=`cross_player_collision`).
- **`LinkResult` + `LinkResultKind`**: enum discriminator + record carrying the optional `ExternalIdHash`. Three kinds: `Linked`, `AlreadyLinkedToSelf`, `AlreadyLinkedToOtherPlayer`.
- **`IGuestUpgradeService` + `GuestUpgradeService`** (AUTH-13, CONTEXT D-12):
  - `UpgradeToPasswordAsync`: SERIALIZABLE-tx insert of `PlayerCredential`; re-issues a non-guest root token; throws `UsernameAlreadyTakenException` on 23505.
  - `UpgradeToLinkedOAuthAsync`: thin delegation to `IIdentityLinker`.
- **`AddAuth` extension**: adds `AddScoped<IIdentityLinker, IdentityLinker>()` + `AddScoped<IGuestUpgradeService, GuestUpgradeService>()`. Guest + Password providers auto-discovered by the plan-02-05 Scrutor scan (publicOnly:false).

### Test code (5 files, 9 new test cases)

- **`TestHelpers.cs`** (168 lines): extracted `ApplyMigrations(connectionString)` + `BuildProvider(connectionString) → TestContext` shared by all four new test classes. `TestContext` manages the ephemeral RSA PEM directory via `IAsyncDisposable`. Reuses the FOLLOW-UP-02-03-01 `AuthRuntimeQueryCustomizer` workaround.
- **`GuestProviderTests`** (1 test): guest login creates Player, zero PlayerIdentity, zero PlayerCredential; JWT claims `is_guest=true`, `provider=guest`, `sub=player_id`.
- **`PasswordProviderTests`** (3 tests): register→login round-trip; wrong-password→`invalid_credentials`; unknown-username→`invalid_credentials` (exercises DummyHash verify path).
- **`GuestUpgradeServiceTests`** (3 tests):
  - Happy path: upgrade mints is_guest=false JWT, `auth.guest.upgraded_password` audit row written.
  - **Success #4**: two guests racing on the same Steam id → exactly 1 `Linked` + 1 `AlreadyLinkedToOtherPlayer`; exactly 1 row in `player_identities`; hash doesn't contain raw externalId.
  - **RESEARCH §15 open q #3**: two guests racing on the same username → 1 success + 1 `UsernameAlreadyTakenException`.
- **`IdentityLinkerTests`** (2 tests):
  - **Success #5**: serial cross-player collision → `AlreadyLinkedToOtherPlayer` with hash; 1 row in `player_identities` owned by Player A; `auth.identity.link_failed_collision` audit row exists with actor=PlayerB, reason=`cross_player_collision`.
  - Idempotent self-link: first call `Linked`, second call `AlreadyLinkedToSelf`.

## Scrutor IOAuthProvider discovery — final roster

Plan 02-05 registered Steam + Discord (2). Plan 02-06 adds Guest + Password (2). Total **4** `IOAuthProvider` implementations auto-registered as `Scoped` by the single `builder.Services.Scan(...)` call in `AddAuth`:

| Provider | File | `Provider` discriminator |
| --- | --- | --- |
| `SteamOAuthProvider` | `src/GameKit.Auth/Providers/Steam/SteamOAuthProvider.cs` | `"steam"` |
| `DiscordOAuthProvider` | `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` | `"discord"` |
| `GuestOAuthProvider` | `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs` | `"guest"` |
| `PasswordOAuthProvider` | `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` | `"password"` |

`ScrutorProviderDiscoveryTests.AddAuth_Registers_*` updated from asserting 2 → 4 registrations; added Guest + Password implementation-type assertions.

## New DI registrations in `AddAuth`

```csharp
builder.Services.AddScoped<IIdentityLinker, IdentityLinker>();
builder.Services.AddScoped<IGuestUpgradeService, GuestUpgradeService>();
```

Both Scoped (same DbContext lifetime as their callers). `GuestUpgradeService` depends on `IIdentityLinker`, so the Scoped lifetime also covers the delegation path.

## T-02-16 timing-attack mitigation — plan 02-04 follow-up CLOSED

Plan 02-04's summary flagged T-02-16 (password-login timing attack reveals whether a username exists) as a follow-up. This plan closes it:

- `PasswordOAuthProvider.DummyHash` is a canned BCrypt-format string.
- On user-not-found: `_ = _hasher.Verify(password, DummyHash)` runs the full work-factor-12 comparison so the wall-clock response time parity-matches the hit path.
- Structural check: `grep -c 'DummyHash' PasswordOAuthProvider.cs == 2` (declaration + one use).
- Functional check: `PasswordProviderTests.Unknown_Username_Returns_Invalid_Credentials` passes without leaking the account-existence signal via the `ErrorCode`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Npgsql execution-strategy exception wrap broke `DbUpdateException` catch**

- **Found during:** Task 3 — first run of `GuestUpgradeServiceTests.ConcurrentGuestLink_Same_Steam_Id_One_Succeeds_One_Collision` surfaced `System.InvalidOperationException: "An exception has been raised that is likely due to a transient failure."` wrapping `Microsoft.EntityFrameworkCore.DbUpdateException` wrapping `Npgsql.PostgresException (40001)`.
- **Issue:** The plan's catch clauses used `catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)`. Npgsql's default `NpgsqlExecutionStrategy` identifies transient failures (including `40001 serialization_failure`) and re-wraps them in `InvalidOperationException`. The `DbUpdateException` catch therefore never matched — the 40001 propagated as an unhandled `InvalidOperationException` out of `LinkAsync`, test fails instead of seeing the intended `AlreadyLinkedToOtherPlayer` via retry.
- **Fix:** Added `TryFindPostgresException(Exception? ex)` helper to each of `IdentityLinker`, `GuestUpgradeService`, and `PasswordOAuthProvider`. Walks `InnerException` chain (bounded depth 8) to find the `PostgresException` regardless of wrapper depth. Catch clauses widened from `DbUpdateException when ex.InnerException is PostgresException` to `Exception when TryFindPostgresException(ex) is { } pg`.
- **Files modified:** `src/GameKit.Auth/Services/IdentityLinker.cs`, `src/GameKit.Auth/Services/GuestUpgradeService.cs`, `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs`.
- **Commit:** `b476a2d`.

**2. [Rule 2 - Missing Coverage] ScrutorProviderDiscoveryTests didn't assert Guest/Password**

- **Found during:** Full-solution regression after Task 3.
- **Issue:** `ScrutorProviderDiscoveryTests.AddAuth_Registers_SteamAndDiscord_IOAuthProvider_Implementations` hard-coded `Assert.Equal(2, descriptors.Count)` with a docstring comment "Phase-2 plan 02-06 will extend this suite to cover Guest + Password." Plan 02-06 shipped those providers; the test would now regress without the update.
- **Fix:** Updated expected count 2→4 and added `Assert.Contains("GuestOAuthProvider", …)` + `Assert.Contains("PasswordOAuthProvider", …)`. This is the natural extension the original test author signposted.
- **Files modified:** `tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs`.
- **Commit:** `b476a2d`.

**3. [Rule 3 - Blocking] CS1574 XML forward-references on build (Task 1)**

- **Found during:** Task 1 first build.
- **Issue:** `GuestOAuthProvider.cs` and `PasswordOAuthProvider.cs` referenced `IGuestUpgradeService` via `<see cref=…>` in XML comments; that type lands in Task 2, so the Task 1 build failed with CS1574.
- **Fix:** Replaced `<see cref="IGuestUpgradeService.UpgradeToPasswordAsync"/>` etc. with plain `<c>…</c>` text spans in the affected comments. No production-behavior change.
- **Commit:** `5bb677a` (Task 1 commit after fix applied).

### Intentional variations from the plan sketch

- **Plan sketch said** `catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg)` — we widened to `catch (Exception ex) when (TryFindPostgresException(ex) is { } pg)` per Deviation #1 above.
- **Plan sketch said** `$"Guest-{playerId.ToString()[..8]}"` — we used `ToString("N")` to strip dashes (`Guest-01234567` vs `Guest-0123456` — consistent 8-char discriminant).
- **Plan sketch inline class body** in `GuestProviderTests.cs` used `sp.CreateAsyncScope()` directly; the actual `TestHelpers.BuildProvider` returns a `TestContext` wrapper so the temp PEM directory is deterministically cleaned up via `IAsyncDisposable`.

## Authentication Gates

None encountered. Auto-mode full-green.

## Test Counts per New Class

| Class | Count | Key Proofs |
| --- | --- | --- |
| `GuestProviderTests` | 1 | AUTH-08 + D-13 is_guest JWT claim |
| `PasswordProviderTests` | 3 | AUTH-09 register + login + wrong-pw + unknown-username |
| `GuestUpgradeServiceTests` | 3 | AUTH-13 happy path + **ROADMAP #4** concurrent link race + RESEARCH §15 username race |
| `IdentityLinkerTests` | 2 | **ROADMAP #5** cross-player collision + idempotent self-link |
| **Total** | **9** | |

## Full Test Suite

Pre-02-06: 196 green (130 Core unit + 35 Auth unit + 9 Core integration + 21 Auth integration + 1 CLI).
Post-02-06: **205 green** (+9 new tests; 130 Core unit + 35 Auth unit + 9 Core integration + 30 Auth integration + 1 CLI).

## Known Stubs

None. All implementations are production-ready:
- BCrypt work factor configurable via `PasswordOptions.WorkFactor` (future-dated); default 12 already used by `BCryptPasswordHasher` (inherited from plan 02-04).
- Audit payload JSON keys are stable and match RESEARCH §8.10.

## Threat Flags

None added beyond what the plan's `<threat_model>` already enumerates. T-02-16 (timing) is now mitigated (was accepted pre-02-04; plan 02-06 closes it). T-02-10 (raw external id disclosure) is mitigated via `IExternalIdHasher` in 409 bodies. T-02-22 (guest-upgrade race) is mitigated via SERIALIZABLE + UNIQUE + retry. T-02-23 (username race) is mitigated via citext UNIQUE + `UsernameAlreadyTakenException` wrapping.

## Self-Check: PASSED

- [x] `src/GameKit.Auth/Providers/Guest/GuestOAuthProvider.cs` — FOUND (92 lines)
- [x] `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` — FOUND (238 lines)
- [x] `src/GameKit.Auth/Services/LinkResult.cs` — FOUND (45 lines)
- [x] `src/GameKit.Auth/Services/IIdentityLinker.cs` — FOUND (43 lines)
- [x] `src/GameKit.Auth/Services/IdentityLinker.cs` — FOUND (196 lines)
- [x] `src/GameKit.Auth/Services/IGuestUpgradeService.cs` — FOUND (73 lines)
- [x] `src/GameKit.Auth/Services/GuestUpgradeService.cs` — FOUND (159 lines)
- [x] `tests/GameKit.Auth.Integration.Tests/TestHelpers.cs` — FOUND (168 lines)
- [x] `tests/GameKit.Auth.Integration.Tests/GuestProviderTests.cs` — FOUND (64 lines)
- [x] `tests/GameKit.Auth.Integration.Tests/PasswordProviderTests.cs` — FOUND (106 lines)
- [x] `tests/GameKit.Auth.Integration.Tests/GuestUpgradeServiceTests.cs` — FOUND (172 lines)
- [x] `tests/GameKit.Auth.Integration.Tests/IdentityLinkerTests.cs` — FOUND (106 lines)
- [x] Commit `5bb677a` (Task 1) — FOUND
- [x] Commit `d986406` (Task 2) — FOUND
- [x] Commit `b476a2d` (Task 3) — FOUND
- [x] All 9 new tests pass; 205/205 solution green
