# GameKit.Auth.Apple / .Epic / .Google — Concepts

## What They Do

`GameKit.Auth.Apple`, `GameKit.Auth.Epic`, and `GameKit.Auth.Google` are lightweight sibling
packages that each ship one `IOAuthProvider` implementation for their respective platform.
They plug into `GameKit.Auth` without modifying the auth package — they extend the same
`IGameKitBuilder` with their own `AddApple()` / `AddEpic()` / `AddGoogle()` method.

These packages use the [aspnet-contrib](https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers)
OAuth libraries (maintained by Martin Costello + Kévin Chalet) to handle the OAuth2 protocol
dance, while `GameKit.Auth` handles the GameKit-side identity linking and JWT issuance.

## Interface Each Package Implements

Each package provides a concrete implementation of `IOAuthProvider` from `GameKit.Auth`:

```csharp
// Defined in GameKit.Auth:
public interface IOAuthProvider
{
    string Scheme { get; }
    // Provides the authentication scheme + claims extraction that
    // GameKit.Auth uses to resolve or create player_identities rows.
}

// Each sibling ships its own implementation:
//   GameKit.Auth.Apple  → AppleOAuthProvider  : IOAuthProvider
//   GameKit.Auth.Epic   → EpicOAuthProvider   : IOAuthProvider
//   GameKit.Auth.Google → GoogleOAuthProvider  : IOAuthProvider
```

## Important: Explicit Registration Required

`GameKit.Auth` uses Scrutor to scan its **own assembly** for `IOAuthProvider` implementations.
The sibling packages live in separate assemblies and are **not** auto-discovered. Each must
be registered explicitly by calling its builder extension after `AddAuth(...)`:

```csharp
gk.AddAuth(auth => { /* Steam, Discord already wired here */ })
  .AddApple(apple =>
  {
      apple.ClientId    = config["Apple:ClientId"]!;
      apple.TeamId      = config["Apple:TeamId"]!;
      apple.KeyId       = config["Apple:KeyId"]!;
      apple.PrivateKey  = config["Apple:PrivateKey"]!;
  })
  .AddGoogle(google =>
  {
      google.ClientId     = config["Google:ClientId"]!;
      google.ClientSecret = config["Google:ClientSecret"]!;
  })
  .AddEpic(epic =>
  {
      epic.ClientId     = config["Epic:ClientId"]!;
      epic.ClientSecret = config["Epic:ClientSecret"]!;
  });
```

Omitting the `Add*` call means the provider is never registered and the corresponding
`/auth/link/{scheme}` endpoint returns 404.

## Call Order Requirement

Each `Add*` extension must be called **after** `AddAuth(...)` on the same builder. The
sibling packages register additional ASP.NET Core authentication schemes that depend on the
auth infrastructure `AddAuth` puts in place.

## Adding a Custom Provider

If you need a provider not covered by the sibling packages (e.g. Twitch, Facebook), implement
`IOAuthProvider` and register it before `AddAuth(...)`:

```csharp
services.AddSingleton<IOAuthProvider, TwitchOAuthProvider>();
gk.AddAuth(auth => { ... });
```

The Scrutor scan runs at `AddAuth` time. Any `IOAuthProvider` already in the service
collection before that call is preserved (Scrutor's `TryAdd` semantics).

## Library-vs-Consumer Responsibility Line

| Package owns | Consumer owns |
|--------------|---------------|
| OAuth2 protocol flow (PKCE, token exchange, claims extraction) | Provider API credentials (ClientId, ClientSecret, keys) |
| `player_identities` row creation / linking | Decision of which providers to enable |
| External ID hashing before storage | None — handled by `IExternalIdHasher` in GameKit.Auth |
| Resilience (retry + circuit-breaker via `Microsoft.Extensions.Http.Resilience`) | None |

## See Also

- [auth.md](auth.md) — core auth package and the `IOAuthProvider` interface.
- [API reference — GameKit.Auth.Apple](../api/GameKit.Auth.Apple.yml)
- [API reference — GameKit.Auth.Epic](../api/GameKit.Auth.Epic.yml)
- [API reference — GameKit.Auth.Google](../api/GameKit.Auth.Google.yml)
- [docs/ops/jwt-keys.md](../ops/jwt-keys.md) — key management (also covers Apple private key).
