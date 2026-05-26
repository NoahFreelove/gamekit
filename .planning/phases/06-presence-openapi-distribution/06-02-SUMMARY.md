---
phase: 06-presence-openapi-distribution
plan: 02
subsystem: core
tags:
  - core
  - ports
  - hosted-service
  - version-assertion
dependency-graph:
  requires: []
  provides:
    - "ISessionLifecycleObserver (port for Plan 06-04 PresenceSessionObserver)"
    - "ISessionStartService + ISessionAbandonService (service contracts for Plan 06-05 /start and /abandon endpoints)"
    - "GameKitVersionMismatchException + GameKitVersionAssertionHostedService (OPS-05 runtime detector for Plan 06-01 GameKit.Build source-gen marker)"
  affects:
    - "src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs (AddGameKit now inserts the version-assertion hosted service at index 0)"
tech-stack:
  added: []
  patterns:
    - "Sibling Core port pattern: ISessionLifecycleObserver mirrors IPostSessionCompleteHandler — optional, runs inside ambient transaction, MUST be idempotent (D-21)"
    - "Discriminated-union service-result pattern: SessionStartResult / SessionAbandonResult mirror SessionCompleteResult abstract record + sealed nested records (D-20)"
    - "IHostedService at services.Insert(0, ...) — PATTERNS warning #2 — guarantees version assertion fires BEFORE every sibling-package migration hosted service"
    - "Eager-load referenced GameKit.* assemblies before AppDomain scan (D-24 / PATTERNS warning #7) — prevents lazy-load packages being silently missed"
key-files:
  created:
    - "src/GameKit.Core/Services/ISessionLifecycleObserver.cs"
    - "src/GameKit.Core/Services/ISessionStartService.cs"
    - "src/GameKit.Core/Services/ISessionAbandonService.cs"
    - "src/GameKit.Core/Services/GameKitVersionMismatchException.cs"
    - "src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs"
  modified:
    - "src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs"
decisions:
  - "Reflection lookup tolerates BOTH Public and NonPublic static fields on GameKitMarker (BindingFlags.Public | NonPublic | Static). The Plan 06-01 source generator emits `internal const string GameKitVersion` per RESEARCH Pattern 5 — `internal` requires `NonPublic`. A Public binding flag alone would silently skip every marker and the assertion would never fire."
  - "Eager-load wrapped in a per-assembly try/catch that LogWarnings on FileNotFoundException/BadImageFormatException/FileLoadException rather than throwing. A missing transitive reference should not crash a host that otherwise has consistent versions among the assemblies that did load."
  - "When GetEntryAssembly() returns null (test hosts that build a DI container without IHost), the eager-load step is skipped with a LogDebug message. The AppDomain scan still runs and operates on whatever was already loaded."
  - "SessionStartRequest / SessionAbandonRequest ship as empty records — Plan 06-05 may extend them during implementation. Defining the contract now lets Plan 06-05 wire endpoints without bouncing back to Core for type updates."
metrics:
  duration: "~10 minutes (small linear plan, no test infra to spin)"
  completed: "2026-05-26"
  tasks_completed: 2
  files_created: 5
  files_modified: 1
---

# Phase 6 Plan 02: Core Ports + Version-Assertion Hosted Service Summary

**One-liner:** Shipped 3 new Core ports/service-interfaces (`ISessionLifecycleObserver`, `ISessionStartService`, `ISessionAbandonService`) plus the `GameKitVersionAssertionHostedService` IHostedService (inserted at index 0 of `AddGameKit()`) that fails fast at IHost.StartAsync on cross-package GameKit version drift via reflection on the Plan-06-01 source-gen-emitted `Internal.GameKitMarker.GameKitVersion` constant.

## What Shipped

### Task 1 — Core ports + service-interface contracts (commit `0c201d4`)

Three new files in `src/GameKit.Core/Services/`:

#### `ISessionLifecycleObserver.cs`
The cross-package observer port that Plan 06-04 `PresenceSessionObserver` will implement and Plan 06-05's three lifecycle endpoints (`/start`, `/complete`, `/abandon`) will resolve via `IEnumerable<ISessionLifecycleObserver>` and invoke inside their transactions.

```csharp
public interface ISessionLifecycleObserver
{
    Task OnSessionStartedAsync   (Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
    Task OnSessionCompletedAsync (Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
    Task OnSessionAbandonedAsync (Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
}
```

Sibling to (not replacement for) `IPostSessionCompleteHandler` — kept per D-21 for Phase-4 Rankings backwards-compat. Plan 06-04 PresenceSessionObserver implements these three methods verbatim.

#### `ISessionStartService.cs`
```csharp
public interface ISessionStartService
{
    Task<SessionStartResult> StartAsync(Guid sessionId, SessionStartRequest req, CancellationToken ct);
}

public sealed record SessionStartRequest();

public abstract record SessionStartResult
{
    public sealed record Started(GameSessionState NewState) : SessionStartResult;
    public sealed record SessionNotFound                    : SessionStartResult;
    public sealed record InvalidState(GameSessionState CurrentState) : SessionStartResult;
}
```

#### `ISessionAbandonService.cs`
```csharp
public interface ISessionAbandonService
{
    Task<SessionAbandonResult> AbandonAsync(Guid sessionId, SessionAbandonRequest req, CancellationToken ct);
}

public sealed record SessionAbandonRequest();

public abstract record SessionAbandonResult
{
    public sealed record Abandoned(GameSessionState NewState)        : SessionAbandonResult;
    public sealed record SessionNotFound                             : SessionAbandonResult;
    public sealed record InvalidState(GameSessionState CurrentState) : SessionAbandonResult;
}
```

Both mirror `ISessionCompleteService` shape (single method returning a discriminated-union result; nested sealed records per case) — Plan 06-05 writes the implementations and endpoint handlers verbatim against these signatures.

### Task 2 — Version-mismatch detector (commit `00279e1`)

#### `src/GameKit.Core/Services/GameKitVersionMismatchException.cs`
Public sealed `Exception` subclass carrying `IReadOnlyDictionary<string, string> VersionsByAssembly`. `Message` is built from the map sorted by assembly name (Ordinal) for stable test-friendly output. Constructor argument-null-throws if the map is null.

#### `src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs`
`internal sealed` IHostedService living in the new `GameKit.Core.Hosting` namespace (placement mirrors `AuthMigrationHostedService` per PATTERNS Block 6).

Primary-constructor injection: `(ILogger<GameKitVersionAssertionHostedService> logger)`.

`StartAsync` body (synchronous; returns `Task.CompletedTask`):
1. **Eager-load step** (D-24 / PATTERNS warning #7): `Assembly.GetEntryAssembly()?.GetReferencedAssemblies().Where(n => n.Name?.StartsWith("GameKit.", Ordinal) == true)` → `Assembly.Load(name)` for each. Wraps in try/catch on `FileNotFoundException`/`BadImageFormatException`/`FileLoadException` that LogWarnings rather than crashing — a missing transitive reference should not crash a host whose loaded-assembly set is otherwise consistent. When `GetEntryAssembly()` returns null (some test hosts), the step is skipped with a LogDebug message.
2. **Collect step**: iterate `AppDomain.CurrentDomain.GetAssemblies()`, filter to `GameKit.*`-prefix names, skip `GameKit.Build` (the analyzer). For each: `asm.GetType($"{asmName}.Internal.GameKitMarker", throwOnError: false)` → if found, `GetField("GameKitVersion", Public | NonPublic | Static)?.GetValue(null) as string` → store in `Dictionary<string, string>(Ordinal)`. (NonPublic flag matters — Plan 06-01 source-gen emits `internal const`.)
3. **Assert step**: if the map is empty → LogDebug + return (intentional during rollout before Plan 06-01 wires the generator). If `versionsByAsm.Values.Distinct(Ordinal).Count() > 1` → throw `GameKitVersionMismatchException(versionsByAsm)`. Else LogInformation with the single shared version.

`StopAsync` returns `Task.CompletedTask`.

#### `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` (modified)
Added at the top of `AddGameKit()` body, right after the argument-null guards and BEFORE `var opts = new GameKitOptions()`:

```csharp
services.Insert(0, ServiceDescriptor.Singleton<IHostedService, GameKitVersionAssertionHostedService>());
```

The block carries a 7-line comment citing D-16 / OPS-05 / PATTERNS warning #2 with the rationale ("must run BEFORE every migration hosted service from sibling packages"). New using clauses: `GameKit.Core.Hosting` (for the type) and `Microsoft.Extensions.Hosting` (for `IHostedService`).

## Verification

- **Build:** `dotnet build GameKit.sln` — 0 warnings, 0 errors (whole solution: 7 GameKit.* projects + tests + sample).
- **Unit tests:** `dotnet test tests/GameKit.Core.Tests/` — **131/131 passed**.
- **Integration tests:** `dotnet test tests/GameKit.Core.Integration.Tests/` — **9/9 passed**. Confirms the version-assertion hosted service does NOT break Core's integration suite when invoked inside a real IHost (with no `GameKitMarker` constants present yet, it logs and returns cleanly per the rollout-tolerance design).

### Must-have truths verified

| Truth | Status |
|-------|--------|
| ISessionLifecycleObserver in src/GameKit.Core/Services/ with the three required method signatures | ✓ |
| GameKitVersionMismatchException is public, surfaces VersionsByAssembly | ✓ |
| ISessionStartService + ISessionAbandonService mirror ISessionCompleteService shape | ✓ |
| GameKitVersionAssertionHostedService is internal sealed, implements IHostedService, eager-loads referenced GameKit.* assemblies, iterates AppDomain filtered to GameKit.* (skip GameKit.Build), reflects on Internal.GameKitMarker.GameKitVersion, throws on distinct>1 | ✓ |
| AddGameKit() registers via services.Insert(0, …) — not AddHostedService<T>() | ✓ — verified by `grep -n 'services.Insert(0' src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` returning a single hit |
| All new public types carry XML doc comments | ✓ — CS1591-as-error build passes |

### Key-link patterns verified

| Pattern (from plan frontmatter) | Verification |
|---|---|
| `services\.Insert\(0,.*GameKitVersionAssertionHostedService` | `services.Insert(0, ServiceDescriptor.Singleton<IHostedService, GameKitVersionAssertionHostedService>());` present in GameKitServiceCollectionExtensions.cs |
| `Internal\.GameKitMarker` | Composed via `const MarkerTypeSuffix = ".Internal.GameKitMarker"` + `asmName + MarkerTypeSuffix` in the hosted service |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 — Bug] Unresolved cref `<see cref="Message"/>` in GameKitVersionMismatchException.cs XML doc**
- **Found during:** Task 2 verify build (CS1574 — XML comment has cref attribute 'Message' that could not be resolved)
- **Issue:** `GameKitVersionMismatchException` does not override `Exception.Message`; it constructs the base message via `: base(BuildMessage(versionsByAssembly))`. Without an override the bare `Message` identifier cannot be cref-resolved at the doc-emit stage.
- **Fix:** Qualified the cref as `<see cref="Exception.Message"/>` (one-line edit).
- **Files modified:** `src/GameKit.Core/Services/GameKitVersionMismatchException.cs`
- **Commit:** included in commit `00279e1` (single Task 2 commit after fix)

### Discretionary additions (not strictly required by plan but justified by Phase 6 PATTERNS warnings)

**1. NonPublic field-binding flag on GameKitMarker reflection** — The plan specifies "reflect `GetField("GameKitVersion", BindingFlags.Public | BindingFlags.Static)`" but RESEARCH Pattern 5 line 644 + the marker pattern emitted by Plan 06-01 use `internal const string GameKitVersion`. `internal const` reflection requires `BindingFlags.NonPublic`. Implementation uses `Public | NonPublic | Static` so the assertion works regardless of whether Plan 06-01 emits the const as `internal` or `public` (the const visibility is an implementation detail of the generator; this binding-flag set tolerates both). Without this, every marker lookup would silently return null and the OPS-05 assertion would never fire — a Rule 2 critical-functionality concern that would not have been caught until much later integration testing.

**2. LogDebug "no markers found" branch** — If the version-assertion service runs in a host that has no `GameKitMarker` constants present (Plan 06-01 source-gen not yet wired, or test hosts that don't reference any GameKit packages), the assertion logs a debug message and returns cleanly rather than treating an empty map as success or failure. This is the rollout-tolerance behavior — Plan 06-02 lands the detector before Plan 06-01 lands the markers it detects, so during the rollout window the detector must NO-OP safely. Once Plan 06-01 is merged, the markers will appear and the assertion will activate naturally.

### Out-of-scope discoveries

None. All work fell squarely inside the Core package boundary.

### Architectural decisions deferred

None. No Rule-4 architectural decisions were needed; both tasks were straightforward additions to the established Core port + hosted-service patterns.

## Threat Flags

No new security-relevant surface introduced by this plan. The only new external-facing behavior is `GameKitVersionAssertionHostedService` throwing at `IHost.StartAsync` — the exception bubbles to the consumer's logger, not to an HTTP response. Per the plan's threat model (T-06-02-02), the per-assembly version map is NOT secret (versions are stamped publicly into every NuGet package's `info.version` and exposed via D-22 OpenAPI `info.version`).

## Output Contract (for downstream plans)

### For Plan 06-04 (Presence implementation)

The `PresenceSessionObserver` MUST implement these three methods verbatim:

```csharp
public sealed class PresenceSessionObserver : ISessionLifecycleObserver
{
    public Task OnSessionStartedAsync   (Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
    public Task OnSessionCompletedAsync (Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
    public Task OnSessionAbandonedAsync (Guid sessionId, IReadOnlyList<Guid> participants, CancellationToken ct);
}
```

### For Plan 06-05 (Sessions /start + /abandon endpoints)

The two new application-service implementations MUST match these signatures:

```csharp
public sealed class SessionStartService   : ISessionStartService
{
    public Task<SessionStartResult>   StartAsync   (Guid sessionId, SessionStartRequest req, CancellationToken ct);
}
public sealed class SessionAbandonService : ISessionAbandonService
{
    public Task<SessionAbandonResult> AbandonAsync (Guid sessionId, SessionAbandonRequest req, CancellationToken ct);
}
```

Both implementations resolve `IEnumerable<ISessionLifecycleObserver>` from DI and invoke each `OnSessionStartedAsync` / `OnSessionAbandonedAsync` inside the ambient `IDbContextTransaction` opened by the service. Plan 06-05 MAY extend the `Request` records (currently empty) and MAY add new result-union cases (e.g. `AlreadyStarted`, `AlreadyAbandoned`) — the published cases (`SessionNotFound`, `InvalidState`) are the minimum the endpoint dispatcher's switch must cover. Adding new cases is binary-compatible with the abstract-record sealed-records pattern.

### Confirmation for OPS-05 plan-level success criterion

`GameKitVersionAssertionHostedService` is registered AT INDEX 0 of `services` in `AddGameKit()` via `services.Insert(0, ServiceDescriptor.Singleton<IHostedService, ...>())`. PATTERNS warning #2 satisfied; sibling-package migration hosted services that register via the default-append `AddHostedService<T>()` land AFTER it in the IHostedService list and therefore run AFTER the version assertion gate at `IHost.StartAsync`.

## Self-Check: PASSED

**Created files exist:**
- `src/GameKit.Core/Services/ISessionLifecycleObserver.cs` — FOUND
- `src/GameKit.Core/Services/ISessionStartService.cs` — FOUND
- `src/GameKit.Core/Services/ISessionAbandonService.cs` — FOUND
- `src/GameKit.Core/Services/GameKitVersionMismatchException.cs` — FOUND
- `src/GameKit.Core/Hosting/GameKitVersionAssertionHostedService.cs` — FOUND

**Commits exist on `worktree-agent-a5c358fd48cbe7cb4`:**
- `0c201d4` — `feat(06-02): add ISessionLifecycleObserver + ISessionStartService + ISessionAbandonService Core ports` — FOUND
- `00279e1` — `feat(06-02): add GameKitVersionAssertionHostedService + GameKitVersionMismatchException (OPS-05)` — FOUND
