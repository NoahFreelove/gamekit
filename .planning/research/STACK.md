# Stack Research

**Domain:** .NET game-services library — v2.0 additive stack (new packages only)
**Researched:** 2026-06-05
**Confidence:** HIGH (all versions verified GA on nuget.org as of research date)

---

## Scope

This document covers ONLY the new NuGet dependencies required for v2.0 features.
The existing v1.0 stack (EF Core 10.0.6, Npgsql 10.0.1, StackExchange.Redis 2.8.41,
FluentValidation 12.1.1, Scrutor 7, Polly 8, MinVer 7, xUnit + Testcontainers 4.11,
MudBlazor 9.3.0, BCrypt.Net-Next 4.1.0, etc.) is pinned and unchanged — do not
re-research or re-pin any v1 dependency.

---

## New Dependencies by Feature Area

### 1. GameKit.Auth.Argon2 — Argon2id password hasher (opt-in sibling package)

**Package:** `Isopoh.Cryptography.Argon2`
**Version:** `2.0.0`
**License:** CC0 (public domain dedication by author Michael Heyman — FSF-recognized free license, GPL-compatible with zero restrictions)
**NuGet TFMs shipped:** netcoreapp3.1, netstandard2.0, net6.0, net7.0
**net10.0 compatibility:** netstandard2.0 TFM runs on net10.0 without restriction. NuGet shows "computed" compatibility for net8.0/net9.0/net10.0 — meaning the runtime resolves it correctly via the netstandard2.0 asset. Confirmed GA on nuget.org 2026-06-05.
**Why Isopoh over alternatives:** 100% managed C# (no native bindings, no P/Invoke); runs identically on Linux/macOS/Windows/WASM; ships `SecureArray` (zeroed-on-dispose sensitive memory); `Hash()` / `Verify()` API is direct rather than a `DeriveBytes` dance; CC0 is zero-encumbrance. Konscious.Security.Cryptography.Argon2 is unmaintained relative to Isopoh and has a native path concern.

**Transitive pull-ins (must also appear in Directory.Packages.props):**
- `Isopoh.Cryptography.Blake2b` >= 2.0.0 (CC0)
- `Isopoh.Cryptography.SecureArray` >= 2.0.0 (CC0)

**Argon2id tuning params (OWASP 2024/2025 recommendation):**

Use `Argon2Type.HybridAddressing` (Argon2id). The OWASP Password Storage Cheat Sheet
recommends a minimum of m=19456 KiB, t=2, p=1. Isopoh defaults are m=65536 (64 MiB),
t=3, p=4 which exceed OWASP minimum — a good production baseline. Recommended
`GameKitArgon2Options` defaults for the `GameKit.Auth.Argon2` sibling package:

| Parameter | Isopoh property | Recommended value | Notes |
|-----------|----------------|-------------------|-------|
| Memory cost | `MemoryCost` | `65536` (64 MiB) | Matches Isopoh default; exceeds OWASP min of 19456 |
| Time cost | `TimeCost` | `3` | Matches Isopoh default; OWASP min is 2 |
| Parallelism | `Lanes` | `1` | OWASP recommends p=1; reduce CPU variance across nodes |
| Threads | `Threads` | `1` | Single-threaded matches Lanes=1 |
| Type | `Type` | `Argon2Type.HybridAddressing` | Argon2id — OWASP recommended variant |
| Hash length | `HashLength` | `32` | 256-bit output |

The `IPasswordHasher` contract from v1 `GameKit.Auth` is already defined — `GameKit.Auth.Argon2`
registers `Argon2idPasswordHasher : IPasswordHasher` and is swapped in by the consumer
calling `.AddArgon2Hasher()` on `IGameKitBuilder`.

---

### 2. OAuth providers — Google, Apple, Epic (opt-in sibling packages)

#### Google

**Recommended package:** `Microsoft.AspNetCore.Authentication.Google`
**Version:** `10.0.8` (latest GA as of 2026-06-05; released 2026-05-12)
**License:** MIT
**net10.0:** First-class net10.0 TFM; no dependencies (resolved from shared framework); confirmed GA.
**Why Microsoft's package over aspnet-contrib:** There is no `AspNet.Security.OAuth.Google` in the
aspnet-contrib registry — Google is not among the 80+ providers in `AspNet.Security.OAuth.Providers`
(that repo focuses on third-party platforms, not big-tech providers with their own first-party
Microsoft-maintained middleware). `Microsoft.AspNetCore.Authentication.Google` is the correct,
first-party, MIT-licensed package and is the standard recommendation in all ASP.NET Core 10 docs.
**Shared framework note:** The package is listed as part of `Microsoft.AspNetCore.App` usage
dependency graph, but it ships as a standalone NuGet to allow version-locked delivery. Add an
explicit `<PackageReference>` pin in `Directory.Packages.props` — the SDK does not implicitly
pull it.

**Package for `GameKit.Auth.Google`:**
```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.Google" Version="10.0.8" />
```

#### Apple

**Package:** `AspNet.Security.OAuth.Apple`
**Version:** `10.0.0` (released 2025-11-11; aligns with the aspnet-contrib v10 release train used for Discord in v1)
**License:** Apache-2.0 (GPL-compatible)
**net10.0:** Explicitly targets net10.0; no further compat concerns.
**Dependencies (net10.0):**
- `Microsoft.IdentityModel.Protocols.OpenIdConnect` >= 8.14.0 (MIT)

**Why aspnet-contrib for Apple but Microsoft's package for Google:** Apple Sign-In has no
Microsoft-maintained middleware package. `AspNet.Security.OAuth.Apple` is maintained by the
same team (Martin Costello / Kévin Chalet) as `AspNet.Security.OAuth.Discord` already in v1.
This keeps the Apple provider on the same v10.0 release train as Discord (consistent).

**Apple client-secret (ES256 / ECDSA P-256) — no additional library required:**
Apple requires a short-lived JWT signed with ES256 as the OAuth client secret, generated from
a `.p8` private key Apple issues from the Developer Portal. `AspNet.Security.OAuth.Apple`
handles this internally:
- Call `options.UsePrivateKey(filePath)` — the package reads the PKCS#8 `.p8` file and
  uses `ECDsa.Create()` + `ImportPkcs8PrivateKey()` from `System.Security.Cryptography`
  (BCL, no NuGet dep) to sign the JWT with ES256.
- Set `options.GenerateClientSecret = true`, `options.KeyId`, `options.TeamId`.
- Secret rotation is built-in via `options.ClientSecretExpiresAfter` (default: 6 months).
- **No helper library needed.** `System.Security.Cryptography.ECDsa` in .NET 10 handles
  PKCS#8 import natively on Linux/macOS/Windows — zero extra NuGet dep.

**Package for `GameKit.Auth.Apple`:**
```xml
<PackageReference Include="AspNet.Security.OAuth.Apple" Version="10.0.0" />
```

#### Epic Games

**Situation:** No `AspNet.Security.OAuth.Epic` exists in the aspnet-contrib registry and no
maintained .NET-specific package exists. Epic Online Services does support standard OAuth 2.0
authorization-code flow with redirect URIs (authorization endpoint:
`https://www.epicgames.com/id/authorize`; token endpoint: `https://api.epicgames.dev/epic/oauth/v1/token`).

**Approach: custom `OAuthHandler<EpicOAuthOptions>` in `GameKit.Auth.Epic`.**
ASP.NET Core's `OAuthHandler<TOptions>` is the standard extension point for any standard
OAuth 2.0 authorization-code provider. Epic's endpoints are standard enough that no extra
library is needed:
1. Extend `OAuthOptions` → `EpicOAuthOptions` (configure Epic authorization/token URLs,
   `basic_profile` scope, `client_credentials` Basic-auth header pattern).
2. Override `CreateTicketsAsync` to extract Epic account ID and display name from the
   userinfo response.
3. Wrap in an `IOAuthProvider` implementation following the same pattern as Discord in v1.

**No new NuGet dependency required** — `OAuthHandler<T>` is in `Microsoft.AspNetCore.App`
(shared framework). Epic-specific HTTP calls use the already-pinned
`Microsoft.Extensions.Http.Resilience` pipeline from v1.

---

### 3. GameKit.Lobby — real-time lobby (SignalR + Redis backplane)

#### SignalR server-side (Hub infrastructure)

**Situation:** ASP.NET Core SignalR hub infrastructure (`Hub`, `IHubContext<T>`,
`AddSignalR()`) is part of `Microsoft.AspNetCore.App` shared framework — no NuGet pin
needed for the hub code itself.

#### Redis backplane for multi-replica Admin UI AND GameKit.Lobby

**Package:** `Microsoft.AspNetCore.SignalR.StackExchangeRedis`
**Version:** `10.0.8` (released 2026-05-12; latest GA on nuget.org 2026-06-05)
**License:** MIT
**net10.0:** Explicit net10.0 TFM; confirmed GA.
**Dependencies (net10.0):**
- `MessagePack` >= 2.5.187 (transitive; MIT — no action needed, pulled automatically)
- `Microsoft.Extensions.Options` >= 10.0.8 (shared framework; no explicit pin needed)
- `StackExchange.Redis` >= 2.7.27 — already pinned at `2.8.41` in v1; constraint satisfied.

**Why `Microsoft.AspNetCore.SignalR.StackExchangeRedis` not Azure SignalR Service:**
The v2.0 constraint is explicit: zero cloud dependencies, GPL self-hosted. Azure SignalR
Service (`Microsoft.Azure.SignalR`) is a managed cloud service — hard excluded. The
StackExchangeRedis backplane works with the same Redis instance already required by
`GameKit.Matchmaking` and `GameKit.Presence`. One Redis, no new infrastructure.

**Configuration pattern:**
```csharp
services.AddSignalR()
    .AddStackExchangeRedis(connectionString, options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal("GameKit");
    });
```

Set a `ChannelPrefix` so the backplane channels don't collide with GameKit's existing
`matchmaking:*` and `presence:*` Redis key namespaces.

#### SignalR vs raw Redis pub/sub for GameKit.Lobby chat

**Use SignalR groups, not raw Redis pub/sub.** Rationale:
- SignalR groups map directly to lobby membership — `AddToGroupAsync(connectionId, lobbyId)` /
  `RemoveFromGroupAsync` handles member routing with zero boilerplate.
- The Redis backplane (`AddStackExchangeRedis`) automatically replicates group messages across
  replicas — no hand-rolled fan-out code.
- Raw `ISubscriber.SubscribeAsync` would require every replica to manage subscription
  lifecycle, reconnect, and re-subscribe manually; SignalR + backplane handles all of this.
- Caveat: SignalR does NOT buffer messages when Redis is temporarily unavailable — messages
  sent during a Redis outage are lost. This is acceptable for in-lobby chat (transient, low-stakes
  real-time comms). Persistent message history requires explicit storage (EF/Postgres), not
  buffering in the backplane.

**Persistence for in-lobby chat (if required):** Store chat messages in a `lobby_messages`
Postgres table (EF Core + Npgsql — no new dep). SignalR delivers real-time; Postgres holds
the persistent log. Ready-check state and group membership go in Redis sorted sets / hashes
(same pattern as matchmaking tickets in v1).

**Package for `GameKit.Lobby` and `GameKit.Admin.UI` (updated):**
```xml
<PackageReference Include="Microsoft.AspNetCore.SignalR.StackExchangeRedis" Version="10.0.8" />
```
Both `GameKit.Lobby` and the updated `GameKit.Admin.UI` declare this dependency.

---

### 4. Account merge, rank decay, placement matches, backfill, regional pools

**No new NuGet dependencies required.** Confirmed:

| Feature | What it needs | How it's covered |
|---------|---------------|-----------------|
| Account merge | Transactional row merges across `player_identities`, `player_credentials`, `player_ranks`, `session_participants`, `refresh_tokens` | EF Core 10 + Npgsql (already pinned); SERIALIZABLE transaction via `ExecutionStrategy.ExecuteInTransactionAsync` |
| Rank decay | Time-based rating adjustment on a `BackgroundService` tick | `BackgroundService` + `PeriodicTimer` + Polly — already the v1 pattern |
| Placement matches | High-RD initial games via `Glicko2Algorithm` config (RD threshold gate) | Vendored Glicko-2 in `GameKit.Rankings`; no new dep |
| Backfill | Mid-session player slot injection; seat-count tracking on `game_sessions` | EF Core + Redis sorted-set slot management (same as matchmaking) |
| Regional pools | First-class `region` column on `matchmaking_tickets` + per-region Redis sorted sets | EF Core migration on `GameKit.Matchmaking`; per-package migration boundary holds |

The pattern for all five: extend existing EF entities, add a migration in the owning package,
implement domain logic in existing service classes. Zero new external dependencies.

---

## Summary Table of New Additions to Directory.Packages.props

| Package | Version | Used By | License | GPL Compatible |
|---------|---------|---------|---------|----------------|
| `Isopoh.Cryptography.Argon2` | `2.0.0` | `GameKit.Auth.Argon2` | CC0 | Yes (public domain) |
| `Isopoh.Cryptography.Blake2b` | `2.0.0` | `GameKit.Auth.Argon2` (transitive) | CC0 | Yes |
| `Isopoh.Cryptography.SecureArray` | `2.0.0` | `GameKit.Auth.Argon2` (transitive) | CC0 | Yes |
| `Microsoft.AspNetCore.Authentication.Google` | `10.0.8` | `GameKit.Auth.Google` | MIT | Yes |
| `AspNet.Security.OAuth.Apple` | `10.0.0` | `GameKit.Auth.Apple` | Apache-2.0 | Yes |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` | `8.14.0` (minimum; pin to latest 8.x) | `GameKit.Auth.Apple` (transitive) | MIT | Yes |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `10.0.8` | `GameKit.Lobby`, `GameKit.Admin.UI` | MIT | Yes |

**New packages that do NOT need a `Directory.Packages.props` entry** (pulled from shared
framework at runtime, no explicit NuGet version pin needed):
- `Microsoft.AspNetCore.SignalR` (hub core) — in `Microsoft.AspNetCore.App`
- `System.Security.Cryptography.ECDsa` — in `System.Security.Cryptography` (BCL)

---

## What NOT to Add

| Package | Why Excluded |
|---------|-------------|
| `Microsoft.Azure.SignalR` | Cloud-only managed service — hard GPL/self-hosted exclusion |
| `Azure.Identity`, any Azure.* SDK | Cloud SDK; violates zero-cloud constraint |
| `OpenIddict` client stack | Overkill for simple OAuth2 auth-code providers; adds complexity |
| `IdentityServer4` / `Duende.IdentityServer` | Archived / commercial — not a dep candidate |
| `MediatR` (any version >= 13) | RPL/commercial licensing after v12 — already on "never" list |
| `AutoMapper` (any version >= 13) | Same RPL licensing issue |
| `Konscious.Security.Cryptography.Argon2` | Older API ergonomics, last NuGet release lags Isopoh, native path concern |
| `Microsoft.AspNetCore.SignalR.Redis` (old) | Depends on StackExchange.Redis 1.x; superseded by `StackExchangeRedis` package |
| Any AI/LLM SDK | GPL self-hosted commitment; explicitly out of scope |

---

## Alternatives Considered

| Feature | Chosen | Alternative | Why Not |
|---------|--------|-------------|---------|
| Google OAuth | `Microsoft.AspNetCore.Authentication.Google` 10.0.8 | `AspNet.Security.OAuth.Google` (aspnet-contrib) | Does not exist in aspnet-contrib — Google is not in that registry |
| Apple OAuth | `AspNet.Security.OAuth.Apple` 10.0.0 | Hand-rolled `OAuthHandler<T>` | ES256 secret generation is non-trivial; aspnet-contrib package is battle-tested and on same v10 train as Discord |
| Epic OAuth | Custom `OAuthHandler<EpicOAuthOptions>` | Any third-party NuGet | No maintained .NET package exists; Epic uses standard OAuth 2.0 auth-code — `OAuthHandler<T>` in the shared framework handles it with minimal code |
| Argon2 | `Isopoh.Cryptography.Argon2` | `Konscious.Security.Cryptography.Argon2` | Isopoh is 100% managed, CC0, more ergonomic API, includes `SecureArray` |
| SignalR backplane | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | Raw Redis pub/sub | SignalR groups abstract fan-out and reconnect; backplane is one method call; raw pub/sub requires per-replica subscription management |
| SignalR backplane | `Microsoft.AspNetCore.SignalR.StackExchangeRedis` | `Microsoft.Azure.SignalR` | Azure SignalR is a managed cloud service — hard excluded by GPL/self-hosted constraint |

---

## Version Compatibility Notes

| Combination | Status | Notes |
|-------------|--------|-------|
| `Isopoh.Cryptography.Argon2` 2.0.0 + net10.0 | ✅ Compatible | Ships netstandard2.0 asset; resolves correctly on net10.0 |
| `Microsoft.AspNetCore.Authentication.Google` 10.0.8 + net10.0 | ✅ GA | net10.0 TFM; released 2026-05-12 |
| `AspNet.Security.OAuth.Apple` 10.0.0 + net10.0 | ✅ GA | net10.0 TFM; released 2025-11-11; aligns with aspnet-contrib v10 train |
| `Microsoft.AspNetCore.SignalR.StackExchangeRedis` 10.0.8 + StackExchange.Redis 2.8.41 | ✅ Satisfied | Requires >= 2.7.27; v1 pins 2.8.41 |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.x + JwtBearer 10 | ✅ Compatible | Both use `Microsoft.IdentityModel.*` 8.x under the hood; no version conflict |

---

## Sources

- [NuGet: Isopoh.Cryptography.Argon2 2.0.0](https://www.nuget.org/packages/Isopoh.Cryptography.Argon2) — version + TFM verified 2026-06-05 — HIGH
- [GitHub: mheyman/Isopoh.Cryptography.Argon2 (README)](https://github.com/mheyman/Isopoh.Cryptography.Argon2/blob/master/README.md) — CC0 license confirmed — HIGH
- [Isopoh Argon2Config API docs](https://mheyman.github.io/Isopoh.Cryptography.Argon2/api/Isopoh.Cryptography.Argon2.Argon2Config.html) — default parameter values — HIGH
- [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html) — Argon2id tuning parameters — HIGH
- [NuGet: Microsoft.AspNetCore.Authentication.Google 10.0.8](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.Google) — version + license + TFM verified — HIGH
- [NuGet: AspNet.Security.OAuth.Apple 10.0.0](https://www.nuget.org/packages/AspNet.Security.OAuth.Apple) — version + TFM + dependencies verified — HIGH
- [aspnet-contrib: sign-in-with-apple.md](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/blob/dev/docs/sign-in-with-apple.md) — UsePrivateKey + ES256 generation approach — HIGH
- [aspnet-contrib: AspNet.Security.OAuth.Providers repo](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers) — confirmed no Google provider, no Epic provider — HIGH
- [Epic Online Services: Auth Web APIs](https://dev.epicgames.com/docs/web-api-ref/authentication) — OAuth 2.0 authorization-code endpoints confirmed — MEDIUM
- [NuGet: Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8](https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.StackExchangeRedis) — version + dependencies + license verified — HIGH
- [MS Learn: Redis backplane for ASP.NET Core SignalR (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/signalr/redis-backplane?view=aspnetcore-10.0) — configuration pattern + behavior (no message buffering) — HIGH
- [FSF: CC BY 4.0 added to free licenses list](https://www.fsf.org/blogs/licensing/cc-by-4-0-and-cc-by-sa-4-0-added-to-our-list-of-free-licenses) — CC0/CC-BY-4.0 GPL compatibility confirmed — HIGH

---

*Stack research for: GameKit v2.0 — new dependency additions only*
*Researched: 2026-06-05*
