<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0007: FluentValidation with explicit injection, not DataAnnotations or auto-MVC binding

**Status:** Accepted

## Context

GameKit's HTTP endpoints (auth, matchmaking, rankings, lobby) receive request
DTOs that require validation. Three approaches were evaluated:

**DataAnnotations (`[Required]`, `[StringLength]`):** Attribute-based,
reflection-driven. Works with MVC model binding but produces poor error messages
for complex conditional validation (e.g., "provide either email or username").
Not composable — validation logic is co-located with the DTO, making unit testing
awkward.

**FluentValidation.AspNetCore (deprecated auto-validation):** The
`FluentValidation.AspNetCore` package historically provided auto-validation via
`AddFluentValidation()` on the MVC builder, which ran validators during model
binding. This package is deprecated as of FluentValidation 10 — the project team
explicitly states that auto-validation "conflates concerns". Furthermore, GameKit
uses **minimal APIs**, not MVC controllers, so auto-binding is not available.

**FluentValidation 12 with explicit `IValidator<T>` injection:** The current
recommended approach from the FluentValidation documentation. Validators are
registered via DI (`AddValidatorsFromAssembly`) and injected into endpoint
handlers. Validation is explicit — the handler calls `await validator.ValidateAsync(dto)`
and maps the result to `ValidationProblemDetails` on failure. This pattern works
correctly with minimal APIs.

FluentValidation 12.1.1 targets .NET 8+ and is fully compatible with .NET 10.
The dependency-injection extensions are in the separate package
`FluentValidation.DependencyInjectionExtensions` (Apache-2.0; GPL-compatible).

## Decision

GameKit uses `FluentValidation` 12.1.1 + `FluentValidation.DependencyInjectionExtensions`
12.1.1 with explicit `IValidator<T>` injection in endpoint handlers.
`FluentValidation.AspNetCore` is not referenced.

Validators are registered in each package's `Add*` extension method. Consumers
can extend validation by implementing additional validators in their own assembly
— Scrutor scanning picks them up (see ADR-0006).

## Consequences

- **Positive:** Explicit, testable validation. Each endpoint handler's validation
  logic is visible in the handler itself, not implied by attributes or middleware.
- **Positive:** Conditional, cross-field, and async validation rules are natural
  in FluentValidation's rule builder API (e.g., "if ExternalToken is set, then
  Provider must also be set").
- **Positive:** Validators are injectable and mockable in unit tests.
- **Negative:** Slightly more verbose than attribute-based validation — every
  handler that validates a DTO must inject `IValidator<T>` and call it. For
  GameKit's surface area this is acceptable.
- **Stability note:** `FluentValidation.DependencyInjectionExtensions` exists as
  a separate NuGet package because the original `FluentValidation.AspNetCore`
  was the prior home for DI registration. The new split package is the canonical
  forward path per the FluentValidation project docs.
