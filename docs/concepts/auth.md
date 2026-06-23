# GameKit.Auth — Concepts

## What It Does

`GameKit.Auth` implements the complete player identity and authentication layer: guest accounts,
password-based accounts, third-party OAuth providers (Steam, Discord, and the sibling
Apple/Epic/Google packages), JWT issuance, refresh-token rotation, account linking, account
merging, and an audit trail. It deliberately does **not** use ASP.NET Core Identity — GameKit
owns its own `players`, `player_identities`, and `player_credentials` schema that fits the
game-backend model without fighting an opinionated user-table convention.

## Key Public Interfaces

### `IOAuthProvider`

The primary extension point. Each external identity provider (Steam, Discord, Apple, Epic,
Google) is an `IOAuthProvider` implementation. Implementations are discovered via Scrutor
assembly scanning of the `GameKit.Auth` assembly. Third-party sibling packages
(`GameKit.Auth.Apple`, `GameKit.Auth.Google`, `GameKit.Auth.Epic`) ship their own
`IOAuthProvider` implementations and **must be registered explicitly** — Scrutor scans only
the `GameKit.Auth` assembly by default (see [auth-providers.md](auth-providers.md)).

```csharp
public class MySteamProvider : IOAuthProvider
{
    public string Scheme => "Steam";
    // ...
}
// Register before AddAuth():
services.AddSingleton<IOAuthProvider, MySteamProvider>();
```

### `IPasswordHasher`

Pluggable password-hashing strategy. The default implementation uses BCrypt
(`BCrypt.Net-Next 4.0.3`). To replace it with Argon2id, install `GameKit.Auth.Argon2` and
call `.UseArgon2()` — see [auth-argon2.md](auth-argon2.md).

```csharp
// Replace with a custom hasher:
services.AddSingleton<IPasswordHasher, MyCustomHasher>();
// — OR install GameKit.Auth.Argon2 and call:
gk.AddAuth(...).UseArgon2();
```

### `IJwtIssuer`

Issues short-lived access JWTs (RS256). The library owns the signing key lifecycle and the
token claims format. Consumers who need custom claims in the JWT should implement
`IJwtIssuer` or use the `IAuthAuditWriter` hook rather than replacing the issuer directly.

### `IRefreshTokenService`

Manages refresh-token families. Raw tokens are never stored — only their SHA-256 hash. Token
rotation is handled automatically: each `/auth/token/refresh` call issues a new token and
invalidates the used one. Replay of an already-rotated refresh token invalidates the entire
family (security circuit-breaker).

### `IIdentityLinker`

Handles the "link a second provider to an existing account" flow. For example, a player who
registered with a guest account can later link their Discord identity without creating a
second player record.

### `IAccountMergeService`

Handles the rare case where a player has two separate accounts (e.g. created a guest account
on a new device, then later authenticates with an OAuth provider that was linked to an older
account). Merge transfers history from the source account and deletes it.

### `IAuthAuditWriter`

Called on significant auth events (token issue, refresh, revoke, login, link, merge). The
default implementation is a no-op `NullAuthAuditWriter`. Replace it to write audit records
to your own store:

```csharp
services.AddSingleton<IAuthAuditWriter, MyDatabaseAuditWriter>();
```

### `IGuestUpgradeService`

Handles the "upgrade guest to registered account" flow — a guest player supplies a username
and password (or OAuth identity) and the transient guest record becomes a full account
without losing progress.

### `IExternalIdHasher`

Hashes third-party user IDs before they are stored in `player_identities`. The default
implementation is a deterministic HMAC-SHA256 keyed from `GameKitOptions.ExternalIdHashKey`.
Replace only if you have a specific key-rotation or storage requirement.

### `IIsGuestResolver`

Utility port for other packages to check whether the currently-authenticated player is a
guest (no password + no linked provider). Used by the admin UI and by profile endpoints
that restrict guest access to certain operations.

## Wire-Up

```csharp
gk.AddAuth(auth =>
{
    auth.JwtPrivateKeyPem  = File.ReadAllText("keys/dev-priv.pem");
    auth.JwtPublicKeyPem   = File.ReadAllText("keys/dev-pub.pem");
    auth.RefreshTokenTtl   = TimeSpan.FromDays(30);
    auth.AccessTokenTtl    = TimeSpan.FromMinutes(15);
    auth.ExternalIdHashKey = config["GameKit:Auth:ExternalIdHashKey"]!;

    // Built-in providers (Steam requires AppId; Discord requires ClientId + Secret):
    auth.AddSteam(steam => { steam.ApplicationKey = config["Steam:ApiKey"]!; });
    auth.AddDiscord(discord =>
    {
        discord.ClientId     = config["Discord:ClientId"]!;
        discord.ClientSecret = config["Discord:ClientSecret"]!;
    });
});

// In the pipeline:
app.UseGameKitAuth();
app.MapAuth();   // /auth/* endpoints
```

## Library-vs-Consumer Responsibility Line

| GameKit.Auth owns | Consumer owns |
|-------------------|---------------|
| JWT issuance + RS256 key lifecycle | Private/public key PEM files on disk |
| Refresh-token rotation + replay detection | Refresh-token TTL configuration |
| Guest account lifecycle | Guest-upgrade UX in the game client |
| Provider credential validation | Provider API keys/secrets (Steam, Discord, …) |
| Identity linking + account merge logic | Decision of when to offer merge to users |
| Audit event dispatch | Audit record storage (`IAuthAuditWriter`) |
| BCrypt hashing default | Argon2id opt-in (`GameKit.Auth.Argon2`) |

## See Also

- [auth-argon2.md](auth-argon2.md) — opt-in Argon2id password hashing.
- [auth-providers.md](auth-providers.md) — Apple, Epic, Google OAuth provider packages.
- [API reference](../../api/GameKit.Auth.yml) — full member-level docs.
- [docs/ops/jwt-keys.md](../ops/jwt-keys.md) — key generation and rotation runbook.
- [docs/security-checklist.md](../security-checklist.md) — auth hardening checklist.
