<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0004: aspnet-contrib OAuth providers, not hand-rolled

**Status:** Accepted

## Context

`GameKit.Auth` supports social login via Steam (OpenID 2.0) and Discord
(OAuth2). Implementing these protocols from scratch involves:

- Steam: OpenID 2.0 verification, claimed identifier parsing, return URL
  validation, nonce tracking — all security-critical and non-trivial.
- Discord: OAuth2 authorisation code flow, token exchange, scope handling, state
  parameter CSRF protection.

Hand-rolling these means owning the security correctness of the implementation
in perpetuity. Even a well-tested implementation would need updating whenever
Steam or Discord changes their provider endpoints or security requirements.

The aspnet-contrib project (`github.com/aspnet-contrib`) provides:

- `AspNet.Security.OpenId.Steam` v10.0.0 — Steam OpenID 2.0 provider.
- `AspNet.Security.OAuth.Discord` v10.0.0 — Discord OAuth2 provider.

Both are maintained by Martin Costello and Kévin Chalet, both Microsoft MVPs
who also maintain OpenIddict. Both track each major .NET release. Both integrate
directly into the standard `AuthenticationBuilder` pipeline — which GameKit's
`IOAuthProvider` abstraction wraps, providing strategy-pluggability at the GameKit
level while delegating the cryptographic OAuth dance to battle-tested code.

Alternative considered: OpenIddict's client stack. OpenIddict is the correct
choice when building an OIDC relying party for a corporate identity provider. For
a game service integrating Steam and Discord (not a corporate IdP) the aspnet-
contrib packages are the right tool — they handle exactly the social-login use
case without the full OIDC relying-party complexity.

## Decision

`GameKit.Auth` depends on `AspNet.Security.OpenId.Steam` and
`AspNet.Security.OAuth.Discord` from the aspnet-contrib project.

The `IOAuthProvider` abstraction wraps the `AuthenticationScheme` that each
contrib package registers, so consumers can add new providers (Apple, Epic, Google)
by implementing `IOAuthProvider` in their own assembly — Scrutor will discover and
register them automatically (see ADR-0006).

## Consequences

- **Positive:** Security-critical OAuth/OpenID logic is in actively-maintained,
  well-reviewed libraries rather than hand-rolled code. Update cadence tracks
  .NET major releases.
- **Positive:** The `IOAuthProvider` abstraction means consumers are not locked in
  to the specific contrib providers — they can add any scheme the ASP.NET Core
  `AuthenticationBuilder` supports.
- **Negative:** aspnet-contrib's README nudges new users toward OpenIddict's client
  stack for OIDC relying-party use cases. If GameKit ever needs to support
  corporate IdP authentication (OIDC, SAML), revisit this decision.
- **Dependency note:** `AspNet.Security.OpenId.Steam` depends on
  `AspNet.Security.OpenId` (the base OpenID 2.0 library from the same aspnet-
  contrib project). Both are MIT-licensed and GPL-compatible.
