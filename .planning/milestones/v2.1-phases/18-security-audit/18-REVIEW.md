---
phase: 18-security-audit
reviewed: 2026-06-23T10:23:32Z
depth: deep
files_reviewed: 12
files_reviewed_list:
  - Directory.Build.props
  - Directory.Packages.props
  - src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs
  - src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs
  - src/GameKit.Auth/Builder/AuthBuilderExtensions.cs
  - src/GameKit.Auth/Data/AuthModelBuilderExtension.cs
  - src/GameKit.Auth/Services/AuthGdprDeleteExtension.cs
  - src/GameKit.Core/Data/GameKitModelCacheKeyFactory.cs
  - src/GameKit.Core/Services/GdprDeleteService.cs
  - src/GameKit.Core/Services/IGdprDeleteExtension.cs
  - src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs
  - src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs
findings:
  critical: 2
  warning: 2
  info: 1
  total: 5
status: issues_found
---

# Phase 18: Security Audit — Code Review Report

**Reviewed:** 2026-06-23T10:23:32Z
**Depth:** deep
**Files Reviewed:** 12
**Status:** issues_found

## Summary

Phase 18 delivers four security remediations: the GDPR FK-gap fix (SEC-04), egress allowlist wiring for Apple/Google OAuth providers (SEC-05), the MessagePack CVE pin (SEC-07), and the EF model-cache-key factory needed by the GDPR integration test. Cross-referencing production registration paths (`AddGameKit`, `AddAuth`, `AddMatchmaking`), migration context factories, the egress handler, and the NuGet audit gate. The GDPR transaction structure and FK coverage are correct. Two blockers found: the egress handler is silently skipped when call order is violated; and `GameKitModelCacheKeyFactory` is shipped as a public production type but is only wired in the test fixture, leaving an undocumented production gap in its XML doc. Two warnings found around silent allowlist failures and undisposed `BuildServiceProvider()` instances.

---

## Critical Issues

### CR-01: Egress bypass — `AddApple`/`AddGoogle` silently skip allowlist and handler when `AddAuth` has not run

**File:** `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs:96-104, 146-154`
**Also:** `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs:89-97, 118-126`

**Issue:** Both `AddApple` and `AddGoogle` use `BuildServiceProvider().GetService<GameKitAuthOptions>()` to retrieve the singleton registered by `AddAuth`. The result is guarded by `if (authOpts is not null)` and `if (resolvedOpts is not null)`. If a consumer calls `AddApple` or `AddGoogle` *before* `AddAuth`, both guards evaluate to `false` and:

1. The provider host (`appleid.apple.com`, `oauth2.googleapis.com`, etc.) is **never added** to `AllowedProviderHosts`.
2. `apple.BackchannelHttpHandler` / `google.BackchannelHttpHandler` is **never assigned**, so the backchannel falls through to the default `HttpClientHandler` with **no egress restriction**.

The documentation says "must be called AFTER `AddAuth`" but this is a doc comment, not an enforced precondition. A misconfigured startup silently produces an unguarded OAuth backchannel. SEC-05's security invariant — "no OAuth token exchange leaves the process without passing through `EgressAllowListHandler`" — is defeated without a build-time or startup-time error.

**Fix:** Replace the null-guard pattern with a fail-fast assertion at the top of `AddApple` and `AddGoogle`:

```csharp
// Enforce call-order contract: AddAuth must precede AddApple.
// BuildServiceProvider() here is acceptable because AddAuth registered GameKitAuthOptions
// as an instance singleton — the same object reference is returned.
var authOpts = builder.Services.BuildServiceProvider().GetService<GameKitAuthOptions>()
    ?? throw new InvalidOperationException(
        "AddApple() requires AddAuth() to have been called first on the same builder. " +
        "GameKitAuthOptions is registered by AddAuth and must be present before Apple " +
        "provider hosts can be appended to AllowedProviderHosts.");
```

Apply the same pattern for the inner `resolvedOpts` resolve (line 146 / 118). Drop the `if (authOpts is not null)` guards entirely — if `AddAuth` ran, the singleton is always resolvable.

---

### CR-02: `GameKitModelCacheKeyFactory` is never registered in production — XML doc asserts it is

**File:** `src/GameKit.Core/Data/GameKitModelCacheKeyFactory.cs:36`

**Issue:** The class `GameKitModelCacheKeyFactory` carries this XML doc:

> Registered via `dbOpts.ReplaceService<IModelCacheKeyFactory, GameKitModelCacheKeyFactory>()` inside `AddGameKit`'s `AddDbContext` call — this is the correct EF Core mechanism for replacing infrastructure-level services on a per-context-options basis.

This statement is **false**. A grep of all production source files confirms `ReplaceService<IModelCacheKeyFactory, GameKitModelCacheKeyFactory>` appears **only** in `tests/GameKit.Core.Integration.Tests/GdprDeleteCoverageTests.cs:409`. `GameKitServiceCollectionExtensions.AddGameKit()` does not call it.

The defect is not that production breaks today — the production path does not share the default cache key with migration contexts because migration contexts use distinct `IModelCustomizer` implementations (`AuthMigrationModelCustomizer`, `MatchmakingMigrationModelCustomizer`, etc.), which are part of the default EF cache key tuple `(contextType, modelCustomizerType, designTime)`. Production has `(GameKitDbContext, RelationalModelCustomizer, false)`; migration contexts have `(GameKitDbContext, AuthMigrationModelCustomizer, false)`. These are distinct keys.

However, the documentation authoritatively states the factory is wired in production. Any future developer reading this will:
(a) believe the production model cache is already protected and skip adding the registration, or
(b) add the registration thinking it is idempotent, when actually doing so for the first time changes production behavior.

Additionally, the class is `public`, meaning library consumers may rely on it for their own integration-test setups — but they have no production guidance, only a misleading "AddGameKit already does this" comment.

**Fix (option A — minimal):** Correct the XML doc to accurately state where the factory is registered (test-only) and why it is not needed in production:

```csharp
/// <remarks>
/// <para>
/// This factory is used by integration test fixtures that run both migration contexts and
/// full-runtime contexts in the same process. In that scenario, multiple <see cref="GameKitDbContext"/>
/// instances share the default EF model cache (keyed by contextType + modelCustomizerType + designTime),
/// and a Core-only migration context can cache a model that is then incorrectly reused for a
/// full-runtime context. Registering this factory via:
/// <code>
/// dbOpts.ReplaceService&lt;IModelCacheKeyFactory, GameKitModelCacheKeyFactory&gt;()
/// </code>
/// on the full-runtime <c>AddDbContext</c> call gives it a distinct cache key.
/// </para>
/// <para>
/// <b>Production:</b> <c>AddGameKit()</c> does NOT register this factory because production
/// migration contexts each use a distinct <see cref="Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer"/>
/// (<c>AuthMigrationModelCustomizer</c>, <c>MatchmakingMigrationModelCustomizer</c>, etc.),
/// which already differentiates their cache keys from the runtime context's
/// <c>(GameKitDbContext, RelationalModelCustomizer, false)</c> key. No collision occurs.
/// </para>
/// </remarks>
```

**Fix (option B — recommended if the factory is also intended for production):** Register it in `AddGameKit`:

```csharp
services.AddDbContext<GameKitDbContext>((sp, dbOpts) =>
    dbOpts.UseNpgsql(opts.ConnectionString, npg => { ... })
        .UseApplicationServiceProvider(sp)
        .ReplaceService<IModelCacheKeyFactory, GameKitModelCacheKeyFactory>()); // SEC-04 cache fix
```

Choose option A if the factory is truly test-only. Choose option B if there is any scenario (e.g., `DbContextPool`, a consumer calling `AddDbContext<GameKitDbContext>` again themselves) where model cache collisions could occur in production.

---

## Warnings

### WR-01: `BuildServiceProvider()` called multiple times per `AddApple`/`AddGoogle` — undisposed `ServiceProvider` instances

**File:** `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs:96, 146`
**Also:** `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs:89, 118`

**Issue:** Each call to `builder.Services.BuildServiceProvider()` constructs a **new** `ServiceProvider`, which is `IDisposable`. There are four such calls across the two files (two per provider), and none are disposed. While `GameKitAuthOptions` is registered as a singleton instance (`AddSingleton(opts)`) so no factory-constructed singleton is leaked, the `ServiceProvider` objects themselves hold internal state (singletons, root scope) and are never returned to the GC in a timely fashion. ASP.NET Core logs the "Call to BuildServiceProvider from application code" warning at startup because of this pattern. With `TreatWarningsAsErrors=true` in the compiler, this is not a build error (it's a runtime log, not a compiler warning), but it is a known anti-pattern.

**Fix:** Since the only thing being resolved is the `GameKitAuthOptions` instance singleton (which is the literal object passed to `AddSingleton(opts)`), retrieve it directly instead of building a service provider:

```csharp
// Instead of:
var authOpts = builder.Services.BuildServiceProvider().GetService<GameKitAuthOptions>();

// Use a descriptor scan to recover the already-registered instance:
var authOpts = builder.Services
    .Where(d => d.ServiceType == typeof(GameKitAuthOptions) && d.ImplementationInstance is not null)
    .Select(d => (GameKitAuthOptions?)d.ImplementationInstance)
    .FirstOrDefault();
```

Or better, expose `GameKitAuthOptions` via the `IGameKitBuilder` interface so sibling packages do not need to build a service provider at all:

```csharp
// On IGameKitBuilder:
GameKitAuthOptions? AuthOptions { get; }
// Set in AddAuth(), read in AddApple()/AddGoogle().
```

Either approach eliminates the undisposed `ServiceProvider` and the startup diagnostic.

---

### WR-02: Duplicate `<summary>` XML doc elements on `AppleProviderHosts` and `GoogleProviderHosts`

**File:** `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs:46-59`
**Also:** `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs:32-50`

**Issue:** Both `AppleProviderHosts` and `GoogleProviderHosts` have two `<summary>` elements in their XML doc block — a longer descriptive one followed by a shorter one. The C# XML documentation specification allows only one `<summary>` per element. The Roslyn XML doc compiler picks the first element and silently ignores the second, producing a malformed generated `.xml` file that IntelliSense and documentation generators (docfx, Sandcastle) may mishandle. `TreatWarningsAsErrors=true` does not catch this because Roslyn emits CS1570 (malformed XML) only when the doc is actually invalid XML, not when a tag is duplicated.

**Fix:** Merge the two `<summary>` elements into one, promoting the remarks content into `<remarks>`:

```csharp
/// <summary>
/// The Apple backchannel provider hosts allowlisted by this package. Added to
/// <see cref="GameKitAuthOptions.AllowedProviderHosts"/> at registration time.
/// </summary>
/// <remarks>
/// Apple token endpoint: <c>https://appleid.apple.com/auth/token</c>.
/// SEC-05: declared in code so that a misconfigured appsettings.json cannot silently clear them.
/// </remarks>
public static readonly string[] AppleProviderHosts = { "appleid.apple.com" };
```

---

## Info

### IN-01: `MatchmakingGdprDeleteExtension` deletes ALL `party_members` rows for the player, including owner-role rows — Postgres CASCADE makes this redundant but harmless

**File:** `src/GameKit.Matchmaking/Services/MatchmakingGdprDeleteExtension.cs:53-56`

**Issue:** The extension deletes all `party_members` rows `WHERE PlayerId = playerId`, which includes rows where the player is the *owner* of a party (the owner is also a member). The comment says "only non-owner memberships" but `pm.PlayerId == playerId` matches all memberships regardless of owner/non-owner. When the player row is subsequently deleted, Postgres's `parties.OwnerPlayerId → ON DELETE CASCADE` fires and would also try to delete the `party_members` rows belonging to the now-cascade-deleted party — but those rows were already deleted by this extension. The result is harmless (the cascade deletes zero rows), but the comment is misleading and the delete is wider than stated.

This is not a bug — the GDPR erasure produces a correct result. The `party_members.PlayerId → RESTRICT` constraint is satisfied for all rows before the player delete, so no FK violation occurs. The extra delete does not leave orphaned data. However, a future reader might add logic assuming the player's owned-party memberships still exist when the player row is deleted.

**Fix:** Either correct the comment to accurately reflect what is deleted:

```csharp
// Remove all party_member rows for this player (both owner and non-owner memberships).
// Owner memberships: this delete pre-empts the Postgres CASCADE on parties.OwnerPlayerId,
// which would attempt to cascade into party_members after the player row is deleted.
// Non-owner memberships: these carry ON DELETE RESTRICT on party_members.PlayerId and must
// be removed before the player row is deleted to avoid Postgres 23503.
```

Or narrow the delete to only non-owner rows to match the documented intent (requires joining on `parties.OwnerPlayerId != playerId`) — though the current wider delete is functionally correct.

---

## REVIEW COMPLETE

**Status:** issues_found
**Findings:** 2 Critical, 2 Warning, 1 Info

The GDPR deletion transaction is structurally correct: extensions run inside the `SERIALIZABLE` transaction before the player row is deleted; a failing extension propagates the exception and rolls back the entire transaction including the audit log; no partial erasure is possible. The FK gap coverage for both `party_members.PlayerId` (RESTRICT) and `account_merges.TargetPlayerId` (RESTRICT) is complete and the registration uses `TryAddEnumerable` to prevent double-registration. The MessagePack CVE pin and NuGet audit gate are correctly structured. Two blockers need remediation before ship: the egress bypass on call-order violation (CR-01) and the false production-registration claim in `GameKitModelCacheKeyFactory`'s XML doc (CR-02).

---

_Reviewed: 2026-06-23T10:23:32Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: deep_
