<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Architecture Decision Records

This directory contains the Architecture Decision Records (ADRs) for GameKit.
ADRs use the [Michael Nygard format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
Title, Status, Context, Decision, Consequences.

All ADRs listed here have status **Accepted** unless otherwise noted.

---

## Index

| # | ADR | Summary |
|---|-----|---------|
| 0001 | [No MediatR or AutoMapper](./0001-no-mediatr-automapper.md) | Both packages moved to RPL/commercial licensing after v13; plain injected services are used instead |
| 0002 | [BackgroundService not Hangfire/Quartz](./0002-backgroundservice-not-hangfire.md) | Libraries cannot add customer-DB tables; BCL `BackgroundService` + Redis leader election + Polly covers all needs |
| 0003 | [Glicko-2 vendored, not NuGet](./0003-glicko2-vendored.md) | All NuGet Glicko-2 packages are unmaintained; the algorithm is ~150 lines vendored under BSD-3-Clause attribution |
| 0004 | [aspnet-contrib OAuth providers](./0004-aspnet-contrib-oauth.md) | Battle-tested Steam OpenID 2.0 + Discord OAuth2 from aspnet-contrib; `IOAuthProvider` abstraction wraps them |
| 0005 | [MinVer for versioning](./0005-minver-versioning.md) | Tag-driven SemVer; no version gaps; zero config; RC→RTM workflow is trivial |
| 0006 | [Scrutor + MS.DI, not source-gen DI](./0006-scrutor-msdi-di.md) | Libraries cannot mandate the consumer's DI container; Scrutor extends MS.DI with assembly scanning |
| 0007 | [FluentValidation with explicit injection](./0007-fluentvalidation-explicit.md) | Auto-MVC validation is deprecated; minimal APIs require explicit `IValidator<T>` injection in handlers |
| 0008 | [BCrypt default + Argon2 opt-in](./0008-bcrypt-default-argon2-optin.md) | BCrypt is the portable default; `GameKit.Auth.Argon2` provides Argon2id via Isopoh for consumers who want memory-hard hashing |
| 0009 | [OpenTelemetry opt-in](./0009-otel-opt-in.md) | Air-gap guarantee: no telemetry leaves the deployment unless the operator configures an OTLP endpoint; phone-home threat model documented |
| 0010 | [No ASP.NET Core Identity](./0010-no-aspnet-identity.md) | Identity's schema and `IdentityUser` conflict with the `players`/`player_identities`/`player_credentials` split required for multi-identity game accounts |

---

## Adding a new ADR

1. Choose the next sequence number (e.g., `0011`).
2. Create `docs/adr/0011-short-title.md` using the template below.
3. Add a row to the index table above.
4. Commit with the message format: `docs: add ADR-0011 <short title>`.

### ADR template

```markdown
# ADR-NNNN: Title

**Status:** Proposed | Accepted | Deprecated | Superseded by ADR-XXXX

## Context

[Describe the situation and forces at play.]

## Decision

[State the decision clearly and concisely.]

## Consequences

[What becomes easier, harder, or is now required as a result.]
```
