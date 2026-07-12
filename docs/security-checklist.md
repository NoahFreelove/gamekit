# GameKit Security Checklist

<!-- SPDX-License-Identifier: Apache-2.0 -->

This document maps every security control in GameKit to the implementation file that enforces it and the test class that proves it. It covers the five surfaces audited in Phase 18: authentication (JWT), admin panel, rate limiting, egress air-gap, and GDPR delete completeness. A CVE supply-chain gate section and a full SEC-01..08 traceability table close the document.

**Audience:** GameKit consumers, security auditors, and contributors reviewing the Phase 18 security posture.

---

## 1. Threat Model Summary (STRIDE)

The table below condenses the threat register from `18-RESEARCH.md §Known Threat Patterns` into the STRIDE categories applicable to GameKit's four trust boundaries.

| # | STRIDE Category | Surface | Threat | Mitigation |
|---|-----------------|---------|--------|------------|
| T-01 | Tampering | JWT | `alg:none` / algorithm-downgrade forgery | `RequireSignedTokens=true` + RSA-SHA256 key validation in `TokenValidationParameters` |
| T-02 | Spoofing | JWT | Audience or issuer confusion (cross-tenant replay) | `ValidateAudience=true`, `ValidateIssuer=true` with configured `ValidAudience`/`ValidIssuer` |
| T-03 | Spoofing | JWT | Expired token replay | `ValidateLifetime=true` with configurable `ClockSkew` |
| T-04 | Elevation of Privilege | Refresh token | Revoked refresh token re-exchange | `RevokeFamilyAsync` sets `RevokedAt`; `/auth/refresh` checks hash against revoked rows → 401 |
| T-05 | Tampering | Admin panel | CSRF on state-mutating admin endpoints | `AntiforgeryValidationFilter` enforces `X-GameKit-Admin-CSRF` token on every admin POST/DELETE → 400 on mismatch |
| T-06 | Spoofing | Admin panel | Player JWT accepted on admin routes | `AdminPolicies` pins `GameKitAdmin` cookie scheme on every protected endpoint; Bearer → 404 in Production |
| T-07 | Information Disclosure | Egress | Outbound HTTP to non-OAuth hosts (SaaS telemetry, supply-chain) | `EgressAllowListHandler` DelegatingHandler on all named `HttpClient` instances; static CI grep gate |
| T-08 | Privacy / Compliance | GDPR delete | Incomplete deletion blocked by FK RESTRICT constraints | `IGdprDeleteExtension` hooks (Auth: `account_merges`; Matchmaking: `party_members`) run inside SERIALIZABLE transaction before `players` delete |
| T-09 | Privacy / Compliance | GDPR delete | PII orphaned in party_members when player is non-owner member | `MatchmakingGdprDeleteExtension` removes `party_members` rows before `players` delete |
| T-10 | Tampering | Supply chain | Vulnerable transitive NuGet package (CVE) | `NuGetAuditMode=all` + explicit CI vulnerability scan step; MessagePack 3.1.7 pin resolves GHSA-hv8m-jj95-wg3x |

---

## 2. JWT Security Controls

### Implementation

**File:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` (lines 190–210)

```csharp
jwt.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer           = true,
    ValidateAudience         = true,
    ValidateIssuerSigningKey = true,
    ValidateLifetime         = true,
    ValidIssuer              = opts.Jwt.Issuer,
    ValidAudience            = opts.Jwt.Audience,
    IssuerSigningKey         = validationKey,   // RSA public key (RS256)
    ClockSkew                = opts.Jwt.ClockSkew,
    RequireSignedTokens      = true,
};
jwt.MapInboundClaims = false;
```

**Signing:** `src/GameKit.Auth/Services/JwtIssuer.cs` — issues tokens signed with `SecurityAlgorithms.RsaSha256` (RSA-SHA256). The `JwtSecurityTokenHandler` enforces the signing algorithm from the key material, making symmetric-key (`alg:HS256`) and unsigned (`alg:none`) tokens structurally invalid at validation time.

### Controls Enforced

| Control | Property / Mechanism | Attack Prevented |
|---------|---------------------|-----------------|
| Algorithm enforcement | RSA signing key + `RequireSignedTokens=true` | `alg:none` forgery; HMAC downgrade |
| Issuer validation | `ValidateIssuer=true` + `ValidIssuer` | Cross-issuer confusion / token reuse |
| Audience validation | `ValidateAudience=true` + `ValidAudience` | Cross-service audience confusion |
| Lifetime enforcement | `ValidateLifetime=true` | Expired token replay |
| Refresh revocation | `RevokeFamilyAsync` sets `RevokedAt`; lookup by SHA-256 hash at exchange time | Stolen refresh token re-exchange |

### Tests

| Test Class | Location | Attacks Covered |
|------------|----------|-----------------|
| `JwtThreatModelTests` | `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs` | `alg:none` forgery, HMAC downgrade, wrong issuer, wrong audience, expired token (5 facts) |
| `RevokedRefreshExchangeTests` | `tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs` | Revoked refresh token → 401 `error=refresh_revoked`; never-issued token → 401 (2 facts, real Postgres) |

---

## 3. Admin Security Controls

### Implementation

| File | Responsibility |
|------|---------------|
| `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` | Defines `Admin` and `Superadmin` policy names; each policy pins `AddAuthenticationSchemes("GameKitAdmin")` |
| `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs` (lines 159–169) | Registers the `GameKitAdmin` cookie scheme; registers `AdminCookieEvents` that suppress the cookie challenge to 404 in Production (prevents route enumeration via 401) |
| `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` | All 14 endpoints; protected endpoints carry `[Authorize(Policy = AdminPolicies.Admin)]` or `[Authorize(Policy = AdminPolicies.Superadmin)]`; mutations also carry `AntiforgeryValidationFilter` |
| `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs` | Calls `IAntiforgery.ValidateRequestAsync`; returns `Results.BadRequest(new { error = "csrf_validation_failed" })` (HTTP 400) on `AntiforgeryValidationException` |

### Endpoint Authorization Inventory

| Endpoint | Policy | Antiforgery |
|----------|--------|-------------|
| `POST /admin/api/login` | AllowAnonymous | No |
| `POST /admin/api/logout` | AllowAnonymous | No |
| `POST /admin/login` (form) | AllowAnonymous | No |
| `POST /admin/login/submit` (form POST handler) | AllowAnonymous | No |
| `POST /admin/logout` (form) | AllowAnonymous | No |
| `GET /admin/api/players/search` | AdminPolicies.Admin | No |
| `POST /admin/api/players/{id}/ban` | AdminPolicies.Admin | Yes |
| `POST /admin/api/players/{id}/unban` | AdminPolicies.Admin | Yes |
| `POST /admin/api/players/{id}/gdpr-delete` | AdminPolicies.Superadmin | Yes |
| `POST /admin/api/players/merge` | AdminPolicies.Superadmin | Yes |
| `GET /admin/api/admins` | AdminPolicies.Superadmin | No |
| `POST /admin/api/admins` | AdminPolicies.Superadmin | Yes |
| `DELETE /admin/api/admins/{id}` | AdminPolicies.Superadmin | Yes |
| `GET /admin/api/audit` | AdminPolicies.Admin | No |
| `GET /admin/api/match-history` | AdminPolicies.Admin | No |
| `GET /admin/api/health` | AdminPolicies.Admin | No |
| `GET /admin/api/commands` | AdminPolicies.Admin | No |

### Controls Enforced

| Control | Mechanism | Attack Prevented |
|---------|-----------|-----------------|
| Cookie scheme isolation | `AdminPolicies` → `AddAuthenticationSchemes("GameKitAdmin")` | Player Bearer JWT accepted on admin routes |
| Challenge suppression | `AdminCookieEvents.OnRedirectToLogin` returns 404 in Production | Admin route enumeration via 401/302 |
| CSRF gate | `AntiforgeryValidationFilter` on all mutation endpoints | CSRF from attacker-controlled page |
| Role separation | `Admin` vs `Superadmin` policies on destructive ops | Privilege escalation by lower-role admins |

### Tests

| Test Class | Location | Controls Covered |
|------------|----------|-----------------|
| `AdminRouteAuthAuditTests` | `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs` | Route enumeration via `EndpointDataSource`: every `/admin/*` route either in anonymous allowlist or carries `Admin`/`Superadmin` policy; player JWT → non-200 on existing admin route (2 facts) |
| `CsrfRegressionTests` | `tests/GameKit.Admin.Integration.Tests/CsrfRegressionTests.cs` | Ban/unban/delete-admin mutations without antiforgery header → exactly HTTP 400 + body `"csrf_validation_failed"` (3 facts, real Postgres + Redis) |

---

## 4. Rate Limiting

### Implementation

| File | Responsibility |
|------|---------------|
| `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs` | Defines `AuthLogin`, `AuthRefresh`, `AuthRegister` sliding-window policies; partition key = IP + fingerprint |
| `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs` | Defines `AdminLogin` (5/min/IP sliding) and `AdminMerge` policies |
| `src/GameKit.Auth/Http/AuthEndpoints.cs` (lines 55–88) | Applies `RequireRateLimiting` to login, refresh, register endpoints |

### Policy Inventory

| Endpoint | Policy | Threshold | Partition Key | Rationale |
|----------|--------|-----------|---------------|-----------|
| `POST /auth/login/{provider}` | `AuthLogin` | 10 req/min | IP + fingerprint | Credential-stuffing prevention |
| `POST /auth/refresh` | `AuthRefresh` | 60 req/min | IP + fingerprint | Token-rotation DoS prevention |
| `POST /auth/register` | `AuthRegister` | 5 req/min | IP + fingerprint | Registration abuse prevention |
| `POST /admin/api/login` | `AdminLogin` | 5 req/min | IP | Admin credential-stuffing prevention |
| `POST /admin/api/players/merge` | `AdminMerge` | (configured) | IP | Merge-loop abuse prevention |

### Deliberate Exclusions

| Endpoint | Excluded From Rate Limiting | Reason |
|----------|----------------------------|--------|
| `POST /auth/logout` | Yes | RFC 7009: blocking logout would leave revocable tokens alive; requires Bearer so DoS surface is limited |
| `POST /auth/logout/all` | Yes | Requires Bearer; destructive only to the authenticated player's own session |
| `GET /auth/me` | Yes | Read-only; requires Bearer |
| `GET /auth/challenge/{provider}` | Yes | Redirects to OAuth provider; no game-state write |
| `GET /auth/callback/{provider}` | Yes | OAuth callback; rate-limit belongs at provider |
| `POST /auth/link/{provider}` | Yes | Requires Bearer; no unauthenticated surface |

### Tests

| Test Class | Location | Controls Covered |
|------------|----------|-----------------|
| `AuthRateLimitAuditTests` | `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs` | Structural: login/refresh/register have `EnableRateLimitingAttribute`; logout has no rate-limit attribute (RFC-7009 exclusion documented) (4 facts, unit, no containers) |

---

## 5. GDPR Delete Completeness

### FK Table Map

All tables with a `PlayerId` FK to `players`, their `OnDelete` behavior, and gap status:

| Table | FK Column | OnDelete | Behavior on Player Delete | Gap Pre-Fix | Fix |
|-------|-----------|----------|--------------------------|-------------|-----|
| `player_credentials` | `PlayerId` | Cascade | Row deleted automatically | None | — |
| `player_identities` | `PlayerId` | Cascade | Row deleted automatically | None | — |
| `refresh_tokens` | `PlayerId` | Cascade | All tokens deleted automatically | None | — |
| `player_ranks` | `PlayerId` | Cascade | Rank rows deleted automatically | None | — |
| `lobby_members` | `PlayerId` | Cascade | Member row deleted automatically | None | — |
| `decline_history` | `PlayerId` | Cascade | Row deleted automatically | None | — |
| `session_participants` | `PlayerId` | SetNull | `PlayerId → NULL` (tombstone row preserved) | None — intentional | — |
| `season_rank_archives` | `PlayerId` | SetNull | `PlayerId → NULL` | None — intentional | — |
| `pending_rating_updates` | `PlayerId` | SetNull | `PlayerId → NULL` | None — intentional | — |
| `lobbies` | `OwnerId` | SetNull | `OwnerId → NULL` (lobby survives) | None — intentional | — |
| `parties` | `OwnerPlayerId` | Cascade | Party deleted → cascades to `party_members` and nulls `matchmaking_tickets.PartyId` | None | — |
| `party_members` | `PlayerId` | **Restrict** | **Blocked player deletion for non-owner members** | **GAP 1** | `MatchmakingGdprDeleteExtension` |
| `account_merges` | `TargetPlayerId` | **Restrict** | **Blocked deletion of surviving merge target** | **GAP 2** | `AuthGdprDeleteExtension` |
| `players` (self) | `MergedIntoPlayerId` | SetNull | `MergedIntoPlayerId → NULL` | None — intentional | — |

### Fix Architecture (IGdprDeleteExtension)

**Interface:** `src/GameKit.Core/Services/IGdprDeleteExtension.cs` — defines `Task DeleteForPlayerAsync(GameKitDbContext ctx, Guid playerId, CancellationToken ct)`. Implementations MUST NOT open their own transactions or call `SaveChangesAsync` / `CommitAsync` — they run inside the caller's SERIALIZABLE transaction.

**Auth implementation:** `src/GameKit.Auth/Services/AuthGdprDeleteExtension.cs` — deletes `account_merges WHERE TargetPlayerId = playerId` (GAP 2 fix). Registered via `TryAddEnumerable(Scoped<IGdprDeleteExtension, AuthGdprDeleteExtension>)` in `AddAuth`.

**Matchmaking implementation:** `src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs` — deletes `party_members WHERE PlayerId = playerId` (GAP 1 fix; leaves the party intact for remaining members). Registered via `TryAddEnumerable(Scoped<IGdprDeleteExtension, MatchmakingGdprDeleteExtension>)` in `AddMatchmaking`.

**Service:** `src/GameKit.Core/Services/GdprDeleteService.cs` — `DeletePlayerAsync` accepts `IEnumerable<IGdprDeleteExtension>` and invokes each between the audit `SaveChangesAsync` and the `players.ExecuteDeleteAsync` call, all within the SERIALIZABLE transaction. `GameKit.Core` has zero upward references to Auth or Matchmaking.

### Tests

| Test Class | Location | Controls Covered |
|------------|----------|-----------------|
| `GdprDeleteCoverageTests` | `tests/GameKit.Core.Integration.Tests/GdprDeleteCoverageTests.cs` | Seeds player across every FK table; calls `DeletePlayerAsync`; asserts zero residual rows in all CASCADE/DELETE tables; RESTRICT tables cleaned by extensions; SET NULL tombstones preserved; bystander player and party survive (integration, real Postgres) |

---

## 6. Egress Controls

### Implementation

| File | Responsibility |
|------|---------------|
| `src/GameKit.Auth/Egress/EgressAllowListHandler.cs` | `DelegatingHandler` — throws `EgressViolationException` for any outbound host not in `AllowedProviderHosts` |
| `src/GameKit.Auth/Egress/DefaultAllowedHosts.cs` | Default allow-list: `steamcommunity.com`, `api.steampowered.com`, `discord.com`, `discordapp.com` |
| `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` (lines 76–84) | Wires `EgressAllowListHandler` on `gamekit.auth.provider.steam` and `gamekit.auth.provider.discord` named HTTP clients |
| `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs` | Appends `appleid.apple.com` to `AllowedProviderHosts`; sets `apple.BackchannelHttpHandler = new EgressAllowListHandler(...)` (Phase 18-05 gap closure) |
| `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs` | Appends `oauth2.googleapis.com`, `www.googleapis.com`, `accounts.google.com` to `AllowedProviderHosts`; sets `google.BackchannelHttpHandler = new EgressAllowListHandler(...)` (Phase 18-05 gap closure) |

### Allowed Hosts by Provider

| Provider Package | Allowed Hosts | How Added |
|-----------------|--------------|-----------|
| Steam (built-in) | `steamcommunity.com`, `api.steampowered.com` | `DefaultAllowedHosts.All` |
| Discord (built-in) | `discord.com`, `discordapp.com` | `DefaultAllowedHosts.All` |
| Apple (`GameKit.Auth.Apple`) | `appleid.apple.com` | `AppleBuilderExtensions.AppleProviderHosts` appended at `AddApple()` time |
| Google (`GameKit.Auth.Google`) | `oauth2.googleapis.com`, `www.googleapis.com`, `accounts.google.com` | `GoogleBuilderExtensions.GoogleProviderHosts` appended at `AddGoogle()` time |

Approach b was chosen for Apple/Google: each provider package appends its own hosts to `GameKitAuthOptions.AllowedProviderHosts` at registration time. If the provider package is not installed, its hosts are never in the allow-list — correct behavior.

### CI Static Gates

Two grep checks in `.github/workflows/ci.yml` (SEC-05 gate step):

1. **Bare `HttpClient` check:** Scans `src/**/*.cs` for `new HttpClient(` lines not in exempted egress-handler wiring files (`EgressAllowListHandler.cs`, `AppleBuilderExtensions.cs`, `GoogleBuilderExtensions.cs`). Fails CI if any match found.

2. **SaaS telemetry hostname check:** Scans `src/` and `samples/` for hardcoded SaaS OTLP/telemetry hostnames (`honeycomb.io`, `datadoghq.com`, `newrelic.com`, `grafana-cloud`, `grafana.net`, `lightstep.com`). Note: generic `otlp` and `otelcol` keywords are NOT in the ban list because the sample app legitimately references the self-hosted OpenTelemetry Collector (no phone-home).

### Tests

| Test Class | Location | Controls Covered |
|------------|----------|-----------------|
| `EgressAuditTests` | `tests/GameKit.Auth.Tests/EgressAuditTests.cs` | Handler rejects non-allowlisted hosts; allowlisted hosts pass; Apple/Google host lists contain expected values; static tree assertions (19 facts, unit, no containers) |

---

## 7. Refresh Token Security

### Implementation

**File:** `src/GameKit.Auth/Services/RefreshTokenService.cs`

```csharp
private static string Sha256Hex(string raw)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}
```

Call sites:
- `IssueRootAsync` (line 68): `TokenHash = Sha256Hex(raw)` — new root token stored as SHA-256 hex; raw value issued to client exactly once and never persisted.
- `RotateAsync` (line 98): `var hash = Sha256Hex(rawRefreshToken)` — lookup by hash for rotation.
- `RevokeFamilyAsync` (line 222): `var hash = Sha256Hex(rawRefreshToken)` — lookup by hash for revocation.

**Rotation:** On every `POST /auth/refresh`, the existing token is revoked and a new root token is issued. Reuse of a revoked token outside the configured grace window triggers `RevokeFamilyAsync` on the entire token family (theft-detection rotation).

### Controls Enforced

| Control | Mechanism | Attack Prevented |
|---------|-----------|-----------------|
| SHA-256 storage | `Sha256Hex()` at all issue/rotate/revoke call sites | Raw token exposure from DB read or backup |
| Single-issue raw value | Raw token returned once, never stored | Replay from DB exfiltration |
| Token rotation | New root token issued on every refresh exchange | Long-lived stolen token reuse |
| Family revocation | `RevokeFamilyAsync` revokes all descendant tokens | Theft-detection via out-of-order reuse |

### Tests

| Test Class | Location | Controls Covered |
|------------|----------|-----------------|
| `RefreshTokenHashingTests` | `tests/GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs` | `IssueRootAsync` stores SHA-256 hex (64-char, `^[0-9a-f]{64}$`), not the raw token; `RotateAsync` stores SHA-256 hex for the child; no string column holds the raw token literal (3 facts, real Postgres) |

---

## 8. CVE Gate

### Gate Configuration

**`Directory.Build.props`** (added in Phase 18-01):

```xml
<!-- SEC-07 (Phase 18-01): NuGet supply-chain audit gate. -->
<NuGetAuditMode>all</NuGetAuditMode>
<NuGetAuditLevel>high</NuGetAuditLevel>
```

`NuGetAuditMode=all` causes `dotnet restore` to scan both direct and transitive dependencies (not just top-level packages). `NuGetAuditLevel=high` fails the restore step on any High or Critical advisory.

**CI step in `.github/workflows/ci.yml`** (added in Phase 18-01):

```yaml
- name: Vulnerability scan (SEC-07 gate)
  run: |
    dotnet list package --vulnerable --include-transitive 2>&1 | tee /tmp/vuln-report.txt
    if grep -qiE '^\s*(High|Critical)' /tmp/vuln-report.txt; then
      echo "ERROR: HIGH or CRITICAL vulnerability found in dependency graph:"
      exit 1
    fi
```

This explicit step provides a CI build artifact (`/tmp/vuln-report.txt`) independent of the MSBuild property gate.

### MessagePack 3.1.7 Transitive Pin

**Advisory resolved:** GHSA-hv8m-jj95-wg3x (HIGH severity — `MessagePack` 2.5.187).

**Dependency chain:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8` → `MessagePack 2.5.187` (transitive). This advisory affected 15 projects in the solution.

**Fix:** `Directory.Packages.props` pins `MessagePack` to `3.1.7` and `MessagePack.Annotations` to `3.1.7`. `CentralPackageTransitivePinningEnabled=true` (already present) propagates the pin to all transitive uses.

```xml
<!-- SEC-07 (Phase 18-01): Transitive pin to resolve GHSA-hv8m-jj95-wg3x. -->
<PackageVersion Include="MessagePack" Version="3.1.7" />
<PackageVersion Include="MessagePack.Annotations" Version="3.1.7" />
```

**This is an upgrade, NOT a suppression.** The advisory is eliminated by upgrading to a version that does not contain the vulnerability. No `NuGetAuditSuppress` entries exist in `Directory.Packages.props`. The pin is auditable in version control.

**Obsoleted workaround:** The MEMORY.md note "Pre-existing MessagePack NU1903" and the build instruction `-p:NuGetAudit=false` are obsolete. Do not use `-p:NuGetAudit=false` in any build or CI command after Phase 18-01.

### Verification

```bash
# Confirm no HIGH/CRITICAL advisories
dotnet list package --vulnerable --include-transitive

# Confirm clean build with audit gate active (no -p:NuGetAudit=false)
dotnet restore GameKit.sln
dotnet build GameKit.sln --configuration Release -warnaserror
```

---

## 9. Requirement Traceability Table

Every SEC requirement from Phase 18 mapped to its implementation file(s) and the test class that guards it.

| Requirement | Description | Implementation File(s) | Test Class | Test Project | Status |
|-------------|-------------|----------------------|------------|-------------|--------|
| SEC-01 | JWT: reject `alg:none` / algorithm-downgrade / wrong audience / wrong issuer / expired token | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` (lines 190–210) | `JwtThreatModelTests` | `GameKit.Auth.Tests` | Done |
| SEC-01 | JWT: reject revoked refresh token exchange | `src/GameKit.Auth/Services/RefreshTokenService.cs` (RevokeFamilyAsync, line 222) | `RevokedRefreshExchangeTests` | `GameKit.Auth.Integration.Tests` | Done |
| SEC-02 | Admin: every `/admin/*` route requires `GameKitAdmin` cookie scheme; player JWT → non-200 | `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs`; `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` | `AdminRouteAuthAuditTests` | `GameKit.Admin.Integration.Tests` | Done |
| SEC-03 | Rate limiting: login / refresh / register endpoints carry enforced policies; logout exclusion documented | `src/GameKit.Auth/Http/AuthEndpoints.cs`; `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs` | `AuthRateLimitAuditTests` | `GameKit.Auth.Tests` | Done |
| SEC-04 | GDPR delete completeness: all FK tables covered; `party_members` and `account_merges` RESTRICT gaps fixed | `src/GameKit.Core/Services/GdprDeleteService.cs`; `src/GameKit.Core/Services/IGdprDeleteExtension.cs`; `src/GameKit.Auth/Services/AuthGdprDeleteExtension.cs`; `src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs` | `GdprDeleteCoverageTests` | `GameKit.Core.Integration.Tests` | Done |
| SEC-05 | Egress: no outbound HTTP beyond OAuth provider hosts; Apple/Google backchannel wired through `EgressAllowListHandler` | `src/GameKit.Auth/Egress/EgressAllowListHandler.cs`; `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs`; `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs` | `EgressAuditTests` | `GameKit.Auth.Tests` | Done |
| SEC-05 | Egress: static CI grep gate — no bare `new HttpClient(` in `src/`; no hardcoded SaaS telemetry hostnames | `.github/workflows/ci.yml` (SEC-05 gate step) | CI grep step (no test class) | CI | Done |
| SEC-06 | Refresh token: stored as SHA-256 hex; raw token never persisted | `src/GameKit.Auth/Services/RefreshTokenService.cs` (`Sha256Hex`, lines 280–284) | `RefreshTokenHashingTests` | `GameKit.Auth.Integration.Tests` | Done |
| SEC-06 | CSRF: admin mutations without antiforgery token → exactly HTTP 400 | `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs` | `CsrfRegressionTests` | `GameKit.Admin.Integration.Tests` | Done |
| SEC-07 | CVE gate: `NuGetAuditMode=all` blocks HIGH/CRITICAL advisories; MessagePack 3.1.7 pin removes GHSA-hv8m-jj95-wg3x | `Directory.Build.props`; `Directory.Packages.props`; `.github/workflows/ci.yml` | Build CI gate (`dotnet restore -warnaserror`) | CI | Done |
| SEC-08 | Security checklist: threat model → implementation → test traceability document | `docs/security-checklist.md` (this file) | Manual review per `18-VALIDATION.md` | N/A | Done |

---

*Generated by Phase 18-06 (SEC-08). Last updated: 2026-06-23.*
