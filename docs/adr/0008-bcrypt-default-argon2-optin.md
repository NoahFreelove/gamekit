<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0008: BCrypt default password hashing; Argon2id as opt-in sibling package

**Status:** Accepted

## Context

`GameKit.Auth` needs a password hashing implementation for the credentials store
(`player_credentials` table). Two families of algorithms were evaluated:

**BCrypt (BCrypt.Net-Next 4.0.3):** Work-factor-based, widely deployed, simple
API (`HashPassword` / `Verify`). BCrypt is well-understood, has no known
practical weaknesses for password hashing, and is trivially available on all
.NET platforms (pure managed code). The downside is that BCrypt has a 72-byte
input limit — passwords longer than 72 bytes are silently truncated. For game
credentials (typically short, device-generated tokens) this limit is
not a practical concern.

**Argon2id:** The winner of the Password Hashing Competition (2015). Memory-hard,
side-channel resistant, recommended by OWASP for new systems. The primary concern
is choosing the right .NET implementation:

- `Konscious.Security.Cryptography.Argon2`: requires driving the `DeriveBytes`
  API manually; last NuGet release predates .NET 9.
- `Isopoh.Cryptography.Argon2`: 100 % managed C# (no native bindings), works
  on Linux/macOS/Windows/WASM; provides `Hash()` / `Verify()` directly; includes
  `SecureArray` (zeroed-on-dispose memory for password handling); actively
  maintained.

The memory-hard nature of Argon2 introduces a tuning concern: consumers on
constrained hardware (embedded, shared-VM, resource-limited containers) need to
tune the memory parameter or risk OOM kills. A poor default Argon2
configuration is worse than a well-configured BCrypt default.

## Decision

- **`GameKit.Auth`** ships with `BCrypt.Net-Next` as the default `IPasswordHasher`
  implementation (`BCryptPasswordHasher`).
- **`GameKit.Auth.Argon2`** is a separate companion NuGet package that provides
  `Argon2idPasswordHasher` using `Isopoh.Cryptography.Argon2`. Consumers opt in
  by referencing this package and registering `Argon2idPasswordHasher` as the
  `IPasswordHasher` in DI.

This follows GameKit's "install only what you need" principle: the default is safe
and portable; the opt-in is for consumers who want memory-hard hashing and are
willing to tune Argon2's parameters for their deployment environment.

## Consequences

- **Positive:** The default (BCrypt) is portable across all platforms with no
  tuning required. Production-grade default for game credentials.
- **Positive:** Consumers who need Argon2 (e.g., applications that also store user
  account passwords for browser-based access) can opt in without changing the
  `IPasswordHasher` interface — just swap the implementation.
- **Negative:** BCrypt's 72-byte input limit is a known theoretical concern.
  GameKit's credential store is primarily used for device tokens and hashed
  external identifiers — not human-entered passwords — mitigating this in practice.
- **Dependency:** `Isopoh.Cryptography.Argon2` is MIT-licensed and GPL-compatible.
  `BCrypt.Net-Next` is MIT-licensed and GPL-compatible.
