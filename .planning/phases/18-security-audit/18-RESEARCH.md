# Phase 18: Security Audit - Research

**Researched:** 2026-06-23
**Domain:** .NET 10 security invariant verification, CVE gating, GDPR completeness, JWT threat model
**Confidence:** HIGH

---

## Summary

Phase 18 is a test-and-gate phase: every security invariant that was implemented across phases 1–17 receives an automated test and a CI enforcement gate. The work is primarily writing tests against already-shipped code — very little production code changes are expected beyond (a) fixing any GDPR gaps discovered and (b) upgrading MessagePack to eliminate the known CVE. All required source files were read directly from the repository for this research.

The phase has eight requirements. The highest-priority work item is SEC-07 (CVE gate), because `NuGetAuditMode=all` cannot be turned on until MessagePack 2.5.187 is replaced. The confirmed fix is a one-line transitive pin in `Directory.Packages.props` upgrading MessagePack to 3.1.7. Once that pin is added, `dotnet restore` and `dotnet build` clean up across the entire solution without the `-p:NuGetAudit=false` workaround.

**Primary recommendation:** Do SEC-07 first (pin MessagePack 3.1.7, enable `NuGetAuditMode=all` in `Directory.Build.props`, update CI), then SEC-04 (fix GdprDeleteService gaps and write the completeness integration test), then SEC-01/SEC-02/SEC-03/SEC-06 (JWT/admin/CSRF/refresh-hash tests), then SEC-05 (egress static check), then SEC-08 (security checklist doc).

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| SEC-01 | JWT threat tests: reject `alg:none`/algorithm-downgrade, wrong audience/issuer, expired tokens, exchange of a revoked refresh token | JWT configuration confirmed in `AuthBuilderExtensions.cs` lines 193–204; JwtIssuer signs with RSA-SHA256; `RequireSignedTokens=true`; test strategy in §SEC-01 below |
| SEC-02 | Admin endpoint auth audit: every `/admin/*` route requires `GameKitAdmin` cookie; player JWT → 401/403 | Confirmed by `AdminPolicies` + `AdminBuilderExtensions`; existing `CrossSchemeIsolationTests.cs` shows the pattern; gaps identified in §SEC-02 |
| SEC-03 | Rate-limit audit: every public auth write endpoint has enforced policy | `AuthEndpoints.cs` confirms login/refresh/register all have `RequireRateLimiting`; logout/logout-all/me/challenge/callback/link identified as NON-rate-limited; enumeration test approach in §SEC-03 |
| SEC-04 | GDPR delete completeness: `DeletePlayerAsync` reaches ALL FK tables | Current `GdprDeleteService.cs` only calls `ExecuteDeleteAsync` on `players`; relies on DB-level CASCADE/SET NULL/RESTRICT; full gap analysis in §SEC-04 |
| SEC-05 | Egress audit: no outbound HTTP beyond OAuth provider hosts | `EgressAllowListHandler` already exists and enforces the list; static grep check + integration test approach in §SEC-05 |
| SEC-06 | Security-invariant regression: SHA-256 refresh token storage + admin CSRF gate returns 400 | `RefreshTokenService.cs` confirms `Sha256Hex()` applied on issue/rotate/revoke; `AntiforgeryValidationFilter.cs` confirms 400 on miss; test approach in §SEC-06 |
| SEC-07 | Dependency/CVE CI gate: `NuGetAuditMode=all` fails on high/critical CVEs | MessagePack 2.5.187 (GHSA-hv8m-jj95-wg3x) is the only blocking advisory; exact fix in §SEC-07 |
| SEC-08 | `docs/security-checklist.md` mapping threat model → implementation → test | Doc-only deliverable; outline in §SEC-08 |
</phase_requirements>

---

## Project Constraints (from CLAUDE.md)

- **Runtime:** .NET 10 (LTS); all packages must target `net10.0`
- **Testing:** xUnit + Testcontainers + Moq; integration tests use real Postgres + Redis via `[Collection("...")]` fixtures; unit tests use `[Trait("Category", "Integration")]` absent for fast-path and present for container tests
- **GPL:** no proprietary deps; every new dep must be GPL-compatible
- **Refresh token hashing:** CLAUDE.md invariant — never store raw tokens, always SHA-256 hash. `RefreshTokenService.Sha256Hex()` at lines 280–284 enforces this
- **No cloud egress:** zero outbound HTTP beyond configured OAuth hosts at runtime
- **XML doc comments on every public API:** any new public type/method needs `<summary>`
- **Test execution pattern:** Unit tests: `dotnet test --filter "Category!=Integration"`; Integration tests: `dotnet test --filter "Category=Integration"`. CI at `.github/workflows/ci.yml`

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| JWT validation | API / Backend (JwtBearer middleware) | — | `TokenValidationParameters` in `AuthBuilderExtensions`; tested via WebApplicationFactory + raw `JwtSecurityTokenHandler` forgery |
| Refresh token revocation | API / Backend (RefreshTokenService) | Database | Hash stored in `refresh_tokens`; revocation sets `RevokedAt`; `RevokeFamilyAsync` is the entry point |
| Admin cookie auth | Frontend Server (cookie middleware) | Authorization policies | `GameKitAdmin` scheme; `AdminPolicies.Admin/Superadmin` pin the scheme via `AddAuthenticationSchemes` |
| Antiforgery (CSRF) | API / Backend (endpoint filter) | Cookie | `AntiforgeryValidationFilter` runs before body deserialization on every admin mutation |
| Rate limiting | API / Backend (ASP.NET Core RateLimiter) | — | `AuthRateLimitRegistrations` + `AdminRateLimitRegistrations`; enforced at endpoint-level via `RequireRateLimiting` |
| GDPR delete | API / Backend (GdprDeleteService) | Database cascades | Service calls `ExecuteDeleteAsync` on `players`; child tables rely on DB-level CASCADE/SET NULL |
| Egress enforcement | API / Backend (EgressAllowListHandler) | — | `DelegatingHandler` on all named `HttpClient` instances in Auth; default allow-list in `DefaultAllowedHosts` |
| CVE gating | Build / CI | Directory.Packages.props | `NuGetAuditMode` property in MSBuild; advisory resolved by transitive pin |

---

## SEC-07: MessagePack CVE Resolution (HIGHEST PRIORITY)

### Advisory Details
[VERIFIED: `dotnet list package --vulnerable --include-transitive` output]

- **Package:** `MessagePack` 2.5.187
- **Advisory:** GHSA-hv8m-jj95-wg3x (HIGH severity)
- **Advisory URL:** `https://github.com/advisories/GHSA-hv8m-jj95-wg3x`
- **Projects affected:** `GameKit.Lobby`, `GameKit.Admin.UI`, `GameKit.Cli`, `GameKit.Matchmaking`, `TicTacToeDuel`, and all their downstream test projects (15 projects total fail `dotnet restore` when `NuGetAuditMode=all`)

### Dependency Chain
[VERIFIED: `dotnet nuget why GameKit.Lobby MessagePack` + `project.assets.json` inspection]

```
Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8
  └── MessagePack 2.5.187   ← VULNERABLE (GHSA-hv8m-jj95-wg3x)
      └── MessagePack.Annotations 2.5.187
```

`MessagePack/2.5.187` is a **transitive** dependency. No GameKit package lists it as a direct `PackageReference`. It enters via `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (version constraint: `>= 2.5.187`), which is already pinned at `10.0.8` in `Directory.Packages.props`.

### Fix: Transitive Pin to 3.1.7
[VERIFIED: `dotnet package search MessagePack` confirms 3.1.7 is latest; constraint `>= 2.5.187` satisfied by 3.x]

`Directory.Packages.props` already has `<CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>` (line 4). This means adding a `<PackageVersion>` entry for `MessagePack` forces the pinned version for all transitive resolutions.

**Exact change — add to `Directory.Packages.props` `<ItemGroup>`:**
```xml
<!-- SEC-07: Transitive pin to resolve GHSA-hv8m-jj95-wg3x (MessagePack 2.5.187 HIGH severity).
     Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8 requires >= 2.5.187;
     3.1.7 satisfies the constraint and is clean of the advisory.
     CentralPackageTransitivePinningEnabled=true propagates this pin to all transitive uses. -->
<PackageVersion Include="MessagePack" Version="3.1.7" />
<PackageVersion Include="MessagePack.Annotations" Version="3.1.7" />
```

**Enable the gate in `Directory.Build.props`:**
```xml
<!-- SEC-07: Enable NuGet audit mode for all packages. This gates builds on high/critical CVEs
     in GameKit's own dependency graph. Was previously suppressed via -p:NuGetAudit=false
     (pre-existing MEMORY.md note) because MessagePack 2.5.187 triggered NU1903.
     After pinning MessagePack 3.1.7, the gate runs clean. -->
<NuGetAuditMode>all</NuGetAuditMode>
<NuGetAuditLevel>high</NuGetAuditLevel>
```

**Update CI (`ci.yml`):** Remove any `-p:NuGetAudit=false` flags from `dotnet restore` / `dotnet build` steps. The current CI at `.github/workflows/ci.yml` does not use this flag (confirmed: `dotnet restore` and `dotnet build --no-restore --configuration Release -warnaserror`), so no CI change is needed beyond the `Directory.Build.props` addition.

**After this phase:** The `MEMORY.md` note "Pre-existing MessagePack NU1903" and the instruction to build affected packages with `-p:NuGetAudit=false` are **obsolete**. The verifier should confirm a clean `dotnet restore` without flags.

### Verification Command
```bash
dotnet restore /home/noah/Desktop/projects/gamekit/GameKit.sln
# Must complete with 0 NU1903 errors
dotnet build /home/noah/Desktop/projects/gamekit/GameKit.sln --configuration Release -warnaserror
# Must complete with 0 errors
```

---

## SEC-04: GDPR Delete Completeness Gap Analysis

### Current GdprDeleteService Behavior
[VERIFIED: `src/GameKit.Core/Services/GdprDeleteService.cs` read directly]

`DeletePlayerAsync` (lines 31–83):
1. Opens SERIALIZABLE transaction
2. Snapshots player state (SELECT AS NO TRACKING)
3. Writes audit row to `admin_audit_log` via `SaveChangesAsync`
4. Calls `ExecuteDeleteAsync` on `players` WHERE `Id = playerId`
5. Commits

**The service calls `ExecuteDeleteAsync` on exactly one table: `players`.**

It relies entirely on PostgreSQL's FK cascade/set-null/restrict behavior to propagate the deletion.

### Complete FK Table Map vs. DeletePlayerAsync

Every table with a `PlayerId` FK to `players`, with its `OnDelete` behavior:

[VERIFIED: all `*Configuration.cs` files read directly]

| Table | FK Column | OnDelete | Behavior on Player Delete | Gap? |
|-------|-----------|----------|--------------------------|------|
| `session_participants` | `PlayerId` | `SetNull` | PlayerId → NULL (row preserved) | No — intentional tombstone |
| `player_credentials` (Auth) | `PlayerId` | `Cascade` | Row deleted automatically | No |
| `player_identities` (Auth) | `PlayerId` | `Cascade` | Row deleted automatically | No |
| `refresh_tokens` (Auth) | `PlayerId` | `Cascade` | All tokens deleted automatically | No |
| `player_ranks` (Rankings) | `PlayerId` | `Cascade` | Rank rows deleted automatically | No |
| `season_rank_archives` (Rankings) | `PlayerId` | `SetNull` | PlayerId → NULL (row preserved) | No — intentional |
| `pending_rating_updates` (Rankings) | `PlayerId` | `SetNull` | PlayerId → NULL | No — intentional |
| `lobby_members` (Lobby) | `PlayerId` | `Cascade` | Member row deleted automatically | No |
| `lobbies` (Lobby) | `OwnerId` | `SetNull` | OwnerId → NULL (lobby survives) | No — intentional |
| `parties` (Matchmaking) | `OwnerPlayerId` | `Cascade` | **Party row DELETED** → cascades to `party_members` (Cascade) and `matchmaking_tickets.PartyId` (SetNull) | No |
| `party_members` (Matchmaking) | `PlayerId` | `Restrict` | **BLOCKS player deletion if player is a non-owner party member** | **GAP — see below** |
| `decline_history` (Matchmaking) | `PlayerId` | `Cascade` | Row deleted automatically | No |
| `account_merges` (Auth) | `TargetPlayerId` | `Restrict` | **BLOCKS deletion of the surviving player if a merge record exists** | **GAP — see below** |
| `players` (self) | `MergedIntoPlayerId` | `SetNull` | MergedIntoPlayerId → NULL (if source player of a merge was already in the table) | No — intentional |

### Identified GDPR Gaps

**GAP 1: `party_members.PlayerId` = RESTRICT**

`PartyMemberConfiguration` line 51: `OnDelete(DeleteBehavior.Restrict)`. This means if a player is a *non-owner member* of a party, `ExecuteDeleteAsync` on `players` will throw a Postgres FK violation (code 23503). The player row cannot be deleted at the DB level until the `party_members` row is removed first.

The current `GdprDeleteService` does not handle this case. If the player to be erased is a party member but not the party owner, the deletion will fail at the Postgres constraint layer and the transaction will roll back.

**Fix required:** Before `ExecuteDeleteAsync`, the service (or a pre-step in the plan) must remove the player from any parties they are a member of but do not own. Options:
- `ExecuteDeleteAsync` on `party_members WHERE PlayerId = playerId` (removes memberships)
- This leaves the party intact (correct behavior — the party owner or remaining members continue)
- This is sufficient because `parties.OwnerPlayerId` has CASCADE — if the player IS the owner, deleting `players` cascades to `parties`, which cascades to `party_members`

**GAP 2: `account_merges.TargetPlayerId` = RESTRICT**

`AccountMergeConfiguration` line 47: `OnDelete(DeleteBehavior.Restrict)`. If the player to be deleted is the *surviving player* (TargetPlayerId) of a completed merge, the `account_merges` row blocks the deletion. The `SourcePlayerId` column has NO FK (bare UUID), so deleting a source player is fine.

**Fix required:** Before `ExecuteDeleteAsync`, the service must check for `account_merges WHERE TargetPlayerId = playerId`. Options:
- Delete the `account_merges` rows for this player as target (safe — the merge record is historical and the source player is already tombstoned)
- Or reassign TargetPlayerId to NULL by adding a SET NULL migration (would require schema change)

The simpler path is to add explicit `ExecuteDeleteAsync` on `account_merges WHERE TargetPlayerId = playerId` before deleting the player row.

### Tables That Do NOT Reference `players` Directly

The following tables exist but have no direct FK to `players` (confirmed by reading all `*Configuration.cs` files):

- `matchmaking_tickets` — FK to `parties` and `game_sessions` only; no direct `PlayerId`
- `ticket_events` — FK to `matchmaking_tickets`
- `session_complete_idempotency` — FK to `game_sessions`
- `admin_users` — no player FK (admin_users is a separate identity store)
- `admin_audit_log` — `ActorId` and `TargetId` are bare Guids with no FK (intentional — audit rows must survive player deletion)

### Required GdprDeleteService Additions

The `DeletePlayerAsync` method in `src/GameKit.Core/Services/GdprDeleteService.cs` must gain two pre-delete steps (both inside the existing SERIALIZABLE transaction):

```csharp
// Step A: Remove player from parties they are a non-owner member of.
// (party_members.PlayerId has RESTRICT; must be removed before players delete)
await _ctx.Set<PartyMember>()
    .Where(pm => pm.PlayerId == playerId)
    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

// Step B: Remove account_merge tombstones where player is the surviving target.
// (account_merges.TargetPlayerId has RESTRICT)
await _ctx.Set<AccountMerge>()
    .Where(am => am.TargetPlayerId == playerId)
    .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
```

**However, GdprDeleteService lives in `GameKit.Core`** and `PartyMember` / `AccountMerge` are entities in `GameKit.Matchmaking` and `GameKit.Auth` respectively. `GameKit.Core` has no ProjectReference to those packages. Three options:

1. **Option A (recommended):** Add an `IGdprDeleteExtension` interface to `GameKit.Core` that Auth + Matchmaking packages register implementations of, and `GdprDeleteService` calls them in a loop before the main delete. This preserves the Core/package boundary.
2. **Option B:** Move the pre-delete cleanup into the admin-layer GDPR endpoint (`AdminEndpoints.GdprDeletePlayerAsync`), which has access to both DbContext and all entity types.
3. **Option C:** Add `GdprDeleteService` pre-delete hooks via `Func<GameKitDbContext, Guid, CancellationToken, Task>` delegates registered at startup.

Option A is most architecturally clean and matches the existing `IModelBuilderExtension` + `IMigrationReadinessReporter` patterns. The planner should choose which option to implement; this research documents all three.

### GDPR Completeness Test Strategy

Test home: `tests/GameKit.Core.Integration.Tests/GdprDeleteCompletenessTests.cs` (new file, extending the existing `GdprDeleteTombstoneTests.cs` pattern).

The test must use a full `ServiceCollection` that includes `AddGameKit`, `AddAuth`, `AddMatchmaking`, `AddLobby`, `AddRankings` so all entity types are registered in the shared `GameKitDbContext`. Seed a player across every FK table, call `DeletePlayerAsync`, then assert zero rows remain in all CASCADE/DELETE tables and expected NULL/preserved values in SET NULL tables.

```csharp
// Seed the player across: player_credentials, player_identities, refresh_tokens,
// party (as owner), party_members (as member of another party), lobby_members,
// player_ranks, decline_history, account_merges (as target), session_participants.
// Call DeletePlayerAsync.
// Assert: players=0, player_credentials=0, refresh_tokens=0, party (own)=0,
//         party_members (own party cascaded)=0, party_members (non-owner) DELETED BY FIX,
//         lobby_members=0, player_ranks=0, decline_history=0,
//         account_merges (as target) DELETED BY FIX,
//         session_participants PlayerId=NULL (SET NULL tombstone).
```

---

## SEC-01: JWT Threat Tests

### Existing JWT Configuration
[VERIFIED: `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 190–205]

```csharp
jwt.TokenValidationParameters = new TokenValidationParameters
{
    ValidateIssuer           = true,
    ValidateAudience         = true,
    ValidateIssuerSigningKey = true,
    ValidateLifetime         = true,
    ValidIssuer              = opts.Jwt.Issuer,
    ValidAudience            = opts.Jwt.Audience,
    IssuerSigningKey         = validationKey,    // RSA public key
    ClockSkew                = opts.Jwt.ClockSkew,
    RequireSignedTokens      = true,
};
jwt.MapInboundClaims = false;
```

**Algorithm:** RSA-SHA256 (`SecurityAlgorithms.RsaSha256`) set at signing time by `JwtIssuer.cs`. The `JwtSecurityTokenHandler` enforces the algorithm from the signing key, so `alg:none` attacks are blocked. However, the test must prove this explicitly.

### Existing JWT Tests
[VERIFIED: `tests/GameKit.Auth.Tests/JwtIssuerTests.cs` read directly]

Existing tests verify: issued token contains D-03 claims; validates with matching public key. **Missing:** alg:none forgery rejection; wrong audience/issuer rejection; expired token rejection; revoked refresh exchange.

### Test Strategy (Unit, no containers — add to `GameKit.Auth.Tests`)

**File:** `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs`

```csharp
// Test 1: alg:none token is rejected
// Forge a JWT with header {"alg":"none","typ":"JWT"} and no signature.
// Send to /auth/me (requires Bearer). Assert 401.
// Implementation: use JwtSecurityTokenHandler.WriteToken with SigningCredentials=null 
// and manually set alg:none via JwtHeader.

// Test 2: HMAC-signed token rejected (algorithm downgrade)
// Sign with HMAC-SHA256 (symmetric key) — the validator expects RSA.
// Assert 401.

// Test 3: wrong issuer token rejected
// Issue with Issuer="evil-issuer". Assert 401.

// Test 4: wrong audience token rejected
// Issue with Audience="evil-audience". Assert 401.

// Test 5: expired token rejected
// Issue with expiry in the past (clock set to future). Assert 401.

// Test 6: revoked refresh token exchange
// Seed a refresh token, revoke it (call RevokeFamilyAsync), 
// then call POST /auth/refresh with the revoked raw token.
// Assert 401 with body error="refresh_revoked".
```

For tests 1–5 (token forgery), use `WebApplicationFactory<TEntryPoint>` or `AuthTestHost` (already exists in integration tests). For test 6, requires real Postgres — use `[Collection("Auth")]` integration test in `GameKit.Auth.Integration.Tests`.

**How to forge alg:none in .NET:**
```csharp
var header = new JwtHeader(); // default alg is "none" if no SigningCredentials
header["alg"] = "none";
var payload = new JwtPayload(issuer: "gk", audience: "gk", 
    claims: new[] { new Claim("sub", playerId.ToString()) },
    notBefore: DateTime.UtcNow,
    expires: DateTime.UtcNow.AddHours(1));
var token = new JwtSecurityToken(header, payload);
var raw = new JwtSecurityTokenHandler().WriteToken(token);
// raw = "eyJ...<header>.<payload>." (no signature segment)
```

---

## SEC-02: Admin Endpoint Auth Audit

### Existing Coverage
[VERIFIED: `tests/GameKit.Admin.Integration.Tests/CrossSchemeIsolationTests.cs` + `CspAndAntiforgeryTests.cs`]

Existing tests prove: player JWT → 404 (Production), player JWT cannot get 200, antiforgery missing → 400. These tests cover the negative space correctly.

**Gap vs. SEC-02 requirement:** SEC-02 requires a **route enumeration test** — a test that walks `IEndpointDataSource.Endpoints` and asserts every `/admin/*` endpoint has the `GameKitAdmin` cookie scheme requirement. This is a structural assertion that prevents future endpoints from being added without the policy.

### Admin Endpoint Inventory
[VERIFIED: `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` read directly]

14 endpoints in `AdminEndpoints.Map()`:
- `POST /admin/api/login` — AllowAnonymous (correct)
- `POST /admin/api/logout` — AllowAnonymous (correct — RFC 7009 semantics)
- `GET /admin/api/players/search` — `AdminPolicies.Admin`
- `POST /admin/api/players/{id}/ban` — `AdminPolicies.Admin` + antiforgery
- `POST /admin/api/players/{id}/unban` — `AdminPolicies.Admin` + antiforgery
- `POST /admin/api/players/{id}/gdpr-delete` — `AdminPolicies.Superadmin` + antiforgery
- `POST /admin/api/players/merge` — `AdminPolicies.Superadmin` + antiforgery
- `GET /admin/api/admins` — `AdminPolicies.Superadmin`
- `POST /admin/api/admins` — `AdminPolicies.Superadmin` + antiforgery
- `DELETE /admin/api/admins/{id}` — `AdminPolicies.Superadmin` + antiforgery
- `GET /admin/api/audit` — `AdminPolicies.Admin`
- `GET /admin/api/match-history` — `AdminPolicies.Admin`
- `GET /admin/api/health` — `AdminPolicies.Admin`
- `GET /admin/api/commands` — `AdminPolicies.Admin`

Plus form endpoints (`POST /admin/login`, `POST /admin/logout`) from `AdminFormEndpoints` — also AllowAnonymous (correct).

### Route Enumeration Test Strategy

Add to `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs`:

```csharp
// Resolve IEndpointDataSource from the test host's service provider.
// Filter endpoints to those whose DisplayName or route pattern starts with "/admin".
// For each such endpoint that is NOT AllowAnonymous:
//   Assert: endpoint has IAuthorizeData with AuthenticationSchemes containing "GameKitAdmin".
// For AllowAnonymous endpoints:
//   Assert: endpoint is in the known-anonymous allowlist (login, logout).
// Fail the test if any /admin/* endpoint is NOT in one of these two buckets.

var datasource = host.Services.GetRequiredService<EndpointDataSource>();
var adminEndpoints = datasource.Endpoints
    .OfType<RouteEndpoint>()
    .Where(e => e.RoutePattern.RawText?.StartsWith("admin") == true 
             || e.RoutePattern.RawText?.StartsWith("/admin") == true);

var knownAnonymous = new HashSet<string> { "/admin/api/login", "/admin/api/logout", "/admin/login", "/admin/logout" };

foreach (var ep in adminEndpoints)
{
    if (knownAnonymous.Contains(ep.RoutePattern.RawText ?? "")) continue;
    var authMeta = ep.Metadata.OfType<IAuthorizeData>().ToList();
    Assert.True(authMeta.Count > 0, $"Endpoint {ep.RoutePattern.RawText} has no authorization metadata");
    // The AdminPolicies enum values resolve to policies that pin AddAuthenticationSchemes("GameKitAdmin")
    var policyNames = authMeta.Select(a => a.Policy).Where(p => p != null).ToHashSet();
    Assert.True(
        policyNames.Contains(AdminPolicies.Admin) || policyNames.Contains(AdminPolicies.Superadmin),
        $"Endpoint {ep.RoutePattern.RawText} uses an unexpected policy: {string.Join(", ", policyNames)}");
}
```

---

## SEC-03: Rate-Limit Audit

### Existing Rate Limits (VERIFIED)
[VERIFIED: `src/GameKit.Auth/Http/AuthEndpoints.cs` lines 55–88; `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs`]

**Auth endpoints with `RequireRateLimiting`:**
- `POST /auth/login/{provider}` → `policies.AuthLogin` (10/min/IP+fp)
- `POST /auth/refresh` → `policies.AuthRefresh` (60/min/IP+fp)
- `POST /auth/register` → `policies.AuthRegister` (5/min/IP+fp)

**Auth endpoints WITHOUT rate limiting:**
- `POST /auth/logout` — no `RequireRateLimiting` (idempotent, logout on expired token is intentional design)
- `POST /auth/logout/all` — no `RequireRateLimiting` (requires Bearer, so DoS is harder)
- `GET /auth/me` — no rate limit (GET, read-only, requires Bearer)
- `GET /auth/challenge/{provider}` — no rate limit
- `GET /auth/callback/{provider}` — no rate limit
- `POST /auth/link/{provider}` — no rate limit (requires Bearer)

Admin login: `POST /admin/api/login` → `AdminRateLimitRegistrations.AdminLoginPolicy` (5/min/IP sliding)
Admin merge: `POST /admin/api/players/merge` → `AdminRateLimitRegistrations.AdminMergePolicy`

### SEC-03 Scope

The requirement says "every public auth write endpoint (login, register, refresh, …)". The endpoints that write state and are publicly reachable without Bearer are:
- login, refresh, register — already rate-limited ✓
- logout — write (revocation), but intentionally unguarded (RFC 7009 design; if blocked, token can't be revoked)

The enumeration test should assert the three core write endpoints (login/refresh/register) are rate-limited and document logout as a deliberate exclusion. The test does NOT need to assert logout is rate-limited because the existing design justification (lines 67–72 in `AuthEndpoints.cs`) is correct.

### Rate-Limit Enumeration Test Strategy

Add to `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs` (unit test, no containers — reads endpoint metadata):

```csharp
// Build a minimal WebApplication (skip PEM registration).
// Resolve EndpointDataSource.
// For /auth/login/{provider}, /auth/refresh, /auth/register:
//   Assert IRateLimiterMetadata is present on the endpoint.
// For /auth/logout:
//   Assert no IRateLimiterMetadata (document this as intentional).
```

---

## SEC-06: Security-Invariant Regression

### SHA-256 Refresh Token Storage
[VERIFIED: `src/GameKit.Auth/Services/RefreshTokenService.cs` lines 280–284 + line 68, line 98]

```csharp
private static string Sha256Hex(string raw)
{
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    return Convert.ToHexString(bytes).ToLowerInvariant();
}
```

Called at:
- `IssueRootAsync` line 68: `TokenHash = Sha256Hex(raw)` — new token stored as hash
- `RotateAsync` line 98: `var hash = Sha256Hex(rawRefreshToken)` — lookup by hash
- `RevokeFamilyAsync` line 222: `var hash = Sha256Hex(rawRefreshToken)` — lookup by hash

**Test strategy (integration, requires Postgres):** After `IssueRootAsync`, query `refresh_tokens` directly and assert `TokenHash` length is 64 hex chars (SHA-256 output) and is NOT equal to the raw token that was returned to the caller. This can be added to `GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs` (new file).

### CSRF Gate
[VERIFIED: `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs`]

`AntiforgeryValidationFilter.InvokeAsync` calls `antiforgery.ValidateRequestAsync(context.HttpContext)` and returns `Results.BadRequest(new { error = "csrf_validation_failed" })` on `AntiforgeryValidationException`. Existing `CspAndAntiforgeryTests.cs` already has CSRF tests. The SEC-06 requirement adds a regression test confirming this returns exactly 400, not 403 or 401.

---

## SEC-05: Egress Audit

### Existing Enforcement
[VERIFIED: `src/GameKit.Auth/Egress/EgressAllowListHandler.cs` + `DefaultAllowedHosts.cs`]

`EgressAllowListHandler` is a `DelegatingHandler` attached to every named `HttpClient` registered in `AuthBuilderExtensions` (lines 76–84: `gamekit.auth.provider.steam` and `gamekit.auth.provider.discord`). It throws `EgressViolationException` for any host not in `AllowedProviderHosts`.

Default allowed hosts (`DefaultAllowedHosts.All`):
- `steamcommunity.com`
- `api.steampowered.com`
- `discord.com`
- `discordapp.com`

Apple (`AspNet.Security.OAuth.Apple`) and Google (`Microsoft.AspNetCore.Authentication.Google`) providers are in separate packages (`GameKit.Auth.Apple`, `GameKit.Auth.Google`). Those packages add their own `HttpClient` configurations. Research confirms they also route through named clients, but verification of whether `EgressAllowListHandler` is wired to those clients is needed during planning.

### Static Grep Check Strategy

SEC-05 requires a static check that no GameKit package makes outbound HTTP via `new HttpClient()` directly (bypassing the egress handler). The CI step can be a shell command:

```bash
# Fail if any src/ file constructs HttpClient directly
! grep -r "new HttpClient(" /home/noah/Desktop/projects/gamekit/src/ --include="*.cs" \
  --exclude-dir="obj" | grep -v "//.*new HttpClient\|AddHttpMessageHandler\|test" | grep -q .
```

The existing `EgressAllowListHandlerTests.cs` in `GameKit.Auth.Tests` already unit-tests the handler. The integration test needed for SEC-05 should prove that a request to a non-allowlisted host throws `EgressViolationException` — this is essentially already done by the handler unit tests.

**Additional static check:** grep for hardcoded SaaS OTLP URLs or telemetry endpoints in `src/` and `samples/`:
```bash
! grep -r "otlp\|otelcol\|collector\|honeycomb\|datadog\|newrelic\|grafana" \
  /home/noah/Desktop/projects/gamekit/src/ --include="*.cs" -i | grep -v "//" | grep -q .
```

---

## SEC-08: Security Checklist Document

**Location:** `docs/security-checklist.md` (create `docs/` directory if it does not exist)

**Sections:**
1. Threat model summary (STRIDE categories applied to auth/admin/GDPR/egress)
2. JWT security controls (alg:none prevention, audience/issuer validation, refresh rotation)
3. Admin security controls (cookie scheme isolation, antiforgery, role separation)
4. Rate limiting (policies, partition keys, thresholds)
5. GDPR delete completeness (table map, cascade/restrict behavior)
6. Egress controls (allow-list, named HTTP clients, default hosts)
7. Refresh token security (SHA-256 storage, rotation, family revocation)
8. CVE gate (NuGetAuditMode=all, MessagePack fix reference)
9. Traceability table: requirement → implementation file → test file

---

## Standard Stack

### Core (already in project — no new additions needed for SEC-01..08)
| Library | Version | Purpose | Note |
|---------|---------|---------|------|
| xUnit | 2.9.2 | Test framework | Already pinned |
| Testcontainers.PostgreSql | 4.11.0 | Real Postgres for GDPR/refresh tests | Already pinned |
| Testcontainers.Redis | 4.11.0 | Real Redis (if needed) | Already pinned |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.0 | `WebApplicationFactory` for JWT/endpoint tests | Already pinned |
| Moq | 4.20.72 | Mocks for IClock, IIdGenerator | Already pinned |
| System.IdentityModel.Tokens.Jwt | 8.14.0 | JWT forgery in tests (`JwtSecurityTokenHandler`) | Already pinned |

### New Package Addition
| Library | Version | Purpose | Legitimacy |
|---------|---------|---------|-----------|
| `MessagePack` (transitive pin) | **3.1.7** | Resolves GHSA-hv8m-jj95-wg3x | [VERIFIED: `dotnet package search MessagePack`; 290M+ downloads; neuecc/aarnott owners; no direct dep, transitive only] |

---

## Package Legitimacy Audit

| Package | Registry | Age | Downloads | Verdict | Disposition |
|---------|----------|-----|-----------|---------|-------------|
| MessagePack 3.1.7 | NuGet | ~8 yrs (neuecc package history) | 290M+ | OK | Approved — transitive pin only, fixes CVE |

No new runtime packages are added. The only registry change is a transitive pin for MessagePack.

---

## Architecture Patterns

### Recommended Test Project Placement

Follow existing conventions — do NOT create a new `GameKit.Security.Tests` project. Distribute tests to existing projects based on subject:

| SEC Requirement | Test File Location | Test Type |
|----------------|-------------------|-----------|
| SEC-01 (JWT forgery) | `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs` + `tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs` | Unit + Integration |
| SEC-02 (admin route enumeration) | `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs` | Integration |
| SEC-03 (rate-limit enumeration) | `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs` | Unit |
| SEC-04 (GDPR completeness) | `tests/GameKit.Core.Integration.Tests/GdprDeleteCompletenessTests.cs` | Integration |
| SEC-05 (egress static + integration) | CI grep step + `tests/GameKit.Auth.Tests/EgressAuditTests.cs` | Static CI + Unit |
| SEC-06 (SHA-256 + CSRF regression) | `tests/GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs` + existing `CspAndAntiforgeryTests.cs` | Integration |
| SEC-07 (CVE gate) | `Directory.Build.props` + `Directory.Packages.props` changes | Build |
| SEC-08 (checklist doc) | `docs/security-checklist.md` | Documentation |

### Existing Test Infra to Reuse

- `AuthTestHost` at `tests/GameKit.Auth.Integration.Tests/AuthTestHost.cs` — `WebApplicationFactory` setup with PEM generation, Postgres, WireMock
- `AdminTestHost` at `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs` — includes `SeedAdminAsync`
- `PostgresFixture` + `RedisFixture` at `tests/GameKit.TestFixtures/` — Testcontainers-backed, shared per `[Collection]`
- `FakePlayerJwtIssuer` at `tests/GameKit.Admin.Integration.Tests/Mocks/FakePlayerJwtIssuer.cs` — issues player JWTs for use in scheme isolation tests

### Pattern: JWT Forgery in Tests

```csharp
// alg:none forgery (no signature, no key):
var header = new JwtHeader(); // defaults alg to none
header["alg"] = "none";
var payload = new JwtPayload(
    issuer: "gk-test", audience: "gk-test",
    claims: [new Claim("sub", Guid.NewGuid().ToString())],
    notBefore: DateTime.UtcNow, expires: DateTime.UtcNow.AddHours(1));
var token = new JwtSecurityToken(header, payload);
var raw = new JwtSecurityTokenHandler().WriteToken(token);
// raw ends in "." (no signature segment)
```

### Pattern: Endpoint Metadata Assertion

```csharp
// From a WebApplicationFactory-based host:
var datasource = app.Services.GetRequiredService<EndpointDataSource>();
var ep = datasource.Endpoints.OfType<RouteEndpoint>()
    .First(e => e.RoutePattern.RawText == "admin/api/players/search");
var authMeta = ep.Metadata.OfType<IAuthorizeData>().Single();
Assert.Equal(AdminPolicies.Admin, authMeta.Policy);
```

### Pattern: Rate-Limiter Metadata Assertion

```csharp
// IRateLimiterMetadata is the marker placed by RequireRateLimiting(policyName)
var ep = datasource.Endpoints.OfType<RouteEndpoint>()
    .First(e => e.RoutePattern.RawText!.StartsWith("auth/login"));
Assert.Contains(ep.Metadata, m => m is IRateLimiterMetadata);
```

---

## Common Pitfalls

### Pitfall 1: JWT `alg:none` Tokens Have No Signature Segment
**What goes wrong:** Manually constructed `alg:none` tokens have the format `header.payload.` (trailing dot, empty signature). Some test helpers strip the trailing dot, producing a malformed 2-part token that is rejected for a different reason (malformed) than the security control (alg:none). Use `JwtSecurityTokenHandler.WriteToken` to get the canonical form.

**How to avoid:** Always write the full token with `WriteToken`, then assert the HTTP response is 401 AND the response body mentions the expected error.

### Pitfall 2: GDPR Completeness Test Needs Full Package Assembly
**What goes wrong:** The completeness test registers only `AddGameKit()` and misses entity types from Auth/Matchmaking/Lobby. The test then can't seed the FK tables and the test is vacuous.

**How to avoid:** Register ALL packages (`AddGameKit + AddAuth + AddMatchmaking + AddLobby + AddRankings`) in the test's `ServiceCollection`. The `GameKitDbContext` will then include all entity types through the `IModelBuilderExtension` pattern.

### Pitfall 3: PartyMember RESTRICT Blocks GDPR Delete in Tests
**What goes wrong:** If `GdprDeleteService` is not yet fixed and the completeness test seeds a `party_members` row for the player being deleted, the test throws a `Npgsql.PostgresException` (23503 FK violation) rather than completing. This masks the real gap.

**How to avoid:** Fix `GdprDeleteService` first (Wave 1 of the plan), then write the completeness test. The test should verify the fixed behavior.

### Pitfall 4: `NuGetAuditMode=all` Fails on All Projects, Not Just Affected Ones
**What goes wrong:** Adding `NuGetAuditMode=all` in `Directory.Build.props` without first pinning MessagePack 3.1.7 fails `dotnet restore` for ALL 15+ projects that transitively reference SignalR. This blocks the build entirely.

**How to avoid:** The plan must pin MessagePack 3.1.7 in `Directory.Packages.props` in the same commit as enabling `NuGetAuditMode=all`. These two changes are atomic — never enable the gate without the fix already in place.

### Pitfall 5: Admin Route Enumeration Test Depends on Host Startup
**What goes wrong:** `IEndpointDataSource` is only populated after `app.Build()` completes. If the test uses a `WebApplicationFactory` but accesses services before the host builds, endpoint metadata is empty.

**How to avoid:** Use `await host.Client.GetAsync("/")` to trigger the first request (which forces `app.Build()` to complete) before querying `IEndpointDataSource`.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JWT forgery for tests | Custom binary serializer | `JwtSecurityTokenHandler` + `JwtSecurityToken` | JwtSecurityTokenHandler produces the exact format the middleware parses |
| Rate-limiter metadata detection | Reflection on middleware pipeline | `IEndpointDataSource` + `IRateLimiterMetadata` | The standard marker placed by `RequireRateLimiting()` |
| CVE scanning | Custom NuGet scraper | `dotnet list package --vulnerable` + `NuGetAuditMode` | Built into .NET 10 SDK |
| Container-based auth test host | Hand-rolled `TestServer` config | `AuthTestHost` (already exists) | Re-use the existing `WebApplicationFactory` setup in `tests/GameKit.Auth.Integration.Tests/` |

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config files | `tests/*/Directory.Build.props` inherits from `Directory.Packages.props` |
| Quick run (unit) | `dotnet test --filter "Category!=Integration" --no-build` |
| Full suite | `dotnet test --configuration Release` |

### Phase Requirements to Test Map

| Req ID | Behavior | Test Type | Automated Command | New File? |
|--------|----------|-----------|-------------------|-----------|
| SEC-01 | Reject alg:none, wrong aud/iss, expired | Unit | `dotnet test --filter "Category!=Integration&FullyQualifiedName~JwtThreatModel" --no-build` | `GameKit.Auth.Tests/JwtThreatModelTests.cs` |
| SEC-01 | Reject revoked refresh exchange | Integration | `dotnet test --filter "Category=Integration&FullyQualifiedName~RevokedRefresh" --no-build` | `GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs` |
| SEC-02 | Admin route auth audit (enumeration) | Integration | `dotnet test --filter "Category=Integration&FullyQualifiedName~AdminRouteAuthAudit" --no-build` | `GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs` |
| SEC-03 | Auth rate-limit enumeration | Unit | `dotnet test --filter "Category!=Integration&FullyQualifiedName~AuthRateLimitAudit" --no-build` | `GameKit.Auth.Tests/AuthRateLimitAuditTests.cs` |
| SEC-04 | GDPR delete completeness | Integration | `dotnet test --filter "Category=Integration&FullyQualifiedName~GdprDeleteCompleteness" --no-build` | `GameKit.Core.Integration.Tests/GdprDeleteCompletenessTests.cs` |
| SEC-05 | Egress handler rejects non-allowlisted hosts | Unit | `dotnet test --filter "Category!=Integration&FullyQualifiedName~EgressAudit" --no-build` | `GameKit.Auth.Tests/EgressAuditTests.cs` |
| SEC-05 | Static grep: no `new HttpClient(` in src/ | CI | Shell step in `.github/workflows/ci.yml` | CI step (no test file) |
| SEC-06 | Refresh token stored as SHA-256 hash | Integration | `dotnet test --filter "Category=Integration&FullyQualifiedName~RefreshTokenHashing" --no-build` | `GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs` |
| SEC-06 | CSRF missing → 400 | Integration | Existing `CspAndAntiforgeryTests.cs` already covers this | Existing |
| SEC-07 | Clean build with NuGetAuditMode=all | Build CI | `dotnet restore && dotnet build -warnaserror` | `Directory.Build.props` + `Directory.Packages.props` |
| SEC-08 | Security checklist doc exists | Manual | N/A | `docs/security-checklist.md` |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "Category!=Integration" --no-build -p:NuGetAudit=false` (until SEC-07 is applied; after SEC-07 drop the flag)
- **Per wave merge:** `dotnet test --no-build`
- **Phase gate:** Full suite green before `/gsd-verify-work`

### Wave 0 Gaps (new files needed)
- [ ] `tests/GameKit.Auth.Tests/JwtThreatModelTests.cs`
- [ ] `tests/GameKit.Auth.Integration.Tests/RevokedRefreshExchangeTests.cs`
- [ ] `tests/GameKit.Admin.Integration.Tests/AdminRouteAuthAuditTests.cs`
- [ ] `tests/GameKit.Auth.Tests/AuthRateLimitAuditTests.cs`
- [ ] `tests/GameKit.Core.Integration.Tests/GdprDeleteCompletenessTests.cs`
- [ ] `tests/GameKit.Auth.Tests/EgressAuditTests.cs`
- [ ] `tests/GameKit.Auth.Integration.Tests/RefreshTokenHashingTests.cs`
- [ ] `docs/security-checklist.md`
- [ ] `src/GameKit.Core/Services/GdprDeleteService.cs` — modify (add pre-delete steps or extension hook)

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes | JwtBearer + BCrypt/Argon2 + cookie scheme |
| V3 Session Management | yes | Refresh token rotation with SHA-256 storage; family revocation |
| V4 Access Control | yes | AdminPolicies with scheme pinning; RequireAuthorization |
| V5 Input Validation | yes | FluentValidation on all DTOs |
| V6 Cryptography | yes | RSA-SHA256 for JWT signing; SHA-256 for refresh token storage; BCrypt for admin password |
| V7 Error Handling | partial | AdminCookieEvents → 404 in Production; Auth returns structured `AuthErrorResponse` |
| V9 Communications | partial | Egress allow-list via `EgressAllowListHandler`; no HTTPS enforcement at library level |
| V13 API and Web Service | yes | Rate limiting via ASP.NET Core RateLimiter |

### Known Threat Patterns

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| `alg:none` JWT attack | Tampering | `RequireSignedTokens=true` + RSA key validation |
| JWT audience/issuer confusion | Spoofing | `ValidateAudience=true`, `ValidateIssuer=true` |
| Refresh token theft + replay | Elevation of privilege | SHA-256 storage; family revocation on reuse outside grace window |
| CSRF on admin mutations | Tampering | `AntiforgeryValidationFilter` on all admin POST/DELETE |
| Admin panel enumeration via JWT | Spoofing | `AdminPolicies` pins `GameKitAdmin` scheme; Bearer yields 404 |
| Egress to SaaS telemetry | Info disclosure | `EgressAllowListHandler`; static grep CI check |
| Vulnerable transitive dep in supply chain | Tampering | `NuGetAuditMode=all` CI gate + MessagePack 3.1.7 pin |
| GDPR incomplete delete (party_members RESTRICT) | Privacy / compliance | Pre-delete `ExecuteDeleteAsync` on `party_members` |
| GDPR incomplete delete (account_merges RESTRICT) | Privacy / compliance | Pre-delete `ExecuteDeleteAsync` on `account_merges` |

---

## Environment Availability

| Dependency | Required By | Available | Version |
|------------|------------|-----------|---------|
| Docker | Testcontainers (integration tests) | ✓ | Confirmed in CI (`docker --version` step in `ci.yml`) |
| .NET 10 SDK | Build + tests | ✓ | `10.0.x` (via `global.json`) |
| PostgreSQL (via Testcontainers) | GDPR completeness, refresh hashing, revoked refresh tests | ✓ | Testcontainers pulls image automatically |
| Redis (via Testcontainers) | Admin integration tests (AdminTestHost) | ✓ | Testcontainers pulls image automatically |

No missing dependencies.

---

## Runtime State Inventory

Not applicable — this is a test + gate + fix phase, not a rename/refactor/migration phase.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8` is compatible with `MessagePack 3.1.7` at runtime (the version floor is `>= 2.5.187`, which 3.x satisfies) | SEC-07 | If 3.x has a breaking API change in the SignalR serialization protocol, the Lobby hub would fail at runtime. Mitigation: run existing Lobby integration tests after the pin to verify. |
| A2 | `GameKit.Auth.Apple` and `GameKit.Auth.Google` packages wire their OAuth HTTP clients through `EgressAllowListHandler` | SEC-05 | If those packages use `new HttpClient()` directly or bypass the named client, the egress audit has a gap. Mitigation: grep both packages' builder code during Wave 1 of planning. |
| A3 | The `IGdprDeleteExtension` pattern is the right architectural choice for pre-delete hooks | SEC-04 | If the planner chooses Option B (admin-layer cleanup) or Option C (delegate registration), the implementation differs significantly. Mark as Claude's discretion in the plan. |

---

## Open Questions

1. **SEC-04: GdprDeleteService extension architecture**
   - What we know: two FK violations block deletion (party_members RESTRICT, account_merges RESTRICT)
   - What's unclear: whether the fix should live in `GameKit.Core` (via extension interface) or in the admin layer
   - Recommendation: planner chooses; either Option A (IGdprDeleteExtension) or Option B (admin endpoint pre-cleanup) is acceptable

2. **SEC-05: Apple + Google egress handler wiring**
   - What we know: `AuthBuilderExtensions` wires `EgressAllowListHandler` on Steam and Discord named clients
   - What's unclear: whether `GameKit.Auth.Apple` and `GameKit.Auth.Google` also wire the handler
   - Recommendation: planner reads those builder files during Wave 1 and adds handler registration if missing

---

## Sources

### Primary (HIGH confidence — verified from codebase)
- `src/GameKit.Core/Services/GdprDeleteService.cs` — GDPR delete behavior verified line by line
- `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 190–205 — JWT validation parameters
- `src/GameKit.Auth/Services/RefreshTokenService.cs` lines 280–284 — SHA-256 storage confirmed
- `src/GameKit.Auth/Http/AuthEndpoints.cs` lines 55–88 — rate-limit assignments per endpoint
- `src/GameKit.Auth/Egress/EgressAllowListHandler.cs` + `DefaultAllowedHosts.cs` — egress enforcement confirmed
- `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` — 14 admin endpoints, authorization metadata listed
- `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` + `AdminBuilderExtensions.cs` lines 159–169 — policy scheme pinning
- `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs` — CSRF returns 400
- All `*Configuration.cs` files in Auth/Matchmaking/Rankings/Lobby — FK cascade/restrict behavior verified
- `Directory.Packages.props` — `CentralPackageTransitivePinningEnabled=true` confirmed
- `.github/workflows/ci.yml` — CI build + test commands confirmed

### Secondary (HIGH confidence — verified via tooling)
- `dotnet list package --vulnerable --include-transitive` — MessagePack 2.5.187 / GHSA-hv8m-jj95-wg3x confirmed across 15 projects
- `dotnet nuget why GameKit.Lobby MessagePack` — dependency chain `SignalR.StackExchangeRedis 10.0.8 → MessagePack 2.5.187` confirmed
- `dotnet package search MessagePack` — MessagePack 3.1.7 confirmed as latest; 290M+ downloads; neuecc + aarnott owners

---

## Metadata

**Confidence breakdown:**
- SEC-07 MessagePack fix: HIGH — dependency chain verified by two independent tools; 3.x version floor satisfied
- SEC-04 GDPR gap analysis: HIGH — all FK configurations read directly; two RESTRICT gaps confirmed
- SEC-01 JWT test strategy: HIGH — `TokenValidationParameters` confirmed; `RequireSignedTokens=true` documented
- SEC-02/03/06 test patterns: HIGH — existing test files read; pattern well-established in test suite
- SEC-05 egress: MEDIUM — Apple/Google egress wiring assumed but not verified (A2)

**Research date:** 2026-06-23
**Valid until:** 2026-07-23 (stable .NET ecosystem; MessagePack version may update but 3.1.7 pin remains clean)

---

## RESEARCH COMPLETE
