<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0001: No MediatR or AutoMapper

**Status:** Accepted

## Context

Both MediatR (by Jimmy Bogard) and AutoMapper underwent a licensing change in
2025/2026. Starting with MediatR v13 and AutoMapper v13, these packages are
licensed under the Reciprocal Public License (RPL-1.5) plus a commercial license
for organisations above a revenue threshold. RPL-1.5 is a copyleft licence that
requires derivative works to be open-source under the same terms — incompatible
with a library that ships inside customers' closed-source applications.

GameKit is a library distributed as NuGet packages. A dependency on a
commercially-restricted package would impose that restriction on every consumer
of GameKit, regardless of their own revenue or licensing intent. The last
fully-open MediatR version (v12) was on .NET 8; v13 is required for .NET 10
SDK compatibility without workarounds.

In addition, for a library that exposes plain application-service interfaces
(`IAuthService`, `IMatchmakingService`, etc.), MediatR's cross-cutting pipeline
offers no architectural benefit. GameKit is not an application — it is a set of
composable modules. Consumers wire GameKit's services as singletons via
constructor injection; there is no need for a mediator pattern or a request/
response pipeline.

AutoMapper similarly adds overhead (runtime reflection, convention-over-
configuration mapping) for a surface area that consists of a small number of
well-understood DTOs that map one-to-one with internal entities. Hand-written
mapping is simpler, faster, and has no licensing risk.

## Decision

GameKit does **not** depend on MediatR or AutoMapper at any version. All
application services are plain injected services (constructor-injected
interfaces). All DTO → entity mapping is hand-written in the relevant package's
service layer.

## Consequences

- **Positive:** No licensing constraint on consumers regardless of their revenue
  or closed-source status. Zero runtime reflection overhead from mapping
  conventions. No mediator pipeline to debug when a handler is not invoked.
- **Negative:** Service classes are slightly more verbose (explicit interface
  injection rather than `IRequest<T>` handler registration). Mapping code is
  explicit rather than convention-inferred. Neither drawback affects consumers
  of the library — only maintainers of GameKit itself.
- **Future:** If the licensing situation changes (open re-licensing or a
  maintained open fork reaches production quality), reconsider. Otherwise this
  decision is permanent for the v1/v2 roadmap.
