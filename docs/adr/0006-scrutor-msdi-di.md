<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0006: Scrutor + Microsoft.Extensions.DependencyInjection, not source-gen DI

**Status:** Accepted

## Context

GameKit's pluggable-strategy design requires that consumers can drop an
`IOAuthProvider`, `IMatchmakingStrategy`, or `IRankingAlgorithm` implementation
into their own assembly and have GameKit discover and register it automatically.
This requires assembly-scanning DI registration.

Three approaches were evaluated:

**Source-generator DI containers (Jab, StrongInject, Pure.DI):** Compile-time DI
containers that produce zero-overhead object-graph wiring. Excellent for
applications where the developer owns the entire composition root. However, they
are incompatible with the library use case: a library cannot mandate the
consumer's DI container. Consumers use `Microsoft.Extensions.DependencyInjection`
(MS.DI) — integrating a Jab or StrongInject container alongside MS.DI for only
the GameKit portion of the DI graph is not feasible.

**Microsoft.Extensions.DependencyInjection alone:** MS.DI has no built-in
assembly-scanning API. We could add our own scanning, but that duplicates the
functionality of Scrutor.

**Scrutor (khellang/Scrutor):** Adds a `Scan(scan => ...)` extension method to
the standard `IServiceCollection`. It is not a separate DI container — it is an
extension to MS.DI. The consumer's `IServiceCollection` is the single composition
root. Scrutor is an internal implementation detail of GameKit's `Add*` extension
methods; consumers never interact with it directly.

## Decision

GameKit's `Add*` extension methods use `Microsoft.Extensions.DependencyInjection`
as the composition root and Scrutor 7.0.0 for assembly-scanning when registering
pluggable strategies. Scrutor is referenced only by `GameKit.Core` (the base
package); all other packages receive it transitively through the builder interface.

Source-generator DI containers are not used.

## Consequences

- **Positive:** Complete compatibility with any consumer DI setup. Consumers can
  mix GameKit registrations with their own services, overrides, and decorators
  freely on the same `IServiceCollection`.
- **Positive:** Consumers can replace a GameKit strategy by registering their own
  implementation first — Scrutor's scanning respects existing registrations.
- **Negative:** Scrutor uses runtime reflection for scanning — no compile-time
  validation of discovered types. However, GameKit's `Add*` methods call `Scan`
  during `IHostedService` startup, so misconfigured strategy implementations
  produce startup errors rather than silent runtime failures.
- **Dependency:** Scrutor is MIT-licensed.
