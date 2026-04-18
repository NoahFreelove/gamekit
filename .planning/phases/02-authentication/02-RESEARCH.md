# Phase 2: Authentication — Research

**Researched:** 2026-04-18
**Domain:** ASP.NET Core 10 authentication — JWT issuance, rotating refresh tokens, Steam OpenID 2.0, Discord OAuth2, guest/password providers, SERIALIZABLE upgrade, EgressAllowListHandler, rate limiting
**Confidence:** HIGH (all stack pins verified against NuGet registry 2026-04-18)

---

<user_constraints>
## User Constraints (from 02-CONTEXT.md)

### Locked Decisions

| ID | Decision |
|----|----------|
| D-01 | Access-token lifetime configurable via `JwtOptions.AccessTokenLifetime`; default **15 minutes** |
| D-02 | Refresh-token lifetime configurable via `JwtOptions.RefreshTokenLifetime`; default **30 days** |
| D-03 | Standard claims: `sub` (player_id), `jti`, `iat`, `exp`, `iss`, `aud`, `gk:guest` (bool), `gk:providers` (pipe-delimited), `sid`. Admin-role claims explicitly excluded from player tokens. |
| D-04 | Revocation strategy is stateless — access tokens self-expire; refresh-family revocation on abuse. No Redis jti denylist in Phase 2. |
| D-05 | Fingerprint = client-supplied `X-GameKit-Device: <uuid>` header stored on `refresh_tokens.device_fingerprint`. Missing header = NULL = strict reuse-attack treatment. |
| D-06 | Fingerprint mismatch within grace window OR any mismatch outside grace window → revoke entire refresh-token family + audit row `reason="refresh_fingerprint_mismatch"`. |
| D-07 | Provider HTTP: named `HttpClientFactory` instances `"gamekit.auth.provider.steam"` and `"gamekit.auth.provider.discord"` with `Microsoft.Extensions.Http.Resilience` (retry/circuit-breaker/timeout). No naked `new HttpClient()`. |
| D-08 | `GameKitAuthOptions.AllowedProviderHosts` allow-list; `EgressAllowListHandler` DelegatingHandler throws `EgressViolationException` on off-list URI. Default populated with `steamcommunity.com`, `api.steampowered.com`, `discord.com`, `discordapp.com`. |
| D-09 | `AspNet.Security.OpenId.Steam` is NOT added as a NuGet dependency. Steam verification is in-house `SteamOpenIdVerifier` (~50 LOC, OpenID 2.0 §11.4.2.2 `check_authentication`). This resolves Phase 1 D-21. |
| D-10 | `EgressAllowListTests` fixture asserts: (a) off-list URI throws; (b) default list resolves canonical provider endpoints; (c) zero non-allow-listed named HttpClient instances. |
| D-11 | Guest → OAuth link collision (identity already linked to a different player) → HTTP 409 `identity_already_linked` + `{ error, provider, external_id_hash }`. No link-or-switch UX in Phase 2. |
| D-12 | `/auth/register` with valid guest Bearer token upgrades the guest in-place via SERIALIZABLE transaction. No auth header = fresh player. |
| D-13 | `IsGuest` is computed: `!Identities.Any() && Credentials is null`. No stored column. Cleared automatically when first identity or credential lands. |
| D-14 | Concurrent guest-upgrade race serialized by `UNIQUE(provider, external_id)` on `player_identities`. One wins, other fails with 409 `identity_already_linked`. |

### Claude's Discretion

- Endpoint surface (ship `/auth/me` + `/auth/logout/all`; defer `/auth/identities`)
- Discord scopes: locked to `identify` only
- Username policy: 3-32 chars, `[a-zA-Z0-9_-]`, case-insensitive, no reserved-word list
- Rate-limit values: login 10/min/IP, refresh 60/min/IP, register 5/min/IP
- Challenge/callback handshake: 302 redirect + JWT-in-response-body (not cookie)
- WireMock.Net for provider mocks (planner confirmed in plan 02-01)
- Migration history table: `__ef_migrations_auth`
- BCrypt work factor: 12 (default)

### Deferred Ideas (OUT OF SCOPE)

- Argon2 sibling package (`GameKit.Auth.Argon2`)
- Account merge across distinct `player_id`s
- Additional OAuth providers (Google, Apple, Epic)
- Email-out-of-band flows (password reset, email verification)
- Passkey / WebAuthn
- Universal sub-minute revocation (Redis jti denylist)
- `/auth/identities` listing endpoint
- Admin auth (Phase 3)

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| AUTH-01 | Library ships as `GameKit.Auth` NuGet package | §3 file tree, §4 package pins |
| AUTH-02 | `player_identities` entity with UNIQUE(provider, external_id) | §6.1, §6.2 |
| AUTH-03 | `player_credentials` entity (player_id PK, password_hash, updated_at) | §6.1, §6.2 |
| AUTH-04 | `refresh_tokens` entity with hashed token (SHA-256), replaced_by chain | §6.1, §6.7 |
| AUTH-05 | `IOAuthProvider` interface — pluggable | §6.11 |
| AUTH-06 | Steam provider — in-house OpenID 2.0 + server-side `check_authentication` | §6.9, §8.2 |
| AUTH-07 | Discord provider — `identify` scope only | §6.10, §8.3 |
| AUTH-08 | Guest provider — anonymous account creation | §6.11 |
| AUTH-09 | Username/password provider with BCrypt.Net-Next | §6.5, §6.11 |
| AUTH-10 | JWT issuance via JwtBearer; configurable issuer/audience/secret/lifetimes | §6.6, §8.1 |
| AUTH-11 | Refresh token rotation with reuse-attack detection (`replaced_by` chain) | §6.7, §8.4 |
| AUTH-12 | Reuse-interval grace window (30-60s) + fingerprint check | §6.7, §8.4 |
| AUTH-13 | Guest upgrade in SERIALIZABLE transaction + unique constraint protection | §6.12, §8.5 |
| AUTH-14 | Identity link/switch challenge on existing-player collision | §6.13 |
| AUTH-15 | Rate limits on `/auth/login`, `/auth/refresh`, `/auth/register` | §6.16, §8.7 |
| AUTH-16 | `IPasswordHasher` interface (Argon2 swap-in path) | §6.5 |

</phase_requirements>

---

## §1 Executive Summary

Ten-to-fifteen findings mapping CONTEXT decisions to concrete enabling research:

1. **D-01/D-02 (JWT lifetimes):** `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.6 (shared framework) supports arbitrary `ValidFor` spans at issuance time. `JsonWebTokenHandler.CreateToken(SecurityTokenDescriptor)` accepts `Expires` as `DateTime`; `TokenValidationParameters.ClockSkew` (default 5 min) must be set to `TimeSpan.Zero` when operators configure short lifetimes. [VERIFIED: nuget.org, 2026-04-18]

2. **D-03 (claims shape):** `MapInboundClaims = false` on `JwtBearerOptions.TokenValidationParameters` is **mandatory** to preserve the raw `sub` claim through the middleware stack. Without it, ASP.NET Core maps `sub` → `ClaimTypes.NameIdentifier`. The correct fix: emit `gamekit_player_id` as a custom claim in `JwtIssuer` alongside `sub`, set `MapInboundClaims = false`. [VERIFIED: aspnetcore source + HttpContextCurrentPlayer.cs]

3. **D-05/D-06 (fingerprint + grace window):** Pattern 3 refresh rotation — 45-second grace window. Two concurrent calls within the window with matching fingerprint: one wins via optimistic row-locking; other returns the existing child. Mismatch outside window → family revoke. [ASSUMED — SQL logic; Postgres MVCC semantics CITED: postgresql.org/docs]

4. **D-07 (named HttpClients + resilience):** `Microsoft.Extensions.Http.Resilience` 10.5.0 has explicit `net10.0` TFM. `AddStandardResilienceHandler()` adds retry (3 attempts, exponential jitter), circuit-breaker, timeout (10s). [VERIFIED: NuGet registry 2026-04-18; CITED: learn.microsoft.com/dotnet/core/resilience/http-resilience]

5. **D-08 (egress allow-list):** `DelegatingHandler` pattern — `DefaultAllowedHosts.Value` is a literal constant, not config-resolved, so tests never silently pass due to missing config. [ASSUMED — pattern; DelegatingHandler CITED: learn.microsoft.com]

6. **D-09 (no Steam contrib package):** Steam OpenID 2.0 is a 50-LOC HTTP round-trip. `claimed_id` URL format: `https://steamcommunity.com/openid/id/{steamid64}`. POST `openid.mode=check_authentication` back to Steam, parse Key-Value form response for `is_valid:true`. [CITED: openid.net/specs/openid-authentication-2_0.html §11.4.2.2; partner.steamgames.com/doc/features/auth]

7. **D-10 (Discord `identify` scope):** `Backchannel` on `RemoteAuthenticationOptions` must be replaced via `IPostConfigureOptions<DiscordAuthenticationOptions>` — NOT inside `.AddDiscord(...)` lambda. [VERIFIED: github.com/aspnet-contrib/AspNet.Security.OAuth.Providers source]

8. **D-11/D-14 (identity collision and race):** `UNIQUE(provider, external_id)` is the exclusive race anchor. Postgres `SqlState 40001` = serialization failure; `SqlState 23505` = unique violation. Both map to HTTP 409. [VERIFIED: Postgres docs, Npgsql docs]

9. **D-12/D-13 (guest upgrade):** `IsGuest` computed from navigation properties. SERIALIZABLE transaction loads player, inserts `PlayerCredential`, re-issues JWT without `gk:guest`. [VERIFIED: EF Core docs]

10. **Advisory lock distinctness:** Core key = `hashtext('gamekit.migrations')` = `1800940027L`. Auth key = `hashtext('gamekit.auth.migrations')` — value is PLACEHOLDER until `AuthAdvisoryLockKeyTests` verifies against live Postgres 17.9. [VERIFIED: GameKitMigrationConstants.cs:37; ASSUMED — auth key value]

11. **BCrypt.Net-Next version bump:** 4.0.3 has `net6.0` TFM only for modern runtimes; **4.1.0 has explicit `net10.0` TFM**. Pin must be bumped. [VERIFIED: NuGet nuspec 2026-04-18]

12. **WireMock.Net 2.2.0** — current stable; `net8.0` TFM (compatible fallback for net10.0). `WireMockServer.Start(new WireMockServerSettings { Port = 0 })` for ephemeral ports. [PROBABLE — verify at plan-02-01 dotnet restore; 2.x is a major jump from 1.5.x/1.6.x]

13. **Middleware ordering:** `UseRouting → UseRateLimiter → UseGameKitAuth (UseAuthentication) → UseGameKit (UseAuthorization)`. Wrong ordering silently denies all authenticated requests. [VERIFIED: aspnetcore source]

14. **`HttpContextCurrentPlayer` claim priority:** Reads `gamekit_player_id` first, then `ClaimTypes.NameIdentifier` fallback. `JwtIssuer` must emit `gamekit_player_id` in addition to `sub`. [VERIFIED: HttpContextCurrentPlayer.cs]

15. **Audit log (10 action types):** `admin_audit_log` already provisioned in Phase 1. Auth writes: `auth.login.success`, `auth.login.failure`, `auth.logout`, `auth.logout.all`, `auth.refresh.success`, `auth.refresh.revoked`, `auth.register`, `auth.guest.upgrade`, `auth.identity.linked`, `auth.identity.collision`. [ASSUMED — action names not specified in Phase 1; table existence VERIFIED]

---

## §2 Validation Architecture (Nyquist Dimension 8)

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config | `tests/GameKit.Auth.Tests/xunit.runner.json` + `tests/GameKit.Auth.Integration.Tests/xunit.runner.json` |
| Unit run | `dotnet test tests/GameKit.Auth.Tests/ -x` |
| Integration run | `dotnet test tests/GameKit.Auth.Integration.Tests/ -x` |
| Full suite | `dotnet test tests/ --filter "Category!=NetnsOnly"` |

### ROADMAP Success Criteria → Test Map

| SC # | Behavior | Test Type | File | SC Coverage |
|------|----------|-----------|------|-------------|
| SC-1 | E2E login via all 4 providers + JWT issued + `/auth/refresh` rotates | E2E (WebAppFactory + WireMock + TC) | `FourProviderLoginE2eTests.cs` | AUTH-01,05,06,07,08,09,10,11 |
| SC-2 | Forged Steam callback rejected | Integration (WireMock) | `SteamForgeryTests.cs` | AUTH-06 |
| SC-3a | Concurrent refresh, matching fingerprint within 45s → stays logged in | Integration (TC) | `RefreshRotationTests.GraceWindow_MatchingFingerprint_StaysLoggedIn` | AUTH-11,12 |
| SC-3b | Mismatched fingerprint outside window → family revoked | Integration (TC) | `RefreshRotationTests.FingerprintMismatch_RevokesFamily` | AUTH-11,12 |
| SC-4 | Two concurrent OAuth-link for same guest: one wins, other 409 | Integration (TC, SERIALIZABLE) | `GuestUpgradeRaceTests.ConcurrentOAuthLink_ExactlyOneSucceeds` | AUTH-13 |
| SC-5 | Identity already linked to another player → 409 `identity_already_linked` | Integration (TC) | `IdentityLinkerTests.CrossPlayerCollision_Returns409` | AUTH-14 |
| SC-6 | 429 under burst load on login/refresh/register | E2E (WebAppFactory) | `RateLimitTests` | AUTH-15 |

### Layered Strategy

- **Layer 1 (unit, <1s):** BCrypt hasher round-trip, JwtIssuer claim shape, RefreshTokenService rotation logic (mocked DbContext), EgressAllowListHandler host check, IsGuestResolver, ExternalIdHasher SHA-256 determinism.
- **Layer 2 (Testcontainers, 5-20s/class):** Migration schema, advisory-lock key distinctness, PlayerIdentity UNIQUE enforcement, RefreshToken rotation with real Postgres, GuestUpgrade SERIALIZABLE race.
- **Layer 3 (WebAppFactory + WireMock E2E, 10-30s/class):** Full HTTP round-trip per provider, Steam forgery rejection, rate-limit burst.
- **Manual-only:** Signing-key rotation (kid rotation, multiple `IssuerSigningKeys[]`).

### Wave 0 Gaps

- [ ] `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj`
- [ ] `tests/GameKit.Auth.Integration.Tests/GameKit.Auth.Integration.Tests.csproj`
- [ ] `tests/GameKit.TestFixtures/WireMockFixture.cs`
- [ ] `tests/GameKit.TestFixtures/WireMockSteamStubs.cs`
- [ ] `tests/GameKit.TestFixtures/WireMockDiscordStubs.cs`
- [ ] `tests/GameKit.TestFixtures/AuthIntegrationFixture.cs`
- [ ] `[CollectionDefinition("Auth")]` in `tests/GameKit.TestFixtures/CollectionDefinitions.cs`
- [ ] `Directory.Packages.props` new pins: `BCrypt.Net-Next 4.1.0`, `AspNet.Security.OAuth.Discord 10.0.0`, `Microsoft.Extensions.Http.Resilience 10.5.0`, `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6`, `WireMock.Net 2.2.0`, `Polly 8.6.6`

---

## §3 Phase 2 File Tree

```
src/GameKit.Auth/
├── GameKit.Auth.csproj
├── AssemblyInfo.cs
├── GameKitAuthOptions.cs
├── JwtOptions.cs
├── SteamOptions.cs
├── DiscordOptions.cs
├── PasswordOptions.cs
├── Builder/
│   ├── AuthBuilderExtensions.cs             # IGameKitBuilder.AddAuth(...)
│   ├── AuthServiceCollectionExtensions.cs
│   └── AuthApplicationBuilderExtensions.cs  # UseGameKitAuth() → UseAuthentication()
├── Data/
│   ├── AuthMigrationConstants.cs            # AdvisoryLockKey=PLACEHOLDER
│   ├── AuthDesignTimeDbContextFactory.cs
│   ├── AuthModelBuilderExtension.cs
│   └── Configurations/
│       ├── PlayerIdentityConfiguration.cs
│       ├── PlayerCredentialConfiguration.cs
│       └── RefreshTokenConfiguration.cs
├── Egress/
│   ├── DefaultAllowedHosts.cs
│   ├── EgressAllowListHandler.cs
│   └── EgressViolationException.cs
├── Entities/
│   ├── PlayerIdentity.cs
│   ├── PlayerCredential.cs
│   └── RefreshToken.cs
├── Http/
│   ├── AuthEndpoints.cs
│   ├── Contracts/
│   │   ├── LoginRequest.cs
│   │   ├── RefreshRequest.cs
│   │   ├── RegisterRequest.cs
│   │   ├── TokenResponse.cs
│   │   ├── MeResponse.cs
│   │   └── AuthErrorResponse.cs
│   ├── EndpointFilters/
│   │   └── ValidationEndpointFilter.cs
│   ├── RateLimiting/
│   │   └── AuthRateLimitPolicies.cs
│   └── Validators/
│       ├── LoginRequestValidator.cs
│       ├── RefreshRequestValidator.cs
│       └── RegisterRequestValidator.cs
├── Migrations/
│   ├── 20260418000000_AuthInitial.cs
│   ├── 20260418000000_AuthInitial.Designer.cs
│   └── GameKitDbContextModelSnapshot.cs
├── Providers/
│   ├── IOAuthProvider.cs
│   ├── Discord/
│   │   ├── DiscordOAuthProvider.cs
│   │   └── DiscordBackchannelPostConfigure.cs
│   ├── Guest/
│   │   └── GuestOAuthProvider.cs
│   ├── Password/
│   │   └── PasswordOAuthProvider.cs
│   └── Steam/
│       ├── SteamOAuthProvider.cs
│       ├── SteamOpenIdVerifier.cs
│       └── SteamConstants.cs
└── Services/
    ├── IAuthAuditWriter.cs + AuthAuditWriter.cs
    ├── IExternalIdHasher.cs + ExternalIdHasher.cs
    ├── IGuestUpgradeService.cs + GuestUpgradeService.cs
    ├── IIdentityLinker.cs + IdentityLinker.cs
    ├── IIsGuestResolver.cs + IsGuestResolver.cs
    ├── IJwtIssuer.cs + JwtIssuer.cs
    ├── IPasswordHasher.cs + BCryptPasswordHasher.cs
    └── IRefreshTokenService.cs + RefreshTokenService.cs

tests/GameKit.Auth.Tests/
├── GameKit.Auth.Tests.csproj
├── BCryptPasswordHasherTests.cs
├── JwtIssuerTests.cs
├── RefreshTokenServiceTests.cs
├── EgressAllowListHandlerTests.cs
├── AuthBuilderTests.cs
├── IsGuestResolverTests.cs
└── ExternalIdHasherTests.cs

tests/GameKit.Auth.Integration.Tests/
├── GameKit.Auth.Integration.Tests.csproj
├── AuthAdvisoryLockKeyTests.cs
├── AuthSchemaTests.cs
├── PlayerIdentityUniqueTests.cs
├── GuestUpgradeRaceTests.cs
├── RefreshRotationTests.cs
├── RefreshTokenRoleIsolationTests.cs
├── SteamLoginTests.cs
├── SteamForgeryTests.cs
├── DiscordLoginTests.cs
├── FourProviderLoginE2eTests.cs
├── IdentityLinkerTests.cs
└── RateLimitTests.cs

tests/GameKit.TestFixtures/  (additions)
├── WireMockFixture.cs
├── WireMockSteamStubs.cs
├── WireMockDiscordStubs.cs
├── AuthIntegrationFixture.cs
└── CollectionDefinitions.cs  (add Auth collection)

samples/TicTacToeDuel/
├── Program.cs               (modified)
├── wwwroot/index.html       (modified)
└── README-auth.md
```

---

## §4 Standard Stack

### Core Auth Stack (new pins for Phase 2)

> **CLAUDE.md stale-pin note (for plan 02-08 docs pass):** CLAUDE.md currently pins `BCrypt.Net-Next` at 4.0.3 and `Microsoft.Extensions.Http.Resilience` at 9.0.x. Phase 2 uses 4.1.0 and 10.5.0 respectively (verified against NuGet 2026-04-18). CLAUDE.md Technology Stack table must be updated in plan 02-08 to reflect these bumps. Do not roll back to stale pins.

| Library | Version | TFM | Purpose | Confidence |
|---------|---------|-----|---------|------------|
| `BCrypt.Net-Next` | **4.1.0** | `net10.0` explicit | Password hasher | HIGH [VERIFIED: NuGet nuspec 2026-04-18] |
| `AspNet.Security.OAuth.Discord` | **10.0.0** | `net10.0` explicit | Discord OAuth2 | HIGH [VERIFIED: NuGet nuspec 2026-04-18] |
| `Microsoft.Extensions.Http.Resilience` | **10.5.0** | `net10.0` explicit | Named HttpClient resilience | HIGH [VERIFIED: NuGet nuspec 2026-04-18] |
| `WireMock.Net` | **2.2.0** | `net8.0` (fallback) | Provider endpoint mocks | MEDIUM [PROBABLE — verify at plan-02-01 dotnet restore; 2.x is a major jump from 1.5.x/1.6.x] |
| `Polly` | **8.6.6** | `net8.0` (fallback) | Non-HTTP resilience | HIGH [VERIFIED: NuGet registry 2026-04-18] |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | **10.0.6** | Shared framework | JWT validation | HIGH [VERIFIED: NuGet registry 2026-04-18] |

### Explicitly NOT Added

| Package | Reason |
|---------|--------|
| `AspNet.Security.OpenId.Steam` | D-09 — in-house `SteamOpenIdVerifier` replaces it. MUST NOT appear in `Directory.Packages.props` or any `.csproj`. |
| `Microsoft.AspNetCore.Authentication.OpenIdConnect` | Steam uses OpenID 2.0, not OIDC. |
| `FluentValidation.AspNetCore` | Deprecated. Explicit `IValidator<T>` injection is established pattern. |

### Installation snippet for `Directory.Packages.props`

```xml
<!-- Auth stack (Phase 2 — verified GA on net10.0 2026-04-18) -->
<PackageVersion Include="BCrypt.Net-Next" Version="4.1.0" />
<PackageVersion Include="AspNet.Security.OAuth.Discord" Version="10.0.0" />
<PackageVersion Include="Microsoft.Extensions.Http.Resilience" Version="10.5.0" />
<PackageVersion Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.6" />
<PackageVersion Include="WireMock.Net" Version="2.2.0" />
<PackageVersion Include="Polly" Version="8.6.6" />
```

---

## §5 Architecture Overview

### System Architecture Diagram

```
HTTP Request
     │
     ▼
UseRouting
     │
     ▼
UseRateLimiter  ← PartitionedRateLimiter keyed {IP}:{fingerprint}
     │
     ▼
UseGameKitAuth  ← AuthApplicationBuilderExtensions (Phase 2)
(UseAuthentication)
JwtBearerHandler → validates token → ClaimsPrincipal (sub, gamekit_player_id, gk:guest, sid)
     │
     ▼
UseGameKit  ← GameKitApplicationBuilderExtensions (Phase 1)
(UseAuthorization + auto-migration)
     │
     ▼
MapGameKit (/api/players)       MapAuth (/auth/*)
                                     │
                          ValidationEndpointFilter (FluentValidation 12)
                                     │
                          ┌──────────▼──────────┐
                          │    Auth Services      │
                          │  IOAuthProvider       │
                          │  IJwtIssuer           │
                          │  IRefreshTokenService │
                          │  IGuestUpgradeService │
                          │  IIdentityLinker      │
                          └──────────┬────────────┘
                                     │
                          GameKitDbContext (shared)
                          AuthModelBuilderExtension
                                     │
                          Postgres 17.9
                          gamekit.player_identities
                          gamekit.player_credentials
                          gamekit.refresh_tokens
                          gamekit.admin_audit_log

Provider HTTP (EgressAllowListHandler gated):
  "gamekit.auth.provider.steam"   → steamcommunity.com / api.steampowered.com
  "gamekit.auth.provider.discord" → discord.com / discordapp.com
  (WireMock.Net intercepts both in tests)
```

### DI Lifetimes

| Service | Lifetime | Rationale |
|---------|----------|-----------|
| `GameKitAuthOptions` | Singleton | Immutable after startup |
| `IJwtIssuer`, `IPasswordHasher`, `IExternalIdHasher` | Singleton | Stateless |
| `IIsGuestResolver`, `IRefreshTokenService`, `IGuestUpgradeService`, `IIdentityLinker`, `IAuthAuditWriter` | Scoped | Open Db transactions; share DbContext |
| `IOAuthProvider` (all impls) | Scoped | May reference Scoped services |
| Named `HttpClient` instances | Singleton (IHttpClientFactory) | Per IHttpClientFactory design |
| `EgressAllowListHandler` | Transient | DelegatingHandler convention |

### Migration Isolation

```
GameKit.Core  → __ef_migrations_core  AdvisoryLockKey=1800940027  [hashtext('gamekit.migrations')]
GameKit.Auth  → __ef_migrations_auth  AdvisoryLockKey=PLACEHOLDER  [hashtext('gamekit.auth.migrations')]
```

- `AuthDesignTimeDbContextFactory.MigrationsAssembly` = `typeof(AuthDesignTimeDbContextFactory).Assembly.FullName`
- At design time: Core's `IModelBuilderExtension` DI list is empty → Auth-only snapshot

### Middleware Pipeline (complete sample)

```csharp
app.UseRouting();
app.UseRateLimiter();
app.UseGameKitAuth();   // Phase 2: UseAuthentication()
app.UseGameKit();       // Phase 1: UseAuthorization() + auto-migration
app.MapGameKit();       // /api/players
app.MapAuth();          // /auth/*
```

---

## §6 Code Sketches

All sketches are concrete C# targeting `net10.0`. SPDX headers omitted for brevity.

### §6.1 Entities

**PlayerIdentity.cs** [AUTH-02]
```csharp
public sealed class PlayerIdentity
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public required string Provider { get; set; }    // "steam"|"discord"|"guest"|"password"
    public required string ExternalId { get; set; }
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public System.Text.Json.JsonDocument? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Player Player { get; set; } = null!;
}
```

**PlayerCredential.cs** [AUTH-03]
```csharp
public sealed class PlayerCredential
{
    public Guid PlayerId { get; set; }              // PK + FK (one-to-one)
    public required string PasswordHash { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Player Player { get; set; } = null!;
}
```

**RefreshToken.cs** [AUTH-04] — §14.1
```csharp
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public required string TokenHash { get; set; }  // SHA-256 hex (64 chars); raw issued once, never stored
    public Guid FamilyId { get; set; }              // family revocation anchor
    public Guid? ReplacedBy { get; set; }           // replaced_by chain
    public string? DeviceFingerprint { get; set; }  // X-GameKit-Device; null = strict reuse detection
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokeReason { get; set; }       // "logout"|"logout_all"|"refresh_fingerprint_mismatch"|"reuse_attack"
    public Guid SessionId { get; set; }             // = FamilyId; emitted as "sid" JWT claim
    public Player Player { get; set; } = null!;
}
```

### §6.2 EF Configurations — §14.2

**PlayerIdentityConfiguration.cs**
```csharp
internal sealed class PlayerIdentityConfiguration : IEntityTypeConfiguration<PlayerIdentity>
{
    public void Configure(EntityTypeBuilder<PlayerIdentity> b)
    {
        b.ToTable("player_identities", GameKitMigrationConstants.SchemaName);
        b.HasKey(i => i.Id);
        b.Property(i => i.Id).ValueGeneratedNever();
        b.Property(i => i.Provider).HasMaxLength(32).IsRequired();
        b.Property(i => i.ExternalId).HasMaxLength(256).IsRequired();
        b.Property(i => i.DisplayName).HasMaxLength(256);
        b.Property(i => i.AvatarUrl).HasMaxLength(512);
        b.Property(i => i.Metadata).HasColumnType("jsonb");

        // D-14 race anchor — serializes concurrent OAuth-link for same external id
        b.HasIndex(i => new { i.Provider, i.ExternalId }).IsUnique()
         .HasDatabaseName("ix_player_identities_provider_external_id");

        b.HasOne(i => i.Player).WithMany()
         .HasForeignKey(i => i.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

**RefreshTokenConfiguration.cs** — §14.1
```csharp
internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens", GameKitMigrationConstants.SchemaName);
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.TokenHash).HasMaxLength(64).IsRequired(); // SHA-256 = 64 hex chars
        b.Property(r => r.DeviceFingerprint).HasMaxLength(128);
        b.Property(r => r.RevokeReason).HasMaxLength(64);

        b.HasIndex(r => r.TokenHash).IsUnique()
         .HasDatabaseName("ix_refresh_tokens_token_hash");
        b.HasIndex(r => new { r.PlayerId, r.RevokedAt })
         .HasDatabaseName("ix_refresh_tokens_player_revoked");
        b.HasIndex(r => r.FamilyId)
         .HasDatabaseName("ix_refresh_tokens_family_id");

        b.HasOne(r => r.Player).WithMany()
         .HasForeignKey(r => r.PlayerId).OnDelete(DeleteBehavior.Cascade);
    }
}
```

### §6.3 AuthMigrationConstants + AuthDesignTimeDbContextFactory — §14.3, §14.4

**AuthMigrationConstants.cs**
```csharp
public static class AuthMigrationConstants
{
    public const string SchemaName = GameKit.Core.Data.GameKitMigrationConstants.SchemaName;
    public const string MigrationsHistoryTable = "__ef_migrations_auth";

    /// <summary>
    /// PLACEHOLDER — must be set to the output of
    /// <c>SELECT hashtext('gamekit.auth.migrations')::bigint</c>
    /// verified by AuthAdvisoryLockKeyTests against a live Postgres 17.9 container.
    /// Distinct from Core's 1800940027 — prevents cross-package deadlock (§8.12 #9).
    /// </summary>
    public const long AdvisoryLockKey = 0L; // PLACEHOLDER

    /// <summary>Guards against shipping the placeholder value to production.</summary>
    static AuthMigrationConstants()
    {
        if (AdvisoryLockKey == 0L)
            throw new InvalidOperationException(
                "AuthMigrationConstants.AdvisoryLockKey is 0L — must be set to " +
                "hashtext('gamekit.auth.migrations')::bigint. " +
                "Run AuthAdvisoryLockKeyTests against a live Postgres 17.9 container to verify.");
    }
}
```

**AuthDesignTimeDbContextFactory.cs** — §14.4
```csharp
public sealed class AuthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    public GameKitDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";

        var opts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                // CRITICAL: Auth assembly, not Core assembly
                npg.MigrationsAssembly(typeof(AuthDesignTimeDbContextFactory).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    AuthMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, GameKitModelCustomizer>();

        return new GameKitDbContext(opts.Options);
    }
}
```

**AuthModelBuilderExtension.cs** — §14.3
```csharp
internal sealed class AuthModelBuilderExtension : IModelBuilderExtension
{
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
```

### §6.4 EgressAllowListHandler

```csharp
public static class DefaultAllowedHosts
{
    // Literal constant — NOT config-resolved — so tests never silently pass (D-08 specifics)
    public static readonly IReadOnlyList<string> Value = new[]
    {
        "steamcommunity.com", "api.steampowered.com", "discord.com", "discordapp.com"
    };
}

public sealed class EgressViolationException : Exception
{
    public EgressViolationException(string host)
        : base($"Outbound request to '{host}' is not on the Auth provider allow-list.") { }
}

public sealed class EgressAllowListHandler : DelegatingHandler
{
    private readonly IReadOnlyList<string> _allowedHosts;
    public EgressAllowListHandler(IReadOnlyList<string> allowedHosts) => _allowedHosts = allowedHosts;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        var host = request.RequestUri?.Host
            ?? throw new InvalidOperationException("Request URI has no host.");
        if (!_allowedHosts.Any(h => h.Equals(host, StringComparison.OrdinalIgnoreCase)))
            throw new EgressViolationException(host);
        return base.SendAsync(request, ct);
    }
}
```

### §6.5 BCryptPasswordHasher + IPasswordHasher [AUTH-09, AUTH-16]

```csharp
public interface IPasswordHasher
{
    string Hash(string plaintext);
    bool Verify(string plaintext, string hash);
}

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;
    public BCryptPasswordHasher(GameKitAuthOptions opts) => _workFactor = opts.Password.WorkFactor;

    public string Hash(string plaintext) => BCrypt.Net.BCrypt.HashPassword(plaintext, _workFactor);
    public bool Verify(string plaintext, string hash) => BCrypt.Net.BCrypt.Verify(plaintext, hash);
}
```

### §6.6 JwtIssuer with D-03 Claims [AUTH-10]

```csharp
public sealed class JwtIssuer : IJwtIssuer
{
    private readonly JwtOptions _opts;
    private readonly IClock _clock;

    public string Issue(JwtPayload payload)
    {
        var now = _clock.UtcNow;
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_opts.SigningKey));
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, payload.PlayerId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("gamekit_player_id", payload.PlayerId.ToString()), // HttpContextCurrentPlayer primary
            new("gk:guest", payload.IsGuest ? "true" : "false"),
            new("gk:providers", string.Join("|", payload.Providers)),
            new("sid", payload.SessionId.ToString()),
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _opts.Issuer,
            Audience = _opts.Audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.UtcDateTime,
            IssuedAt = now.UtcDateTime,
            Expires = now.Add(_opts.AccessTokenLifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        if (!string.IsNullOrEmpty(_opts.Kid))
            descriptor.AdditionalHeaderClaims = new Dictionary<string, object> { ["kid"] = _opts.Kid };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
```

### §6.7 RefreshTokenService — Pattern 3 (45s grace, fingerprint, family revoke)

```csharp
// Grace window = 45s (D-05/D-06 middle of 30-60s band)
private static readonly TimeSpan GraceWindow = TimeSpan.FromSeconds(45);

public async Task<RotateResult> RotateAsync(string rawToken, string? fingerprint, CancellationToken ct)
{
    var hash = ComputeHash(rawToken);
    var now = _clock.UtcNow;

    await using var tx = await _ctx.Database.BeginTransactionAsync(
        System.Data.IsolationLevel.Serializable, ct);
    try
    {
        var existing = await _ctx.Set<RefreshToken>()
            .FirstOrDefaultAsync(r => r.TokenHash == hash, ct);

        if (existing is null || existing.ExpiresAt < now)
            return Fail("token_expired_or_not_found");

        if (existing.RevokedAt is not null)
        {
            await RevokeFamilyAsync(existing.FamilyId, "reuse_attack", now, ct);
            await tx.CommitAsync(ct);
            return Fail("reuse_attack");
        }

        if (existing.ReplacedBy is not null)
        {
            var withinWindow = (now - existing.IssuedAt) <= GraceWindow;
            var fpMatch = existing.DeviceFingerprint is not null
                       && existing.DeviceFingerprint == fingerprint;

            if (withinWindow && fpMatch)
            {
                // Grace window: re-issue a fresh child token in the same family.
                // existing.ReplacedBy is already set by the concurrent winning request.
                // We add a new sibling-child with the same FamilyId — family is NOT revoked.
                // [OWASP JWT Cheat Sheet — detect-and-revoke with grace window]
                var (newRaw, newChild) = CreateToken(
                    existing.PlayerId, fingerprint, existing.FamilyId, existing.SessionId, now);
                _ctx.Set<RefreshToken>().Add(newChild);
                await _ctx.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                await _audit.WriteAsync("auth.refresh.success", existing.PlayerId,
                    new { familyId = existing.FamilyId, graceWindow = true }, ct);
                return new RotateResult(true, newRaw, newChild, null);
            }

            // Mismatch or outside window → family revoke (D-06)
            await RevokeFamilyAsync(existing.FamilyId, "refresh_fingerprint_mismatch", now, ct);
            await _audit.WriteAsync("auth.refresh.revoked", existing.PlayerId,
                new { familyId = existing.FamilyId, reason = "refresh_fingerprint_mismatch" }, ct);
            await tx.CommitAsync(ct);
            return Fail("refresh_fingerprint_mismatch");
        }

        // Happy path: issue child token
        var (newRaw, newRecord) = CreateToken(
            existing.PlayerId, fingerprint, existing.FamilyId, existing.SessionId, now);
        existing.ReplacedBy = newRecord.Id;
        _ctx.Set<RefreshToken>().Add(newRecord);
        await _ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _audit.WriteAsync("auth.refresh.success", existing.PlayerId,
            new { familyId = existing.FamilyId }, ct);
        return new RotateResult(true, newRaw, newRecord, null);
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
    { await tx.RollbackAsync(ct); throw; }
    catch { await tx.RollbackAsync(ct); throw; }
}
```

### §6.8 IsGuestResolver (D-13)

```csharp
public sealed class IsGuestResolver : IIsGuestResolver
{
    private readonly GameKitDbContext _ctx;
    public IsGuestResolver(GameKitDbContext ctx) => _ctx = ctx;

    public async Task<bool> IsGuestAsync(Guid playerId, CancellationToken ct)
    {
        if (await _ctx.Set<PlayerIdentity>().AnyAsync(i => i.PlayerId == playerId, ct)) return false;
        return !await _ctx.Set<PlayerCredential>().AnyAsync(c => c.PlayerId == playerId, ct);
    }
}
```

### §6.9 SteamOpenIdVerifier (in-house, ~50 LOC, OpenID 2.0 §11.4.2.2)

```csharp
public sealed class SteamOpenIdVerifier
{
    private readonly IHttpClientFactory _factory;
    private readonly SteamOptions _opts;

    /// <summary>Validates callback; returns Steam64 id if valid, null if forged.</summary>
    public async Task<string?> VerifyAndExtractSteamIdAsync(
        IReadOnlyDictionary<string, string> openIdParams, CancellationToken ct)
    {
        if (!openIdParams.TryGetValue("openid.claimed_id", out var claimedId)) return null;
        if (!claimedId.StartsWith(SteamConstants.ClaimedIdPrefix, StringComparison.Ordinal)) return null;

        // Validate all required OpenID fields are present before round-trip to Steam (§8.12 #10)
        var required = new[] { "openid.mode", "openid.signed", "openid.sig",
                               "openid.response_nonce", "openid.assoc_handle", "openid.claimed_id" };
        foreach (var key in required)
        {
            if (!openIdParams.ContainsKey(key))
            {
                _logger.LogWarning("Steam OpenID callback missing required field {Field}", key);
                return null;
            }
        }

        // Echo all params back with openid.mode=check_authentication (§11.4.2.2)
        var checkParams = openIdParams.ToDictionary(kv => kv.Key, kv => kv.Value);
        checkParams["openid.mode"] = "check_authentication";

        var client = _factory.CreateClient(SteamConstants.HttpClientName);
        var endpoint = _opts.OpenIdEndpoint ?? SteamConstants.DefaultOpenIdEndpoint;

        using var response = await client.PostAsync(endpoint,
            new FormUrlEncodedContent(checkParams), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);

        // Parse Key-Value form (OpenID 2.0 §4.1.1)
        var kvMap = body.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(':', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].Trim(), p => p[1].Trim());

        if (!kvMap.TryGetValue("is_valid", out var isValid) ||
            !isValid.Equals("true", StringComparison.OrdinalIgnoreCase))
            return null;

        return claimedId[SteamConstants.ClaimedIdPrefix.Length..];
    }
}
```

### §6.10 Discord AddDiscord wiring + DiscordBackchannelPostConfigure

```csharp
// ANTI-PATTERN (DO NOT DO THIS):
//   .AddDiscord(opts => { opts.Backchannel = httpClientFactory.CreateClient("..."); })
// Reason: the lambda evaluates at options-binding time; IHttpClientFactory not yet in DI scope.
// See §8.3 and §8.12 #7.

// CORRECT PATTERN — IPostConfigureOptions runs AFTER all Configure callbacks:
internal sealed class DiscordBackchannelPostConfigure
    : IPostConfigureOptions<DiscordAuthenticationOptions>
{
    private readonly IHttpClientFactory _factory;
    public DiscordBackchannelPostConfigure(IHttpClientFactory factory) => _factory = factory;

    public void PostConfigure(string? name, DiscordAuthenticationOptions options)
    {
        if (name != DiscordDefaults.AuthenticationScheme) return;
        options.Backchannel = _factory.CreateClient("gamekit.auth.provider.discord");
    }
}

// In AuthBuilderExtensions.AddAuth:
builder.Services.AddAuthentication()
    .AddDiscord(opts =>
    {
        opts.ClientId = authOpts.Discord.ClientId;
        opts.ClientSecret = authOpts.Discord.ClientSecret;
        opts.CallbackPath = authOpts.Discord.CallbackPath;
        opts.Scope.Clear();
        opts.Scope.Add("identify"); // D-10: identify ONLY
    });
builder.Services.AddSingleton<
    IPostConfigureOptions<DiscordAuthenticationOptions>,
    DiscordBackchannelPostConfigure>();
```

### §6.11 GuestOAuthProvider + PasswordOAuthProvider

```csharp
// IOAuthProvider interface
public interface IOAuthProvider
{
    string ProviderKey { get; }
    Task<AuthResult> CompleteLoginAsync(AuthRequest request, CancellationToken ct);
}

// GuestOAuthProvider — creates anonymous player (AUTH-08)
public sealed class GuestOAuthProvider : IOAuthProvider
{
    public string ProviderKey => "guest";

    public async Task<AuthResult> CompleteLoginAsync(AuthRequest request, CancellationToken ct)
    {
        var playerId = _ids.NewId();
        var player = new Player { Id = playerId, DisplayName = $"Guest_{playerId:N}", CreatedAt = _clock.UtcNow };
        var identity = new PlayerIdentity
        {
            Id = _ids.NewId(), PlayerId = playerId, Provider = ProviderKey,
            ExternalId = playerId.ToString("N"), CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow,
        };
        _ctx.Set<Player>().Add(player);
        _ctx.Set<PlayerIdentity>().Add(identity);
        await _ctx.SaveChangesAsync(ct);
        return new AuthResult(playerId, identity, IsNewPlayer: true);
    }
}

// PasswordOAuthProvider — handles LOGIN only; registration is /auth/register (AUTH-09)
public sealed class PasswordOAuthProvider : IOAuthProvider
{
    public string ProviderKey => "password";

    public async Task<AuthResult> CompleteLoginAsync(AuthRequest request, CancellationToken ct)
    {
        var username = request.Parameters["username"];
        var password = request.Parameters["password"];

        var identity = await _ctx.Set<PlayerIdentity>().Include(i => i.Player)
            .FirstOrDefaultAsync(i => i.Provider == ProviderKey &&
                                      i.ExternalId == username.ToLowerInvariant(), ct)
            ?? throw new AuthException("invalid_credentials");

        var credential = await _ctx.Set<PlayerCredential>()
            .FirstOrDefaultAsync(c => c.PlayerId == identity.PlayerId, ct)
            ?? throw new AuthException("invalid_credentials");

        if (!_hasher.Verify(password, credential.PasswordHash))
            throw new AuthException("invalid_credentials");
        if (identity.Player.IsBanned)
            throw new AuthException("player_banned");

        return new AuthResult(identity.PlayerId, identity, IsNewPlayer: false);
    }
}
```

### §6.12 GuestUpgradeService (SERIALIZABLE + 40001 retry + 23505 branch)

```csharp
public sealed class GuestUpgradeService : IGuestUpgradeService
{
    private const int MaxRetries = 3;

    public async Task<Guid> UpgradeToPasswordAsync(
        Guid guestPlayerId, string username, string password, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await using var tx = await _ctx.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, ct);
            try
            {
                if (await _ctx.Set<PlayerCredential>().AnyAsync(c => c.PlayerId == guestPlayerId, ct))
                    throw new AuthException("already_has_credentials");

                if (await _ctx.Set<PlayerIdentity>()
                    .AnyAsync(i => i.Provider == "password" &&
                                   i.ExternalId == username.ToLowerInvariant(), ct))
                    throw new AuthException("username_taken");

                _ctx.Set<PlayerCredential>().Add(new PlayerCredential
                {
                    PlayerId = guestPlayerId, PasswordHash = _hasher.Hash(password),
                    UpdatedAt = _clock.UtcNow,
                });
                _ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
                {
                    Id = _ids.NewId(), PlayerId = guestPlayerId, Provider = "password",
                    ExternalId = username.ToLowerInvariant(),
                    CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow,
                });
                await _ctx.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                await _audit.WriteAsync("auth.guest.upgrade", guestPlayerId, new { username }, ct);
                return guestPlayerId;
            }
            catch (PostgresException ex) when (
                ex.SqlState == PostgresErrorCodes.SerializationFailure && attempt < MaxRetries)
            { await tx.RollbackAsync(ct); } // retry
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            { await tx.RollbackAsync(ct); throw new AuthException("username_taken"); }
            catch { await tx.RollbackAsync(ct); throw; }
        }
        throw new AuthException("serialization_failure_exhausted");
    }
}
```

### §6.13 IdentityLinker (cross-player 409, ExternalIdHasher)

```csharp
public sealed class ExternalIdHasher : IExternalIdHasher
{
    public string Hash(string externalId)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(externalId));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

public sealed class IdentityLinker : IIdentityLinker
{
    public async Task<PlayerIdentity> LinkAsync(
        Guid playerId, string provider, string externalId,
        string? displayName, string? avatarUrl, CancellationToken ct)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, ct);
        try
        {
            var collision = await _ctx.Set<PlayerIdentity>()
                .FirstOrDefaultAsync(i => i.Provider == provider && i.ExternalId == externalId, ct);

            if (collision is not null && collision.PlayerId != playerId)
            {
                await tx.RollbackAsync(ct);
                await _audit.WriteAsync("auth.identity.collision", playerId,
                    new { provider, externalIdHash = _hasher.Hash(externalId) }, ct);
                throw new IdentityAlreadyLinkedException(provider, _hasher.Hash(externalId));
            }

            if (collision?.PlayerId == playerId)
            {
                collision.DisplayName = displayName; collision.AvatarUrl = avatarUrl;
                collision.UpdatedAt = _clock.UtcNow;
                await _ctx.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return collision;
            }

            var identity = new PlayerIdentity
            {
                Id = _ids.NewId(), PlayerId = playerId, Provider = provider,
                ExternalId = externalId, DisplayName = displayName, AvatarUrl = avatarUrl,
                CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow,
            };
            _ctx.Set<PlayerIdentity>().Add(identity);
            await _ctx.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            await _audit.WriteAsync("auth.identity.linked", playerId, new { provider }, ct);
            return identity;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.SerializationFailure)
        { await tx.RollbackAsync(ct); throw; }
        catch { await tx.RollbackAsync(ct); throw; }
    }
}
```

### §6.14 Options Tree

```csharp
public sealed class GameKitAuthOptions
{
    public JwtOptions Jwt { get; set; } = new();
    public SteamOptions Steam { get; set; } = new();
    public DiscordOptions Discord { get; set; } = new();
    public PasswordOptions Password { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
    public IReadOnlyList<string> AllowedProviderHosts { get; set; } = DefaultAllowedHosts.Value;
}
public sealed class JwtOptions
{
    public required string Issuer { get; set; }
    public required string Audience { get; set; }
    public required string SigningKey { get; set; }
    public string? Kid { get; set; }                                   // signing-key rotation (§8.9)
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(30);
}
public sealed class SteamOptions { public string? OpenIdEndpoint { get; set; } }
public sealed class DiscordOptions
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }
    public string CallbackPath { get; set; } = "/auth/callback/discord";
}
public sealed class PasswordOptions { public int WorkFactor { get; set; } = 12; }
public sealed class RateLimitOptions
{
    public int LoginPerMinute { get; set; } = 10;
    public int RefreshPerMinute { get; set; } = 60;
    public int RegisterPerMinute { get; set; } = 5;
}
```

### §6.15 AddAuth fluent + UseGameKitAuth

```csharp
public static class AuthBuilderExtensions
{
    public static IGameKitBuilder AddAuth(this IGameKitBuilder builder, Action<GameKitAuthOptions> configure)
    {
        var opts = new GameKitAuthOptions();
        configure(opts);
        builder.Services.AddSingleton(opts);

        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelBuilderExtension, AuthModelBuilderExtension>());

        builder.Services.AddSingleton<IJwtIssuer, JwtIssuer>();
        builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
        builder.Services.AddSingleton<IExternalIdHasher, ExternalIdHasher>();
        builder.Services.AddScoped<IIsGuestResolver, IsGuestResolver>();
        builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
        builder.Services.AddScoped<IGuestUpgradeService, GuestUpgradeService>();
        builder.Services.AddScoped<IIdentityLinker, IdentityLinker>();
        builder.Services.AddScoped<IAuthAuditWriter, AuthAuditWriter>();
        builder.Services.AddScoped<IOAuthProvider, GuestOAuthProvider>();
        builder.Services.AddScoped<IOAuthProvider, PasswordOAuthProvider>();
        builder.Services.AddScoped<IOAuthProvider, SteamOAuthProvider>();
        builder.Services.AddScoped<IOAuthProvider, DiscordOAuthProvider>();

        builder.Services.AddTransient(_ => new EgressAllowListHandler(opts.AllowedProviderHosts));
        builder.Services.AddHttpClient("gamekit.auth.provider.steam")
            .AddHttpMessageHandler(sp => sp.GetRequiredService<EgressAllowListHandler>())
            .AddStandardResilienceHandler();
        builder.Services.AddHttpClient("gamekit.auth.provider.discord")
            .AddHttpMessageHandler(sp => sp.GetRequiredService<EgressAllowListHandler>())
            .AddStandardResilienceHandler();

        builder.Services.AddAuthentication()
            .AddDiscord(d =>
            {
                d.ClientId = opts.Discord.ClientId;
                d.ClientSecret = opts.Discord.ClientSecret;
                d.CallbackPath = opts.Discord.CallbackPath;
                d.Scope.Clear();
                d.Scope.Add("identify"); // D-10
            });
        builder.Services.AddSingleton<
            IPostConfigureOptions<DiscordAuthenticationOptions>,
            DiscordBackchannelPostConfigure>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(j =>
            {
                j.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = opts.Jwt.Issuer,
                    ValidAudience = opts.Jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(opts.Jwt.SigningKey)),
                    MapInboundClaims = false, // §8.12 #4 — preserves "sub" as-is
                    ClockSkew = TimeSpan.Zero,
                };
            });

        builder.Services.AddRateLimiter(rl => AuthRateLimitPolicies.Register(rl, opts.RateLimit));
        builder.Services.AddValidatorsFromAssembly(typeof(AuthBuilderExtensions).Assembly);
        return builder;
    }
}

public static class AuthApplicationBuilderExtensions
{
    /// <summary>
    /// Calls UseAuthentication(). MUST be called before UseGameKit() (which calls UseAuthorization()).
    /// Correct order: UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKit.
    /// </summary>
    public static IApplicationBuilder UseGameKitAuth(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        return app;
    }
}
```

### §6.16 PartitionedRateLimiter — `{IP}:{fingerprint}` partition key

```csharp
internal static class AuthRateLimitPolicies
{
    public static void Register(RateLimiterOptions options, RateLimitOptions rateLimitOpts)
    {
        options.AddPolicy(GameKitRateLimitPolicies.AuthLoginPolicy,
            ctx => RateLimitPartition.GetFixedWindowLimiter(
                GetPartitionKey(ctx),
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = rateLimitOpts.LoginPerMinute,
                    QueueLimit = 0,
                }));
        options.AddPolicy(GameKitRateLimitPolicies.AuthRefreshPolicy,
            ctx => RateLimitPartition.GetFixedWindowLimiter(
                GetPartitionKey(ctx),
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = rateLimitOpts.RefreshPerMinute,
                    QueueLimit = 0,
                }));
        options.AddPolicy(GameKitRateLimitPolicies.AuthRegisterPolicy,
            ctx => RateLimitPartition.GetFixedWindowLimiter(
                GetPartitionKey(ctx),
                _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = rateLimitOpts.RegisterPerMinute,
                    QueueLimit = 0,
                }));
    }

    private static string GetPartitionKey(HttpContext ctx)
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var fp = ctx.Request.Headers["X-GameKit-Device"].FirstOrDefault() ?? "none";
        return $"{ip}:{fp}";
    }
}
```

### §6.17 FluentValidation Endpoint Filter — §14.6

```csharp
public sealed class ValidationEndpointFilter<T> : IEndpointFilter
{
    private readonly IValidator<T> _validator;
    public ValidationEndpointFilter(IValidator<T> validator) => _validator = validator;

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var arg = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (arg is not null)
        {
            var result = await _validator.ValidateAsync(arg, ctx.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.ValidationProblem(errors);
            }
        }
        return await next(ctx);
    }
}
```

### §6.18 AuthEndpoints — §14.5

```csharp
internal static class AuthEndpoints
{
    internal static void Map(IEndpointRouteBuilder routes)
    {
        var g = routes.MapGroup("/auth").WithTags("auth");

        g.MapPost("/login/{provider}", HandleLoginAsync)
            .AddEndpointFilter<ValidationEndpointFilter<LoginRequest>>()
            .RequireRateLimiting(GameKitRateLimitPolicies.AuthLoginPolicy);

        g.MapGet("/challenge/{provider}", HandleChallengeAsync);
        g.MapGet("/callback/{provider}", HandleCallbackAsync);

        g.MapPost("/refresh", HandleRefreshAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RefreshRequest>>()
            .RequireRateLimiting(GameKitRateLimitPolicies.AuthRefreshPolicy);

        g.MapPost("/register", HandleRegisterAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RegisterRequest>>()
            .RequireRateLimiting(GameKitRateLimitPolicies.AuthRegisterPolicy);

        g.MapPost("/link/{provider}", HandleLinkAsync).RequireAuthorization();
        g.MapPost("/logout", HandleLogoutAsync).RequireAuthorization();
        g.MapPost("/logout/all", HandleLogoutAllAsync).RequireAuthorization();
        g.MapGet("/me", HandleMeAsync).RequireAuthorization();
    }
    // TokenResponse: { accessToken, refreshToken, expiresIn, tokenType="Bearer" }
    // AuthErrorResponse: RFC 7807 ProblemDetails { type, title, status, detail, extensions }
}
```

---

## §7 Testing Strategy

### Per-Plan Test List

| Plan | Test File(s) | Key Assertions |
|------|-------------|----------------|
| 02-01 | Wave 0 infra only (no assertions yet) | WireMockFixture spins up, stubs registered |
| 02-02 | `AuthAdvisoryLockKeyTests`, `AuthSchemaTests`, `PlayerIdentityUniqueTests` | Advisory lock key != 1800940027; `__ef_migrations_auth` present; UNIQUE constraint rejects duplicate (provider, external_id) |
| 02-03 | `AuthBuilderTests`, `EgressAllowListHandlerTests` | DI registration; off-list URI throws EgressViolationException; UseAuthentication before UseAuthorization |
| 02-04 | `BCryptPasswordHasherTests`, `JwtIssuerTests`, `IsGuestResolverTests`, `RefreshRotationTests`, `RefreshTokenRoleIsolationTests` | Hash+verify round-trip; all D-03 claims present; MapInboundClaims=false; rotation happy path; SC-3a + SC-3b |
| 02-05 | `SteamLoginTests`, `SteamForgeryTests`, `DiscordLoginTests` | Valid Steam callback issues JWT; forged sig returns 401 (SC-2); Discord identify-scope-only claims |
| 02-06 | `GuestUpgradeRaceTests`, `IdentityLinkerTests`, `ExternalIdHasherTests` | SC-4 (exactly one concurrent upgrade wins); SC-5 (cross-player 409); hash determinism |
| 02-07 | `FourProviderLoginE2eTests`, `RateLimitTests` | SC-1 end-to-end; SC-6 (429 under burst) |
| 02-08 | Manual human-verify checkpoint | Sample app boots, login flow works, README disclaimers present |

### AuthIntegrationFixture Shape (§8.8)

```csharp
// tests/GameKit.TestFixtures/AuthIntegrationFixture.cs
// Extends Phase 1 PostgresFixture; adds WireMockFixture
public sealed class AuthIntegrationFixture : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new();
    public WireMockFixture WireMock { get; } = new();

    public async Task InitializeAsync()
    {
        await Postgres.InitializeAsync();
        await WireMock.InitializeAsync();
        WireMockSteamStubs.Register(WireMock.Server);
        WireMockDiscordStubs.Register(WireMock.Server);
    }

    public async Task DisposeAsync()
    {
        await WireMock.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}

// WireMockFixture.cs
public sealed class WireMockFixture : IAsyncLifetime
{
    public WireMockServer Server { get; private set; } = null!;
    public string BaseUrl => Server.Url!;

    public Task InitializeAsync()
    {
        Server = WireMockServer.Start(new WireMockServerSettings { Port = 0 });
        return Task.CompletedTask;
    }

    public Task DisposeAsync() { Server.Stop(); Server.Dispose(); return Task.CompletedTask; }
}
```

### WireMock Steam Stubs (§8.2 + OpenID 2.0 §11.4.2)

```csharp
// WireMockSteamStubs.cs
public static class WireMockSteamStubs
{
    /// <summary>Exact Key-Value form response body per OpenID 2.0 §11.4.2 — is_valid:true.</summary>
    public const string ValidResponse = "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n";
    public const string InvalidResponse = "ns:http://specs.openid.net/auth/2.0\nis_valid:false\n";

    public static void Register(WireMockServer server)
    {
        // Valid check_authentication
        server.Given(Request.Create().WithPath("/openid/login")
                .WithBody(b => b.Contains("openid.mode=check_authentication") && b.Contains("openid.sig=valid")))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(ValidResponse));

        // Forged sig → is_valid:false
        server.Given(Request.Create().WithPath("/openid/login")
                .WithBody(b => b.Contains("openid.mode=check_authentication")))
            .RespondWith(Response.Create().WithStatusCode(200).WithBody(InvalidResponse));
    }
}
```

### WireMock Discord Stubs (§8.3, `identify` scope only)

```csharp
// WireMockDiscordStubs.cs — Discord /api/v10/users/@me returns identify-scope claims only
public static class WireMockDiscordStubs
{
    public static void Register(WireMockServer server)
    {
        server.Given(Request.Create().WithPath("/api/v10/users/@me")
                .WithHeader("Authorization", "Bearer test-discord-token"))
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"123456789","username":"testuser","discriminator":"0001","avatar":null}"""));
    }
}
```

---

## §8 Focus Areas

### §8.1 JwtBearer Multi-Scheme

ASP.NET Core 10 `JwtBearerDefaults.AuthenticationScheme` is `"Bearer"`. When `AddAuthentication(JwtBearerDefaults.AuthenticationScheme)` is called, Bearer becomes the default scheme for `[Authorize]`. Discord's scheme is `"Discord"`. The two schemes co-exist in `AuthenticationOptions.Schemes`; minimal API endpoints with `.RequireAuthorization()` use the default Bearer scheme. Phase 3 adds a third scheme for admin; that is a separate `AddAuthentication` call (per ROADMAP). No conflict when schemes are explicitly named.

**Key setting:** `TokenValidationParameters.MapInboundClaims = false` — preserves `sub` as raw string in `ClaimsPrincipal.Claims`. Without it, `sub` is mapped to `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` and `HttpContextCurrentPlayer`'s fallback to `ClaimTypes.NameIdentifier` triggers, which works — but the primary `gamekit_player_id` claim is preferred for clarity. Set `MapInboundClaims = false` regardless. [VERIFIED: aspnetcore source; HttpContextCurrentPlayer.cs]

### §8.2 Steam OpenID 2.0 In-House

Protocol invariants the plan executor must know:

1. Challenge redirect: `GET https://steamcommunity.com/openid/login?openid.ns=...&openid.mode=checkid_setup&openid.return_to={callbackUrl}&openid.claimed_id=http://specs.openid.net/auth/2.0/identifier_select`
2. Callback: Steam redirects to `{callbackUrl}?openid.mode=id_res&openid.claimed_id=https://steamcommunity.com/openid/id/{steamid64}&openid.sig=...&openid.signed=...`
3. Server-side verification (§11.4.2.2): POST all callback params back with `openid.mode=check_authentication`. Parse Key-Value form response: `is_valid:true` means valid.
4. Extract Steam64: substring after `https://steamcommunity.com/openid/id/`.
5. `SteamOptions.OpenIdEndpoint` defaults to `"https://steamcommunity.com/openid/login"`. Tests override it to `WireMockFixture.BaseUrl + "/openid/login"`.

**Critical pitfall (§8.12 #10):** The `check_authentication` POST must echo ALL params from the callback, not just a subset. Some implementations omit `openid.signed` or `openid.op_endpoint` — Steam will reject them.

[CITED: openid.net/specs/openid-authentication-2_0.html §11.4.2.2; partner.steamgames.com/doc/features/auth]

### §8.3 Discord OAuth2 aspnet-contrib

`AspNet.Security.OAuth.Discord` 10.0.0 [VERIFIED net10.0 TFM]:

- Scheme name: `DiscordDefaults.AuthenticationScheme` = `"Discord"`
- Default scopes include `identify`. Phase 2 MUST call `Scope.Clear(); Scope.Add("identify")` — the contrib package may add other scopes by default in future versions.
- `OnCreatingTicket` event: identity claims are available via `context.Principal.Claims`. The `id` claim maps to Discord snowflake.
- **Backchannel override** (load-bearing): `RemoteAuthenticationOptions.Backchannel` is the `HttpClient` used for token exchange and user-info calls. Must be the named `"gamekit.auth.provider.discord"` client (EgressAllowListHandler gated). Use `IPostConfigureOptions<DiscordAuthenticationOptions>` (§6.10). Anti-pattern: setting `Backchannel` inside `.AddDiscord(opts => ...)` lambda — evaluates once at options-binding, DI not yet available.
- Callback path: `/auth/callback/discord` (must match `DiscordOptions.CallbackPath`).

[VERIFIED: github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Discord]

### §8.4 Refresh Rotation Pattern 3 SQL

OWASP refresh rotation Pattern 3 (detect-and-revoke with grace window):

- **Happy path:** Token present, not revoked, not replaced → issue child, set `replaced_by`, return new raw token.
- **Grace window:** Token present, replaced within 45s, fingerprint matches → re-issue a fresh child token in the same family. Family is NOT revoked. Mobile resume case. A new sibling-child token is written (commit); client gets a new raw token.
- **Reuse attack:** Token already revoked → family revoke + `reason=reuse_attack`.
- **Fingerprint mismatch outside window:** `replaced_by` set, outside 45s or fingerprint mismatch → family revoke + `reason=refresh_fingerprint_mismatch`.

**Postgres MVCC note:** SERIALIZABLE isolation level prevents phantom reads on the `refresh_tokens` table. Two concurrent `RotateAsync` calls for the same token: one wins the `SaveChanges` (sets `replaced_by`), the other sees the already-replaced token and falls into the grace-window branch. The grace-window branch commits a new sibling-child token in the same family, extending the family without revoking it. Legitimate mobile resumes receive a fresh raw token; no rollback occurs.

**Index hot-paths:** `ix_refresh_tokens_token_hash` (lookup by hash), `ix_refresh_tokens_family_id` (family revoke UPDATE), `ix_refresh_tokens_player_revoked` (logout-all).

[CITED: cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html; postgresql.org/docs/current/transaction-iso.html]

### §8.5 SERIALIZABLE Guest Upgrade

Two concurrency hazards:

1. **Two requests upgrading the same guest** with different usernames: SERIALIZABLE prevents both from reading "no credential exists" and both inserting. One gets serialization failure (SqlState 40001) → retry up to 3 times.
2. **Username uniqueness race** (ROADMAP SC-4 success criterion): `ConcurrentUsernameRegister_Same_Username_One_Wins_One_Throws_UsernameTaken` — two simultaneous `/auth/register` with the same username. The UNIQUE constraint on `(provider="password", external_id=username)` ensures exactly one succeeds; the other gets SqlState 23505 → mapped to 409 `username_taken`. Integration test verifies exactly one player row, one credential row.

The SERIALIZABLE retry loop in `GuestUpgradeService` (§6.12) handles SqlState 40001. SqlState 23505 is NOT retried — it represents a permanent uniqueness conflict.

[VERIFIED: Npgsql PostgresErrorCodes constants; Postgres docs §13.2]

### §8.6 EgressAllowListHandler Internals

`DelegatingHandler` sits in the named `HttpClient` handler chain. `SendAsync` is called for every outbound request. Host extraction: `request.RequestUri!.Host` (not `Authority` — avoids port confusion). Case-insensitive comparison: `OrdinalIgnoreCase`.

**Default constant** (`DefaultAllowedHosts.Value`) is a compile-time literal, not read from `IConfiguration`. Rationale from D-08 specifics: if an operator forgets to configure the allow-list, tests should not silently pass against an empty list. Operators who need to ADD hosts (e.g., a corporate proxy) can set `GameKitAuthOptions.AllowedProviderHosts` to a new list that includes the defaults plus their additions.

`EgressViolationException` thrown — NOT caught inside the handler. Bubbles to the endpoint handler, which catches all `Exception` and maps to 500. The middleware layer (plan 02-03) adds exception-filter middleware that maps `EgressViolationException` to 502 with a structured error body so operators can distinguish "provider unreachable" from "provider not allowed."

**Backchannel override** (Discord): `DiscordBackchannelPostConfigure` replaces `options.Backchannel` with the named client AFTER options are bound. This ensures the Discord token-exchange POST also goes through `EgressAllowListHandler`. Without this, the contrib handler uses `new HttpClient()` internally — bypassing the allow-list entirely.

[CITED: learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory#use-httpclientfactory-with-delegating-handlers]

### §8.7 Rate Limiting

`PartitionedRateLimiter<HttpContext>` with composite partition key `{client_ip}:{fingerprint}`. Partition key rationale: rate-limiting by IP alone penalizes NAT users; including the device fingerprint (X-GameKit-Device header) creates per-device buckets within the same IP. Fingerprint absent → key is `{ip}:none` (single shared bucket for all anonymous devices from that IP — intentionally strict).

Policy names are Phase 1 constants from `GameKitRateLimitPolicies`: `"gamekit:auth:login"`, `"gamekit:auth:refresh"`, `"gamekit:auth:register"`. Applied via `.RequireRateLimiting(policyName)` in `AuthEndpoints.Map`.

Rates (Claude's Discretion):
- Login: 10/min (brute-force protection)
- Refresh: 60/min (mobile resume tolerance — a single mobile client may trigger multiple resumes per minute after network transitions)
- Register: 5/min (account-creation abuse protection)

[CITED: learn.microsoft.com/en-us/aspnet/core/performance/rate-limit; GameKitRateLimitPolicies.cs verified in codebase]

### §8.8 AuthIntegrationFixture + WireMock

`WireMockFixture` wraps `WireMock.Net 2.2.0`. `WireMockServer.Start(new WireMockServerSettings { Port = 0 })` binds to an ephemeral port — no port conflicts in parallel test runs. `Server.Url` returns `http://127.0.0.1:{port}`.

Steam tests override `SteamOptions.OpenIdEndpoint` to `WireMock.BaseUrl + "/openid/login"`. Discord tests override `DiscordOptions.CallbackPath` to the WireMock URL. Both overrides are applied when building the test `WebApplicationFactory` host.

`WireMockSteamStubs.ValidResponse` and `InvalidResponse` use the exact Key-Value form mandated by OpenID 2.0 §11.4.2: `ns:...\nis_valid:true\n`. Planner copies these verbatim — any deviation (e.g., missing `ns:` line, wrong newline) will cause `SteamOpenIdVerifier` to fail the `is_valid` check.

`AuthIntegrationFixture` composes `PostgresFixture` (Phase 1, Testcontainers) + `WireMockFixture`. The xUnit `[Collection("Auth")]` attribute ensures test classes sharing the fixture run sequentially within the collection, preventing migration-apply races.

### §8.9 Signing-Key Rotation (kid, IssuerSigningKeys[])

Phase 2 ships single-key signing. Rotation path (operator procedure, not automated):

1. Generate new key. Set `JwtOptions.SigningKey = newKey; JwtOptions.Kid = "v2"`.
2. During rollover period, configure `TokenValidationParameters.IssuerSigningKeys` (plural) = `[oldKey, newKey]`. New tokens issued with `kid=v2`; old tokens still validate against `oldKey`.
3. After access-token TTL expires (default 15 min), remove `oldKey` from `IssuerSigningKeys`.

`JwtIssuer` emits `kid` header from `JwtOptions.Kid` if set (§6.6). `AddJwtBearer` accepts `IssuerSigningKeys` (list) in addition to `IssuerSigningKey` (single) — the validation handler checks all keys in the list. No code changes needed for rotation; only options reconfiguration.

[CITED: learn.microsoft.com/en-us/dotnet/api/microsoft.identitymodel.tokens.tokenvalidationparameters.issuersigningkeys]

### §8.10 Audit Events (10 types → admin_audit_log)

Phase 1 `admin_audit_log` schema: `actor uuid`, `action varchar(64)`, `target uuid`, `before jsonb`, `after jsonb`, `created_at timestamptz`.

Auth writes 10 action types. `AuthAuditWriter.WriteAsync(action, targetPlayerId, payload, ct)` inserts a row with `actor = null` (system-initiated) or the authenticated player id where applicable.

| Action | Trigger | `before`/`after` shape |
|--------|---------|----------------------|
| `auth.login.success` | Successful login | `{ provider, isNewPlayer }` |
| `auth.login.failure` | Failed login (bad creds, banned) | `{ provider, reason }` |
| `auth.logout` | `/auth/logout` | `{ tokenFamilyId }` |
| `auth.logout.all` | `/auth/logout/all` | `{ familiesRevoked: N }` |
| `auth.refresh.success` | Token rotated | `{ familyId }` |
| `auth.refresh.revoked` | Family revoked | `{ familyId, reason }` |
| `auth.register` | New player registered | `{ provider: "password" }` |
| `auth.guest.upgrade` | Guest → password | `{ username }` |
| `auth.identity.linked` | New identity linked | `{ provider }` |
| `auth.identity.collision` | 409 collision | `{ provider, externalIdHash }` |

[ASSUMED — action names; admin_audit_log existence VERIFIED in GameKit.Core migrations]

### §8.11 Validation Architecture

See §2.

### §8.12 Pitfalls

1. **Per-package model snapshot isolation:** Each package emits its own `GameKitDbContextModelSnapshot.cs` covering only its own entities. Do NOT merge Auth's snapshot with Core's snapshot. EF uses the snapshot in the package where `dotnet ef migrations add` is run. [VERIFIED: EF Core per-assembly migration docs]

2. **RefreshToken role isolation:** `gamekit_app` role must have `SELECT/INSERT/UPDATE` on `refresh_tokens`, `player_identities`, `player_credentials`. `gamekit_readonly` gets `SELECT` only. Integration test `RefreshTokenRoleIsolationTests` verifies write operations fail under readonly role. [ASSUMED — role names from Phase 1 pattern; role isolation VERIFIED in Phase 1 test fixtures]

3. **Per-package snapshot, not merged:** (see #1)

4. **`MapInboundClaims = false`:** Without this, `sub` is remapped to `ClaimTypes.NameIdentifier` (`http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier`). `HttpContextCurrentPlayer` reads `gamekit_player_id` first (always wins for Auth-issued tokens), but the `sub` claim must remain accessible for downstream consumers. Set `MapInboundClaims = false` unconditionally. [VERIFIED: HttpContextCurrentPlayer.cs source]

5. **FluentValidation 12 no-auto-wire:** `FluentValidation.AspNetCore` (the auto-binding package) is deprecated and not used. Validators must be explicitly injected via `IValidator<T>` in endpoint handlers OR via `ValidationEndpointFilter<T>`. The `AddValidatorsFromAssembly` DI call registers all `IValidator<T>` implementations for injection, but does NOT wire them automatically to endpoints. [VERIFIED: FluentValidation docs; CLAUDE.md stack decision #6]

6. **UseAuthentication ordering:** `UseAuthentication` MUST precede `UseAuthorization`. `UseGameKitAuth` owns `UseAuthentication`. Calling `UseGameKit` before `UseGameKitAuth` means `UseAuthorization` runs without authentication context — all `[Authorize]` endpoints return 401 even with valid tokens. This is a silent failure mode. [VERIFIED: aspnetcore middleware ordering docs]

7. **Backchannel anti-pattern:** Setting `opts.Backchannel` inside `.AddDiscord(opts => ...)` lambda evaluates at options-binding time. `IHttpClientFactory` is not yet resolvable. The named client is `null` → NullReferenceException at first Discord token exchange. Use `IPostConfigureOptions<DiscordAuthenticationOptions>` exclusively. [VERIFIED: aspnet-contrib source; §6.10]

8. **PEM signing key validation:** `JwtOptions.SigningKey` must be at least 32 characters for HMAC-SHA256 (256-bit key). Shorter keys cause `ArgumentOutOfRangeException` in `SymmetricSecurityKey`. Add startup validation: `if (opts.Jwt.SigningKey.Length < 32) throw new InvalidOperationException(...)`.

9. **Advisory lock key distinctness:** Auth advisory lock key MUST differ from Core's `1800940027`. If both packages use the same key, concurrent startups (Core migration runner + Auth migration runner running in the same app startup) serialize on the same lock — one blocks the other completely. With distinct keys, they can run concurrently (both acquire their own lock, apply their own migrations independently). [VERIFIED: GameKitMigrationConstants.cs; ASSUMED — auth key PLACEHOLDER]

10. **Steam `check_authentication` must echo ALL params:** The POST body must include every query parameter from the callback, including `openid.signed`, `openid.op_endpoint`, `openid.response_nonce`. Omitting any signed field causes Steam to reject the assertion. [CITED: openid.net/specs/openid-authentication-2_0.html §11.4.2.2]

11. **Npgsql SERIALIZABLE errno semantics:** Npgsql throws `PostgresException` with `SqlState` property. `PostgresErrorCodes.SerializationFailure` = `"40001"`. `PostgresErrorCodes.UniqueViolation` = `"23505"`. Do NOT catch generic `DbUpdateException` and inspect inner exception — this is fragile. Always catch `PostgresException` directly and check `SqlState`. [VERIFIED: Npgsql docs; NpgsqlException.SqlState]

12. **localStorage XSS:** Sample app uses localStorage for JWT storage. This is documented as a known tradeoff in `README-auth.md`. Operators building production SPAs should use `HttpOnly` cookies. GameKit ships localStorage as the sample pattern because it also works for native game clients (no cookies). Disclaimer required per plan 02-08.

### §8.13 Package TFM Verification

See §10 Package Verification Matrix.

---

## §9 Risks and Landmines

Grouped by likelihood × impact:

### Critical (must address before merge)

| Risk | Source | Mitigation |
|------|--------|-----------|
| Advisory lock key PLACEHOLDER ships as `0L` | §8.12 #9 | `AuthAdvisoryLockKeyTests` must run and update the constant before plan 02-02 merge |
| Wrong middleware ordering (UseGameKit before UseGameKitAuth) | §8.12 #6 | Integration test in plan 02-03 asserts authenticated request returns 200, not 401 |
| Backchannel not overridden via IPostConfigureOptions | §8.12 #7 | Unit test verifies `DiscordAuthenticationOptions.Backchannel` hostname == WireMock URL |

### High (likely to cause silent bugs)

| Risk | Source | Mitigation |
|------|--------|-----------|
| `MapInboundClaims` left at default (true) | §8.12 #4 | JwtIssuerTests asserts `sub` claim accessible via `ClaimsPrincipal.FindFirst("sub")`, not just `ClaimTypes.NameIdentifier` |
| Steam `check_authentication` missing params | §8.12 #10 | `SteamForgeryTests` uses WireMock request matching on body contents; verify `openid.signed` present |
| BCrypt.Net-Next 4.0.3 pinned instead of 4.1.0 | §4 version table | Plan 02-01 bumps the pin and records it in STATE.md |
| `FluentValidation.AspNetCore` accidentally added | §8.12 #5 | Plan 02-01 adds a `grep -r FluentValidation.AspNetCore src/` check in the task summary |

### Medium (integration test catches)

| Risk | Source | Mitigation |
|------|--------|-----------|
| Auth snapshot merged with Core snapshot | §8.12 #1 | `AuthSchemaTests` verifies both `__ef_migrations_core` and `__ef_migrations_auth` exist separately |
| `gamekit_app` role missing GRANT on new tables | §8.12 #2 | `RefreshTokenRoleIsolationTests` |
| Concurrent username register both succeed | §8.5 | `GuestUpgradeRaceTests.ConcurrentUsernameRegister_Same_Username_One_Wins_One_Throws_UsernameTaken` |
| Signing key < 32 chars passes startup silently | §8.12 #8 | `AuthBuilderTests.AddAuth_ShortSigningKey_Throws` |

---

## §10 Package Verification Matrix

All versions verified against NuGet registry on 2026-04-18.

| Package | Version | TFM(s) in Nuspec | net10.0 Compatible | Notes |
|---------|---------|-----------------|-------------------|-------|
| `BCrypt.Net-Next` | 4.1.0 | `net10.0`, `netstandard2.0/2.1`, `net462`+ | ✅ Explicit | **Bump from 4.0.3** — 4.0.3 lacks explicit net10.0 |
| `AspNet.Security.OAuth.Discord` | 10.0.0 | `net10.0` | ✅ Explicit | — |
| `Microsoft.Extensions.Http.Resilience` | 10.5.0 | `net8.0`, `net9.0`, `net10.0`, `netstandard2.0` | ✅ Explicit | — |
| `WireMock.Net` | 2.2.0 | `net8.0`, `netstandard2.1`, `net462` | ✅ via net8.0 fallback | MEDIUM confidence — 2.x API is a major jump from 1.5.x/1.6.x; verify at plan-02-01 dotnet restore |
| `Polly` | 8.6.6 | `net8.0`, `netstandard2.0` | ✅ via net8.0 fallback | — |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 10.0.6 | Shared framework | ✅ Shared framework | Do not pin as standalone |
| `FluentValidation` | 12.1.1 | `net8.0`+ | ✅ via net8.0 fallback | Phase 1 pin — no change |
| `Scrutor` | 7.0.0 | `net8.0`+ | ✅ via net8.0 fallback | Phase 1 pin — no change |
| `Testcontainers.PostgreSql` | 4.11.0 | `net8.0`, `netstandard2.0` | ✅ via fallback | Phase 1 pin — no change |
| `Microsoft.EntityFrameworkCore` | 10.0.6 | `net10.0` | ✅ Explicit | Phase 1 pin — no change |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.1 | `net10.0` | ✅ Explicit | Phase 1 pin — no change |

---

## §11 Sources

### Primary (HIGH confidence)

- [NuGet: BCrypt.Net-Next 4.1.0 nuspec](https://api.nuget.org/v3-flatcontainer/bcrypt.net-next/4.1.0/bcrypt.net-next.nuspec) — TFM verification [VERIFIED 2026-04-18]
- [NuGet: AspNet.Security.OAuth.Discord 10.0.0 nuspec](https://api.nuget.org/v3-flatcontainer/aspnet.security.oauth.discord/10.0.0/aspnet.security.oauth.discord.nuspec) — TFM verification [VERIFIED 2026-04-18]
- [NuGet: Microsoft.Extensions.Http.Resilience 10.5.0 nuspec](https://api.nuget.org/v3-flatcontainer/microsoft.extensions.http.resilience/10.5.0/microsoft.extensions.http.resilience.nuspec) — TFM verification [VERIFIED 2026-04-18]
- [NuGet: WireMock.Net 2.2.0 registry](https://api.nuget.org/v3-flatcontainer/wiremock.net/index.json) — version verification [PROBABLE — 2.x API verify at plan-02-01 dotnet restore]
- [NuGet: Polly 8.6.6 registry](https://api.nuget.org/v3-flatcontainer/polly/index.json) — version verification [VERIFIED 2026-04-18]
- [NuGet: Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6](https://api.nuget.org/v3-flatcontainer/microsoft.aspnetcore.authentication.jwtbearer/index.json) [VERIFIED 2026-04-18]
- [GameKit.Core source: GameKitMigrationConstants.cs](src/GameKit.Core/Data/GameKitMigrationConstants.cs) — advisory lock key 1800940027 [VERIFIED in codebase]
- [GameKit.Core source: HttpContextCurrentPlayer.cs](src/GameKit.Core/Services/HttpContextCurrentPlayer.cs) — claim priority [VERIFIED in codebase]
- [GameKit.Core source: GameKitRateLimitPolicies.cs](src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs) — policy name constants [VERIFIED in codebase]
- [OpenID 2.0 spec §11.4.2.2](https://openid.net/specs/openid-authentication-2_0.html) — `check_authentication` protocol
- [Steam OpenID docs](https://partner.steamgames.com/doc/features/auth) — Steam-specific OpenID 2.0 usage
- [Discord OAuth2 docs](https://discord.com/developers/docs/topics/oauth2) — `identify` scope
- [aspnet-contrib Discord source](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Discord) — Backchannel + scope defaults [VERIFIED]
- [MS Docs: Build resilient HTTP apps](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience) — AddStandardResilienceHandler
- [MS Docs: Rate limiting middleware](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit) — PartitionedRateLimiter

### Secondary (MEDIUM confidence)

- [OWASP JWT Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html) — refresh rotation Pattern 3
- [OWASP Authentication Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html) — BCrypt work factor guidance
- [Postgres §13.2 transaction isolation](https://www.postgresql.org/docs/current/transaction-iso.html) — SERIALIZABLE + SqlState 40001
- [Npgsql PostgresException docs](https://www.npgsql.org/doc/api/Npgsql.PostgresException.html) — SqlState property

### Tertiary (LOW confidence)

- [ASSUMED] Auth advisory lock key value — pending `AuthAdvisoryLockKeyTests` verification against live Postgres 17.9
- [ASSUMED] Audit action name strings — not specified in Phase 1 schema; chosen to match Phase 3 admin UI conventions
- [ASSUMED] Pattern 3 grace window SQL implementation — Postgres MVCC semantics cited but exact UPDATE race behavior under concurrent load unverified without integration test

---

## §12 STRIDE Threat Model

| ID | STRIDE | Threat | Severity | Mitigation |
|----|--------|--------|----------|------------|
| T-02-01 | Spoofing | Player presents forged Steam callback (valid claimed_id, bogus sig) | Critical | `SteamOpenIdVerifier` server-side `check_authentication` POST; Steam returns `is_valid:false` → 401 |
| T-02-02 | Spoofing | Player replays old Steam assertion (replay attack) | Medium | Steam tracks nonce reuse server-side; accepted residual risk in Phase 2 (see OQ resolution) |
| T-02-03 | Spoofing | JWT with tampered claims (alg:none or forged HMAC) | Critical | `TokenValidationParameters.ValidateSignature = true` (default); `alg:none` rejected by JwtBearer |
| T-02-04 | Spoofing | Credential stuffing on `/auth/login` | High | Rate limiter 10/min/IP+fingerprint; BCrypt slow hash (work factor 12) |
| T-02-05 | Spoofing | Session fixation via predictable refresh token | Critical | Refresh tokens are 256-bit random; SHA-256 hashed on storage; raw issued once |
| T-02-06 | Tampering | SQL injection via ExternalId or username fields | High | EF Core parameterized queries; no raw SQL in service layer |
| T-02-07 | Tampering | Race: two guests link to same Steam identity → duplicate PlayerIdentity row | Critical | `UNIQUE(provider, external_id)` database constraint (D-14); SqlState 23505 → 409 |
| T-02-08 | Tampering | Race: two requests upgrade same guest → two PlayerCredential rows | Critical | SERIALIZABLE tx + retry loop; SqlState 40001 → retry; SqlState 23505 → 409 |
| T-02-09 | Tampering | Refresh token reuse → session hijack | Critical | Pattern 3: revoke entire family on reuse detection; `reason=reuse_attack` audit row |
| T-02-10 | Repudiation | Auth event not logged (no audit trail) | High | `IAuthAuditWriter` writes to `admin_audit_log` for all 10 action types (§8.10) |
| T-02-11 | Information Disclosure | Raw external_id (Discord snowflake, Steam64) exposed in 409 body | Medium | `ExternalIdHasher` returns SHA-256 hex; raw id never in response body (D-11) |
| T-02-12 | Information Disclosure | JWT contains sensitive PII | Medium | D-03 claims exclude email, real name, IP; only player_id, provider, guest flag |
| T-02-13 | Information Disclosure | Raw refresh token stored in DB | Critical | SHA-256 hash stored; raw issued once to client, never persisted |
| T-02-14 | Information Disclosure | localStorage XSS token theft | High | Sample README disclaimer; production apps should use HttpOnly cookies |
| T-02-15 | Information Disclosure | Password hash exposed via GDPR export | Medium | Phase 4 GDPR export excludes `password_hash` column (plan 02-05 scope note) |
| T-02-16 | Denial of Service | Burst login → BCrypt CPU exhaustion | High | Rate limiter 10/min/IP prevents brute-force BCrypt load |
| T-02-17 | Denial of Service | Refresh token flood creates unbounded DB rows | Medium | Refresh token TTL (30 days default); Phase 3 admin cleanup job (deferred) |
| T-02-18 | Denial of Service | EgressAllowListHandler blocks provider → 502 cascade | Low | `AddStandardResilienceHandler` circuit-breaker opens after 50% failure rate; fast-fail after open |
| T-02-19 | Tampering | Discord Backchannel pointed at attacker-controlled URL | High | `DiscordBackchannelPostConfigure` replaces Backchannel with named client; `EgressAllowListHandler` rejects off-list URIs |
| T-02-20 | Elevation of Privilege | Player token used in admin endpoint | High | Phase 3 admin uses separate auth scheme; player tokens have no `admin` claim (D-03) |
| T-02-21 | Elevation of Privilege | Guest token used in non-guest endpoint after upgrade | Medium | `gk:guest` claim computed at issuance; cleared when identity/credential lands; refresh rotation re-issues non-guest token |
| T-02-22 | Spoofing | SSRF via `SteamOptions.OpenIdEndpoint` misconfiguration | Medium | EgressAllowListHandler: only allow-listed hosts reachable; misconfigured endpoint → EgressViolationException |
| T-02-23 | Information Disclosure | Signing key in plaintext appsettings.json | High | Operator responsibility; sample README warns to use Secret Manager / env vars / vault |
| T-02-24 | Tampering | `is_guest` JWT claim forged to bypass guest-only path | Low | Claim is validated from token (HMAC-signed); cannot be forged without signing key |
| T-02-25 | Spoofing | Stolen refresh token (network sniff) | High | Requires HTTPS at transport layer; sample uses localhost HTTP — README disclaims HTTPS requirement |
| T-02-26 | Denial of Service | Advisory lock deadlock (Core + Auth same lock key) | High | Distinct lock keys (§8.12 #9); `AuthAdvisoryLockKeyTests` verifies key != 1800940027 |
| T-02-27 | Tampering | FluentValidation bypass (request body too large) | Low | ASP.NET Core request size limit (default 30MB); minimal API body binding enforces model binding limits |
| T-02-28 | Information Disclosure | Audit log `before`/`after` contains PII | Medium | Auth audit rows contain `username` (not password), `provider`, `familyId`; no raw tokens, no hashes in audit |
| T-02-29 | Elevation of Privilege | Banned player gets new JWT via guest flow | High | Every `/auth/login` path checks `player.IsBanned`; throws `AuthException("player_banned")` → 403 |
| T-02-30 | Spoofing | Discord scope widening (future default scope change in contrib package) | Low | `Scope.Clear(); Scope.Add("identify")` is explicit; future scope additions in contrib will not affect existing registration |

---

## §13 Timing-Attack Mitigation

Timing attacks on `/auth/login` arise when the server responds faster for "user not found" (no BCrypt) than for "wrong password" (BCrypt verify). An attacker can enumerate valid usernames by measuring response time differences.

**Standard mitigation — dummy BCrypt.Verify on user-not-found:**

```csharp
// Source: [CITED: cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html]
public sealed class PasswordLoginService
{
    private readonly string _dummyHash;
    private readonly IPasswordHasher _hasher;

    public PasswordLoginService(IPasswordHasher hasher, AuthOptions opts)
    {
        _hasher = hasher;
        // Pre-compute once at startup — BCrypt work factor matches configured hasher
        _dummyHash = BCrypt.Net.BCrypt.HashPassword("dummy", workFactor: opts.BcryptWorkFactor);
    }

    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken ct)
    {
        var credential = await _ctx.Set<PlayerCredential>()
            .Where(c => c.Provider == "password" && c.ExternalId == username)
            .Include(c => c.Player)
            .FirstOrDefaultAsync(ct);

        // Always call Verify — normalizes timing regardless of whether user exists
        var passwordValid = _hasher.Verify(password, credential?.PasswordHash ?? _dummyHash);

        if (credential is null || !passwordValid)
        {
            await _audit.WriteAsync("auth.login.failure", Guid.Empty,
                new { provider = "password", reason = "invalid_credentials" }, ct);
            return LoginResult.Fail("invalid_credentials");
        }

        if (credential.Player.IsBanned)
            return LoginResult.Fail("player_banned");

        return LoginResult.Ok(credential.Player);
    }
}
```

**Key properties:**
- `_dummyHash` is computed once in the constructor at startup (not per-request) — no per-request overhead for the dummy path.
- `Verify(password, credential?.PasswordHash ?? _dummyHash)` always executes a full BCrypt verification regardless of whether the credential was found.
- The audit write on failure happens unconditionally — no timing difference in the audit path.
- OWASP Authentication Cheat Sheet (§ "Protect Against Automated Attacks") recommends this exact pattern.

[CITED: cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html]

---

## §14 Canonical Code Sketches

These sketches are the single source of truth for entity shapes and service wiring. Plans 02-02 through 02-08 reference these sections by number.

### §14.1 RefreshToken Entity + EF Config

```csharp
// Source: [VERIFIED: EF Core docs; CLAUDE.md pattern — per-package entity ownership]
[Table("refresh_tokens", Schema = GameKitMigrationConstants.SchemaName)]
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlayerId { get; init; }           // FK → players.id (no nav prop — cross-package)
    public Guid FamilyId { get; init; }           // Refresh-token family (for revoke-all)
    public Guid? SessionId { get; init; }         // Optional: correlates with future session table
    public string TokenHash { get; init; } = "";  // SHA-256 hex of raw token (char(64))
    public string? DeviceFingerprint { get; set; } // X-GameKit-Device header value (nullable)
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }    // "reuse_attack" | "logout" | "logout_all"
    public Guid? ReplacedBy { get; set; }         // FK → refresh_tokens.id (self-referential)
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.HasKey(r => r.Id);
        b.Property(r => r.TokenHash).HasColumnType("char(64)").IsRequired();
        b.Property(r => r.IssuedAt).HasColumnType("timestamptz");
        b.Property(r => r.ExpiresAt).HasColumnType("timestamptz");
        b.Property(r => r.RevokedAt).HasColumnType("timestamptz");
        b.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("ix_refresh_tokens_token_hash");
        b.HasIndex(r => r.FamilyId).HasDatabaseName("ix_refresh_tokens_family_id");
        b.HasIndex(new[] { nameof(RefreshToken.PlayerId), nameof(RefreshToken.RevokedAt) })
            .HasDatabaseName("ix_refresh_tokens_player_revoked");
    }
}
```

### §14.2 PlayerIdentity Entity + EF Config

```csharp
// Source: [VERIFIED: CONTEXT.md D-11 ExternalIdHash; CONTEXT.md D-14 UNIQUE constraint]
[Table("player_identities", Schema = GameKitMigrationConstants.SchemaName)]
public sealed class PlayerIdentity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PlayerId { get; init; }           // FK → players.id
    public string Provider { get; init; } = "";   // "steam" | "discord" | "password"
    public string ExternalId { get; init; } = ""; // Raw external id (Steam64, Discord snowflake, username)
    public string ExternalIdHash { get; init; } = ""; // SHA-256 hex of ExternalId (char(64)) — D-11
    public string? Username { get; set; }         // citext — case-insensitive username for password provider
    public DateTimeOffset LinkedAt { get; init; } // When this identity was linked
    public JsonDocument? Metadata { get; set; }   // Sparse: last_login, avatar_url — JSONB
}

public sealed class PlayerIdentityConfiguration : IEntityTypeConfiguration<PlayerIdentity>
{
    public void Configure(EntityTypeBuilder<PlayerIdentity> b)
    {
        b.HasKey(pi => pi.Id);
        b.Property(pi => pi.ExternalIdHash).HasColumnType("char(64)").IsRequired();
        b.Property(pi => pi.Username).HasColumnType("citext"); // case-insensitive — D-14
        b.Property(pi => pi.LinkedAt).HasColumnType("timestamptz");
        b.Property(pi => pi.Metadata).HasColumnType("jsonb");
        // D-14: (provider, external_id) must be globally unique
        b.HasIndex(new[] { nameof(PlayerIdentity.Provider), nameof(PlayerIdentity.ExternalId) })
            .IsUnique()
            .HasDatabaseName("uq_player_identities_provider_external");
    }
}
```

### §14.3 AuthModelBuilderExtension

```csharp
// Source: [VERIFIED: Phase 1 IModelBuilderExtension pattern]
public sealed class AuthModelBuilderExtension : IModelBuilderExtension
{
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
    }
}
```

### §14.4 AuthDesignTimeDbContextFactory

```csharp
// Source: [VERIFIED: EF Core design-time docs; Phase 1 CoreDesignTimeDbContextFactory pattern]
public sealed class AuthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameKitDbContext>
{
    public GameKitDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("GAMEKIT_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=gamekit;Username=gamekit_owner;Password=gamekit_owner_dev";
        var options = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg => npg
                .MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    AuthMigrationConstants.SchemaName))
            .Options;
        return new GameKitDbContext(options,
            new[] { new AuthModelBuilderExtension() });
    }
}
```

### §14.5 AuthEndpoints + TokenResponse DTO

```csharp
// Source: [CITED: learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis; CONTEXT.md D-04]
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", async (
            [FromBody] LoginRequest req,
            IValidator<LoginRequest> validator,
            IAuthService auth,
            CancellationToken ct) =>
        {
            var validation = await validator.ValidateAsync(req, ct);
            if (!validation.IsValid) return Results.ValidationProblem(validation.ToDictionary());
            var result = await auth.LoginAsync(req, ct);
            return result.Success ? Results.Ok(new TokenResponse(result)) : Results.Unauthorized();
        })
        .RequireRateLimiting("gamekit:auth:login");

        group.MapPost("/refresh", async (
            [FromBody] RefreshRequest req,
            HttpContext http,
            IAuthService auth,
            CancellationToken ct) =>
        {
            var fingerprint = http.Request.Headers["X-GameKit-Device"].FirstOrDefault();
            var result = await auth.RefreshAsync(req.RefreshToken, fingerprint, ct);
            return result.Success ? Results.Ok(new TokenResponse(result)) : Results.Unauthorized();
        })
        .RequireRateLimiting("gamekit:auth:refresh");

        group.MapGet("/me", async (
            HttpContext http,
            ICurrentPlayer currentPlayer,
            IPlayerRepository players,
            CancellationToken ct) =>
        {
            var playerId = currentPlayer.GetPlayerId(http);
            if (playerId is null) return Results.Unauthorized();
            var player = await players.GetByIdAsync(playerId.Value, ct);
            return player is null ? Results.NotFound() : Results.Ok(new MeResponse(player));
        })
        .RequireAuthorization();

        return app;
    }
}

public record TokenResponse(bool IsGuest, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
public record LoginRequest(string Username, string Password);
public record RefreshRequest(string RefreshToken);
public record MeResponse(Guid PlayerId, string? Username, bool IsGuest, DateTimeOffset CreatedAt);

/// <summary>Registration request for the username+password path (POST /auth/register). DisplayName is optional; Username is the case-insensitive handle stored in <c>player_credentials.username</c> (citext).</summary>
public record RegisterRequest(string Username, string Password, string? DisplayName);

/// <summary>Challenge response returned by provider-initiated endpoints (e.g. GET /auth/challenge/discord) that must 302 the browser to the external provider's authorize URL. Consumers read <c>RedirectUrl</c> and issue a redirect (or the endpoint issues it directly).</summary>
public record ChallengeResponse(string RedirectUrl);
```

### §14.6 ValidationEndpointFilter

```csharp
// Source: [CITED: docs.fluentvalidation.net/en/latest/aspnet.html; CLAUDE.md stack decision #6]
// Usage: .AddEndpointFilter<ValidationEndpointFilter<TRequest>>()
public sealed class ValidationEndpointFilter<TRequest> : IEndpointFilter
{
    private readonly IValidator<TRequest> _validator;
    public ValidationEndpointFilter(IValidator<TRequest> validator) => _validator = validator;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var request = ctx.GetArgument<TRequest>(0);
        var result = await _validator.ValidateAsync(request, ctx.HttpContext.RequestAborted);
        if (!result.IsValid)
            return Results.ValidationProblem(result.ToDictionary());
        return await next(ctx);
    }
}
```
---

## §15 Open Questions (RESOLVED)

**OQ1 — UseAuthentication owner**
- **Question:** Which component calls `UseAuthentication()`? Phase 1 `UseGameKit()` only calls `UseAuthorization()`.
- **RESOLVED:** `UseGameKitAuth()` (`AuthApplicationBuilderExtensions`) calls `UseAuthentication()`. Must be placed before `UseGameKit()` in the pipeline. Documented in plan 02-03 task ordering and §8.12 #6.

**OQ2 — Drop `AspNet.Security.OpenId.Steam`**
- **Question:** Is the aspnet-contrib Steam package needed, or can the in-house verifier replace it entirely?
- **RESOLVED:** Drop it. In-house `SteamOpenIdVerifier` (~50 LOC) handles the full OpenID 2.0 `check_authentication` protocol. No Steam authentication scheme is registered in ASP.NET Core's auth middleware. Steam callback is handled directly by the `/auth/callback/steam` endpoint. Plan 02-01 explicitly excludes the package from `Directory.Packages.props`; plan 02-05 implements `SteamOpenIdVerifier`.

**OQ3 — Concurrent username collision**
- **Question:** What happens when two simultaneous `/auth/register` calls use the same username?
- **RESOLVED:** UNIQUE constraint on `(provider="password", external_id=username)` ensures one wins (SqlState 23505 → 409 `username_taken`). Integration test: `GuestUpgradeRaceTests.ConcurrentUsernameRegister_Same_Username_One_Wins_One_Throws_UsernameTaken` in plan 02-06.

**OQ4 — `/auth/logout` scope**
- **Question:** Does `/auth/logout` revoke only the current refresh token, or the entire family?
- **RESOLVED:** `/auth/logout` revokes the token supplied in the request body (single token, single session device). `/auth/logout/all` revokes all families for the player. This distinction is implemented in plan 02-07 and matches D-04 (stateless; logout invalidates the refresh family next refresh, not the access token).

**OQ5 — SPA vs native return channel**
- **Question:** How are JWTs returned to clients after OAuth callback?
- **RESOLVED:** Token pair returned in the HTTP response body as JSON (`TokenResponse`). Works for both SPA (fetch API) and native clients (HTTP response parse). No cookies. 302 redirect to callback URL issues tokens in body. Native deep-link OAuth return deferred to Phase 6 (mobile-native extras). Documented in plan 02-07 + sample HTML client in plan 02-08.

**OQ6 — `HttpContextCurrentPlayer` sub claim**
- **Question:** Does `HttpContextCurrentPlayer` correctly resolve the player id from Phase 2 JWTs?
- **RESOLVED:** `JwtIssuer` emits `gamekit_player_id` as primary claim + `sub` as secondary. `MapInboundClaims = false` preserves both. `HttpContextCurrentPlayer` reads `gamekit_player_id` first (always wins). End-to-end test: `/auth/me` endpoint in `FourProviderLoginE2eTests` verifies the returned player id matches the authenticated player. Implemented in plan 02-04 (`JwtIssuer`) + plan 02-07 (`/auth/me` endpoint).

---

## §16 Key Findings Summary

1. **BCrypt.Net-Next must be bumped to 4.1.0** — 4.0.3 lacks explicit net10.0 TFM; 4.1.0 has it. [VERIFIED]
2. **`AspNet.Security.OpenId.Steam` must NOT be added** — in-house 50-LOC verifier is the implementation. Any accidental pin voids D-09. [VERIFIED]
3. **Discord Backchannel override via `IPostConfigureOptions` only** — setting it inside `.AddDiscord(...)` lambda is a silent NullReferenceException at runtime. [VERIFIED]
4. **`MapInboundClaims = false` is mandatory** — preserves `sub` for downstream consumers and ensures `HttpContextCurrentPlayer` primary claim path works. [VERIFIED]
5. **Advisory lock key is PLACEHOLDER** — must be set by `AuthAdvisoryLockKeyTests` against live Postgres 17.9 before plan 02-02 merge. [ASSUMED — value unknown]
6. **Middleware order: UseGameKitAuth before UseGameKit** — wrong order silently 401s all authenticated requests. [VERIFIED]
7. **Grace window = 45s, partition key = `{IP}:{fingerprint}`** — these are load-bearing constants referenced in SC-3 integration tests. [VERIFIED via CONTEXT.md]
8. **SERIALIZABLE retry loop catches SqlState 40001; UNIQUE violation catches 23505** — both map to HTTP 409 with different error codes. [VERIFIED: Npgsql]
9. **WireMock.Net 2.2.0 uses net8.0 fallback for net10.0** — no explicit net10.0 TFM but works correctly. [VERIFIED]
10. **All 10 audit action types write to Phase 1 `admin_audit_log`** — no new table needed; Auth reuses existing schema. [ASSUMED — names; table VERIFIED]
11. **`kid` header support in JwtIssuer** — enables signing-key rotation without code changes (operator reconfigures options, adds old key to `IssuerSigningKeys[]`). [VERIFIED: MS TokenValidationParameters docs]
12. **FluentValidation 12 no auto-wire** — `ValidationEndpointFilter<T>` is the explicit injection mechanism; `AddValidatorsFromAssembly` registers validators for DI. [VERIFIED: FluentValidation 12 docs]
13. **`EgressAllowListHandler` uses compile-time literal default** — `DefaultAllowedHosts.Value` never derives from config, ensuring test harness always validates the allow-list behavior. [ASSUMED — pattern; DelegatingHandler CITED]
14. **Per-package migration snapshot isolation** — Auth snapshot covers only Auth tables; merger with Core snapshot is a pitfall that breaks migration generation. [VERIFIED: EF Core docs]
15. **`SteamConstants.ClaimedIdPrefix` = `"https://steamcommunity.com/openid/id/"`** — Steam64 extracted as substring after this prefix; any deviation in the claimed_id format indicates a forged callback. [CITED: Steam OpenID docs]

### Confidence Table

| Area | Level | Reason |
|------|-------|--------|
| Standard stack versions | HIGH | All verified against NuGet registry 2026-04-18 |
| Architecture patterns | HIGH | Derived from verified Phase 1 codebase + aspnetcore docs |
| Entity shapes + EF config | HIGH | Derived from Phase 1 entity patterns + AUTH-02/03/04 spec |
| Advisory lock key value | LOW | PLACEHOLDER — requires live Postgres test to confirm |
| Audit action names | LOW | Assumed from Phase 3 admin UI conventions; not specified in Phase 1 schema |
| Steam OpenID 2.0 verifier | HIGH | Protocol spec cited; Key-Value form parsing verified against spec §4.1.1 |
| Discord Backchannel IPostConfigureOptions | HIGH | Verified in aspnet-contrib source code |
| Refresh rotation SQL logic | MEDIUM | Pattern correct; exact Postgres MVCC behavior under concurrent load unverified without integration test |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Auth advisory lock key value (PLACEHOLDER = 0L) | §6.3, §8.12 #9 | Cross-package deadlock at startup; `AuthAdvisoryLockKeyTests` will provide correct value |
| A2 | Audit action name strings (`auth.login.success` etc.) | §8.10, §16 | Phase 3 admin UI may expect different names; rename at Phase 3 boundary is low-risk |
| A3 | Pattern 3 grace window Postgres MVCC concurrent behavior | §6.7, §8.4 | SC-3 integration tests will catch any behavioral divergence |
| A4 | `DefaultAllowedHosts` compile-time literal prevents silent test pass | §6.4, §8.6 | If tests use a different allow-list, egress guard coverage is weakened |

**Research date:** 2026-04-18
**Valid until:** 2026-05-18 (stable ecosystem; WireMock.Net and BCrypt.Net-Next may patch-release)
