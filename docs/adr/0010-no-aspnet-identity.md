<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0010: No ASP.NET Core Identity

**Status:** Accepted

## Context

ASP.NET Core Identity is Microsoft's built-in user management framework. It
provides: a user entity (`IdentityUser`), a role system, password hashing via
`IPasswordHasher<TUser>`, email confirmation, two-factor authentication, token
providers, and EF Core entity configurations.

GameKit has an overlapping but distinct data model:

| ASP.NET Core Identity | GameKit |
|-----------------------|---------|
| `IdentityUser` (single table, flat) | `players` (identity) + `player_identities` (per-provider) + `player_credentials` (credentials) |
| `IdentityRole` + `UserRole` | Admin role concept lives in `GameKit.Admin.UI.admin_users` |
| `IPasswordHasher<TUser>` | `IPasswordHasher` (non-generic, no user reference — GameKit hashes credentials independently of the player entity) |
| Migration: `AspNetUsers`, `AspNetRoles`, etc. | Migration: `gamekit.players`, `gamekit.player_identities`, `gamekit.player_credentials` — schema is game-service-specific |

If GameKit took a dependency on ASP.NET Core Identity, it would:

1. Introduce the Identity schema (`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`,
   etc.) into the consumer's database alongside GameKit's own tables — creating
   two competing user-management schemas.
2. Force consumers to use `IdentityUser` as the player entity, which is
   incompatible with GameKit's `players` / `player_identities` / `player_credentials`
   split (the split is required to support multiple linked identities per player
   — Steam + Discord + guest on the same account).
3. Pull in Identity's UI scaffolding and opinionated routing that conflicts with
   GameKit's minimal-API approach and the `/auth/*` route surface.

The pattern "GameKit replaces ASP.NET Core Identity for game services" is
explicit in the project design. Consumers who need enterprise authentication
(corporate AD/Azure AD, SAML) alongside GameKit should use Identity or OpenIddict
in parallel, not instead of, GameKit's auth package — they serve different
populations (employees vs. players).

## Decision

GameKit.Auth does not depend on or integrate with ASP.NET Core Identity. All
player identity management, credential storage, and password hashing is
implemented directly using EF Core entities (`player.cs`, `player_identity.cs`,
`player_credential.cs`) and the `IPasswordHasher` abstraction (implemented by
`BCryptPasswordHasher` from ADR-0008).

The `Microsoft.AspNetCore.Authentication.JwtBearer` middleware is used for token
validation — this is a shared-framework component, not an Identity component.

## Consequences

- **Positive:** Clean, game-service-specific data model with no competing schemas.
  The `players` / `player_identities` / `player_credentials` split is native to
  the GameKit entity model and supports multi-identity, multi-device, and
  guest-upgrade flows without adapters.
- **Positive:** No Identity UI scaffolding, no `RoleManager`, no `UserManager`.
  GameKit's surface is smaller and its migration boundaries are entirely under
  the project's control (see CLAUDE.md migration boundary constraint).
- **Negative:** Consumers who need enterprise features (AD integration, SAML, MFA
  via phone/email) cannot reuse Identity's built-in providers for those use cases.
  They must bring their own implementation or use Identity in parallel. This is
  documented as an explicit GameKit scope boundary.
- **Future:** If a significant use case requires enterprise integration, consider a
  `GameKit.Auth.EnterpriseIdentity` companion package that bridges Identity's
  `IUserStore<TUser>` with GameKit's `players` table rather than adding Identity
  as a core dependency.
