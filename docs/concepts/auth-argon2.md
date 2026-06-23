# GameKit.Auth.Argon2 — Concepts

## What It Does

`GameKit.Auth.Argon2` is an opt-in sibling package that replaces `GameKit.Auth`'s BCrypt
default with an **Argon2id** password hasher backed by
[Isopoh.Cryptography.Argon2](https://github.com/mheyman/Isopoh.Cryptography.Argon2). It is
a drop-in replacement — no schema changes, no migration — and it supports a transparent
**BCrypt → Argon2id migration window** so existing password hashes are verified with BCrypt
on first login and then re-hashed with Argon2id before the response returns.

## Why Isopoh?

- **Fully managed C#** — no native bindings, portable across Linux, macOS, Windows, and WASM.
- **Single-call API** — `Hash()` + `Verify()` rather than the `DeriveBytes` pattern.
- **`SecureArray`** — zeroed-on-dispose sensitive memory included in the library.

BCrypt is still the default because it is widely understood and has no platform constraints.
Argon2id is the recommended migration target for high-security deployments or where memory-hard
hashing is required.

## Interface Implemented

`GameKit.Auth.Argon2` provides a concrete implementation of `IPasswordHasher` from
`GameKit.Auth`:

```csharp
// The interface (lives in GameKit.Auth):
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

// GameKit.Auth.Argon2 ships:
//   Argon2idPasswordHasher : IPasswordHasher
// which is registered when you call .UseArgon2().
```

## How to Opt In

```csharp
// In Program.cs, after AddAuth(...):
gk.AddAuth(auth => { /* ... */ })
  .UseArgon2(argon2 =>
  {
      // Optional — defaults are safe for most deployments:
      argon2.MemoryCost     = 65536;  // 64 MiB
      argon2.TimeCost       = 3;      // 3 iterations
      argon2.Parallelism    = 1;
      argon2.HashLength     = 32;
  });
```

Calling `.UseArgon2()` replaces the BCrypt `IPasswordHasher` registration with
`Argon2idPasswordHasher`. Existing BCrypt hashes stored in `player_credentials.password_hash`
are still verified transparently during the migration window — the hasher tries Argon2id
first, falls back to BCrypt, and if BCrypt succeeds it immediately re-hashes the password
with Argon2id before returning.

## Library-vs-Consumer Responsibility Line

| GameKit.Auth.Argon2 owns | Consumer owns |
|--------------------------|---------------|
| Argon2id hash + verify implementation | Argon2 parameter tuning (memory, time, parallelism) |
| BCrypt → Argon2id migration window | Decision of when to migrate (install the package) |
| Thread-safe `SecureArray` memory handling | None — managed automatically |

## Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `Isopoh.Cryptography.Argon2` | pinned in `Directory.Packages.props` | Argon2id implementation |
| `BCrypt.Net-Next` | 4.0.3 | Required during migration window to verify legacy BCrypt hashes |

## See Also

- [auth.md](auth.md) — the core auth package and `IPasswordHasher` context.
- [API reference](../api/GameKit.Auth.Argon2.yml) — full member-level docs.
