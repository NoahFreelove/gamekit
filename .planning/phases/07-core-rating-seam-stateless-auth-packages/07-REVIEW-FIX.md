---
phase: 07-core-rating-seam-stateless-auth-packages
fixed_at: 2026-06-05T00:00:00Z
review_path: .planning/phases/07-core-rating-seam-stateless-auth-packages/07-REVIEW.md
iteration: 1
findings_in_scope: 6
fixed: 6
skipped: 0
status: all_fixed
---

# Phase 07: Code Review Fix Report

**Fixed at:** 2026-06-05
**Source review:** `.planning/phases/07-core-rating-seam-stateless-auth-packages/07-REVIEW.md`
**Iteration:** 1

**Summary:**
- Findings in scope: 6
- Fixed: 6
- Skipped: 0

**Build result:** `dotnet build GameKit.sln` — 0 warnings, 0 errors
**Test results:** All affected unit test projects passed (65 tests total across 5 test assemblies)

---

## Fixed Issues

### CR-01: Timing-oracle — dummy BCrypt hash replaced with valid 60-char hash

**Files modified:** `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs`, `tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs`
**Commit:** `2e66b9b`
**Applied fix:** Replaced the 59-character malformed `DummyHash` constant with a real BCrypt work-factor-12 hash generated from `BCrypt.Net.BCrypt.HashPassword("gamekit-dummy-password-never-matches-7f3k9m", 12)` — output is exactly 60 characters and `BCrypt.Verify("x", DummyHash)` returns `false` without throwing `SaltParseException`. Added detailed comment explaining the CR-01 origin (pre-existing v1 defect since 02-03). Added two regression tests:
- `DummyHash_HasCorrectLength_60Chars` — asserts length == 60 via reflection
- `DummyHash_Verify_ReturnsFalse_WithoutThrowing` — asserts `BCrypt.Verify` returns false (not throws) against the constant
Both tests use `BindingFlags.NonPublic | BindingFlags.Static` reflection so they remain valid even if the field is ever renamed. Note: this is a logic-correctness fix verified by the two new tests.

### WR-01: Apple scheme guard extended to all four required credentials

**Files modified:** `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs`, `tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs`
**Commit:** `feb8bc6`
**Applied fix:** Extended the conditional-scheme guard in `AddApple()` to fail fast with `InvalidOperationException` at registration time when `TeamId` or `KeyId` are missing while `ServiceId` and `PrivateKeyBase64` are present. Each exception names the missing field in the message. Also removed the null-forgiving operators `teamId!` / `keyId!` (replaced with non-nullable locals since the guard ensures non-null). Added two regression tests:
- `AddApple_ThrowsInvalidOperationException_WhenTeamIdMissing`
- `AddApple_ThrowsInvalidOperationException_WhenKeyIdMissing`

### WR-02: OWASP 2025 parameter floor validation added to UseArgon2()

**Files modified:** `src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs`, `tests/GameKit.Auth.Argon2.Tests/Argon2HasherTests.cs`
**Commit:** `c6a8f1a`
**Applied fix:** Added validation after `configure?.Invoke(opts)` in `UseArgon2()` that throws `ArgumentOutOfRangeException` when `MemoryCost < 19456 KiB`, `TimeCost < 2`, or `Lanes < 1`. The `Lanes` property (the actual field name in `GameKitArgon2Options`) maps to the OWASP parallelism floor. Added three tests:
- `UseArgon2_ThrowsArgumentOutOfRangeException_WhenMemoryCostBelowOwaspMinimum`
- `UseArgon2_ThrowsArgumentOutOfRangeException_WhenTimeCostBelowOwaspMinimum`
- `UseArgon2_DefaultOptions_DoNotThrow`

### WR-03: RegisterAsync audit action corrected to "auth.register.failure"

**Files modified:** `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs`
**Commit:** `aea12fe`
**Applied fix:** Changed the audit action string in the UNIQUE(Username) collision handler from `"auth.login.failure"` to `"auth.register.failure"`. The collision occurs in `RegisterAsync` — this is a registration failure, not a login failure. Monitoring/alerting tools querying for login failures would previously over-count, and queries for registration failures would miss this event.

### IN-01: Length guard added to externalId[^6..] in three OAuth providers

**Files modified:** `src/GameKit.Auth.Google/Providers/Google/GoogleOAuthProvider.cs`, `src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs`, `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthProvider.cs`
**Commit:** `e0093ae`
**Applied fix:** Added `var suffix = externalId.Length >= 6 ? externalId[^6..] : externalId;` guard before the `$"…User-{externalId[^6..]}"` fallback display name construction in all three providers. In production all real external IDs are longer than 6 chars (Google sub ≈ 21 digits, Apple sub ≈ 30–50 chars, Epic account_id = 32 hex chars), but a test double or unexpected short sub would crash without this guard.

### IN-02: CORE-18 TryAddSingleton comment rewritten for Phase 8 correctness

**Files modified:** `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs`
**Commit:** `7f598d4`
**Applied fix:** Rewrote the comment to accurately state: `TryAddSingleton` is a no-op when the service is already registered; Phase 8 must use `RemoveAll<IPlayerRatingProvider>()` + `AddSingleton` (or plain `AddSingleton` which replaces), NOT `TryAddSingleton`. The previous comment contradicted itself by claiming "Rankings silently replaces it" after correctly stating "the first TryAdd wins the race".

---

## Skipped Issues

None — all 6 findings were fixed successfully.

---

_Fixed: 2026-06-05_
_Fixer: Claude (gsd-code-fixer)_
_Iteration: 1_
