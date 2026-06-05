# Phase 7: Core Rating Seam + Stateless Auth Packages - Pattern Map

**Mapped:** 2026-06-05
**Files analyzed:** 22 new/modified files across 5 new packages + 3 existing file modifications
**Analogs found:** 22 / 22

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Core/Services/IPlayerRatingProvider.cs` | service interface | request-response | `src/GameKit.Core/Services/IPresenceProvider.cs` | exact |
| `src/GameKit.Core/Services/NullPlayerRatingProvider.cs` | service (null-object) | request-response | `IPresenceProvider` null-object pattern in builder | exact |
| `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` (modify) | config / DI registration | — | existing file at same path (lines 80–87 GetService factory) | exact |
| `src/GameKit.Auth/Services/IPasswordHasher.cs` (modify) | service interface | — | existing file at same path | exact |
| `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` (modify) | service / provider | request-response | existing file at same path (lines 120–138 verify+return) | exact |
| `src/GameKit.Auth.Argon2/GameKit.Auth.Argon2.csproj` | config | — | `src/GameKit.Auth/GameKit.Auth.csproj` | exact |
| `src/GameKit.Auth.Argon2/AssemblyInfo.cs` | config | — | `src/GameKit.Auth/AssemblyInfo.cs` | exact |
| `src/GameKit.Auth.Argon2/Configuration/GameKitArgon2Options.cs` | config | — | `src/GameKit.Auth/GameKitAuthOptions.cs` (nested sub-options shape) | role-match |
| `src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs` | service | request-response | `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` | exact |
| `src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs` | config / DI registration | — | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` (lines 83, AddSingleton<IPasswordHasher>) | role-match |
| `src/GameKit.Auth.Google/GameKit.Auth.Google.csproj` | config | — | `src/GameKit.Auth/GameKit.Auth.csproj` | exact |
| `src/GameKit.Auth.Google/AssemblyInfo.cs` | config | — | `src/GameKit.Auth/AssemblyInfo.cs` | exact |
| `src/GameKit.Auth.Google/Configuration/GameKitGoogleOptions.cs` | config | — | `src/GameKit.Auth/GameKitAuthOptions.cs` (Discord sub-options shape) | role-match |
| `src/GameKit.Auth.Google/Providers/Google/GoogleOAuthProvider.cs` | service / provider | request-response | `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` | exact |
| `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs` | config / DI registration | — | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 200–252 (Discord conditional scheme block) | exact |
| `src/GameKit.Auth.Apple/GameKit.Auth.Apple.csproj` | config | — | `src/GameKit.Auth/GameKit.Auth.csproj` | exact |
| `src/GameKit.Auth.Apple/AssemblyInfo.cs` | config | — | `src/GameKit.Auth/AssemblyInfo.cs` | exact |
| `src/GameKit.Auth.Apple/Configuration/GameKitAppleOptions.cs` | config | — | Discord sub-options shape in `GameKitAuthOptions.cs` | role-match |
| `src/GameKit.Auth.Apple/Providers/Apple/AppleOAuthProvider.cs` | service / provider | request-response | `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` | exact |
| `src/GameKit.Auth.Apple/Builder/AppleBuilderExtensions.cs` | config / DI registration | — | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 200–252 | exact |
| `src/GameKit.Auth.Epic/GameKit.Auth.Epic.csproj` | config | — | `src/GameKit.Auth/GameKit.Auth.csproj` | exact |
| `src/GameKit.Auth.Epic/AssemblyInfo.cs` | config | — | `src/GameKit.Auth/AssemblyInfo.cs` | exact |
| `src/GameKit.Auth.Epic/Configuration/GameKitEpicOptions.cs` | config | — | Discord sub-options shape in `GameKitAuthOptions.cs` | role-match |
| `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthOptions.cs` | config | — | `OAuthOptions` subclass (shared framework) | partial-match |
| `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs` | service | request-response | `OAuthHandler<T>` (shared framework) | partial-match |
| `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthProvider.cs` | service / provider | request-response | `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` | exact |
| `src/GameKit.Auth.Epic/Builder/EpicBuilderExtensions.cs` | config / DI registration | — | `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 200–252 | exact |
| `tests/GameKit.Auth.Argon2.Tests/GameKit.Auth.Argon2.Tests.csproj` | test | — | `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` | exact |
| `tests/GameKit.Auth.Argon2.Tests/Argon2HasherTests.cs` | test | — | `tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs` | exact |
| `tests/GameKit.Auth.Google.Tests/GameKit.Auth.Google.Tests.csproj` | test | — | `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` | exact |
| `tests/GameKit.Auth.Google.Tests/GoogleProviderTests.cs` | test | — | `tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs` | exact |
| `tests/GameKit.Auth.Apple.Tests/GameKit.Auth.Apple.Tests.csproj` | test | — | `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` | exact |
| `tests/GameKit.Auth.Apple.Tests/AppleProviderTests.cs` | test | — | `tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs` | exact |
| `tests/GameKit.Auth.Epic.Tests/GameKit.Auth.Epic.Tests.csproj` | test | — | `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` | exact |
| `tests/GameKit.Auth.Epic.Tests/EpicProviderTests.cs` | test | — | `tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs` | exact |
| `tests/GameKit.Core.Tests/IPlayerRatingProviderTests.cs` | test | — | `tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs` (unit test shape) | role-match |

---

## Pattern Assignments

### `src/GameKit.Core/Services/IPlayerRatingProvider.cs` (service interface, request-response)

**Analog:** `src/GameKit.Core/Services/IPresenceProvider.cs`

**GPL header + namespace pattern** (lines 1–9):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;
```

**Optional-port interface shape** (lines 28–36 of IPresenceProvider.cs):
```csharp
/// <summary>
/// Optional presence provider. Implemented by <c>GameKit.Presence</c> (Phase 6) using Redis TTL-keyed heartbeats.
/// Core defines the interface so <c>GameKit.Admin.UI</c> (Phase 3) can light up presence panels when the sibling
/// package is installed and degrade gracefully when it is absent.
/// </summary>
public interface IPresenceProvider
{
    /// <summary>Returns the current presence status for the given player.</summary>
    ValueTask<PresenceStatus> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Returns up to <paramref name="take"/> ids of players currently <see cref="PresenceStatus.Online"/>.</summary>
    ValueTask<IReadOnlyList<Guid>> GetOnlinePlayerIdsAsync(int take, CancellationToken cancellationToken = default);
}
```

**`IPlayerRatingProvider` must mirror this shape:** one interface with XML docs on every member, `ValueTask` returns, `CancellationToken` parameter, `public` access. The companion `PlayerRatingSnapshot` record uses `double` fields to match the `Rating`, `RatingDeviation`, `Volatility` field types already in `src/GameKit.Rankings/Entities/PlayerRank.cs` lines 39–45.

---

### `src/GameKit.Core/Services/NullPlayerRatingProvider.cs` (null-object, request-response)

**Analog:** null-object default pattern for `IPresenceProvider` — no separate file, inlined in factory lambda. For `IPlayerRatingProvider`, a named `internal sealed` class is required (the type is referenced by `TryAddSingleton<IPlayerRatingProvider, NullPlayerRatingProvider>()` rather than an anonymous factory).

**Implementation contract:** return `ImmutableDictionary<Guid, PlayerRatingSnapshot>.Empty` (all players absent = empty dict). Phase 8's `MatchmakingService` will use `rating ?? 0` fallback. GPL header + `internal sealed` + `namespace GameKit.Core.Services`.

---

### `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` (modify — add TryAddSingleton block)

**Analog:** same file, lines 80–87 (GetService optional-port factory pattern):
```csharp
// Source: src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs lines 80-87
services.AddScoped<ISessionCompleteService>(sp => new SessionCompleteService(
    sp.GetRequiredService<GameKitDbContext>(),
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SessionCompleteService>>(),
    sp.GetServices<ISessionLifecycleObserver>(),
    sp.GetService<IPostSessionCompleteHandler>(),   // <-- GetService = nullable optional port
    sp.GetService<IIdempotencyStore>(),
    sp.GetService<ICanonicalRequestHasher>()));
```

**New block to INSERT** (after existing optional-port registrations, before `services.AddSingleton<IGameKitRateLimitPolicies, ...>`):
```csharp
// Phase 7 (CORE-18): IPlayerRatingProvider optional port — null-object default so
// Matchmaking operates in zero-rated mode when GameKit.Rankings is not installed.
// GameKit.Rankings registers its PlayerRankingsProvider via TryAddSingleton, which
// skips registration when NullPlayerRatingProvider is already present — the Rankings
// registration must run AFTER AddGameKit() to win the TryAdd race.
services.TryAddSingleton<IPlayerRatingProvider, NullPlayerRatingProvider>();
```

**Required using:** `using Microsoft.Extensions.DependencyInjection.Extensions;` is already present in the file's using block.

---

### `src/GameKit.Auth/Services/IPasswordHasher.cs` (modify — add NeedsRehash method)

**Analog:** same file (lines 1–23):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Services;

/// <summary>
/// Password hashing + verification abstraction. Default implementation is
/// <see cref="BCryptPasswordHasher"/> using BCrypt.Net-Next. AUTH-16 allows a future
/// <c>Argon2idPasswordHasher</c> sibling package (AUTH-V2-01) to be a drop-in replacement.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Returns a self-contained hash string (salt + work factor + ciphertext).</summary>
    string Hash(string password);

    /// <summary>Returns true iff <paramref name="password"/> verifies against <paramref name="hash"/>.</summary>
    bool Verify(string password, string hash);
}
```

**New method to ADD** after `Verify`:
```csharp
    /// <summary>
    /// Returns <c>true</c> when <paramref name="hash"/> was produced by a prior hasher and
    /// should be transparently re-hashed on the next successful login.
    /// <c>BCryptPasswordHasher</c> always returns <c>false</c> (no upgrade path from BCrypt to BCrypt).
    /// <c>Argon2idPasswordHasher</c> returns <c>true</c> for <c>$2a$</c> / <c>$2b$</c> prefixes.
    /// </summary>
    /// <param name="hash">The previously-stored hash string to inspect.</param>
    bool NeedsRehash(string hash);
```

**BCryptPasswordHasher.NeedsRehash implementation** (add to `src/GameKit.Auth/Services/BCryptPasswordHasher.cs`):
```csharp
    /// <inheritdoc />
    public bool NeedsRehash(string hash) => false;
    // BCrypt is the default hasher; it never needs re-hash by a newer BCrypt hasher.
    // Returns false unconditionally — Argon2idPasswordHasher overrides this to return
    // true for $2a$/$2b$ prefixes.
```

---

### `src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs` (modify — add rehash-on-verify block)

**Analog:** same file, lines 120–138 (the successful-verify path):
```csharp
// Source: src/GameKit.Auth/Providers/Password/PasswordOAuthProvider.cs lines 120-138
        if (!_hasher.Verify(password, credential.PasswordHash))
        {
            await _audit.WriteAsync(
                action: "auth.login.failure",
                targetType: "player",
                targetId: credential.PlayerId,
                actorId: credential.PlayerId,
                after: new { provider = "password", reason_code = "wrong_password" },
                reason: null,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return OAuthResult.Fail("invalid_credentials");
        }

        var banned = await BannedCheckHelper.CheckAsync(_ctx, credential.PlayerId, cancellationToken).ConfigureAwait(false);
        if (banned is not null) return banned;
        var tokens = await _refresh
            .IssueRootAsync(credential.PlayerId, Provider, fingerprint, cancellationToken)
            .ConfigureAwait(false);
        return OAuthResult.Ok(credential.PlayerId, tokens);
```

**Rehash block to INSERT** between the Verify success (after the `if (!_hasher.Verify(...))` block, before the banned check):
```csharp
        // AUTH-18 rehash-on-verify: when Argon2idPasswordHasher is the active IPasswordHasher,
        // NeedsRehash returns true for $2a$/$2b$ prefixes so BCrypt hashes are transparently
        // upgraded in the same request scope. BCryptPasswordHasher.NeedsRehash always returns false.
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

---

### `src/GameKit.Auth.Argon2/GameKit.Auth.Argon2.csproj` (config)

**Analog:** `src/GameKit.Auth/GameKit.Auth.csproj` (full file, 62 lines):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>GameKit.Auth</PackageId>
    <Description>...</Description>
    <PackageTags>gamekit;auth;jwt;steam;discord;oauth;gpl</PackageTags>
    <RootNamespace>GameKit.Auth</RootNamespace>
    <AssemblyName>GameKit.Auth</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\GameKit.Core\GameKit.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="BCrypt.Net-Next" />
    ...
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
  </ItemGroup>
</Project>
```

**Argon2 csproj delta from base Auth shape:**
- `PackageId` = `GameKit.Auth.Argon2`
- `RootNamespace` / `AssemblyName` = `GameKit.Auth.Argon2`
- `Description` = Argon2id password hasher for GameKit — optional sibling to BCrypt default. Phase 7.
- `PackageTags` = `gamekit;auth;argon2;password;gpl`
- `ProjectReference` points to `..\GameKit.Auth\GameKit.Auth.csproj` (NOT GameKit.Core — needs `IPasswordHasher`)
- No `Microsoft.EntityFrameworkCore.Design` (no migrations in this package)
- New package refs:
  ```xml
  <PackageReference Include="Isopoh.Cryptography.Argon2" />
  <PackageReference Include="BCrypt.Net-Next" />   <!-- migration-window verify path -->
  ```
- `GameKit.Build` Analyzer reference is **required** (MinVer version stamp) — same pattern as Auth.

---

### `src/GameKit.Auth.Argon2/AssemblyInfo.cs` (config)

**Analog:** `src/GameKit.Auth/AssemblyInfo.cs` (lines 1–30):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Auth.Tests")]
[assembly: InternalsVisibleTo("GameKit.Auth.Integration.Tests")]
...
namespace GameKit.Auth;

/// <summary>Marker type so other assemblies can pin a reference to GameKit.Auth at compile time.</summary>
internal static class AuthMarker { }
```

**Argon2 AssemblyInfo.cs:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Auth.Argon2.Tests")]

namespace GameKit.Auth.Argon2;

/// <summary>Marker type for GameKit.Auth.Argon2 assembly references.</summary>
internal static class Argon2Marker { }
```

Apply the same pattern to `AssemblyInfo.cs` for Google, Apple, Epic packages — only the `InternalsVisibleTo` grant name and namespace differ.

---

### `src/GameKit.Auth.Argon2/Services/Argon2idPasswordHasher.cs` (service, request-response)

**Analog:** `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` (full file, 37 lines):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Auth.Services;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by BCrypt.Net-Next 4.1.0. Work factor is configurable
/// via <see cref="PasswordOptions.BCryptWorkFactor"/> (default 12 per CONTEXT discretion).
/// </summary>
public sealed class BCryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    /// <summary>Constructs the hasher; reads work factor from <see cref="GameKitAuthOptions.Password"/>.</summary>
    public BCryptPasswordHasher(GameKitAuthOptions opts)
    {
        _workFactor = opts.Password.BCryptWorkFactor;
    }

    /// <inheritdoc />
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    /// <inheritdoc />
    public bool Verify(string password, string hash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
```

**Core pattern for Argon2idPasswordHasher** (mirrors BCrypt shape, adds NeedsRehash + dual-verify):
- `internal sealed class Argon2idPasswordHasher : IPasswordHasher` — **`internal sealed`** matching BCrypt's `public sealed` class scope (RESEARCH note: BCrypt is `public sealed`; Argon2 may be `public sealed` too since it's the only type in a public-API package — follow BCrypt's `public sealed` convention)
- Constructor takes `GameKitArgon2Options opts` (same dependency-injection shape as BCrypt taking `GameKitAuthOptions opts`)
- `Hash(string password)` calls Isopoh `Argon2.Hash(config)` returning `$argon2id$...` string
- `Verify(string password, string hash)` dispatches on prefix: `$2a$`/`$2b$` → `BCrypt.Net.BCrypt.Verify` with `SaltParseException` catch; otherwise → `Argon2.Verify(hash, password)` (hash first in Isopoh API)
- `NeedsRehash(string hash)` returns `hash.StartsWith("$2a$", StringComparison.Ordinal) || hash.StartsWith("$2b$", StringComparison.Ordinal)`

**Namespace:** `GameKit.Auth.Argon2.Services` (not `GameKit.Auth.Services` — different assembly)

---

### `src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs` (config / DI registration)

**Analog:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` line 83 (`AddSingleton<IPasswordHasher, BCryptPasswordHasher>()`):
```csharp
// Source: src/GameKit.Auth/Builder/AuthBuilderExtensions.cs line 83
builder.Services.AddSingleton<IPasswordHasher, BCryptPasswordHasher>();
```

**UseArgon2 extension pattern:**
```csharp
// File: src/GameKit.Auth.Argon2/Builder/Argon2BuilderExtensions.cs
// Extends IGameKitBuilder (NOT a new IGameKitAuthBuilder — per RESEARCH Open Question 3)
public static class Argon2BuilderExtensions
{
    /// <summary>
    /// Replaces the default <see cref="BCryptPasswordHasher"/> with <see cref="Argon2idPasswordHasher"/>.
    /// Call AFTER <c>.AddAuth(...)</c>. The existing <c>IPasswordHasher</c> singleton is removed and
    /// replaced so only one hasher is active at runtime.
    /// </summary>
    public static IGameKitBuilder UseArgon2(
        this IGameKitBuilder builder,
        Action<GameKitArgon2Options>? configure = null)
    {
        var opts = new GameKitArgon2Options();
        configure?.Invoke(opts);
        builder.Services.AddSingleton(opts);
        // Remove BCryptPasswordHasher registered by AddAuth(); replace with Argon2.
        builder.Services.RemoveAll<IPasswordHasher>();
        builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        return builder;
    }
}
```

**Required usings:** `GameKit.Core.Builder` (for `IGameKitBuilder`), `GameKit.Auth.Services` (for `IPasswordHasher`), `Microsoft.Extensions.DependencyInjection.Extensions` (for `RemoveAll<T>`).

---

### `src/GameKit.Auth.Google/Providers/Google/GoogleOAuthProvider.cs` (service/provider, request-response)

**Analog:** `src/GameKit.Auth/Providers/Discord/DiscordOAuthProvider.cs` (full file, 99 lines):

**Imports pattern** (lines 1–13):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
```

**Class declaration pattern** (lines 22–23):
```csharp
internal sealed class DiscordOAuthProvider : IOAuthProvider
```

**Constructor pattern** (lines 29–37) — inject `GameKitDbContext`, `IClock`, `IIdGenerator`, `IRefreshTokenService`:
```csharp
    public DiscordOAuthProvider(GameKitDbContext ctx, IClock clock, IIdGenerator ids, IRefreshTokenService refresh)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(refresh);
        _ctx = ctx; _clock = clock; _ids = ids; _refresh = refresh;
    }
```

**Provider discriminator** (line 40): `public string Provider => "discord";` — for Google: `"google"`, Apple: `"apple"`, Epic: `"epic"`

**Core upsert pattern** (lines 52–98) — existing-identity update vs new Player + PlayerIdentity creation, then BannedCheckHelper + IssueRootAsync:
```csharp
        var existing = await _ctx.Set<PlayerIdentity>()
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Provider == Provider && i.ExternalId == externalId, cancellationToken)
            .ConfigureAwait(false);

        Guid playerId;
        if (existing is not null)
        {
            playerId = existing.PlayerId;
            var tracked = await _ctx.Set<PlayerIdentity>()
                .FirstAsync(i => i.Id == existing.Id, cancellationToken).ConfigureAwait(false);
            tracked.DisplayName = displayName ?? tracked.DisplayName;
            tracked.AvatarUrl = avatarUrl ?? tracked.AvatarUrl;
            tracked.UpdatedAt = _clock.UtcNow;
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            playerId = _ids.NewId();
            var fallbackName = displayName ?? $"DiscordUser-{externalId[^6..]}";
            _ctx.Players.Add(new Player { Id = playerId, DisplayName = fallbackName, CreatedAt = _clock.UtcNow });
            _ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
            {
                Id = _ids.NewId(), PlayerId = playerId, Provider = Provider,
                ExternalId = externalId, DisplayName = displayName, AvatarUrl = avatarUrl,
                CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow,
            });
            await _ctx.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        var banned = await BannedCheckHelper.CheckAsync(_ctx, playerId, cancellationToken).ConfigureAwait(false);
        if (banned is not null) return banned;
        var tokens = await _refresh.IssueRootAsync(playerId, Provider, fingerprint, cancellationToken).ConfigureAwait(false);
        return OAuthResult.Ok(playerId, tokens);
```

**Apple delta:** Apple `CompleteLoginAsync` receives `sub` as `externalId` (NOT email). The relay email and name are stored in `PlayerIdentity.Metadata` JSONB on first login only (when `existing is null`). The `Metadata` column is `JsonDocument?` per `PlayerIdentity.cs` line 37.

---

### `src/GameKit.Auth.Google/Builder/GoogleBuilderExtensions.cs` (config / DI registration)

**Analog:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 200–252 (Discord conditional scheme block + OnCreatingTicket):

**Conditional scheme registration pattern** (lines 200–201):
```csharp
// Source: src/GameKit.Auth/Builder/AuthBuilderExtensions.cs lines 200-201
if (!string.IsNullOrEmpty(opts.Discord.ClientId) && !string.IsNullOrEmpty(opts.Discord.ClientSecret))
{
    authBuilder.AddDiscord(discord => { ... });
}
```

**Provider resolution in OnCreatingTicket** (lines 226–235):
```csharp
// Source: src/GameKit.Auth/Builder/AuthBuilderExtensions.cs lines 226-235
var providers = ctx.HttpContext.RequestServices.GetServices<IOAuthProvider>();
IOAuthProvider? provider = null;
foreach (var p in providers)
{
    if (p.Provider == "discord") { provider = p; break; }
}
if (provider is null) return;
var fingerprint = ctx.HttpContext.Request.Headers["X-GameKit-Device"].ToString();
var fp = string.IsNullOrEmpty(fingerprint) ? null : fingerprint;
var result = await provider.CompleteLoginAsync(discordId, username, avatarUrl: null, fp, ctx.HttpContext.RequestAborted)
    .ConfigureAwait(false);
```

**Token stash in properties** (lines 241–249):
```csharp
// Source: src/GameKit.Auth/Builder/AuthBuilderExtensions.cs lines 241-249
if (result is { Success: true, Tokens: not null })
{
    ctx.Properties.Items["gamekit.access_jwt"] = result.Tokens.AccessJwt;
    ctx.Properties.Items["gamekit.refresh_raw"] = result.Tokens.RawRefresh;
    ctx.Properties.Items["gamekit.player_id"] = result.PlayerId?.ToString();
}
```

**Critical pitfall (RESEARCH §Pitfall 4):** The existing Scrutor scan `FromAssemblyOf<IOAuthProvider>()` in `AuthBuilderExtensions.cs` line 116 scans only the `GameKit.Auth` assembly. Each new provider's `Add*()` extension method MUST explicitly register its own provider:
```csharp
// Required in every new provider's Add* extension — NOT auto-discovered by Scrutor
builder.Services.AddScoped<IOAuthProvider, GoogleOAuthProvider>();
// Register BEFORE the scheme so OnCreatingTicket can resolve the provider.
```

**Extension target:** `IGameKitBuilder` (not a new `IGameKitAuthBuilder` — per RESEARCH Open Question 3).

**Required auth scheme helpers:**
- Google: `builder.Services.AddAuthentication().AddGoogle(...)` (no `authBuilder` local required if `AddAuth()` already called)
- Apple: `authBuilder.AddApple(...)` from `AspNet.Security.OAuth.Apple`
- Epic: `authBuilder.AddOAuth<EpicOAuthOptions, EpicOAuthHandler>(...)`

---

### `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthOptions.cs` + `EpicOAuthHandler.cs`

**Analog:** ASP.NET Core's `OAuthOptions` + `OAuthHandler<T>` (shared framework types — no analog in codebase).

**EpicOAuthOptions shape** (from RESEARCH §Pattern 3):
```csharp
// File: src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthOptions.cs
public class EpicOAuthOptions : OAuthOptions
{
    public EpicOAuthOptions()
    {
        AuthorizationEndpoint    = "https://www.epicgames.com/id/authorize";
        TokenEndpoint            = "https://api.epicgames.dev/epic/oauth/v1/token";
        UserInformationEndpoint  = "https://api.epicgames.dev/epic/oauth/v1/userInfo";
        Scope.Add("basic_profile");
        CallbackPath = new PathString("/signin-epic");
    }
}
```

**EpicOAuthHandler override surface:**
- `CreateTicketAsync` — extracts `account_id` claim (Epic's stable `sub` equivalent) + `display_name`
- `ExchangeCodeAsync` — override to inject `Authorization: Basic base64(clientId:clientSecret)` header (RESEARCH §Pitfall 6: Epic token endpoint uses HTTP Basic auth, not form fields)

---

### `tests/GameKit.Auth.Argon2.Tests/GameKit.Auth.Argon2.Tests.csproj` (test config)

**Analog:** `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` (full file, 21 lines):
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>GameKit.Auth.Tests</RootNamespace>
    <AssemblyName>GameKit.Auth.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <WarningsAsErrors />
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Moq" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GameKit.Auth\GameKit.Auth.csproj" />
    <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  </ItemGroup>
</Project>
```

**Argon2 test csproj delta:**
- `RootNamespace` / `AssemblyName` = `GameKit.Auth.Argon2.Tests`
- `ProjectReference` points to `..\..\src\GameKit.Auth.Argon2\GameKit.Auth.Argon2.csproj`
- Also needs `ProjectReference` to `..\..\src\GameKit.Auth\GameKit.Auth.csproj` (for `IPasswordHasher` interface)
- No `Microsoft.EntityFrameworkCore.InMemory` needed for pure unit tests of Argon2 hashing

**Google/Apple/Epic test csproj shape:** identical pattern — `RootNamespace`/`AssemblyName` = `GameKit.Auth.{Provider}.Tests`, `ProjectReference` to `..\..\src\GameKit.Auth.{Provider}\GameKit.Auth.{Provider}.csproj` + Auth + TestFixtures. Add `<PackageReference Include="WireMock.Net" />` (already in Directory.Packages.props at 2.2.0) for OAuth callback stubs.

---

### `tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs` (unit test shape — for Argon2HasherTests analog)

**Analog:** `tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs` (full file, 47 lines):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Services;
using Xunit;

namespace GameKit.Auth.Tests;

public sealed class BCryptPasswordHasherTests
{
    private static BCryptPasswordHasher NewHasher(int workFactor = 4)
    {
        var opts = new GameKitAuthOptions();
        opts.Password.BCryptWorkFactor = workFactor;
        return new BCryptPasswordHasher(opts);
    }

    [Fact]
    public void Hash_Then_Verify_With_Same_Password_Returns_True() { ... }

    [Fact]
    public void Verify_With_Wrong_Password_Returns_False() { ... }

    [Fact]
    public void Verify_With_Malformed_Hash_Returns_False_Not_Throws() { ... }

    [Fact]
    public void Different_Hashes_For_Same_Password() { ... }  // salts are random
}
```

**Argon2HasherTests additions** over BCrypt shape:
- Factory method: `new Argon2idPasswordHasher(new GameKitArgon2Options { TimeCost = 1, MemoryCost = 1024 })` (low params for test speed)
- `NeedsRehash_BcryptHash_ReturnsTrue` — pass `"$2a$12$abc..."` → assert true
- `NeedsRehash_Argon2Hash_ReturnsFalse` — pass result of `Hash("x")` → assert false
- `Verify_BcryptHash_CorrectPassword_ReturnsTrue` — verify BCrypt hash with correct password (migration path)
- `Hash_Returns_Argon2id_Prefix` — assert result starts with `"$argon2id$"`

---

### `tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs` (provider DI test shape — for Google/Apple/Epic provider tests)

**Analog:** `tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs` (full file, 87 lines):
```csharp
// Source: tests/GameKit.Auth.Tests/ScrutorProviderDiscoveryTests.cs lines 19-87
public sealed class ScrutorProviderDiscoveryTests
{
    private static IServiceCollection BuildServicesWithAuth()
    {
        var services = new ServiceCollection();
        var builder = services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=x;Username=gamekit_app;Password=x";
            o.AutoMigrate = false;
        });
        builder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "x";
            o.Jwt.Audience = "x";
        });
        return services;
    }

    [Fact]
    public void AddAuth_Registers_SteamAndDiscord_IOAuthProvider_Implementations()
    {
        // resolve descriptors via services.Where(d => d.ServiceType == typeof(IOAuthProvider))
        // assert Count == 4 (Steam + Discord + Guest + Password)
    }

    [Fact]
    public void IOAuthProvider_Registrations_Are_Scoped()
    {
        Assert.All(descriptors, d => Assert.Equal(ServiceLifetime.Scoped, d.Lifetime));
    }
}
```

**GoogleProviderTests / AppleProviderTests / EpicProviderTests shape:**
- `BuildServicesWith{Provider}()` helper calls `AddGameKit().AddAuth(skip=true).Add{Provider}(opts => { ClientId = "x"; ClientSecret = "x"; ... })`
- Assert `GetServices<IOAuthProvider>()` contains entry with `Provider == "google"` (or `"apple"`, `"epic"`)
- Assert it is `ServiceLifetime.Scoped`
- Assert conditional: when ClientId is empty, scheme is NOT registered (`GetServices<IAuthenticationSchemeProvider>()` does not include the scheme name)
- No Postgres container required for DI-smoke tests; `SkipAuthenticationSchemeRegistration = true` (or equivalent) to avoid PEM path requirements

---

### `tests/GameKit.Core.Tests/IPlayerRatingProviderTests.cs`

**Analog:** `tests/GameKit.Auth.Tests/BCryptPasswordHasherTests.cs` (unit test shape — no DB required):

**Test shape:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
// namespace GameKit.Core.Tests
// Uses ServiceCollection directly, no Testcontainers
public sealed class IPlayerRatingProviderTests
{
    [Fact]
    public async Task NullPlayerRatingProvider_Returns_EmptyDictionary_For_Any_Players()
    {
        var provider = new NullPlayerRatingProvider();
        var result = await provider.GetRatingsAsync(
            new[] { Guid.NewGuid(), Guid.NewGuid() }, ladderId: Guid.NewGuid());
        Assert.Empty(result);
    }

    [Fact]
    public void AddGameKit_Registers_NullPlayerRatingProvider_As_Singleton()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=x;Username=gamekit_app;Password=x";
            o.AutoMigrate = false;
        });
        var descriptor = services.Single(d => d.ServiceType == typeof(IPlayerRatingProvider));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(NullPlayerRatingProvider), descriptor.ImplementationType);
    }
}
```

**NullPlayerRatingProvider must be `internal` but visible to test via `InternalsVisibleTo("GameKit.Core.Tests")`.** Check that `src/GameKit.Core/AssemblyInfo.cs` already grants this; if not, a new grant is required.

---

## Shared Patterns

### GPL Header
**Source:** Every existing `.cs` file (e.g. `src/GameKit.Auth/Services/BCryptPasswordHasher.cs` lines 1–2)
**Apply to:** Every new `.cs` file in all new packages
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### XML Doc on Every Public API
**Source:** Directory.Build.props line 9 (`<WarningsAsErrors>CS1591;nullable</WarningsAsErrors>`)
**Apply to:** Every `public` type and member in `IPlayerRatingProvider.cs`, `PlayerRatingSnapshot`, `GameKitArgon2Options`, `Argon2idPasswordHasher`, all `Add*` extension methods, all options classes
**Enforcement:** CS1591 is a hard build error. Test projects suppress via `<NoWarn>$(NoWarn);CS1591</NoWarn>` + `<WarningsAsErrors />` (clears the inherited error list) as seen in `tests/GameKit.Auth.Tests/GameKit.Auth.Tests.csproj` lines 7–8.

### MinVer + GameKit.Build Analyzer
**Source:** `src/GameKit.Auth/GameKit.Auth.csproj` lines 57–61
**Apply to:** All five new library csproj files (NOT test projects)
```xml
<ItemGroup>
  <ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```
**Why required:** `GameKit.Build` source-generator emits `GameKitMarker.GameKitVersion` + `GameKitMarker.AssemblyName` constants at compile time, consumed by `GameKitVersionAssertionHostedService` to enforce the coordinated release train (DIST-07 / OPS-05).

### Central Package Management (CPM)
**Source:** `Directory.Packages.props` line 1–3
**Apply to:** All `<PackageReference>` elements in new csproj files — **never** include a `Version=""` attribute
**New pins needed in `Directory.Packages.props`:**
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

### IGameKitBuilder Extension Target
**Source:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` line 44 + `src/GameKit.Core/Builder/IGameKitBuilder.cs`
**Apply to:** All `Add*` / `Use*` extension methods (`UseArgon2`, `AddGoogle`, `AddApple`, `AddEpic`)
```csharp
// Extension target is always IGameKitBuilder
public static IGameKitBuilder AddGoogle(
    this IGameKitBuilder builder,
    Action<GameKitGoogleOptions> configure)
// Returns IGameKitBuilder to allow fluent chaining
```

### IOAuthProvider Self-Registration Pattern (Critical — RESEARCH §Pitfall 4)
**Source:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 115–119 (Scrutor scan scopes to `GameKit.Auth` assembly only)
**Apply to:** `GoogleBuilderExtensions.cs`, `AppleBuilderExtensions.cs`, `EpicBuilderExtensions.cs`
```csharp
// In each Add* method, BEFORE registering the authentication scheme:
builder.Services.AddScoped<IOAuthProvider, GoogleOAuthProvider>();
// This is NOT auto-discovered by Scrutor — sibling packages must self-register.
```

### Conditional Authentication Scheme Registration
**Source:** `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` lines 200–201
**Apply to:** All three new OAuth provider builder extensions
```csharp
if (!string.IsNullOrEmpty(opts.ClientId) && !string.IsNullOrEmpty(opts.ClientSecret))
{
    // Register auth scheme with authBuilder
}
// If credentials absent: skip scheme registration; IOAuthProvider is still registered
// so DI smoke tests can resolve it without scheme infrastructure.
```

### FrameworkReference Instead of NuGet for ASP.NET Core Types
**Source:** `src/GameKit.Auth/GameKit.Auth.csproj` lines 14–16
**Apply to:** All four new library csproj files
```xml
<FrameworkReference Include="Microsoft.AspNetCore.App" />
```
This provides `OAuthHandler<T>`, `OAuthOptions`, `AuthenticationBuilder`, `IApplicationBuilder` — none of these should be NuGet-referenced.

---

## No Analog Found

All Phase 7 files have analogs in the existing codebase. The only partial-match cases are:

| File | Role | Data Flow | Note |
|------|------|-----------|------|
| `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthHandler.cs` | service | request-response | `OAuthHandler<T>` derivation has no existing in-codebase analog — follow shared-framework base class. RESEARCH §Pattern 3 provides the override surface. |
| `src/GameKit.Auth.Epic/Providers/Epic/EpicOAuthOptions.cs` | config | — | `OAuthOptions` subclass has no existing in-codebase analog — two-field endpoint constructor pattern is standard ASP.NET Core convention. |

---

## Metadata

**Analog search scope:** `src/GameKit.Core/`, `src/GameKit.Auth/`, `src/GameKit.Rankings/`, `tests/GameKit.Auth.Tests/`, `tests/GameKit.Auth.Integration.Tests/`, `tests/GameKit.Core.Tests/`, `tests/GameKit.TestFixtures/`, `Directory.Build.props`, `Directory.Packages.props`
**Files scanned:** 28 source files read directly
**Pattern extraction date:** 2026-06-05
