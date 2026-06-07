---
phase: 07-core-rating-seam-stateless-auth-packages
reviewed: 2026-06-05T00:00:00Z
depth: deep
files_reviewed: 19
files_reviewed_list:
  - src/GameKit.Core/Services/IPlayerRatingProvider.cs
  - src/GameKit.Core/Services/NullPlayerRatingProvider.cs
  - src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs
  - src/GameKit.Auth/Services/IPasswordHasher.cs
  - src/GameKit.Auth/Services/BCryptPasswordHasher.cs
  - src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs
  - src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.cs
  - src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.Designer.cs
  - src/GameKit.Auth/Data/Configurations/PlayerCredentialConfiguration.cs
  - src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs
  - src/GameKit.Auth.Argon2/Configuration/GameKitArgon2Options.cs
  - src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs
  - src/GameKit.Auth.Argon2/AssemblyInfo.cs
  - src/GameKit.Auth.Google/Providers/Google/GoogleOAuthProvider.cs
  - src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs
  - src/GameKit.Auth.Google/Configuration/GameKitGoogleOptions.cs
  - src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs
  - src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs
  - src/GameKit.Auth.Apple/Configuration/GameKitAppleOptions.cs
  - src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthProvider.cs
  - src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs
  - src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthOptions.cs
  - src/GameKit.Auth.Epic/Builder/EpicBuilderExtensions.cs
  - src/GameKit.Auth.Epic/Configuration/GameKitEpicOptions.cs
findings:
  critical: 1
  warning: 3
  info: 2
  total: 6
status: fixed
---

# Phase 07: Code Review Report

**Reviewed:** 2026-06-05
**Depth:** deep
**Files Reviewed:** 24
**Status:** issues_found

## Summary

Phase 7 introduces the `IPlayerRatingProvider` Core seam, the BCrypt→Argon2 live-migration path, and three new stateless OAuth packages (Google, Apple, Epic). The architectural decisions are sound: identity keys use stable provider-specific identifiers (`sub`/`account_id`, never email), the Epic handler correctly sends credentials via `Authorization: Basic` header only, the Apple `.p8` key is handled ephemerally via delegate, and the Argon2 defaults comfortably exceed OWASP minimums. Migration and snapshot are consistent at 512 chars.

One critical security defect was found: the timing-attack dummy hash used in `PasswordOAuthProvider` is one character short of a valid BCrypt hash (59 chars vs 60 required), causing `BCrypt.Verify` to throw `SaltParseException` and return immediately — before performing any crypto work. This negates the timing parity the code is designed to provide.

Three warnings are present: the Apple scheme-registration guard omits `TeamId`/`KeyId` checks (leading to null-forgiving operator usage that produces cryptic runtime failures), no OWASP floor is enforced on `GameKitArgon2Options` at registration time, and the `RegisterAsync` audit action string is wrong (`auth.login.failure` instead of `auth.register.failure`).

---

## Critical Issues

### CR-01: Timing-oracle — dummy BCrypt hash is malformed; `SaltParseException` short-circuits full work-factor computation

**File:** `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs:40`

**Issue:** The `DummyHash` constant is 59 characters long:

```
$2a$12$abcdefghijklmnopqrstuu1234567890123456789012345678ab
```

A valid BCrypt hash requires exactly 60 characters (`$2a$` + cost-prefix + `$` + 53 chars of base64-encoded salt+hash). The salt+hash section here is 52 chars; BCrypt.Net-Next expects 53. `BCrypt.Verify` calls `ExtractVersion`, which validates the section length and throws `SaltParseException` immediately — before the Blowfish key-setup (the expensive part) runs. The `catch (BCrypt.Net.SaltParseException)` block at `BCryptPasswordHasher:31` returns `false` in microseconds, not ~200 ms.

Consequence: an attacker observing wall-clock differences can distinguish `"username not found"` (~0 ms) from `"wrong password"` (~200 ms with work factor 12), giving them an account-enumeration oracle.

The same defect applies when `Argon2idPasswordHasher` is the active hasher: the dummy hash starts with `$2a$`, so `Argon2idPasswordHasher.Verify` dispatches to `BCrypt.Verify`, which throws the same `SaltParseException` immediately.

**Fix:** Replace `DummyHash` with a real BCrypt hash generated from a known-garbage password at the deployment work factor. The comment already says "Generated once via `BCryptPasswordHasher.Hash("never-matches-never-matches") at work factor 12`" — but the hash stored does not match that spec. Generate a fresh one and use it:

```csharp
// Generate once: BCrypt.Net.BCrypt.HashPassword("never-matches-never-matches", 12)
// and paste the FULL 60-character result here.
private const string DummyHash = "$2a$12$<paste 60-char hash here>";
```

For the Argon2 active-hasher case, the DummyHash must also be an Argon2id-encoded string when `Argon2idPasswordHasher` is in use, since BCrypt dispatching still short-circuits on the malformed hash. The cleanest fix is to generate the dummy at startup from the active `IPasswordHasher`:

```csharp
// In constructor:
_dummyHash = _hasher.Hash("__gamekit_dummy_never_matches__");
```

(The lazy-generation cost of one hash at startup is negligible and ensures the dummy is always in the correct format for the active hasher.)

---

## Warnings

### WR-01: Apple scheme registered with potentially null `TeamId`/`KeyId` — null-forgiving operator conceals misconfiguration until runtime

**File:** `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs:71,85-86`

**Issue:** The guard that conditionally registers the Apple authentication scheme checks only `ServiceId` and `PrivateKeyBase64`:

```csharp
if (!string.IsNullOrEmpty(opts.ServiceId) && !string.IsNullOrEmpty(opts.PrivateKeyBase64))
{
    ...
    apple.TeamId = teamId!;  // null-forgiving — suppresses warning but not runtime NPE
    apple.KeyId  = keyId!;   // same
```

`TeamId` and `KeyId` are required by `AspNet.Security.OAuth.Apple` for ES256 client-secret generation (`GenerateClientSecret = true`). If a developer provides `ServiceId` + `PrivateKeyBase64` but omits `TeamId` or `KeyId`, the scheme registers successfully, but the first token-exchange attempt throws a `NullReferenceException` deep inside the aspnet-contrib handler's JWT-signing path — with no indication that configuration is missing.

**Fix:** Extend the guard to all four required values and provide an actionable exception at startup:

```csharp
if (!string.IsNullOrEmpty(opts.ServiceId)
    && !string.IsNullOrEmpty(opts.TeamId)
    && !string.IsNullOrEmpty(opts.KeyId)
    && !string.IsNullOrEmpty(opts.PrivateKeyBase64))
{
    ...
    apple.TeamId = opts.TeamId;   // non-null by guard above
    apple.KeyId  = opts.KeyId;
```

Alternatively, validate at builder time:

```csharp
if (!string.IsNullOrEmpty(opts.ServiceId) && !string.IsNullOrEmpty(opts.PrivateKeyBase64))
{
    if (string.IsNullOrEmpty(opts.TeamId))
        throw new InvalidOperationException("GameKitAppleOptions.TeamId must be set when ServiceId and PrivateKeyBase64 are provided.");
    if (string.IsNullOrEmpty(opts.KeyId))
        throw new InvalidOperationException("GameKitAppleOptions.KeyId must be set when ServiceId and PrivateKeyBase64 are provided.");
```

---

### WR-02: `GameKitArgon2Options` parameters are not validated against OWASP minimums at registration time

**File:** `src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs:36-52`

**Issue:** The `UseArgon2(configure)` builder extension applies the `configure` delegate to `GameKitArgon2Options` and registers the result without any validation. A developer can configure parameters below the OWASP 2025 minimums (m ≥ 19456 KiB, t ≥ 2 iterations) and receive no error:

```csharp
.UseArgon2(o => {
    o.MemoryCost = 1024;  // far below 19456 KiB OWASP minimum
    o.TimeCost   = 1;     // below 2-iteration minimum
})
```

Isopoh.Argon2 accepts these values and produces hashes — the library has no floor of its own. Passwords end up protected by negligibly weak parameters, with no log warning or exception.

The documentation for both properties states the minimums in `<remarks>` but enforces nothing at runtime.

**Fix:** Add validation in `UseArgon2` after calling `configure`:

```csharp
configure?.Invoke(opts);

const int OwaspMinMemoryCostKib = 19456;
const int OwaspMinTimeCost      = 2;

if (opts.MemoryCost < OwaspMinMemoryCostKib)
    throw new ArgumentOutOfRangeException(nameof(configure),
        $"GameKitArgon2Options.MemoryCost ({opts.MemoryCost} KiB) is below the OWASP 2025 minimum ({OwaspMinMemoryCostKib} KiB). " +
        "Reduce to below minimum only in a test environment.");

if (opts.TimeCost < OwaspMinTimeCost)
    throw new ArgumentOutOfRangeException(nameof(configure),
        $"GameKitArgon2Options.TimeCost ({opts.TimeCost}) is below the OWASP 2025 minimum ({OwaspMinTimeCost} iterations).");
```

---

### WR-03: `PasswordOAuthProvider.RegisterAsync` writes incorrect audit action `"auth.login.failure"` for username-taken registration collision

**File:** `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs:221`

**Issue:** When a concurrent `RegisterAsync` call loses the `UNIQUE(Username)` race (Postgres error 23505), the code writes:

```csharp
action: "auth.login.failure",
...
after: new { provider = "password", reason_code = "username_taken" },
```

This is a **registration** failure, not a login failure. Using `"auth.login.failure"` as the action will cause monitoring/alerting tools that query `admin_audit_log` for login failures to over-count, and tools querying for registration failures to miss this event.

**Fix:**

```csharp
action: "auth.register.failure",
```

---

## Info

### IN-01: `externalId[^6..]` in three OAuth provider fallback display-name paths lacks a length guard

**Files:**
- `src/GameKit.Auth.Google/Providers/Google/GoogleOAuthProvider.cs:73`
- `src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs:99`
- `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthProvider.cs:80`

**Issue:** All three new OAuth providers use `externalId[^6..]` (C# range index from the end) to build a fallback display name when `displayName` is null on a first-time registration. If `externalId.Length < 6`, the range operator throws `ArgumentOutOfRangeException`.

In practice, all real OAuth provider subject identifiers are far longer than 6 characters (Google `sub` ≈ 21 digits, Apple `sub` ≈ 30–50 chars, Epic `account_id` = 32 hex chars). However, there is no defensive guard, and a future provider or a test double that passes a short stub `externalId` would crash this path.

**Fix:** Add a length-safe fallback:

```csharp
var suffix = externalId.Length >= 6 ? externalId[^6..] : externalId;
var fallbackName = displayName ?? $"GoogleUser-{suffix}";
```

---

### IN-02: Misleading comment in `GameKitServiceCollectionExtensions` — `TryAddSingleton` does NOT allow later `TryAddSingleton` to replace

**File:** `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs:107-110`

**Issue:** The comment reads:

```
// GameKit.Rankings (Phase 8) registers its RankingsRatingSource via TryAddSingleton
// AFTER AddGameKit() returns, which means the first TryAdd (this one) wins the race
// and Rankings silently replaces it — so Rankings MUST register after AddGameKit().
```

The last clause ("Rankings silently replaces it") is contradicted by the first clause ("the first TryAdd wins the race"). `TryAddSingleton` is a **no-op** when the service is already registered. If Phase 8 uses `TryAddSingleton` as indicated, the `NullPlayerRatingProvider` will remain active and rankings will silently not function. Phase 8 must use `services.RemoveAll<IPlayerRatingProvider>()` followed by `AddSingleton<IPlayerRatingProvider, RankingsRatingSource>()`, or simply `AddSingleton` (which replaces any existing registration).

**Fix:** Correct the comment to avoid sending Phase 8 implementors in the wrong direction:

```csharp
// Phase 7 (CORE-18): IPlayerRatingProvider optional port — null-object default so
// Matchmaking operates in zero-rated mode when GameKit.Rankings is not installed.
// GameKit.Rankings (Phase 8) must call services.RemoveAll<IPlayerRatingProvider>()
// followed by services.AddSingleton<IPlayerRatingProvider, RankingsRatingSource>()
// (or simply services.AddSingleton which replaces), NOT TryAddSingleton which is a
// no-op when the interface is already registered.
services.TryAddSingleton<IPlayerRatingProvider, NullPlayerRatingProvider>();
```

---

_Reviewed: 2026-06-05_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
