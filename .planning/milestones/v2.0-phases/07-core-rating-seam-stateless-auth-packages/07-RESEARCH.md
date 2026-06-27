<!-- REUSE-IgnoreStart -->
# Phase 7: Core Rating Seam + Stateless Auth Packages — Research

**Researched:** 2026-06-05
**Domain:** .NET 10 optional-port seam design; stateless NuGet sibling-package pattern; Argon2 rehash-on-verify; aspnet-contrib OAuth provider integration; custom OAuthHandler derivation
**Confidence:** HIGH (all findings grounded in direct src/ reads; package versions verified on nuget.org)

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Rating Seam (CORE-18)**
- `IPlayerRatingProvider` defined in `GameKit.Core` mirroring the existing `IPresenceProvider` optional-port pattern; method shape returns rating + RD for a player/ladder (align with Glicko-2 `double` rating already used).
- A null-object default implementation registered by Core returns the v1 behaviour (rating=0 / default RD) so Matchmaking-without-Rankings is unchanged. The MatchmakingService consumption wiring (reading the provider at `EnqueueAsync` and caching into the Redis ticket hash) is built in Phase 8, NOT here — but the seam + null-object default land here.

**Auth — Argon2 (AUTH-17/18)**
- `GameKit.Auth.Argon2` provides `Argon2idPasswordHasher : IPasswordHasher` using `Isopoh.Cryptography.Argon2` 2.0.0 (CC0). Params: m=65536 (64 MiB), t=3, p=1.
- BCrypt→Argon2 migration is rehash-on-verify via hash-format detection (`$2a$`/`$2b$` ⇒ BCrypt verify then re-hash with Argon2; `$argon2id$` ⇒ Argon2 verify). No `player_credentials` schema change — format prefix is sufficient discriminator (no migration).

**Auth — OAuth Providers (AUTH-19/20/21/22)**
- Google: `GameKit.Auth.Google` wraps `Microsoft.AspNetCore.Authentication.Google` 10.0.8 (no aspnet-contrib Google exists).
- Apple: `GameKit.Auth.Apple` wraps `AspNet.Security.OAuth.Apple` 10.0.0; `GenerateClientSecret = true` (ES256 client secret regenerated per exchange from a `.p8` private key via BCL `ECDsa.ImportPkcs8PrivateKey`); `sub` is stored as `external_id` (NOT email); name/email captured first-login-only; private-relay email stored as-is.
- Epic: `GameKit.Auth.Epic` is a custom `OAuthHandler<EpicOAuthOptions>` against Epic's standard OAuth2 endpoints — no NuGet dep (no maintained package exists).
- All four register their `IOAuthProvider` via the existing Scrutor scan (`publicOnly:false`) and honour the `(provider, external_id)` uniqueness contract; minimal scopes only. Conditional scheme registration mirrors the v1 Discord pattern.

**Distribution**
- All five new package IDs join the coordinated MinVer release train (same version, exact-pinned `[X.Y.Z]` sibling refs) — formal release-train wiring + version-assertion coverage is closed out in Phase 12 (DIST-07), but new `.csproj`s must follow the existing Directory.Build.props/Directory.Packages.props conventions now.

### Claude's Discretion

Exact interface method signatures, file layout, options-class shape, and test structure are at Claude's discretion — follow existing v1 patterns (`IPresenceProvider`, Discord `IOAuthProvider`, `BCryptPasswordHasher`, per-package csproj conventions). Discuss was skipped per user setting; research basis is `.planning/research/STACK.md`, `FEATURES.md`, `ARCHITECTURE.md`, `PITFALLS.md`, `SUMMARY.md`.

### Deferred Ideas (OUT OF SCOPE)

- Rating-aware EloRange consumption + `RankingsRatingSource` implementation + guardrails → Phase 8.
- Formal release-train version-assertion coverage for the 5 new packages → Phase 12 (DIST-07).
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CORE-18 | `IPlayerRatingProvider` optional-port interface in `GameKit.Core` with null-object default (rating=0 / default RD) — consumed by Matchmaking without a hard compile-time dep on Rankings | IPresenceProvider pattern verified in src/; registration via `GetService<T>` factory lambda confirmed in GameKitServiceCollectionExtensions.cs |
| AUTH-17 | `GameKit.Auth.Argon2` opt-in sibling package with `Argon2idPasswordHasher : IPasswordHasher` using Isopoh.Cryptography.Argon2 2.0.0 (m=64 MiB, t=3, p=1) | IPasswordHasher interface verified; BCryptPasswordHasher shape confirmed; Isopoh 2.0.0 verified on nuget.org |
| AUTH-18 | Transparent BCrypt→Argon2 migration via rehash-on-verify with hash-format detection; no forced password reset | PasswordOAuthProvider.CompleteLoginAsync verified as the single call site; `PlayerCredential.PasswordHash` column shape confirmed; NeedsRehash extension needed on IPasswordHasher |
| AUTH-19 | `GameKit.Auth.Google` wrapping `Microsoft.AspNetCore.Authentication.Google` 10.0.8 | Package verified at 10.0.8 on nuget.org; no aspnet-contrib Google exists (confirmed); IOAuthProvider + Discord provider pattern grounded in src/ |
| AUTH-20 | `GameKit.Auth.Apple` with ES256 per-exchange client secret, `sub` as canonical identity, first-login-only name/email | AspNet.Security.OAuth.Apple 10.0.0 verified; PlayerIdentity.Metadata JSONB column confirmed for relay-email storage |
| AUTH-21 | `GameKit.Auth.Epic` as custom `OAuthHandler<EpicOAuthOptions>` with no NuGet dep | Custom handler approach confirmed as correct; Epic endpoints are standard OAuth 2.0 auth-code |
| AUTH-22 | All new providers integrate with `IOAuthProvider` + `(provider, external_id)` uniqueness contract; minimal scopes; conditional scheme registration | Existing UNIQUE(provider, external_id) constraint on PlayerIdentity verified in src/; conditional scheme registration pattern verified from Discord in AuthBuilderExtensions.cs |
</phase_requirements>

---

## Summary

Phase 7 delivers the `IPlayerRatingProvider` rating-provider seam in `GameKit.Core` and four new stateless auth sibling packages. It is a zero-migration phase — every deliverable is pure interface + implementation code that plugs into existing DI registrations and existing database tables. The phase is the lowest-risk entry point for v2.0 because it establishes the foundation all subsequent phases depend on (the rating seam unblocks Phase 8's rating-aware matchmaking; the auth packages unblock Phase 10's account merge logic).

The `IPlayerRatingProvider` seam follows the established `IPresenceProvider` / `IPostSessionCompleteHandler` optional-port pattern: Core defines the interface; the concrete implementation lives in `GameKit.Rankings` (Phase 8); Core's builder registers a null-object default via a factory lambda so Matchmaking-without-Rankings degrades gracefully. Phase 7 does NOT wire the provider into `MatchmakingService.EnqueueAsync` — that single line of code wiring (replacing the `Rating: 0` hardcode at line 203 of MatchmakingService.cs) is Phase 8's work.

The Argon2 package introduces one non-obvious implementation requirement: `IPasswordHasher` must gain a `NeedsRehash(string hash)` method, and `PasswordOAuthProvider.CompleteLoginAsync` in `GameKit.Auth` must call it after a successful `Verify` to detect BCrypt hashes and transparently rehash + UPDATE `player_credentials.password_hash` in the same request scope. Without this, `Argon2idPasswordHasher` cannot signal to the caller that a rehash is needed (the interface returns only `bool` from `Verify`).

The four OAuth provider packages all follow the Discord provider shape (`IOAuthProvider` implementation, `internal sealed` class, Scrutor-discovered, conditional scheme registration). Google and Apple use ASP.NET Core `AuthenticationBuilder` handlers; Epic is a custom `OAuthHandler<T>` derivation. None require a migration — `player_identities` already stores `provider` as a free-form discriminator string with the `UNIQUE(provider, external_id)` constraint covering all providers.

**Primary recommendation:** Plan waves in parallel groups — (Wave 1) `IPlayerRatingProvider` seam in Core; (Wave 2, parallel) `IPasswordHasher.NeedsRehash` + `Argon2idPasswordHasher` + rehash wiring; (Wave 3, parallel) Google + Apple + Epic provider packages; (Wave 4) tests for all. The seam and auth providers are fully independent.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| `IPlayerRatingProvider` interface | `GameKit.Core` library | — | Core defines optional ports; sibling packages implement them without creating back-references (IPresenceProvider precedent) |
| `NullPlayerRatingProvider` default | `GameKit.Core` builder | — | Null-object default registered in `AddGameKit()` via factory lambda so Matchmaking degrades gracefully without Rankings |
| `PlayerRankingsProvider` (Phase 8) | `GameKit.Rankings` | — | Implementation belongs in the package that owns `player_ranks` — Phase 7 does not ship this |
| `Argon2idPasswordHasher` | `GameKit.Auth.Argon2` new package | `GameKit.Auth` (IPasswordHasher interface + rehash call site) | Sibling replaces the singleton `IPasswordHasher` registration; rehash call site stays in Auth's `PasswordOAuthProvider` |
| Google/Apple/Epic `IOAuthProvider` impls | `GameKit.Auth.Google/Apple/Epic` new packages | `GameKit.Auth` (Scrutor scan + `player_identities` table) | Each package adds an `IOAuthProvider` discovered by the existing Scrutor scan in `AddAuth()` |
| OAuth scheme registration (Google/Apple/Epic) | Each new auth package's `Add*()` extension | — | Conditional registration mirrors Discord; test harnesses supply credentials to enable |

---

## Standard Stack

### Core (Phase 7 net-new additions only — v1 stack unchanged)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Isopoh.Cryptography.Argon2` | `2.0.0` | Argon2id hashing in `GameKit.Auth.Argon2` | CC0 license; fully managed C# (no P/Invoke); direct `Hash()`/`Verify()` API; includes `SecureArray` for zeroed-on-dispose memory. OWASP 2025 recommended variant. [VERIFIED: nuget.org] |
| `Isopoh.Cryptography.Blake2b` | `2.0.0` | Transitive dep of Argon2 (hashing primitive) | Pulled automatically; pin in Directory.Packages.props for CPM reproducibility. [VERIFIED: nuget.org] |
| `Isopoh.Cryptography.SecureArray` | `2.0.0` | Transitive dep of Argon2 (zeroed memory) | Pulled automatically; pin for CPM. [VERIFIED: nuget.org] |
| `Microsoft.AspNetCore.Authentication.Google` | `10.0.8` | Google OAuth2 handler for `GameKit.Auth.Google` | Only first-party Microsoft package for Google (aspnet-contrib has no Google provider); MIT; net10.0 TFM. [VERIFIED: nuget.org] |
| `AspNet.Security.OAuth.Apple` | `10.0.0` | Apple Sign-In handler for `GameKit.Auth.Apple` | Same aspnet-contrib release train as Discord 10.0.0 (already in v1); ES256 client-secret generation built-in; Apache-2.0. [VERIFIED: nuget.org] |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | `8.14.0` (minimum; latest 8.x = 8.19.1) | Transitive dep of AspNet.Security.OAuth.Apple | Compatible with JwtBearer 10.0.6 (both use IdentityModel 8.x under the hood; no diamond conflict). [VERIFIED: nuget.org] |

### Supporting (no new libraries required)

Epic Games provider: zero new NuGet dependencies. `OAuthHandler<T>` is in `Microsoft.AspNetCore.App` (shared framework). [VERIFIED: ARCHITECTURE.md codebase read]

SignalR backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) is a Phase 11 dep, not Phase 7.

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `Microsoft.AspNetCore.Authentication.Google` | Hand-rolled `OAuthHandler<GoogleOAuthOptions>` | No benefit — the Microsoft package is the Google handler; aspnet-contrib doesn't ship one |
| `AspNet.Security.OAuth.Apple` | Hand-rolled ES256 JWT client-secret generation | Non-trivial: ES256 PKCS#8 .p8 import + short-lived JWT signing. The aspnet-contrib package is battle-tested and on the same v10 release train as the existing Discord dep |
| Custom `OAuthHandler<EpicOAuthOptions>` | Any third-party Epic NuGet | No maintained .NET package exists; Epic OAuth 2.0 is standard enough for `OAuthHandler<T>` with zero extra deps |
| `Isopoh.Cryptography.Argon2` | `Konscious.Security.Cryptography.Argon2` | Konscious uses `DeriveBytes` API (more ceremony); last NuGet release is older; potential native path on some platforms. Isopoh is CC0, fully managed, direct API |

**Installation (additions to Directory.Packages.props):**
```xml
<!-- GameKit.Auth.Argon2 -->
<PackageVersion Include="Isopoh.Cryptography.Argon2" Version="2.0.0" />
<PackageVersion Include="Isopoh.Cryptography.Blake2b" Version="2.0.0" />
<PackageVersion Include="Isopoh.Cryptography.SecureArray" Version="2.0.0" />

<!-- GameKit.Auth.Google -->
<PackageVersion Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.8" />

<!-- GameKit.Auth.Apple -->
<PackageVersion Include="AspNet.Security.OAuth.Apple" Version="10.0.0" />
<PackageVersion Include="Microsoft.IdentityModel.Protocols.OpenIdConnect" Version="8.14.0" />
```

**Version verification (nuget.org API, 2026-06-05):**

| Package | Verified Latest | Pinned | Status |
|---------|-----------------|--------|--------|
| `Isopoh.Cryptography.Argon2` | 2.0.0 | 2.0.0 | Current |
| `Isopoh.Cryptography.Blake2b` | 2.0.0 | 2.0.0 | Current |
| `Isopoh.Cryptography.SecureArray` | 2.0.0 | 2.0.0 | Current |
| `Microsoft.AspNetCore.Authentication.Google` | 10.0.8 | 10.0.8 | Current stable 10.x |
| `AspNet.Security.OAuth.Apple` | 10.0.0 | 10.0.0 | Current (no 10.x patch) |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | 8.19.1 | 8.14.0 (min) | Compatible with JwtBearer 10.0.6 |

---

## Package Legitimacy Audit

> slopcheck was run against the PyPI registry. All Phase 7 packages are **NuGet** packages, not PyPI packages — slopcheck [SLOP] verdicts are false-positives from ecosystem confusion (a known hallucination vector). NuGet verification was performed via direct nuget.org flat-container API calls.

| Package | Registry | Age | Downloads (approx) | Source Repo | slopcheck | Disposition |
|---------|----------|-----|--------------------|-------------|-----------|-------------|
| `Isopoh.Cryptography.Argon2` | nuget.org | ~8 yrs | 7M+ total | github.com/mheyman/Isopoh.Cryptography.Argon2 | N/A (NuGet) | Approved — verified via nuget.org API; confirmed in .planning/research/STACK.md |
| `Isopoh.Cryptography.Blake2b` | nuget.org | ~8 yrs | Transitive of Argon2 | Same repo | N/A (NuGet) | Approved — transitive; same author |
| `Isopoh.Cryptography.SecureArray` | nuget.org | ~8 yrs | Transitive of Argon2 | Same repo | N/A (NuGet) | Approved — transitive; same author |
| `Microsoft.AspNetCore.Authentication.Google` | nuget.org | ~8 yrs | 100M+ | github.com/dotnet/aspnetcore | N/A (NuGet) | Approved — Microsoft first-party package |
| `AspNet.Security.OAuth.Apple` | nuget.org | ~5 yrs | 3M+ | github.com/aspnet-contrib/AspNet.Security.OAuth.Providers | N/A (NuGet) | Approved — same aspnet-contrib org as Discord 10.0.0 already in v1 |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | nuget.org | ~9 yrs | 800M+ | github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet | N/A (NuGet) | Approved — Microsoft first-party; already transitively present via JwtBearer 10.0.6 |

**Packages removed due to slopcheck [SLOP] verdict:** none — all slopcheck verdicts were ecosystem-confusion false positives (NuGet packages checked against PyPI).
**Packages flagged as suspicious [SUS]:** none.

*slopcheck was run but operates on PyPI; all packages verified via NuGet flat-container API with HTTP 200 responses. Provenance confirmed via official documentation and .planning/research/STACK.md (HIGH confidence).*

---

## Architecture Patterns

### System Architecture Diagram

```
Phase 7 additions (zero migrations, zero new tables)

GameKit.Core/Services/
  IPlayerRatingProvider ─────────────────────────────────────────┐
  PlayerRatingSnapshot                                            │
  NullPlayerRatingProvider (null-object default, rating=0/RD=0)  │
       │                                                          │
       │ registered in AddGameKit() via GetService<T> factory     │
       ▼                                                          │
  GameKitServiceCollectionExtensions.AddGameKit()                 │
  (sp.GetService<IPlayerRatingProvider>() → null-object if absent)│
                                                                  │
GameKit.Matchmaking/                                              │
  MatchmakingService.EnqueueAsync ◄─────── Phase 8 wires this ───┘
  (IPlayerRatingProvider? left as null for Phase 8)


GameKit.Auth.Argon2/ ──────────────────────────────────────────────────────────
  Argon2idPasswordHasher : IPasswordHasher                 │
  ── Hash(pwd)      → Isopoh.Argon2.Hash(config)           │
  ── Verify(pwd,h)  → detect prefix → BCrypt or Argon2     │
  ── NeedsRehash(h) → true when prefix is "$2a$"/"$2b$"    │
       │                                                    │
       │ replaces BCryptPasswordHasher in DI                │
       │                                                    ▼
  GameKit.Auth/                                    player_credentials.password_hash
  PasswordOAuthProvider.CompleteLoginAsync()       (column unchanged — no migration)
  ── after _hasher.Verify() succeeds:
      if (_hasher.NeedsRehash(stored_hash))
          UPDATE player_credentials SET password_hash = _hasher.Hash(password)


GameKit.Auth.Google/ ───────────────────────────────────────────────────────────
GameKit.Auth.Apple/  ── Each package:                             │
GameKit.Auth.Epic/      ├─ IOAuthProvider impl (internal sealed)  │
                        │     Provider string (e.g. "google")     │
                        │     CompleteLoginAsync() → upsert        ▼
                        │     PlayerIdentity (provider, external_id=sub)
                        │                         player_identities
                        ├─ AuthenticationScheme registration       (no new tables)
                        │   conditional on ClientId+Secret present
                        └─ AddAuth().AddGoogle(opts => ...) extension method
                           auto-discovered by existing Scrutor scan in AddAuth()
```

### Recommended Project Structure (new packages)

```
src/
├── GameKit.Auth.Argon2/
│   ├── GameKit.Auth.Argon2.csproj
│   ├── AssemblyInfo.cs              # GPL header + InternalsVisibleTo("GameKit.Auth.Argon2.Tests")
│   ├── Builder/
│   │   └── Argon2BuilderExtensions.cs   # AddGameKit().AddAuth().UseArgon2()
│   ├── Configuration/
│   │   └── GameKitArgon2Options.cs      # MemoryCost, TimeCost, Lanes, Threads, HashLength
│   └── Services/
│       └── Argon2idPasswordHasher.cs    # : IPasswordHasher
│
├── GameKit.Auth.Google/
│   ├── GameKit.Auth.Google.csproj
│   ├── AssemblyInfo.cs
│   ├── Builder/
│   │   └── GoogleBuilderExtensions.cs   # AddGameKit().AddAuth().AddGoogle(opts => ...)
│   ├── Configuration/
│   │   └── GameKitGoogleOptions.cs      # ClientId, ClientSecret, CallbackPath
│   └── Providers/
│       └── Google/
│           └── GoogleOAuthProvider.cs   # internal sealed : IOAuthProvider
│
├── GameKit.Auth.Apple/
│   ├── GameKit.Auth.Apple.csproj
│   ├── AssemblyInfo.cs
│   ├── Builder/
│   │   └── AppleBuilderExtensions.cs    # .AddApple(opts => ...)
│   ├── Configuration/
│   │   └── GameKitAppleOptions.cs       # TeamId, KeyId, PrivateKeyPath, ClientSecretExpiresAfter
│   └── Providers/
│       └── Apple/
│           └── AppleOAuthProvider.cs    # internal sealed : IOAuthProvider
│
└── GameKit.Auth.Epic/
    ├── GameKit.Auth.Epic.csproj
    ├── AssemblyInfo.cs
    ├── Builder/
    │   └── EpicBuilderExtensions.cs     # .AddEpic(opts => ...)
    ├── Configuration/
    │   └── GameKitEpicOptions.cs        # ClientId, ClientSecret, CallbackPath
    └── Providers/
        └── Epic/
            ├── EpicOAuthOptions.cs      # : OAuthOptions
            ├── EpicOAuthHandler.cs      # : OAuthHandler<EpicOAuthOptions>
            └── EpicOAuthProvider.cs     # internal sealed : IOAuthProvider
```

```
src/GameKit.Core/Services/
  IPlayerRatingProvider.cs          # NEW interface + PlayerRatingSnapshot record
  NullPlayerRatingProvider.cs       # NEW null-object default
```

```
src/GameKit.Auth/Services/
  IPasswordHasher.cs                # MODIFIED — add NeedsRehash(string hash) method
src/GameKit.Auth/Providers/Password/
  PasswordOAuthProvider.cs          # MODIFIED — add rehash-on-verify after Verify() succeeds
```

### Pattern 1: Optional-Port Null-Object Registration (IPlayerRatingProvider)

**What:** Core defines an interface for an optional capability. The implementing package (Rankings) registers via `TryAddSingleton`. Core's builder registers a null-object via factory lambda as fallback.

**When to use:** When Core needs to consume a sibling-package capability without a hard compile-time dep at runtime.

**Verified precedent:** `IPresenceProvider` (src/GameKit.Core/Services/IPresenceProvider.cs) + `PresenceBuilderExtensions.cs` line 80: `builder.Services.TryAddSingleton<IPresenceProvider>(sp => sp.GetRequiredService<RedisPresenceProvider>())`; `PresencePanel.razor.cs` line 49: `_presence = Sp.GetService<IPresenceProvider>()` — nullable GetService. [VERIFIED: direct src/ read]

**IPlayerRatingProvider shape:**
```csharp
// Source: src/GameKit.Core/Services/ (new file, mirrors IPresenceProvider shape)
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Core.Services;

/// <summary>
/// Optional rating provider port. Implemented by <c>GameKit.Rankings</c>.
/// When not installed, returns Glicko-2 defaults so Matchmaking operates
/// in zero-rated mode (v1 behaviour).
/// </summary>
public interface IPlayerRatingProvider
{
    /// <summary>
    /// Returns Glicko-2 snapshots for <paramref name="playerIds"/> on <paramref name="ladderId"/>.
    /// Players with no rank row return the Glicko-2 standard defaults
    /// (Rating=1500.0, RatingDeviation=350.0, Volatility=0.06).
    /// </summary>
    ValueTask<IReadOnlyDictionary<Guid, PlayerRatingSnapshot>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default);
}

/// <summary>Immutable Glicko-2 rating snapshot for a single player on a single ladder.</summary>
public sealed record PlayerRatingSnapshot(
    Guid PlayerId,
    double Rating,
    double RatingDeviation,
    double Volatility);
```

**Null-object registration in AddGameKit():**
```csharp
// Source: GameKitServiceCollectionExtensions.cs (new block, mirrors IPresenceProvider pattern)
// Mirrors the IPostSessionCompleteHandler GetService<T> pattern already at line 85.
services.TryAddSingleton<IPlayerRatingProvider, NullPlayerRatingProvider>();
```

**NullPlayerRatingProvider:**
```csharp
// All players get Glicko-2 defaults (not rating=0 — consistent with IPresenceProvider null-object philosophy)
// Phase 8 wires MatchmakingService to read from this; null-object means zero-rated behaviour is preserved
// because QueuedPartyMember ctor uses r?.Rating ?? 0 fallback.
internal sealed class NullPlayerRatingProvider : IPlayerRatingProvider
{
    public ValueTask<IReadOnlyDictionary<Guid, PlayerRatingSnapshot>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default)
        => ValueTask.FromResult<IReadOnlyDictionary<Guid, PlayerRatingSnapshot>>(
               ImmutableDictionary<Guid, PlayerRatingSnapshot>.Empty);
}
```

### Pattern 2: IPasswordHasher.NeedsRehash + Rehash-on-Verify

**What:** `IPasswordHasher` gets a `NeedsRehash(string hash)` method. `BCryptPasswordHasher` always returns `false` (it owns BCrypt hashes; no rehash needed unless work-factor upgrade). `Argon2idPasswordHasher.NeedsRehash` returns `true` when the hash starts with `$2a$` or `$2b$`. `PasswordOAuthProvider.CompleteLoginAsync` calls `NeedsRehash` after a successful `Verify` and performs the UPDATE if needed.

**When to use:** Any time the active `IPasswordHasher` replaces its predecessor.

**IPasswordHasher extension:**
```csharp
// Source: src/GameKit.Auth/Services/IPasswordHasher.cs — add method
/// <summary>
/// Returns <c>true</c> when <paramref name="hash"/> was produced by a prior hasher
/// and should be transparently re-hashed on the next successful login.
/// <c>BCryptPasswordHasher</c> always returns <c>false</c>.
/// <c>Argon2idPasswordHasher</c> returns <c>true</c> for <c>$2a$</c> / <c>$2b$</c> prefixes.
/// </summary>
bool NeedsRehash(string hash);
```

**Argon2idPasswordHasher.Verify logic (AUTH-18):**
```csharp
// Source: GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs (new file)
public bool Verify(string password, string hash)
{
    if (hash.StartsWith("$2a$", StringComparison.Ordinal) ||
        hash.StartsWith("$2b$", StringComparison.Ordinal))
    {
        // BCrypt hash — verify with BCrypt so live migration can proceed
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch (BCrypt.Net.SaltParseException) { return false; }
    }
    // Argon2id hash — verify with Isopoh
    return Argon2.Verify(hash, password);
}

public bool NeedsRehash(string hash)
    => hash.StartsWith("$2a$", StringComparison.Ordinal) ||
       hash.StartsWith("$2b$", StringComparison.Ordinal);
```

**Dependency implication:** `GameKit.Auth.Argon2` needs a `PackageReference` to `BCrypt.Net-Next` to call `BCrypt.Verify` during the live migration window. This is a deliberate coupling — it is the only way to verify the stored BCrypt hash without schema migration. The coupling is documented in the package XML doc and in the options as a migration-window concern.

**Rehash call site in PasswordOAuthProvider (AUTH-18):**
```csharp
// Source: src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs
// After: if (!_hasher.Verify(password, credential.PasswordHash)) return Fail(...)
// Add:
if (_hasher.NeedsRehash(credential.PasswordHash))
{
    var tracked = await _ctx.Set<PlayerCredential>()
        .FirstAsync(c => c.PlayerId == credential.PlayerId, cancellationToken)
        .ConfigureAwait(false);
    tracked.PasswordHash = _hasher.Hash(password);
    tracked.UpdatedAt = _clock.UtcNow;
    await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
}
```

### Pattern 3: OAuth Provider Sibling Package Shape (Google/Apple/Epic)

**What:** Each new auth provider package follows the Discord provider pattern exactly: `internal sealed` `IOAuthProvider` implementation, discovered by the existing `Scrutor` scan in `AddAuth()`, plus a new `Add*()` extension method on `IGameKitBuilder` (or on `IGameKitAuthBuilder` if that type is added in Phase 7) that conditionally registers the ASP.NET Core authentication scheme.

**Verified shape from Discord provider:**
- File: `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` — `internal sealed class DiscordOAuthProvider : IOAuthProvider` [VERIFIED: direct read]
- Registration: `builder.Services.Scan(scan => scan.FromAssemblyOf<IOAuthProvider>().AddClasses(c => c.AssignableTo<IOAuthProvider>(), publicOnly: false).AsImplementedInterfaces().WithScopedLifetime())` — in `AuthBuilderExtensions.cs` line 115 [VERIFIED: direct read]
- Conditional scheme: `if (!string.IsNullOrEmpty(opts.Discord.ClientId) && !string.IsNullOrEmpty(opts.Discord.ClientSecret))` — line 200 [VERIFIED: direct read]
- The `OnCreatingTicket` callback resolves the `IOAuthProvider` via `ctx.HttpContext.RequestServices.GetServices<IOAuthProvider>()` and filters by `p.Provider == "discord"` [VERIFIED: direct read]

**Apple-specific delta from Discord pattern:**
```csharp
// Source: GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs
authBuilder.AddApple(apple =>
{
    apple.ClientId     = opts.ServiceId;        // Apple "Service ID" (not Team ID)
    apple.TeamId       = opts.TeamId;
    apple.KeyId        = opts.KeyId;
    apple.CallbackPath = opts.CallbackPath;     // default "/signin-apple"
    apple.GenerateClientSecret = true;           // per-exchange ES256 JWT — NEVER false
    apple.ClientSecretExpiresAfter = TimeSpan.FromDays(170);  // < 180 day safety margin
    apple.UsePrivateKey((keyId, ct) =>
        ValueTask.FromResult(
            System.Security.Cryptography.ECDsa.Create()
                .Tap(k => k.ImportPkcs8PrivateKey(
                    Convert.FromBase64String(opts.PrivateKeyBase64), out _))));
    apple.Scope.Clear(); apple.Scope.Add("name"); apple.Scope.Add("email");
    apple.SaveTokens = false;
    apple.Events.OnCreatingTicket = async ctx =>
    {
        var sub = ctx.Principal?.FindFirst("sub")?.Value;     // NOT email
        if (string.IsNullOrEmpty(sub)) return;
        var name  = ctx.Principal?.FindFirst("name")?.Value;  // first login only
        var email = ctx.Principal?.FindFirst("email")?.Value; // relay OK to store in metadata
        // resolve AppleOAuthProvider, call CompleteLoginAsync(sub, name, null, fp, ct)
    };
});
```

**Epic-specific delta — custom OAuthHandler derivation:**
```csharp
// Source: GameKit.Auth.Epic/Providers/Epic/EpicOAuthOptions.cs
public class EpicOAuthOptions : OAuthOptions
{
    public EpicOAuthOptions()
    {
        AuthorizationEndpoint = "https://www.epicgames.com/id/authorize";
        TokenEndpoint         = "https://api.epicgames.dev/epic/oauth/v1/token";
        UserInformationEndpoint = "https://api.epicgames.dev/epic/oauth/v1/userInfo";
        Scope.Add("basic_profile");
        CallbackPath = new PathString("/signin-epic");
    }
}

// Source: GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs
internal sealed class EpicOAuthHandler : OAuthHandler<EpicOAuthOptions>
{
    // Override CreateTicketAsync to extract Epic account ID and display name
    protected override async Task<AuthenticationTicket> CreateTicketAsync(
        ClaimsIdentity identity, AuthenticationProperties properties,
        OAuthTokenResponse tokens) { ... }
}
```

### Pattern 4: New Sibling Package csproj Shape

Every new sibling package follows `GameKit.Auth.csproj` exactly. Key elements confirmed from direct read: [VERIFIED: src/GameKit.Auth/GameKit.Auth.csproj]

1. `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — brings in shared framework types without extra NuGet weight
2. `<ProjectReference Include="..\GameKit.Auth\GameKit.Auth.csproj" />` — for the `IOAuthProvider` / `IPasswordHasher` interface
3. All package versions resolved via CPM (no version attributes in `<PackageReference>` elements)
4. `<ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` — source generator for `GameKitMarker` version stamp
5. `<PrivateAssets>all</PrivateAssets>` on `Microsoft.EntityFrameworkCore.Design` — new auth packages do NOT have migrations, so this ref is absent entirely
6. `<NoWarn>$(NoWarn);CS1591</NoWarn>` and `<WarningsAsErrors />` on test csproj (not on library csproj — library treats CS1591 as error per Directory.Build.props)

**Auth.Argon2 csproj additions beyond the base sibling shape:**
- `<PackageReference Include="Isopoh.Cryptography.Argon2" />`
- `<PackageReference Include="BCrypt.Net-Next" />` — required for BCrypt.Verify during live migration window

### Anti-Patterns to Avoid

- **Registering IPlayerRatingProvider as Required:** `GetRequiredService<IPlayerRatingProvider>()` throws on Core-only installs. Use `TryAddSingleton<IPlayerRatingProvider, NullPlayerRatingProvider>()` in Core so the null-object is always resolved.
- **Storing Apple relay email as external_id:** Apple `sub` is the stable canonical identifier. The relay email changes if the Apple Service ID is recreated. Store email in `PlayerIdentity.Metadata` JSONB only.
- **Caching the Apple ES256 client secret:** `GenerateClientSecret = true` generates a fresh JWT per authorization exchange. Never cache, never set to false.
- **Adding a migration to new auth packages:** These packages are stateless — they only add `IOAuthProvider` / `IPasswordHasher` implementations. Any migration would require a new advisory lock key and violate the Phase 7 "no migration" constraint.
- **Applying Scrutor scan to a new assembly for IOAuthProvider:** The existing scan `FromAssemblyOf<IOAuthProvider>()` scans the `GameKit.Auth` assembly only. New sibling packages' implementations are NOT in that assembly. Each new package must register its own `IOAuthProvider` with `services.AddScoped<IOAuthProvider, XxxOAuthProvider>()` in its `Add*()` extension method (the Scrutor scan in Auth discovers providers already in the Auth assembly; sibling packages must self-register).

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Apple ES256 client-secret JWT signing | Custom `ECDsa` + `JwtSecurityTokenHandler` pipeline | `AspNet.Security.OAuth.Apple` with `GenerateClientSecret = true` + `UsePrivateKey()` | The library handles PKCS#8 P-256 key import, JWT structure, expiry, and per-request regeneration. One wrong field in the Apple JWT format produces `invalid_client` with no useful error. |
| Argon2id hashing with secure memory zeroing | Custom Argon2 impl | `Isopoh.Cryptography.Argon2` with `SecureArray` | Argon2 has a complex memory-filling algorithm; side-channel resistance and parameter validation are non-trivial. CC0, 100% managed, 8-year track record. |
| BCrypt format-prefix detection during Argon2 migration | Custom regex on hash | `hash.StartsWith("$2a$", ...)` — BCrypt format is well-specified | Simple string prefix check; no parsing needed. BCrypt canonical format is `$2a$`/`$2b$`; Argon2id is `$argon2id$`. |
| Epic OAuth 2.0 auth-code flow | Full custom HTTP implementation | Derive `OAuthHandler<EpicOAuthOptions>` | `OAuthHandler<T>` handles PKCE, state, token exchange, and backchannel. Only `CreateTicketAsync` override needed. |
| Google OAuth scopes + `sub` extraction | Custom handler | `Microsoft.AspNetCore.Authentication.Google` | Microsoft's first-party handler; handles `code` flow, `state`, `nonce`, `sub` claim extraction. |

**Key insight:** In this domain every "hand-rolled" authentication component has exactly the kind of edge cases (clock skew, PKCE, token format, secret rotation) that produce silent security failures. The library surface for these providers is small (one `Add*()` call, one `OnCreatingTicket` callback) and the cost of a mistake is a production outage or account hijack.

---

## Common Pitfalls

### Pitfall 1: Apple Client-Secret Expiry → Production Outage at 6 Months

**What goes wrong:** If `GenerateClientSecret = false` and a static JWT is configured, Apple logins return `invalid_client` exactly 6 months after the last deploy — with no warning.

**Why it happens:** Apple mandates a short-lived ES256 JWT as the OAuth client secret. Developers familiar with other providers assume `ClientSecret` is a static string.

**How to avoid:**
- Set `GenerateClientSecret = true` unconditionally.
- Set `ClientSecretExpiresAfter = TimeSpan.FromDays(170)` (< 180-day safety margin).
- Load the `.p8` key from `GameKitAppleOptions.PrivateKeyBase64` (env var / secrets manager — never baked into image).
- Integration test: assert `options.GenerateClientSecret == true` and `options.ClientSecretExpiresAfter.TotalDays < 180`.

**Warning signs:** All Apple logins return `invalid_client`; other providers work normally. [VERIFIED: .planning/research/PITFALLS.md §Pitfall 1]

### Pitfall 2: Apple Private-Relay Email as Identity Key

**What goes wrong:** `OnCreatingTicket` uses the `email` claim as `external_id`. Apple relay emails are unique per app/org registration and change on credential recreation. Stored identities become invalid.

**How to avoid:** Always use `ctx.Principal.FindFirst("sub")` as `external_id`. Store email in `PlayerIdentity.Metadata` JSONB only. [VERIFIED: .planning/research/PITFALLS.md §Pitfall 2]

### Pitfall 3: Rehash Not Saved — BCrypt Hashes Never Migrate

**What goes wrong:** `Argon2idPasswordHasher.Verify` succeeds on a BCrypt hash, `NeedsRehash` returns true, but `PasswordOAuthProvider` does not execute the `UPDATE player_credentials SET password_hash = ...`. The column never migrates.

**How to avoid:** The UPDATE must happen inside the same HTTP request scope as the login — i.e., in `PasswordOAuthProvider.CompleteLoginAsync` after a successful verify. The `GameKitDbContext` is scoped to the request; `SaveChangesAsync` commits the new hash in the same transaction window.

**Warning signs:** After 30 days with Argon2 enabled, the majority of active users still have `$2a$`-prefixed hashes in `player_credentials`. [VERIFIED: .planning/research/PITFALLS.md integration gotchas table]

### Pitfall 4: Scrutor Scan Misses Sibling-Package IOAuthProvider Implementations

**What goes wrong:** The `Scrutor` scan in `AuthBuilderExtensions.cs` (`FromAssemblyOf<IOAuthProvider>()`) scans only the `GameKit.Auth` assembly. New implementations in `GameKit.Auth.Google`, `GameKit.Auth.Apple`, `GameKit.Auth.Epic` are in different assemblies and are not auto-discovered.

**How to avoid:** Each new provider's `Add*()` extension method explicitly registers the provider:
```csharp
builder.Services.AddScoped<IOAuthProvider, GoogleOAuthProvider>();
```
This is additive — the existing Scrutor scan still discovers built-in providers; explicit registration covers the new sibling-package providers.

**Warning signs:** `POST /auth/login/google` returns 404 or "provider not found" despite `AddGoogle()` being called; `GetServices<IOAuthProvider>()` returns no entry with `Provider == "google"`. [ASSUMED — inferred from Scrutor scan scope; verified by reading AuthBuilderExtensions.cs line 115]

### Pitfall 5: Diamond-Dependency Conflict Between IdentityModel Versions

**What goes wrong:** `AspNet.Security.OAuth.Apple` pulls `Microsoft.IdentityModel.Protocols.OpenIdConnect` >= 8.14.0. The existing `Microsoft.AspNetCore.Authentication.JwtBearer 10.0.6` also uses IdentityModel 8.x (pinned at 8.3.0 in Directory.Packages.props). These are different IdentityModel packages but they must be version-compatible.

**How to avoid:** Pin `Microsoft.IdentityModel.Protocols.OpenIdConnect` to a 8.x version compatible with JwtBearer 10.0.6. The `.planning/research/STACK.md` confirms the pairing is compatible: "Both use `Microsoft.IdentityModel.*` 8.x under the hood; no version conflict." [VERIFIED: STACK.md + nuget.org version matrix]. Verify by running `dotnet build` — any NU1109 downgrade error is the canary.

**Warning signs:** `NU1109: Detected package downgrade` in build output referencing IdentityModel packages.

### Pitfall 6: Epic Scope / Client-Auth Header Mismatch

**What goes wrong:** Epic's token endpoint uses HTTP Basic auth (`client_credentials` grant) for the client id/secret, not a form body. `OAuthHandler<T>`'s default `ExchangeCodeAsync` sends them as form fields.

**How to avoid:** Override `ExchangeCodeAsync` in `EpicOAuthHandler` to add `Authorization: Basic base64(clientId:clientSecret)` header. Alternatively set `OAuthOptions.UsePkce = false` and configure the handler's `TokenEndpointAuthMethod`. Verify against Epic EOS sandbox before merge. [ASSUMED — Epic documentation; flag for execution-time verification against live EOS credentials]

---

## Code Examples

### IPlayerRatingProvider Registration in AddGameKit()

```csharp
// Source: pattern derived from GameKitServiceCollectionExtensions.cs lines 80-87 (GetService<T> factory)
// and PresenceBuilderExtensions.cs line 80 (TryAddSingleton with factory)
// NEW block to add in AddGameKit() after existing optional-port registrations:
services.TryAddSingleton<IPlayerRatingProvider, NullPlayerRatingProvider>();
```

### Argon2idPasswordHasher — Full Hash/Verify Shape

```csharp
// Source: derived from BCryptPasswordHasher.cs shape + Isopoh API docs (mheyman.github.io)
// File: GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs
internal sealed class Argon2idPasswordHasher : IPasswordHasher
{
    private readonly Argon2Config _config;

    public Argon2idPasswordHasher(GameKitArgon2Options opts)
    {
        _config = new Argon2Config
        {
            Type           = Argon2Type.HybridAddressing,   // Argon2id
            Version        = Argon2Version.Nineteen,
            MemoryCost     = opts.MemoryCost,                // default 65536 (64 MiB)
            TimeCost       = opts.TimeCost,                  // default 3
            Lanes          = opts.Lanes,                     // default 1
            Threads        = opts.Threads,                   // default 1
            HashLength     = opts.HashLength,                // default 32
        };
    }

    public string Hash(string password)
    {
        var cfg = _config with { Password = Encoding.UTF8.GetBytes(password) };
        using var argon2 = new Argon2(cfg);
        using var hash = argon2.Hash();
        return hash.ToString();   // "$argon2id$v=19$m=65536,t=3,p=1$..."
    }

    public bool Verify(string password, string hash)
    {
        if (hash.StartsWith("$2a$", StringComparison.Ordinal) ||
            hash.StartsWith("$2b$", StringComparison.Ordinal))
        {
            try { return BCrypt.Net.BCrypt.Verify(password, hash); }
            catch (BCrypt.Net.SaltParseException) { return false; }
        }
        return Argon2.Verify(hash, password);
    }

    public bool NeedsRehash(string hash)
        => hash.StartsWith("$2a$", StringComparison.Ordinal) ||
           hash.StartsWith("$2b$", StringComparison.Ordinal);
}
```

### GoogleOAuthProvider — OnCreatingTicket shape

```csharp
// Source: derived from DiscordOAuthProvider.cs + Google OIDC docs (sub as external_id)
google.Events.OnCreatingTicket = async ctx =>
{
    // Google's sub claim is a stable numeric string — use it as external_id, NOT email
    var sub   = ctx.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
             ?? ctx.User.GetString("sub");
    var name  = ctx.User.GetString("name");
    var email = ctx.User.GetString("email");  // informational only; not used as linking key
    var avatar = ctx.User.GetString("picture");
    if (string.IsNullOrEmpty(sub)) return;

    var provider = ctx.HttpContext.RequestServices
        .GetServices<IOAuthProvider>()
        .FirstOrDefault(p => p.Provider == "google");
    if (provider is null) return;

    var fp = ctx.HttpContext.Request.Headers["X-GameKit-Device"].ToString().NullIfEmpty();
    var result = await provider.CompleteLoginAsync(sub, name, avatar, fp, ctx.HttpContext.RequestAborted);
    if (result is { Success: true, Tokens: not null })
    {
        ctx.Properties.Items["gamekit.access_jwt"]  = result.Tokens.AccessJwt;
        ctx.Properties.Items["gamekit.refresh_raw"] = result.Tokens.RawRefresh;
        ctx.Properties.Items["gamekit.player_id"]   = result.PlayerId?.ToString();
    }
};
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Rating=0 hardcode in MatchmakingService.EnqueueAsync | IPlayerRatingProvider optional-port seam | Phase 7 (this phase — interface only; consumption in Phase 8) | Unblocks rating-aware matchmaking without creating Matchmaking→Rankings runtime dep |
| BCrypt-only IPasswordHasher | IPasswordHasher with NeedsRehash; Argon2id as opt-in sibling | Phase 7 | OWASP-recommended password hashing available without breaking existing BCrypt hashes |
| Steam + Discord providers only | + Google + Apple + Epic (all stateless, zero migration) | Phase 7 | Covers 4 of the top 5 identity providers for game backends |
| OAuthHandler custom handler (none) | Custom OAuthHandler for Epic (shared-framework pattern) | Phase 7 | No NuGet dep for Epic; zero external supply-chain risk |

**Deprecated/outdated:**
- `Rating: 0` hardcode in `MatchmakingService.cs` line 203: replaced by seam consumption in Phase 8 (do NOT touch in Phase 7).
- `BCrypt.Net-Next`-only password hashing: still the default; Argon2 is opt-in via `UseArgon2()`.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The existing Scrutor scan `FromAssemblyOf<IOAuthProvider>()` in `AddAuth()` scans only `GameKit.Auth` assembly, not sibling package assemblies | Architecture Patterns §Pitfall 4 | New providers not discovered at runtime; `GetServices<IOAuthProvider>()` returns no "google"/"apple"/"epic" entries. Mitigation: explicit `AddScoped<IOAuthProvider, XxxProvider>()` in each Add*() method. |
| A2 | Epic's token endpoint requires HTTP Basic auth header (not form fields) | Code Examples §Pitfall 6 | `ExchangeCodeAsync` fails with `400 Bad Request` from Epic EOS. Verify at execution time with live EOS sandbox credentials. |
| A3 | `Argon2.Verify(hash, password)` in Isopoh takes the stored hash string as the first argument (not the raw bytes) | Code Examples §Argon2 shape | Argon2 verification silently fails for all users; login blocked until corrected. Must confirm from Isopoh API docs / unit test before merge. |

**If this table is empty: not applicable — three assumptions documented above.**

---

## Open Questions

1. **Argon2.Verify signature — hash first or password first?**
   - What we know: Isopoh's `Argon2.Verify(string encoded, string password)` is documented at mheyman.github.io/Isopoh.Cryptography.Argon2; API docs confirm the encoded hash string is the first argument.
   - What's unclear: whether the signature changed in 2.0.0 vs earlier versions.
   - Recommendation: The planner should add a Wave 0 unit test that round-trips `Hash` → `Verify` before any other task.

2. **Epic token endpoint auth method — form vs Basic header**
   - What we know: Epic EOS standard docs show client credentials in the Authorization header for some grant types.
   - What's unclear: whether `OAuthHandler<T>`'s default `ExchangeCodeAsync` sends form or header format.
   - Recommendation: Override `ExchangeCodeAsync` in `EpicOAuthHandler` to add the Basic auth header. Flag as "unit-testable with a WireMock stub" but "only fully verified against live EOS credentials."

3. **IGameKitAuthBuilder vs IGameKitBuilder as extension target**
   - What we know: `AddAuth()` returns `IGameKitBuilder` (not a new `IGameKitAuthBuilder`). New provider Add methods should extend `IGameKitBuilder` with the caller first calling `AddAuth()`.
   - What's unclear: whether an `IGameKitAuthBuilder` returned by `AddAuth()` should be introduced in Phase 7 to enforce ordering.
   - Recommendation: Do not introduce a new builder type in Phase 7. Use `IGameKitBuilder` as the extension target and document ordering in XML docs. This matches the existing v1 pattern.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | All new packages | ✓ | 10.0.106 (via global.json) | — |
| Postgres (Testcontainers) | Integration tests for rehash-on-verify | ✓ | 17.9 (Testcontainers 4.11.0) | — |
| Redis (Testcontainers) | Not required (Phase 7 is stateless) | ✓ | Available | — |
| Apple `.p8` key | Apple provider integration test | ✗ | — | Mock with WireMock.Net stub; flag as "live credentials required for full integration" |
| Epic EOS credentials | Epic provider integration test | ✗ | — | Mock with WireMock.Net stub; flag as "live credentials required for full integration" |
| Google OAuth credentials | Google provider integration test | ✗ | — | Mock with WireMock.Net stub |

**Missing dependencies with no fallback:** none.

**Missing dependencies with fallback:**
- Apple `.p8` key: all integration tests use WireMock.Net stubs. A separate note in test XML docs flags that a real `.p8` key is needed for live-credential smoke test (acceptable human-verify gate before production deploy).
- Epic EOS + Google OAuth: same WireMock stub approach as Apple.

---

## Validation Architecture

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | Not needed — follows per-package convention |
| Quick run command | `dotnet test tests/GameKit.Auth.Argon2.Tests/ -x` |
| Full suite command | `dotnet test --filter "FullyQualifiedName~GameKit.Auth" -x` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CORE-18 | `IPlayerRatingProvider` registered as singleton; `NullPlayerRatingProvider` returns empty dict | Unit | `dotnet test tests/GameKit.Core.Tests/ -x -filter "IPlayerRatingProvider"` | ❌ Wave 0 |
| CORE-18 | Matchmaking resolves `null` for `IPlayerRatingProvider?` when Rankings not installed | Unit | `dotnet test tests/GameKit.Core.Tests/ -x -filter "NullRatingProvider"` | ❌ Wave 0 |
| AUTH-17 | `Argon2idPasswordHasher.Hash()` returns `$argon2id$`-prefixed string | Unit | `dotnet test tests/GameKit.Auth.Argon2.Tests/ -x -filter "HashPrefixTest"` | ❌ Wave 0 |
| AUTH-17 | `Hash()` → `Verify()` round-trip returns true for correct password | Unit | `dotnet test tests/GameKit.Auth.Argon2.Tests/ -x -filter "RoundTrip"` | ❌ Wave 0 |
| AUTH-17 | Default params exceed OWASP minimums (m≥19456, t≥2) | Unit | `dotnet test tests/GameKit.Auth.Argon2.Tests/ -x -filter "OptionsDefaults"` | ❌ Wave 0 |
| AUTH-18 | `NeedsRehash("$2a$12$...")` returns true; `NeedsRehash("$argon2id$...")` returns false | Unit | `dotnet test tests/GameKit.Auth.Argon2.Tests/ -x -filter "NeedsRehash"` | ❌ Wave 0 |
| AUTH-18 | `Verify()` with BCrypt hash returns true for correct password (live migration path) | Unit | `dotnet test tests/GameKit.Auth.Argon2.Tests/ -x -filter "BCryptVerifyCompat"` | ❌ Wave 0 |
| AUTH-18 | `PasswordOAuthProvider.CompleteLoginAsync` updates `player_credentials.password_hash` when `NeedsRehash` returns true | Integration (Testcontainers Postgres) | `dotnet test tests/GameKit.Auth.Integration.Tests/ -x -filter "ArgonRehash"` | ❌ Wave 0 |
| AUTH-19 | Google `OnCreatingTicket` extracts `sub` as `external_id` (not email) | Unit (WireMock) | `dotnet test tests/GameKit.Auth.Google.Tests/ -x -filter "SubNotEmail"` | ❌ Wave 0 |
| AUTH-20 | Apple `GenerateClientSecret == true` and `ClientSecretExpiresAfter.TotalDays < 180` | Unit (options shape) | `dotnet test tests/GameKit.Auth.Apple.Tests/ -x -filter "ClientSecretOptions"` | ❌ Wave 0 |
| AUTH-20 | Apple `OnCreatingTicket` extracts `sub` as `external_id`; relay email to Metadata | Unit (WireMock) | `dotnet test tests/GameKit.Auth.Apple.Tests/ -x -filter "SubExtraction"` | ❌ Wave 0 |
| AUTH-21 | Epic `OAuthHandler` token exchange succeeds against WireMock stub | Unit (WireMock) | `dotnet test tests/GameKit.Auth.Epic.Tests/ -x -filter "TokenExchange"` | ❌ Wave 0 |
| AUTH-22 | `AddGoogle()` / `AddApple()` / `AddEpic()` registers `IOAuthProvider` in DI; `GetServices<IOAuthProvider>()` finds entries with Provider=="google"/"apple"/"epic" | Integration | `dotnet test tests/GameKit.Auth.{Google,Apple,Epic}.Tests/ -x -filter "DI_Smoke"` | ❌ Wave 0 |
| AUTH-22 | Provider scheme not registered when ClientId absent (conditional registration guard) | Unit | `dotnet test tests/GameKit.Auth.Google.Tests/ -x -filter "ConditionalScheme"` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet test tests/GameKit.Auth.Argon2.Tests/ tests/GameKit.Core.Tests/ -x`
- **Per wave merge:** `dotnet test --filter "FullyQualifiedName~GameKit.Auth || FullyQualifiedName~GameKit.Core" -x`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

- [ ] `tests/GameKit.Auth.Argon2.Tests/GameKit.Auth.Argon2.Tests.csproj` — new project
- [ ] `tests/GameKit.Auth.Argon2.Tests/Argon2HasherTests.cs` — covers AUTH-17, AUTH-18
- [ ] `tests/GameKit.Auth.Google.Tests/GameKit.Auth.Google.Tests.csproj` — new project
- [ ] `tests/GameKit.Auth.Google.Tests/GoogleProviderTests.cs` — covers AUTH-19, AUTH-22
- [ ] `tests/GameKit.Auth.Apple.Tests/GameKit.Auth.Apple.Tests.csproj` — new project
- [ ] `tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs` — covers AUTH-20, AUTH-22
- [ ] `tests/GameKit.Auth.Epic.Tests/GameKit.Auth.Epic.Tests.csproj` — new project
- [ ] `tests/GameKit.Auth.Epic.Tests/EpicProviderTests.cs` — covers AUTH-21, AUTH-22
- [ ] `tests/GameKit.Core.Tests/` additions: `IPlayerRatingProviderTests.cs` — covers CORE-18
- [ ] Framework install: already present (xUnit + Testcontainers in TestFixtures)

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | Yes | `IPasswordHasher` with Argon2id (OWASP-recommended) + BCrypt compatibility; OAuth providers via aspnet-contrib + Microsoft first-party handlers |
| V3 Session Management | No (existing refresh-token rotation unchanged; no new session surface in Phase 7) | — |
| V4 Access Control | No (no new endpoints in Phase 7) | — |
| V5 Input Validation | Yes (partial) | FluentValidation on provider options (ClientId not empty); `sub` claim validation in OnCreatingTicket |
| V6 Cryptography | Yes | Argon2id m=64MiB t=3 (OWASP 2025 minimum exceeded); Apple ES256 client secret per-exchange with BCL ECDsa; BCrypt.Net-Next work-factor-12 preserved for legacy hashes during migration window |

### Known Threat Patterns for Phase 7 Stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Apple client-secret reuse (static JWT) | Spoofing | `GenerateClientSecret = true`; assert in integration test |
| Apple private-relay email as identity key | Information Disclosure / Identity Confusion | Extract `sub` claim only; relay email to Metadata JSONB |
| Epic email unavailable for linking | Information Disclosure | Use `sub` only; do not offer email-based merge for Epic |
| BCrypt timing attack on user-not-found | Information Disclosure | Existing `DummyHash` path in `PasswordOAuthProvider` already mitigates; `Argon2idPasswordHasher.NeedsRehash` returns false for dummy hash (`$2a$12$...`) so timing is still BCrypt-equalized |
| Argon2 memory exhaustion under concurrent login burst | Denial of Service | Default `Lanes=1, Threads=1` limits per-verify memory to 64 MiB; tune if host has > 50 concurrent logins |
| `.p8` key in image layer | Spoofing / Elevation | Load from env var / volume mount; document in GameKitAppleOptions XML |

---

## Project Constraints (from CLAUDE.md)

| Directive | Impact on Phase 7 |
|-----------|------------------|
| GPL license + per-file SPDX headers | All new `.cs` files in all 4 new packages require `// SPDX-License-Identifier: GPL-3.0-or-later` + copyright line |
| Zero cloud / SaaS dependencies | Apple `.p8` key loaded from local filesystem / env var; no Apple Developer API calls at runtime |
| .NET 10 LTS runtime | All new csproj files inherit `<TargetFramework>net10.0</TargetFramework>` from Directory.Build.props |
| `XML doc comments on every public API — no exceptions` | `CS1591` is `WarningsAsErrors` in Directory.Build.props; every public type in the 4 new packages requires XML docs |
| Per-package migration boundaries — never modify Core tables | No migrations in Phase 7; confirmed. New auth packages are stateless. |
| BCrypt.Net-Next for password hashing (default) | `GameKit.Auth.Argon2` must use BCrypt.Net-Next for the migration-window `Verify` path (dep already pinned in Directory.Packages.props) |
| No MediatR >= 13, no AutoMapper >= 13 | Not applicable to Phase 7 |
| MinVer coordinated release train | All 4 new package csproj files must follow the `GameKit.Build` Analyzer reference pattern for the `GameKitMarker` version stamp; sibling refs are exact-pinned at Pack time via GameKit.targets |
| `InternalsVisibleTo` grants on test assemblies | Each new package needs `AssemblyInfo.cs` with `[assembly: InternalsVisibleTo("GameKit.Auth.Argon2.Tests")]` etc. |

---

## Sources

### Primary (HIGH confidence)

- `src/GameKit.Core/Services/IPresenceProvider.cs` — optional-port pattern for IPlayerRatingProvider design [VERIFIED: direct read]
- `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` lines 80-87 — GetService\<T\> factory lambda pattern for optional ports [VERIFIED: direct read]
- `src/GameKit.Auth/Services/IPasswordHasher.cs` — interface to be extended with NeedsRehash [VERIFIED: direct read]
- `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` — hasher shape to mirror in Argon2idPasswordHasher [VERIFIED: direct read]
- `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` — IOAuthProvider implementation shape [VERIFIED: direct read]
- `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 115-119, 200-253 — Scrutor scan + conditional scheme registration [VERIFIED: direct read]
- `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` lines 95-138 — rehash call site in CompleteLoginAsync [VERIFIED: direct read]
- `src/GameKit.Auth/Entities/PlayerIdentity.cs` — Metadata JSONB column + UNIQUE(provider, external_id) [VERIFIED: direct read]
- `src/GameKit.Auth/Entities/PlayerCredential.cs` — PasswordHash column shape [VERIFIED: direct read]
- `src/GameKit.Rankings/Entities/PlayerRank.cs` — PlayerRatingSnapshot field names (Rating/RatingDeviation/Volatility as double) [VERIFIED: direct read]
- `src/GameKit.Auth/GameKit.Auth.csproj` — csproj pattern for sibling packages [VERIFIED: direct read]
- `Directory.Build.props` — CS1591-as-error, MinVer, SourceLink, GameKit.Build analyzer ref [VERIFIED: direct read]
- `Directory.Packages.props` — CPM pin pattern; confirmed BCrypt.Net-Next 4.1.0 already pinned [VERIFIED: direct read]
- `.planning/research/STACK.md` — package versions + NuGet verification (HIGH confidence, 2026-06-05)
- `.planning/research/ARCHITECTURE.md` — IPlayerRatingProvider interface shape + NullPlayerRatingProvider design + PlayerRankingsProvider placement
- `.planning/research/PITFALLS.md` — Apple ES256 expiry, private-relay identity, Argon2 rehash pitfalls
- `.planning/STATE.md` — v1 advisory lock keys, Scrutor publicOnly:false locked decision, Discord conditional scheme locked decision
- nuget.org flat-container API — all 6 new packages verified with HTTP 200 responses at specific versions [VERIFIED: direct API calls, 2026-06-05]

### Secondary (MEDIUM confidence)

- `.planning/research/FEATURES.md` §Argon2 — rehash-on-login pattern ("detect prefix, verify with correct hasher, re-hash, update player_credentials.password_hash in same request transaction")
- `.planning/research/SUMMARY.md` — Phase 7 build order rationale

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all package versions verified via nuget.org API; consistent with STACK.md
- Architecture: HIGH — grounded in direct src/ reads of all relevant files
- Pitfalls: HIGH — Apple pitfalls from PITFALLS.md; rehash pitfall from FEATURES.md + PasswordOAuthProvider code read
- IPasswordHasher NeedsRehash design: MEDIUM — the interface extension requirement is derived from reading the current interface (no `NeedsRehash`) and the requirement that rehash must be triggered by the caller, not the hasher itself; logical consequence confirmed by FEATURES.md §Argon2

**Research date:** 2026-06-05
**Valid until:** 2026-09-05 (stable packages; Apple/Epic endpoint URLs may change)
<!-- REUSE-IgnoreEnd -->
