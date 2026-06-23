---
phase: 18-security-audit
plan: "03"
subsystem: GameKit.Auth
tags: [security, jwt, threat-model, refresh-token, revocation, testing]
requirements: [SEC-01]
dependency_graph:
  requires: ["18-01"]
  provides: [jwt-threat-model-tests, revoked-refresh-exchange-tests]
  affects: [GameKit.Auth.Tests, GameKit.Auth.Integration.Tests]
tech_stack:
  added: []
  patterns:
    - JWT forgery via JwtSecurityTokenHandler.WriteToken (alg:none canonical 3-segment form)
    - ProductionParams() mirror pattern for unit-testing TokenValidationParameters
    - POST /auth/logout as RevokeFamilyAsync driver for integration revocation tests
key_files:
  created:
    - tests/GameKit.Auth.Tests/JwtThreatModelTests.cs
    - tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs
decisions:
  - "Drive revocation via POST /auth/logout (not direct service scope) to avoid modifying AuthTestHost and to exercise the real HTTP path"
  - "alg:none exception is SecurityTokenInvalidSignatureException (IDX10504: token has no signature), not SecurityTokenSignatureKeyNotFoundException"
  - "Logout endpoint returns 204 NoContent (not 200 OK)"
metrics:
  duration: "~30 minutes"
  completed: "2026-06-23"
  tasks_completed: 2
  tasks_total: 2
  files_changed: 2
status: complete
---

# Phase 18 Plan 03: SEC-01 JWT Threat-Model Tests Summary

Makes the GameKit JWT security posture a regression-gated CI invariant: RSA-SHA256 signing with RequireSignedTokens, ValidateIssuer/Audience/Lifetime rejecting every forgery class, and RevokeFamilyAsync blocking revoked refresh token exchange.

## What Was Built

### Task 1: JwtThreatModelTests (unit, no containers)

`tests/GameKit.Auth.Tests/JwtThreatModelTests.cs` — 5 forgery-rejection facts:

| Fact | Forgery | Exception Thrown |
|------|---------|-----------------|
| `AlgNone_Token_Is_Rejected` | `alg:none` via `WriteToken` (trailing-dot canonical form) | `SecurityTokenInvalidSignatureException` |
| `HmacDowngrade_Token_Is_Rejected` | HMAC-SHA256 signed vs RSA validator | `SecurityTokenException` (any) |
| `WrongIssuer_Token_Is_Rejected` | `iss=evil-issuer` | `SecurityTokenInvalidIssuerException` |
| `WrongAudience_Token_Is_Rejected` | `aud=evil-audience` | `SecurityTokenInvalidAudienceException` |
| `Expired_Token_Is_Rejected` | `exp = now - 1h`, ClockSkew=0 | `SecurityTokenExpiredException` |

All tests use `ProductionParams()` — a helper that mirrors `AuthBuilderExtensions.cs` lines 199-210 exactly (ValidateIssuer/Audience/IssuerSigningKey/Lifetime=true, RequireSignedTokens=true, ClockSkew=TimeSpan.Zero for determinism).

### Task 2: RevokedRefreshExchangeTests (integration, Testcontainers Postgres)

`tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs` — 2 facts:

| Fact | Scenario | Expected Response |
|------|----------|------------------|
| `Revoked_RefreshToken_Cannot_Be_Exchanged` | Guest login → POST /auth/logout (RevokeFamilyAsync) → POST /auth/refresh with revoked raw token | 401 + `error=refresh_revoked` |
| `NeverIssued_RefreshToken_Returns_401` | Random never-issued token at POST /auth/refresh | 401 (indistinguishable from revoked) |

Uses `AuthTestHost` (real `WebApplicationFactory` + Postgres + WireMock). Revocation driven through the HTTP logout endpoint — no service-layer scope bypasses needed.

## Test Results

```
GameKit.Auth.Tests (full suite):
  Passed: 42, Failed: 0, Skipped: 0

GameKit.Auth.Integration.Tests (full suite):
  Passed: 48, Failed: 0, Skipped: 0
```

Targeted filter runs:
```
dotnet test GameKit.Auth.Tests --filter "FullyQualifiedName~JwtThreatModel" → 5/5 passed
dotnet test GameKit.Auth.Integration.Tests --filter "FullyQualifiedName~RevokedRefreshExchange" → 2/2 passed
```

## Threat Model Coverage

| Threat ID | Category | Forgery / Attack | Control | Test |
|-----------|----------|------------------|---------|------|
| T-18-03-01 | Tampering | alg:none / algorithm-downgrade JWT | RequireSignedTokens=true + RSA key | `AlgNone_Token_Is_Rejected`, `HmacDowngrade_Token_Is_Rejected` |
| T-18-03-02 | Spoofing | Wrong issuer/audience confusion | ValidateIssuer/Audience=true | `WrongIssuer_Token_Is_Rejected`, `WrongAudience_Token_Is_Rejected` |
| T-18-03-03 | Spoofing | Expired token replay | ValidateLifetime=true | `Expired_Token_Is_Rejected` |
| T-18-03-04 | Elevation of Privilege | Revoked refresh token replay | RevokeFamilyAsync; /auth/refresh → 401 | `Revoked_RefreshToken_Cannot_Be_Exchanged`, `NeverIssued_RefreshToken_Returns_401` |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Wrong exception type for alg:none test**
- **Found during:** Task 1 first run
- **Issue:** Plan spec implied `SecurityTokenSignatureKeyNotFoundException`; the actual exception thrown by `JwtSecurityTokenHandler` for a no-signature token is `SecurityTokenInvalidSignatureException` (IDX10504: "token does not have a signature")
- **Fix:** Changed the `Assert.Throws<>` generic type to `SecurityTokenInvalidSignatureException` and updated the inline comment to explain why
- **Files modified:** `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs`
- **Commit:** 040be46

**2. [Rule 1 - Bug] Logout endpoint returns 204 NoContent, not 200 OK**
- **Found during:** Task 2 first run
- **Issue:** `POST /auth/logout` returns `Results.NoContent()` (204), not 200 OK
- **Fix:** Changed assertion from `HttpStatusCode.OK` to `HttpStatusCode.NoContent`
- **Files modified:** `tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs`
- **Commit:** f73a803

## Commits

| Hash | Task | Description |
|------|------|-------------|
| `040be46` | 1 | test(18-03): SEC-01 JwtThreatModelTests — forge & reject alg:none, downgrade, wrong aud/iss, expired |
| `f73a803` | 2 | test(18-03): SEC-01 RevokedRefreshExchangeTests — revoked & unknown refresh tokens return 401 |

## SEC-08 Checklist Mapping

For the `docs/security-checklist.md` traceability table:

| Requirement | Implementation File | Test File | Fact Names |
|-------------|---------------------|-----------|------------|
| SEC-01 (JWT forgery) | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs:199-210` | `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs` | `AlgNone_Token_Is_Rejected`, `HmacDowngrade_Token_Is_Rejected`, `WrongIssuer_Token_Is_Rejected`, `WrongAudience_Token_Is_Rejected`, `Expired_Token_Is_Rejected` |
| SEC-01 (revoked refresh) | `src/GameKit.Auth/Services/RefreshTokenService.cs:217` | `tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs` | `Revoked_RefreshToken_Cannot_Be_Exchanged`, `NeverIssued_RefreshToken_Returns_401` |

## Self-Check: PASSED

- `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs` — EXISTS, 5 facts
- `tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs` — EXISTS, 2 facts
- Commit `040be46` — EXISTS (`git log --oneline | grep 040be46`)
- Commit `f73a803` — EXISTS (`git log --oneline | grep f73a803`)
- Full suites green: Auth.Tests 42/42, Auth.Integration.Tests 48/48
