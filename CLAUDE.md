<!-- GSD:project-start source:PROJECT.md -->
## Project

**GameKit**

GameKit is a self-hostable, GPL-licensed open-source .NET library that gives game developers auth, player management, matchmaking, rankings, and session tracking as composable ASP.NET Core modules. It is **not** a standalone server — it is a set of NuGet packages a game developer integrates into their own ASP.NET Core application to produce a complete, self-hosted backend running on hardware they control.

**Core Value:** A .NET-native, composable, extensible, fully self-hosted game services backend where every algorithm and strategy is an interface the developer can replace — install only what you need, own the rest, depend on no cloud service.

### Constraints

- **License:** GPL — fully open-source. No proprietary deps, no telemetry, no phone-home.
- **Self-hosted only:** Zero cloud-service dependencies. A game developer must be able to stand up a complete, production-capable backend with only this library + Postgres + Redis on hardware they control. No AI integrations, no SaaS APIs, no subscription services anywhere in the runtime path.
- **Runtime:** .NET 10 (LTS, released 2026-04-14) — required across all packages
- **Framework:** ASP.NET Core 10 — required
- **ORM:** Entity Framework Core 10 with Npgsql provider — Postgres only for v1
- **Cache:** Redis via StackExchange.Redis — required for matchmaking + presence
- **Auth tokens:** JWT via `Microsoft.AspNetCore.Authentication.JwtBearer`
- **Password hashing:** BCrypt.Net-Next (Argon2 as a stretch goal)
- **Admin UI:** Blazor Server (per spec; flagged as an open question to revisit)
- **Testing:** xUnit + Testcontainers + Moq — required for integration tests against real Postgres + Redis
- **Distribution:** every `/src` project ships as its own NuGet package
- **Public API discipline:** XML doc comments on every public API — no exceptions
- **Migration boundaries:** packages never modify Core tables in their migrations — only add new tables or FK references
- **Refresh token storage:** never store raw tokens — always SHA-256 hash; raw issued to client once
- **Metadata JSONB columns:** sparse, infrequently-written, non-relational data only — documented constraint
<!-- GSD:project-end -->

<!-- GSD:stack-start source:research/STACK.md -->
## Technology Stack

## Recommended Stack
### Core Technologies (pinned — reference only)
| Technology | Version | Purpose | Why (already decided) |
|------------|---------|---------|-----------------------|
| .NET | **10.0 LTS** (released 2026-04-14; SDK 10.0.106 pinned via `global.json`) | Runtime for all packages | LTS with 3-year support; required across all packages |
| ASP.NET Core | 10.0 | HTTP pipeline, minimal APIs, Blazor | Aligned with .NET 10 runtime |
| Entity Framework Core | **10.0.6** | ORM + migrations | GA on `net10.0`; per-package migrations pattern (PITFALLS.md #3) |
| Npgsql.EntityFrameworkCore.PostgreSQL | **10.0.1** | Postgres provider (jsonb, arrays, range types) | GA on `net10.0`, released 2026-03-12 |
| StackExchange.Redis | **2.8.41** | Redis client for matchmaking + presence | Latest stable on `net10.0` |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0 (shared framework) | JWT validation middleware | Phase 2 scope |
| BCrypt.Net-Next | 4.0.3 | Password hashing (default) | Phase 2 scope |
| xUnit + Testcontainers + Moq | xUnit 2.9.2; Testcontainers 4.11.0; Moq 4.20.72 | Testing | Standard |
### Supporting Libraries (the decisions this doc actually makes)
| Library | Version | Purpose | Package(s) That Depend On It |
|---------|---------|---------|-------------------------------|
| **AspNet.Security.OpenId.Steam** | 10.0.0 | Steam OpenID 2.0 provider (wraps `AspNet.Security.OpenId`) | `GameKit.Auth` |
| **AspNet.Security.OAuth.Discord** | 10.0.0 | Discord OAuth2 provider (part of aspnet-contrib) | `GameKit.Auth` |
| **Glicko-2** | in-house port of `MaartenStaa/glicko2-csharp` (MIT) embedded in `GameKit.Rankings` | Default `IRankingAlgorithm` | `GameKit.Rankings` |
| **FluentValidation** + **FluentValidation.DependencyInjectionExtensions** | **12.1.1** | Request DTO validation | `GameKit.Core`, consumed by all HTTP packages |
| **Scrutor** | **7.0.0** | Assembly-scanning DI registration for pluggable strategies (`IOAuthProvider`, `IMatchmakingStrategy`, `IRankingAlgorithm`) | `GameKit.Core` |
| **Microsoft.Extensions.Http.Resilience** (Polly v8 under the hood) | 9.0.x | Retry / circuit-breaker / timeout for OAuth provider HTTP calls | `GameKit.Auth` |
| **Polly** (direct) | 8.5.x | Non-HTTP resilience (Redis reconnects, background ticker backoff) | `GameKit.Matchmaking`, `GameKit.Presence` |
| **OpenTelemetry** | 1.10.x | Tracing / metrics API | `GameKit.Core` (Abstractions only — opt-in) |
| **OpenTelemetry.Extensions.Hosting** | 1.10.x | Hosting integration | `GameKit.Core` (opt-in extension method) |
| **OpenTelemetry.Instrumentation.AspNetCore** | 1.10.x | HTTP server instrumentation | Sample app only; not a hard dep |
| **OpenTelemetry.Instrumentation.StackExchangeRedis** | 1.x (beta channel still as of 2025) | Redis instrumentation | Sample app only |
| **Testcontainers.PostgreSql** | 4.11.0 | Integration tests | `tests/` |
| **Testcontainers.Redis** | 4.11.0 | Integration tests | `tests/` |
| **MinVer** | **7.0.0** | Git-tag-driven SemVer for all NuGet packages | Repo root (Directory.Build.props) |
| **Microsoft.SourceLink.GitHub** | **10.0.202** | Source Link for NuGet symbol debugging | Repo root |
| **Spectre.Console.Cli** | 0.49.1 | CLI framework for GameKit.Cli (D-08) | `GameKit.Cli` |
| **Microsoft.TemplateEngine.Authoring.Templates** | 10.0.x (SDK-aligned) | Scaffolding for `dotnet new gamekit` | `templates/` |
### Explicitly NOT added as dependencies
| Library | Why Not | What We Do Instead |
|---------|---------|--------------------|
| **MediatR** | Went dual-licensed (RPL-1.5 + commercial) with v13 in July 2025 via Lucky Penny Software. Free only for <$5M-revenue orgs — unacceptable constraint for a library that ships inside customers' apps. | No mediator. Use plain constructor-injected application services (`IAuthService`, `IMatchmakingService`, etc.). This is a library, not an app — we don't need cross-cutting pipelines. |
| **AutoMapper** | Same licensing flip. | Hand-written mapping (small surface; we control the DTOs). |
| **Hangfire** | Requires Postgres storage add-on (`Hangfire.PostgreSql`), writes `hangfire.*` tables into the customer's DB, adds a dashboard we don't control. Over-kill for two recurring jobs (matchmaking tick + queue reconciliation). | `BackgroundService` / `IHostedService` (native) + Polly backoff. See "Background Jobs" decision below. |
| **Quartz.NET** | Enterprise-grade cron/clustering we don't need; complex API; extra tables in customer DB. | Same as above. |
| **IdentityServer4** | Abandoned (archived 2022). | Plain `Microsoft.AspNetCore.Authentication.JwtBearer` + our own token-issuance service (already pinned). |
| **OpenIddict** | Overkill. We're issuing our own short-lived JWTs from our own credentials store, not running a full OAuth2/OIDC authorization server. Revisit only if customers ask for "GameKit-as-IdP." | Custom JWT issuance in `GameKit.Auth`. |
| **ASP.NET Core Identity** | Drags in its own user schema, UI scaffolding, and opinions that fight our `players` / `player_identities` / `player_credentials` split. | Hand-rolled identity using EF Core entities (Core decision). |
| **Nerdbank.GitVersioning** | Uses Git height for patch — produces version gaps and pushes every commit toward a release. Heavier config than we need. | **MinVer** (tag-driven; pure SemVer; zero config). |
| **GitVersion** | Slow, config-heavy, historically fragile on shallow CI clones. | MinVer. |
| **Jab / StrongInject / Pure.DI** | Compile-time DI containers. Incompatible with the customer's `IServiceCollection` — a library can't impose its own container. | Stay on `Microsoft.Extensions.DependencyInjection` + Scrutor. |
| **FluentValidation.AspNetCore** | Deprecated auto-validation package. | `FluentValidation` + `FluentValidation.DependencyInjectionExtensions` + explicit `IValidator<T>` injection in handlers. |
| **Isopoh.Cryptography.Argon2** (for the stretch Argon2 goal) | 100% managed, portable, easy default — **this is actually our pick** (see below), listed here only to contrast with Konscious. | — |
| **Konscious.Security.Cryptography.Argon2** (for stretch Argon2) | Still maintained, but last NuGet release is older; API is `DeriveBytes`-shaped rather than a single hash/verify call. | Prefer Isopoh if we ship Argon2. |
## Per-Package NuGet Dependencies
### `GameKit.Core`
### `GameKit.Auth`
### `GameKit.Matchmaking`
### `GameKit.Rankings`
### `GameKit.Presence`
### `GameKit.Admin.UI`
### `tests/*` (not shipped)
### Repo-root (Directory.Build.props / Packages.props)
## Key Open-Question Decisions Made Here
### 1. Background jobs: native `BackgroundService` — **not** Hangfire or Quartz
- Matchmaking ticker (every ~500ms scan of Redis sorted sets)
- Queue reconciliation (every ~30s — reconciles Redis live queue against Postgres ticket durability)
- **We're a library, not an app.** Hangfire needs storage (adds tables to the customer's DB), a dashboard, and a worker setup. Forcing that on customers violates "install only what you need."
- **Our jobs are trivially periodic.** We don't need fan-out, cron, clustering, or durable job queues. Redis + Postgres already give us state.
- **Native = zero dependency.** Matches the decision tree from the 2025/2026 .NET background-jobs literature: "for simple tasks, BackgroundService + Polly + OpenTelemetry is more than enough."
- **Clustering concern:** If a customer runs multiple instances of their app, we need leader election for the ticker. Solve with a Redis distributed lock (`SET NX PX`) — not by adopting Hangfire's scheduler-per-instance model or Quartz's clustering.
### 2. Glicko-2: in-house port — **not** a NuGet dependency
- The three C# Glicko-2 libraries on NuGet (`Glicko-2RankingSystem`, `Glicko2`, `MaartenStaa/glicko2-csharp`) are all thin, stagnant ports of Glickman's 2012 paper. None is actively maintained as a library — they're reference implementations.
- The algorithm fits in ~150 lines. Vendoring it:
- MIT license permits vendoring with attribution. Credit `MaartenStaa` in the source file header.
- Unit tests: ship Glickman's original worked example (from glicko.net PDF) as a regression fixture.
### 3. OAuth providers: aspnet-contrib packages — **not** hand-rolled
- Both are maintained by Martin Costello + Kévin Chalet (Microsoft MVPs; also work on OpenIddict). They track each major .NET release — v10 aligns with .NET 10 but is binary-compat with .NET 9.
- They plug into the standard `AuthenticationBuilder` pipeline — our `IOAuthProvider` abstraction wraps the `AuthenticationScheme` they register, giving us strategy-pluggability at the GameKit level while delegating the crypto/OAuth dance to battle-tested code.
- Note: aspnet-contrib's own README nudges new users toward OpenIddict's client stack. For our needs (we're not an OIDC relying party for a corporate IdP, we're a game integrating Steam and Discord) the contrib packages remain the right tool.
### 4. Versioning: MinVer — **not** Nerdbank.GitVersioning
- Tag-driven = one source of truth (the Git tag), not a JSON file in the repo plus tags plus CI.
- MinVer produces no version gaps (unlike Nerdbank's Git-height patch math).
- RC → RTM workflow is trivial: tag `v1.0.0-rc.1`, build, publish; later tag the same commit `v1.0.0`, build, publish.
- Zero config. Drop it in `Directory.Build.props` with `<MinVerTagPrefix>v</MinVerTagPrefix>` and done.
- All GameKit packages share a version (coupled release train) — simplifies the composable-package story for consumers. A user who pins `GameKit.Core@1.2.0` can pin every sibling to `1.2.0`.
### 5. DI registration: `Microsoft.Extensions.DependencyInjection` + Scrutor — **not** source-generator DI
- Source-generator DI containers (Jab, StrongInject, Pure.DI) are compile-time containers. **They only work when the container owns the object graph** — they cannot integrate with a customer's `IServiceCollection`. A library cannot dictate the customer's DI choice.
- Scrutor adds a `Scan(scan => scan.FromAssemblies(...))` API to MS.DI — ideal for our pluggable-strategy story where customers may drop an `IOAuthProvider` implementation into their assembly and expect GameKit to find it.
- Our fluent builder (`AddGameKit().AddAuth()...`) stays in plain MS.DI; Scrutor is an internal implementation detail of the `Add*` extension methods.
### 6. Validation: FluentValidation 12 (split packages) — **not** DataAnnotations, not the deprecated AspNetCore auto-bind package
- FluentValidation 12 targets .NET 8+ (compatible with .NET 10).
- The auto-MVC-validation path was removed because it conflated concerns. Explicit `IValidator<T>` injection in endpoint handlers is the current best practice.
- We're using minimal APIs, not MVC controllers — auto-binding isn't even available to us.
### 7. Resilience: `Microsoft.Extensions.Http.Resilience` for HTTP, raw Polly 8 for non-HTTP
- `GameKit.Auth` uses `Microsoft.Extensions.Http.Resilience` on the `HttpClient` instances that call Steam/Discord endpoints.
- `GameKit.Matchmaking` + `GameKit.Presence` use `Polly` 8 pipelines for Redis reconnect/backoff.
- `Microsoft.Extensions.Http.Resilience` is Microsoft's recommended layer on top of Polly v8 as of .NET 8+ — zero-allocation, composable, and the idiomatic choice for HTTP.
- For Redis/non-HTTP we want Polly directly because there's no `HttpClient` to hang a handler off.
### 8. Observability: OpenTelemetry, but opt-in — **not** a hard dependency
- Forcing OTel on every consumer violates "install only what you need."
- `ActivitySource` / `Meter` are the OTel-friendly primitives — any OTel SDK in the host will auto-pick them up if the customer registers `AddSource("GameKit.*")`.
- Pattern matches what MS itself does in ASP.NET Core, EF Core, and StackExchange.Redis.
### 9. Password hashing: BCrypt.Net-Next default; Argon2 as sibling package
- `GameKit.Auth` provides `BCryptPasswordHasher : IPasswordHasher` using `BCrypt.Net-Next` 4.0.3 (default).
- `GameKit.Auth.Argon2` (sibling package) provides `Argon2idPasswordHasher` using **Isopoh.Cryptography.Argon2** (fully-managed, portable to Linux/macOS/Windows/WASM). Customer opts in.
- Isopoh is 100% managed C# (no native bindings); Konscious has a native path concern on some platforms.
- Isopoh provides `Hash()` / `Verify()` directly; Konscious requires driving the `DeriveBytes` pattern manually.
- Isopoh includes `SecureArray` (zeroed-on-dispose sensitive memory) — useful for password-handling hygiene.
### 10. `dotnet new gamekit` template
## Installation (reference)
# Consumers:
# Optional:
# Start from template:
## Version Compatibility Notes
| Combination | Status | Notes |
|-------------|--------|-------|
| .NET 10 LTS + EF Core 10.0.6 + Npgsql 10.0.1 | ✅ | Current stable as of 2026-04-15. |
| .NET 10 + AspNet.Security.OpenId.Steam 10.0.0 | ✅ | v10 aligns with .NET 10; verify `net10.0` TFM in package assets before committing. |
| .NET 10 + FluentValidation 12.1.1 | ✅ | FV12 requires .NET 8+. |
| StackExchange.Redis 2.8.41 + Testcontainers.Redis 4.11 | ✅ | Client connects to whatever Redis image Testcontainers spins up (default Redis 8). |
| Testcontainers 4.11 on .NET 10 | ✅ | Package targets `net8.0` + `netstandard2.0` — works fine on net10.0. |
| MinVer 7 + Source Link 10.0.202 | ✅ | Both slot into `Directory.Build.props` without conflict. |
| MediatR 12 (last OSS) on .NET 10 | ⚠️ | Works, but we explicitly decline — avoid future upgrade trap when v13+ is RPL-licensed. |

### Version Provenance (2026-04-15)

All versions above verified GA on NuGet on 2026-04-15 during Phase 1 research (see `.planning/phases/01-foundation-core-migrations-ops-defaults-gpl/01-RESEARCH.md` § Standard Stack). The D-15 preview-pin fallback is NOT needed — every dependency has a stable `net10.0` TFM.

This table supersedes any earlier .NET 9 / MinVer 6 / SourceLink 8 references. The earlier pre-LTS research material has been retained in project history but should not be treated as authoritative.
## Alternatives Considered (quick reference)
| Our Pick | Considered | Why Not Picked |
|----------|------------|----------------|
| BackgroundService + Polly | Hangfire | Adds customer-DB tables + dashboard; over-kill |
| BackgroundService + Polly | Quartz.NET | Enterprise scheduling we don't need |
| BackgroundService + Polly | TickerQ, NCronJob | Promising but young; stick with BCL |
| MinVer | Nerdbank.GitVersioning | Git-height patch math creates version gaps |
| MinVer | GitVersion | Heavy config, shallow-clone issues in CI |
| Scrutor | Pure.DI / Jab / StrongInject | Libraries can't mandate a customer's DI container |
| In-house Glicko-2 | Glicko-2RankingSystem NuGet | Unmaintained; 150 LOC doesn't warrant a dep |
| Isopoh Argon2 | Konscious Argon2 | API ergonomics + fully-managed portability |
| aspnet-contrib OAuth | OpenIddict client | Overkill for Steam + Discord integration |
| None (plain services) | MediatR | Licensing (RPL/commercial after v13) |
| None (hand-written mapping) | AutoMapper | Same licensing concern; small mapping surface |
## Sources
- [NuGet: AspNet.Security.OpenId.Steam 10.0.0](https://www.nuget.org/packages/AspNet.Security.OpenId.Steam) — HIGH
- [NuGet: AspNet.Security.OAuth.Discord 10.0.0](https://www.nuget.org/packages/AspNet.Security.OAuth.Discord/) — HIGH
- [GitHub: aspnet-contrib/AspNet.Security.OpenId.Providers](https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers) — HIGH
- [Jimmy Bogard: AutoMapper and MediatR Licensing Update](https://www.jimmybogard.com/automapper-and-mediatr-licensing-update/) — HIGH (primary source on the licensing change)
- [Milan Jovanović: MediatR and MassTransit Going Commercial](https://www.milanjovanovic.tech/blog/mediatr-and-masstransit-going-commercial-what-this-means-for-you) — MEDIUM (analysis)
- [NuGet: MinVer 6.0.0](https://www.nuget.org/packages/minver) — HIGH
- [GitHub: adamralph/minver](https://github.com/adamralph/minver) — HIGH
- [Rehan Saeed: The Easiest Way to Version NuGet Packages](https://rehansaeed.com/the-easiest-way-to-version-nuget-packages/) — MEDIUM (comparison)
- [GitHub: MaartenStaa/glicko2-csharp](https://github.com/MaartenStaa/glicko2-csharp) — HIGH (chosen reference impl)
- [NuGet: Glicko-2RankingSystem](https://www.nuget.org/packages/Glicko-2RankingSystem) — MEDIUM (inspected; rejected as dep)
- [Glickman: Example of the Glicko-2 System (PDF)](https://www.glicko.net/glicko/glicko2.pdf) — HIGH (algorithm spec for regression tests)
- [FluentValidation: ASP.NET Core integration docs](https://docs.fluentvalidation.net/en/latest/aspnet.html) — HIGH
- [FluentValidation 9.0 upgrade notes](https://docs.fluentvalidation.net/en/latest/upgrading-to-9.html) — MEDIUM
- [GitHub: FluentValidation/FluentValidation.AspNetCore (deprecated)](https://github.com/FluentValidation/FluentValidation.AspNetCore) — HIGH (deprecation confirmed)
- [MS Learn: .NET Observability with OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel) — HIGH
- [GitHub: OpenTelemetry.Instrumentation.AspNetCore README](https://github.com/open-telemetry/opentelemetry-dotnet-contrib/blob/main/src/OpenTelemetry.Instrumentation.AspNetCore/README.md) — HIGH
- [MS Learn: Build resilient HTTP apps](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience) — HIGH
- [Pollydocs](https://www.pollydocs.org/) — HIGH
- [Milan Jovanović: Building Resilient Cloud Applications With .NET](https://www.milanjovanovic.tech/blog/building-resilient-cloud-applications-with-dotnet) — MEDIUM
- [NuGet: Testcontainers.PostgreSql 4.11.0](https://www.nuget.org/packages/Testcontainers.PostgreSql) — HIGH
- [NuGet: Testcontainers.Redis 4.11.0](https://www.nuget.org/packages/Testcontainers.Redis) — HIGH
- [GitHub: khellang/Scrutor](https://github.com/khellang/Scrutor) — HIGH
- [GitHub: mheyman/Isopoh.Cryptography.Argon2](https://github.com/mheyman/Isopoh.Cryptography.Argon2) — HIGH
- [GitHub: kmaragon/Konscious.Security.Cryptography](https://github.com/kmaragon/Konscious.Security.Cryptography) — MEDIUM
- [GitHub: samuel-lucas6/benchmark-argon2-dotnet](https://github.com/samuel-lucas6/benchmark-argon2-dotnet) — HIGH (for tuning params)
- [MS Learn: Create a template package for dotnet new](https://learn.microsoft.com/en-us/dotnet/core/tutorials/cli-templates-create-template-package) — HIGH
- [boldsign: ASP.NET Core Background Jobs architecture](https://boldsign.com/blogs/aspnet-core-background-jobs-hosted-services-hangfire-quartz/) — MEDIUM
- [amarozka.dev: Background Jobs in .NET 2026](https://amarozka.dev/background-jobs-schedulers-dotnet-hangfire-quartz-temporal/) — MEDIUM
- [GitHub: pakrym/jab](https://github.com/pakrym/jab) — MEDIUM (for alternative comparison)
- [GitHub: YairHalberstadt/stronginject](https://github.com/YairHalberstadt/stronginject) — MEDIUM
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

Conventions not yet established. Will populate as patterns emerge during development.
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

Architecture not yet mapped. Follow existing patterns found in the codebase.
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills to any of: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, or `.github/skills/` with a `SKILL.md` index file.
<!-- GSD:skills-end -->

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before using Edit, Write, or other file-changing tools, start work through a GSD command so planning artifacts and execution context stay in sync.

Use these entry points:
- `/gsd-quick` for small fixes, doc updates, and ad-hoc tasks
- `/gsd-debug` for investigation and bug fixing
- `/gsd-execute-phase` for planned phase work

Do not make direct repo edits outside a GSD workflow unless the user explicitly asks to bypass it.
<!-- GSD:workflow-end -->



<!-- GSD:profile-start -->
## Developer Profile

> Profile not yet configured. Run `/gsd-profile-user` to generate your developer profile.
> This section is managed by `generate-claude-profile` -- do not edit manually.
<!-- GSD:profile-end -->
