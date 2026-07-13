# Phase 13: Observability Foundations - Research

**Researched:** 2026-06-14
**Domain:** Roslyn analyzer authoring, .NET OpenTelemetry library patterns, self-hosted OTel Collector + Prometheus + Grafana + Tempo via Docker Compose
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**Naming Convention (D-01):** ActivitySource/Meter names stay PascalCase namespace-style `GameKit.<Package>` — matches existing live sources and .NET OTel ecosystem norm. Zero source renames. Metric instrument names + span attribute keys use lowercase-dotted `gamekit.<package>.*` / `ladder.id` per OTel semantic conventions.

**GameKitTelemetry constants + enforcement test (D-02):** Core exposes prefix and each canonical source/meter name as `const`s; meter/source version pinned `"1.0.0"` centrally. A unit test asserts every per-package `Telemetry/` class references the Core constant.

**Span Attribute Keys (D-03):** Retrofit Matchmaking camelCase tags to lowercase-dotted in this phase (`ladderId`→`ladder.id`, `poolName`→`pool.name`, `candidatesEvaluated`→`candidates.evaluated`, `matchesFormed`→`matches.formed`, etc.). Rankings already dotted-compliant.

**Core dimension key constants (D-04):** `GameKitTelemetry` seeded with `ladder.id`, `pool.name`, `ladder.name`, `region`, `status`, `result`, `error.type`. High-cardinality identifiers (`player.id`, `ticket.id`) FORBIDDEN as tags.

**Extract RankingsActivitySource (D-05):** Inline `_activitySource` in `RankingsTickerService` → `Telemetry/RankingsActivitySource.cs`, mirroring `MatchmakingActivitySource`.

**PII Lint Gate — Roslyn analyzer, repo-build-only (D-06):** Source analyzer inspects first arg of `SetTag`/`AddTag` calls in `src/` during GameKit's own build + CI. NOT shipped in consumer NuGet packages.

**Token-split + whole-token match + allow-list (D-07):** Tokenize attribute key on dots and case-boundaries; match whole tokens against denylist `{player, user, email, token, ip, fingerprint}`. Allow-list file documents intentional exceptions.

**Tempo default; Jaeger documented swap (D-08):** Ship Tempo (AGPLv3 — operator-pulled container, GameKit does not link or distribute it). Document Jaeger (Apache-2.0) as one-line overlay swap.

**OTLP push, app stays on host (D-09):** Sample app runs via `dotnet run`, pushes OTLP to dockerized Collector on host-published `:4317`. Collector fans out to Prometheus (metrics) + Tempo (traces); Grafana reads both. Prometheus and its scrape target stay on internal Docker network only.

**Sample-local compose pair (D-10):** `samples/TicTacToeDuel/docker-compose.yml` (base: Postgres + Redis) and `docker-compose.observability.yml` (overlay: Collector + Prometheus + Grafana + Tempo). Sample Postgres on host `:5433`.

**Grafana provisioned-as-code, 2 dashboards (D-11):** Commit `datasources.yml` + 2 dashboard JSONs (matchmaking queue depth + ticker health) auto-loading on start.

### Claude's Discretion

- Exact analyzer project layout / diagnostic IDs
- OTel Collector pipeline config shape
- Prometheus scrape interval
- Dashboard panel composition
- Precise final attribute-key strings for normalized Matchmaking tags beyond the D-04 seed set (follow lowercase-dotted rule)

### Deferred Ideas (OUT OF SCOPE)

- Per-package instrumentation (Phase 15): OBS-04 background-job metrics, OBS-05 lobby SignalR metrics, OBS-06 W3C trace-context propagation
- Shipping the PII analyzer to consumers
- Final-demo 3D multiplayer platformer

</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| OBS-01 | `AddGameKitObservability()` extension in `GameKit.Core` registers every GameKit `ActivitySource`/`Meter`; OTel SDK + OTLP exporter are opt-in dependencies; shipped packages use only in-box `System.Diagnostics.DiagnosticSource` with no OTel SDK hard dependency | §AddGameKitObservability() Pattern, §Standard Stack |
| OBS-02 | Per-package named `ActivitySource` following `gamekit.<package>.*` naming convention centralized as constants in Core (`GameKitTelemetry`) | §GameKitTelemetry Constants, §Attribute Key Normalization |
| OBS-03 | Per-package `Meter` RED metrics namespaced `gamekit.<package>.*`, only low-cardinality labels (never player_id/ticket_id) | §GameKitTelemetry Constants, §PII Lint Gate |
| OBS-07 | PII/secret span-attribute guard — CI lint gate + documented attribute allow-list fails build if player_id, email, tokens, or secrets are tagged; established as FIRST task | §PII Lint Gate — Roslyn Analyzer, §Validation Architecture |
| OBS-08 | Self-hosted observability stack in `samples/TicTacToeDuel` (`docker-compose.observability.yml`) + pre-provisioned Grafana dashboards; Prometheus isolated on internal Docker network | §Self-Hosted Observability Stack |

</phase_requirements>

---

## Summary

Phase 13 establishes a PII-safe observability foundation across five distinct work streams: (1) a repo-build-only Roslyn analyzer that enforces the PII attribute denylist, (2) a `GameKitTelemetry` constants class in `GameKit.Core` as the single source of truth for all source/meter/attribute key names, (3) the `AddGameKitObservability()` extension method on `IGameKitBuilder` that registers all known GameKit sources without forcing the OTel SDK on consumers, (4) normalization of existing Matchmaking camelCase span tags and extraction of the Rankings inline `_activitySource`, and (5) a self-hosted OTel Collector + Prometheus + Grafana + Tempo compose stack in `samples/TicTacToeDuel` with pre-provisioned dashboards.

The most novel work is the Roslyn analyzer (D-06/D-07). The project already has `GameKit.Build` (`IsRoslynComponent=true`, `netstandard2.0`, `ManagePackageVersionsCentrally=false`) as the pattern for a build-only Roslyn component. The PII analyzer follows the same project layout but uses `DiagnosticAnalyzer` instead of `IIncrementalGenerator`. It is wired into all `src/` projects via `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` — identical to how `GameKit.Build` is wired today — so it participates in the solution build and CI but is invisible to consumers.

The `AddGameKitObservability()` pattern is a thin extension on `IGameKitBuilder` that exposes the canonical source/meter name list to the host's OTel builder. Because shipped packages use only `System.Diagnostics.ActivitySource` and `System.Diagnostics.Metrics.Meter` (both in-box in .NET), the opt-in nature is preserved. The extension method requires `OpenTelemetry.Extensions.Hosting` (already pinned at 1.15.3) and provides two helper methods: one for `TracerProviderBuilder` and one for `MeterProviderBuilder`.

The Docker compose stack design centers on network isolation: Prometheus and its scrape target (the Collector's Prometheus exporter on port 8889) never touch any host-published port, satisfying criterion #3's `curl http://localhost:9090` isolation test. The OTel Collector is the only container that publishes a port to the host (`:4317` for OTLP gRPC), since the sample app runs on the host and must push telemetry to the Collector.

**Primary recommendation:** Build the Roslyn PII analyzer first (the D-06 order mandate), using the existing `GameKit.Build` csproj layout as the template. Then lay the `GameKitTelemetry` constants and `AddGameKitObservability()`. The compose stack is purely additive and can proceed in parallel with the analyzer once the Wave 0 scaffolding is in place.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| PII lint gate | Build tooling (Roslyn analyzer) | CI | Build-time enforcement is the only reliable gate; runtime checks are too late and too broad |
| `GameKitTelemetry` constants | `GameKit.Core` library | — | Constants must be accessible by every sibling package at compile time; Core is the universal dep |
| `AddGameKitObservability()` | `GameKit.Core` builder extension | OTel SDK (opt-in, host-owned) | Follows `AddGameKit()` builder pattern already established; Core is the single integration point |
| Per-package `Telemetry/` classes | Each `GameKit.*` package | — | Encapsulation: each package owns its own instrument declarations |
| Attribute key normalization | `GameKit.Matchmaking` sources | `GameKit.Core` constants | Tags are written at callsite in per-package code; constants define the allowed keys |
| OTel Collector | Docker container (infra) | — | Receives OTLP from host app, fans out to Prometheus exporter + Tempo |
| Prometheus + Grafana + Tempo | Docker containers (infra) | — | Operator-pulled; zero linking with GameKit library code |
| Grafana dashboards | Sample app `/observability/` | — | Provisioned-as-code in `samples/TicTacToeDuel`; not a NuGet artifact |

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| `Microsoft.CodeAnalysis.CSharp` | 4.13.0 | Roslyn API for DiagnosticAnalyzer | Already pinned in `GameKit.Build.csproj`; matches the .NET 10 SDK's Roslyn bundle [VERIFIED: csproj] |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` | 1.1.4 | xUnit-based analyzer test verifier pattern | Microsoft/RoslynTeam; 11.6M+ downloads; standard test harness for `DiagnosticAnalyzer` [VERIFIED: npm registry via `dotnet package search`] |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` | 1.1.2 | xUnit integration shim for the above | Same release train; ties verifier to xUnit test runner already used in repo [VERIFIED: npm registry via `dotnet package search`] |
| `OpenTelemetry` | 1.15.3 | OTel API + SDK (opt-in) | Already pinned in `Directory.Packages.props` [VERIFIED: Directory.Packages.props] |
| `OpenTelemetry.Extensions.Hosting` | 1.15.3 | `AddOpenTelemetry()` / `WithTracing()` / `WithMetrics()` hosting integration | Already pinned [VERIFIED: Directory.Packages.props] |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.15.3 | OTLP exporter (AddOtlpExporter) | Already pinned [VERIFIED: Directory.Packages.props] |

### Supporting — Infra Images
| Image | Version | Purpose | Notes |
|-------|---------|---------|-------|
| `otel/opentelemetry-collector-contrib` | `0.154.0` | OTLP gRPC receiver, Prometheus exporter, OTLP exporter to Tempo | Latest stable as of research date [CITED: github.com/open-telemetry/opentelemetry-collector-contrib/releases] |
| `prom/prometheus` | `v3.11.2` | Scrapes Collector Prometheus exporter; stored in internal network only | Latest stable as of research date [CITED: github.com/prometheus/prometheus/releases] |
| `grafana/grafana` | `13.0.2` | Dashboard + datasource provisioning | Latest stable as of 2026-06-14 [CITED: grafana.com/grafana/download] |
| `grafana/tempo` | `2.6.1` | Trace storage (AGPLv3 — operator pulled) | Latest confirmed stable; 3.0.x has breaking migration from 2.x [CITED: github.com/grafana/tempo/releases] |

> **Tempo version note:** Tempo 3.0.x is available but introduces breaking storage format changes requiring migration tooling. `2.6.1` is the recommended conservative pin for a new dev stack. Pin the version explicitly — never use `latest`. [CITED: grafana.com/docs/tempo/latest/release-notes/]

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `grafana/tempo` (AGPLv3) | `jaegertracing/jaeger` (Apache-2.0) | Jaeger is the documented one-line swap per D-08; no GameKit GPL-conflict with either since both are operator-pulled containers |
| `otel/opentelemetry-collector-contrib` | `otel/opentelemetry-collector` (core, smaller) | Contrib has the `prometheusexporter` receiver and `prometheus` exporter; core does not ship them |

### Installation (new NuGet additions)
```bash
# In tests/GameKit.Build.Tests/ (new project — analyzer test project only):
dotnet add package Microsoft.CodeAnalysis.CSharp.Analyzer.Testing --version 1.1.4
dotnet add package Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit --version 1.1.2
# The OpenTelemetry packages are already in Directory.Packages.props:
# OpenTelemetry 1.15.3, OpenTelemetry.Extensions.Hosting 1.15.3, OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.3
```

---

## Package Legitimacy Audit

> slopcheck was not run (pip unavailable in this environment). All NuGet packages below verified via `dotnet package search` against nuget.org. Docker images verified via web search and official release pages.

| Package | Registry | Age | Downloads | Source Repo | slopcheck | Disposition |
|---------|----------|-----|-----------|-------------|-----------|-------------|
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` | NuGet | ~4 yrs | 11.6M | github.com/dotnet/roslyn-analyzers | [ASSUMED] | Approved — Microsoft/RoslynTeam official; part of dotnet/roslyn-analyzers |
| `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` | NuGet | ~4 yrs | 3.0M | github.com/dotnet/roslyn-analyzers | [ASSUMED] | Approved — same release train as above |
| `OpenTelemetry` 1.15.3 | NuGet | Already pinned | — | github.com/open-telemetry/opentelemetry-dotnet | [ASSUMED] | Already approved in `Directory.Packages.props` |
| `OpenTelemetry.Extensions.Hosting` 1.15.3 | NuGet | Already pinned | — | same | [ASSUMED] | Already approved |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3 | NuGet | Already pinned | — | same | [ASSUMED] | Already approved |

**Packages removed due to slopcheck [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

*slopcheck was unavailable at research time. The three new NuGet packages (`Microsoft.CodeAnalysis.CSharp.Analyzer.Testing`, `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit`) are from the dotnet/roslyn-analyzers official Microsoft repo with 3M–12M downloads; the planner must add a `checkpoint:human-verify` before adding them to `Directory.Packages.props`.*

---

## Architecture Patterns

### System Architecture Diagram

```
Host Machine
┌─────────────────────────────────────────────────────────────┐
│  dotnet run (TicTacToeDuel sample app)                       │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │  OtlpExporter → push OTLP/gRPC → host:4317             │ │
│  └─────────────────────────────────────────────────────────┘ │
└───────────────────────┬─────────────────────────────────────┘
                        │ host port 4317
┌───────────────────────▼─────────────────────────────────────┐
│  Docker internal network: gamekit-obs                        │
│                                                              │
│  ┌──────────────────────────┐                               │
│  │  otel-collector :4317    │  ← OTLP gRPC receiver        │
│  │  → prometheus exporter   │    exposes :8889 (internal)   │
│  │  → otlp exporter         │    forwards to tempo:4317     │
│  └──────────┬──────────────┘                               │
│             │                                               │
│     ┌───────▼──────┐    ┌────────────────┐                 │
│     │ prom:9090    │    │  tempo:4317    │                 │
│     │ (scrapes     │    │  (trace store) │                 │
│     │  collector:  │    └───────┬────────┘                 │
│     │  8889)       │            │                           │
│     └──────┬───────┘            │                           │
│            │                   │                            │
│     ┌──────▼──────────────────▼─┐                          │
│     │  grafana :3000 (internal)  │                          │
│     │  datasources auto-loaded   │                          │
│     └───────────────────────────┘                           │
│                                                              │
│  Postgres :5432 (internal) — host maps to :5433             │
│  Redis :6379 (internal)                                      │
└─────────────────────────────────────────────────────────────┘

Host curl http://localhost:9090 → NOTHING (no port binding for Prometheus)
Host curl http://localhost:3000 → Grafana (published for dev convenience)
Host OTLP push → localhost:4317 → Collector (published for app-on-host)
```

**Key isolation:** Prometheus port `9090` is NOT published to the host. Grafana `3000` IS published (developers need the UI). The Collector `4317` IS published (app pushes from host). This satisfies criterion #3: `curl http://localhost:9090` does not reach app metrics.

### Recommended Project Structure (new additions only)

```
src/
├── GameKit.Core/
│   ├── Telemetry/
│   │   └── GameKitTelemetry.cs          # NEW: constants class
│   └── Builder/
│       └── GameKitObservabilityBuilderExtensions.cs  # NEW: AddGameKitObservability()
├── GameKit.Rankings/
│   └── Telemetry/
│       └── RankingsActivitySource.cs    # NEW: extracted from RankingsTickerService
├── GameKit.Matchmaking/
│   └── Telemetry/
│       ├── MatchmakingActivitySource.cs # MODIFY: normalize camelCase tags to dotted
│       └── MatchmakingMeter.cs          # unchanged
src/
└── GameKit.Build/                       # already exists — add PiiAttributeAnalyzer.cs here
    └── PiiAttributeAnalyzer.cs          # NEW: DiagnosticAnalyzer

tests/
└── GameKit.Build.Tests/                 # NEW: analyzer test project
    ├── GameKit.Build.Tests.csproj
    └── PiiAttributeAnalyzerTests.cs

samples/TicTacToeDuel/
├── docker-compose.yml                   # NEW: base compose (Postgres:5433 + Redis)
├── docker-compose.observability.yml     # NEW: overlay (Collector + Prometheus + Grafana + Tempo)
└── observability/
    ├── otel-collector-config.yml
    ├── prometheus.yml
    └── grafana/
        ├── provisioning/
        │   ├── datasources/
        │   │   └── datasources.yml
        │   └── dashboards/
        │       └── dashboards.yml
        └── dashboards/
            ├── matchmaking-queue-depth.json
            └── ticker-health.json
```

---

## PII Lint Gate — Roslyn Analyzer (D-06, D-07, OBS-07, Criterion #1)

### Analyzer Project Layout

The existing `GameKit.Build` project is the canonical template. The PII analyzer lives in a **new file within `GameKit.Build`** rather than a new project:

```
src/GameKit.Build/
├── GameKit.Build.csproj       (already exists — ManagePackageVersionsCentrally=false, netstandard2.0, IsRoslynComponent=true)
├── GameKitVersionGenerator.cs (already exists)
└── PiiAttributeAnalyzer.cs    (NEW)
```

This is the correct layout because:
1. `GameKit.Build` is already wired into all `src/GameKit.*.csproj` files as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"`.
2. A Roslyn analyzer assembly can contain both `IIncrementalGenerator` implementations and `DiagnosticAnalyzer` implementations.
3. No new .csproj, no new solution entry, no CPM conflict. [VERIFIED: GameKit.Build.csproj and all src csproj files]

**No consumer NuGet exposure:** The `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` `ProjectReference` means the analyzer participates only in the referencing project's build. When `GameKit.Core` is packed as a NuGet, `GameKit.Build.dll` does NOT flow into the package — the `PrivateAssets="all"` + `IncludeBuildOutput=false` settings in `GameKit.Build.csproj` already enforce this. [VERIFIED: GameKit.Build.csproj `IsPackable=false`] [ASSUMED: PrivateAssets semantics on analyzer ProjectReference work identically to PackageReference for build-only scenarios — standard MSBuild behavior]

### DiagnosticAnalyzer vs Source Generator Choice

Use `DiagnosticAnalyzer` (not `IIncrementalGenerator`) because:
- Analyzers report diagnostics (errors/warnings on specific syntax nodes) — the exact capability needed for a lint gate.
- Source generators emit code; they do not report diagnostics against existing code.
- `DiagnosticAnalyzer` with a `SyntaxKind.InvocationExpression` registered action gives AST-precise diagnostics with correct file/line/column. [CITED: github.com/dotnet/roslyn-analyzers]

### Analyzer Implementation Pattern

```csharp
// Source: dotnet/roslyn-analyzers DiagnosticAnalyzer pattern [CITED: github.com/dotnet/roslyn-analyzers]
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PiiAttributeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "GK0001";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticId,
        title: "PII attribute key in span tag",
        messageFormat: "Span tag key '{0}' contains a PII token '{1}'. " +
                       "Add to the allow-list (pii-allowlist.txt) if intentional.",
        category: "GameKit.Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prevents player identifiers, email, tokens, IP addresses, and fingerprints " +
                     "from being emitted as span attributes (GDPR/OBS-07).");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        // 1. Check method name is SetTag or AddTag
        // 2. Use semantic model to confirm receiver is Activity or ActivityTagsCollection
        // 3. Extract first argument (the key string)
        // 4. Tokenize key: split on dots and PascalCase boundaries
        // 5. Match whole tokens against denylist
        // 6. If denylist match, check allow-list file (AdditionalFiles)
        // 7. If not in allow-list → report GK0001
    }
}
```

### Semantic Model Precision (not text-search)

Use `context.SemanticModel.GetSymbolInfo(invocation)` to resolve the called method symbol. Check that the containing type is `System.Diagnostics.Activity` or that the method name matches `SetTag`/`AddTag` on known extension types. This avoids false positives on unrelated `SetTag` methods in non-telemetry code. [CITED: github.com/dotnet/roslyn]

### Token-Split Algorithm

```csharp
// Split "candidatesEvaluated" → ["candidates", "Evaluated"] → ["candidates", "evaluated"]
// Split "client.ip" → ["client", "ip"]
// Split "phase.hashFanoutMs" → ["phase", "hash", "Fanout", "Ms"] → ["phase", "hash", "fanout", "ms"]
private static IEnumerable<string> Tokenize(string key)
{
    // Step 1: split on dots
    foreach (var dotPart in key.Split('.'))
    {
        // Step 2: split on PascalCase/camelCase boundaries using regex [A-Z][a-z]+ or sequences
        foreach (var token in SplitOnCaseBoundary(dotPart))
            yield return token.ToLowerInvariant();
    }
}

private static readonly string[] Denylist =
    ["player", "user", "email", "token", "ip", "fingerprint"];
```

**Avoiding false positives:**
- `recipient.count` → tokens `["recipient", "count"]` → no match (avoids naive `Contains("ip")` hitting "rec**ip**ient")
- `description` → tokens `["description"]` → no match (avoids `Contains("ip")`)
- `zip.code` → tokens `["zip", "code"]` → no match
- `client.ip` → tokens `["client", "ip"]` → BLOCKED (`ip` is in denylist)
- `poolName` → tokens `["pool", "name"]` → no match (post D-03 rename, but the analyzer still guards the old form during the transition)

### Non-Literal Key Handling

When the first argument is not a string literal (it is a variable, const reference, or expression), the analyzer CANNOT statically evaluate the key. Recommended behavior: **warn with a distinct diagnostic ID `GK0002`** flagging the non-literal case, advising the developer to use a constant from `GameKitTelemetry`. This becomes a reminder rather than a hard error for the non-literal case. [ASSUMED: this is discretionary; the planner should confirm whether GK0002 should be Error or Warning severity]

Alternatively, since `GameKitTelemetry` constants will cover all legitimate attribute keys, a stricter policy (also valid) is: require the first argument to be a string literal that passes the token check OR a `const` reference whose value the compiler can fold — in which case the analyzer resolves the constant value via `context.SemanticModel.GetConstantValue()` and evaluates it. The folded-constant path is strongly preferred. [CITED: Microsoft.CodeAnalysis IOperation API — constant folding via `GetConstantValue`]

### Allow-List Wiring

The allow-list is a plain text file committed to the repo (e.g., `src/GameKit.Build/pii-allowlist.txt`), listed as an `AdditionalFiles` entry in `Directory.Build.props` so it is passed to every analyzer invocation:

```xml
<!-- Directory.Build.props -->
<ItemGroup>
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)src/GameKit.Build/pii-allowlist.txt" />
</ItemGroup>
```

The analyzer reads this file via `context.Options.AdditionalFiles`. Each line is a fully-qualified key pattern that is exempt from the denylist check. [ASSUMED: AdditionalFiles path resolution is relative to the project file — the absolute path above avoids ambiguity]

### Testing the Analyzer

Use `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` (1.1.2) + `CSharpAnalyzerVerifier<PiiAttributeAnalyzer>` pattern:

```csharp
// Source: dotnet/roslyn-analyzers test pattern [CITED: github.com/dotnet/roslyn-analyzers]
public class PiiAttributeAnalyzerTests
{
    [Fact]
    public async Task PlayerToken_InLiteral_Blocked()
    {
        const string source = """
            activity.SetTag("player.id", someGuid.ToString());
            """;
        // Expect GK0001 at the "player.id" location
        await CSharpAnalyzerVerifier<PiiAttributeAnalyzer>.VerifyAnalyzerAsync(
            source,
            DiagnosticResult.CompilerError("GK0001").WithLocation(1, 15));
    }

    [Fact]
    public async Task RecipientCount_Clean_NoError()
    {
        const string source = """
            activity.SetTag("recipient.count", 5);
            """;
        await CSharpAnalyzerVerifier<PiiAttributeAnalyzer>.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task LadderId_Clean_NoError()
    {
        const string source = """
            activity.SetTag("ladder.id", ladderId.ToString());
            """;
        await CSharpAnalyzerVerifier<PiiAttributeAnalyzer>.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ClientIp_Blocked()  // D-07 example
    {
        const string source = """activity.SetTag("client.ip", "1.2.3.4");""";
        await CSharpAnalyzerVerifier<PiiAttributeAnalyzer>.VerifyAnalyzerAsync(
            source,
            DiagnosticResult.CompilerError("GK0001").WithLocation(1, ...));
    }
}
```

The test project `tests/GameKit.Build.Tests/` is a standard `xunit` project on `net10.0` (NOT `netstandard2.0`) — analyzer tests run in the normal .NET runtime environment. It does NOT reference `GameKit.Build` as an Analyzer; it references it directly as a `ProjectReference` (no `OutputItemType="Analyzer"`). [CITED: dotnet/roslyn-analyzers test project pattern]

---

## AddGameKitObservability() Pattern (D-02, OBS-01/02, Criterion #2)

### What "registers all known GameKit sources" means concretely

`AddGameKitObservability()` does NOT register an OTel SDK. It is an extension method on `IGameKitBuilder` that calls `AddSource`/`AddMeter` on a `TracerProviderBuilder`/`MeterProviderBuilder` supplied by the host. The idiomatic .NET OTel library pattern is:

**Option A — Extension on TracerProviderBuilder + MeterProviderBuilder (standard OTel library approach):**
```csharp
// Host wires GameKit sources into its own OTel setup:
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddGameKitSources())   // exposed by GameKit.Core
    .WithMetrics(m => m.AddGameKitMeters());   // exposed by GameKit.Core
```

**Option B — AddGameKitObservability() wires everything in one call on IGameKitBuilder:**
```csharp
// CHOSEN approach — matches GameKit's builder-chain idiom:
services.AddGameKit(...)
    .AddAuth(...)
    .AddMatchmaking(...)
    .AddGameKitObservability(otel =>
    {
        // optional: configure OTLP endpoint
        otel.OtlpEndpoint = "http://localhost:4317";
    });
```

The implementation of `AddGameKitObservability()` can do BOTH: call `services.AddOpenTelemetry().WithTracing(t => t.AddSource(…)).WithMetrics(m => m.AddMeter(…))` internally when called, AND expose the name list as public constants so consumers who manage their own `TracerProvider` can cherry-pick with `AddSource(GameKitTelemetry.MatchmakingTickerSourceName)`.

**Key constraint:** Shipped NuGet packages (`GameKit.Core`, `GameKit.Matchmaking`, etc.) must NOT take a hard `PackageReference` to `OpenTelemetry` or `OpenTelemetry.Extensions.Hosting`. The `AddGameKitObservability()` method lives in `GameKit.Core` and can reference OTel packages because Core already has `OpenTelemetry.Api` transitively (it is pinned globally for CVE reasons). However, to keep the published NuGet clean, the `AddGameKitObservability()` implementation that depends on `OpenTelemetry.Extensions.Hosting` should be in a separate file with `#if` gating, OR the OTel packages should be listed as `PrivateAssets="all"` so they don't flow to consumers. [ASSUMED: the cleanest approach is to add OTel packages as non-private `PackageReference` to `GameKit.Core.csproj` with a note that consumers must install the OTel SDK separately; this matches decision OBS-01's "opt-in" intent — the planner should decide whether to use the already-pinned transitive reference or add explicit PackageReferences]

**What the constants list covers:**
```csharp
// src/GameKit.Core/Telemetry/GameKitTelemetry.cs
public static class GameKitTelemetry
{
    // Source/meter name prefix convention (D-01: PascalCase, not lowercased)
    public const string SourcePrefix = "GameKit";

    // Activity sources — operators register these with AddSource(...)
    public const string MatchmakingTickerSourceName   = "GameKit.Matchmaking.Ticker";
    public const string RankingsTickerSourceName      = "GameKit.Rankings.Ticker";
    // (Phase 15 will add Auth, Presence, Lobby sources here)

    // Meters — operators register these with AddMeter(...)
    public const string MatchmakingMeterName          = "GameKit.Matchmaking";
    // (Phase 15 will add Rankings, Lobby meters here)

    // Shared version string
    public const string Version                       = "1.0.0";

    // Low-cardinality dimension key constants (D-04)
    public const string AttrLadderId                  = "ladder.id";
    public const string AttrPoolName                  = "pool.name";
    public const string AttrLadderName                = "ladder.name";
    public const string AttrRegion                    = "region";
    public const string AttrStatus                    = "status";
    public const string AttrResult                    = "result";
    public const string AttrErrorType                 = "error.type";
}
```

### Constants Enforcement Test

A reflection-based unit test that verifies no per-package `Telemetry/` class contains a magic string that matches a GameKit source/meter name without going through `GameKitTelemetry`:

```csharp
[Fact]
public void PerPackageTelemetryClasses_ReferenceGameKitTelemetryConstants()
{
    // Scan all types in GameKit.Matchmaking + GameKit.Rankings assemblies
    // whose namespace ends in ".Telemetry"
    // Assert: no string fields equal to any GameKitTelemetry const value
    //         unless the field's initializer IS the GameKitTelemetry const
    //         (i.e., the constant is referenced, not duplicated as a literal)
    //
    // In practice: verify via reflection that each per-package SourceName const
    // equals the corresponding GameKitTelemetry.xxxSourceName at runtime.
    // This catches drift between the constants.
}
```

A simpler and more robust approach: each per-package `Telemetry/` class exposes its `SourceName` as a `const` that is initialized FROM `GameKitTelemetry.MatchmakingTickerSourceName` — the compiler inlines the value, but the test verifies them equal by reflection at runtime. [ASSUMED: reflection-based test is the chosen approach; source-scan is more fragile]

---

## Attribute Key Normalization (D-03, OBS-03, Criterion #4/#5)

### Complete Rename Map for MatchmakerTickerService + MatchmakingActivitySource

All existing camelCase tags that appear in `MatchmakerTickerService.cs` and `MatchmakingActivitySource.cs` must be renamed to lowercase-dotted:

| Old Key (camelCase) | New Key (lowercase-dotted) | File | Line (approx) |
|---------------------|--------------------------|------|----------------|
| `ladderId` | `ladder.id` | MatchmakingActivitySource.cs | 67 |
| `poolName` | `pool.name` | MatchmakingActivitySource.cs | 68 |
| `candidatesEvaluated` | `candidates.evaluated` | MatchmakerTickerService.cs | 362 |
| `phase.hashFanoutMs` | `phase.hash_fanout_ms` | MatchmakerTickerService.cs | 395 |
| `budgetBail` | `budget.bail` | MatchmakerTickerService.cs | 434 |
| `matchCapBail` | `match_cap.bail` | MatchmakerTickerService.cs | 439 |
| `matchesFormed` | `matches.formed` | MatchmakerTickerService.cs | 496 |
| `phase.matchLoopMs` | `phase.match_loop_ms` | MatchmakerTickerService.cs | 497 |
| `phase.totalMs` | `phase.total_ms` | MatchmakerTickerService.cs | 498 |
| `paused` | `paused` | MatchmakerTickerService.cs | 209 | (already single-word, no change needed) |
| `reaped` | `reaped` | MatchmakerTickerService.cs | 261 | (single-word, no change) |

**Rankings — already compliant** (verified in source): `ladder.id`, `ladder.name`, `result`, `error` → rename `error` to `error.type` to match D-04 constant. [VERIFIED: RankingsTickerService.cs lines 214-215, 440, 455-456]

> **Note on `phase.*` prefix:** the `phase.hashFanoutMs` etc. keys use a dotted prefix but camelCase suffix. The OTel semantic convention for metric instrument names and attribute keys uses `snake_case` for the suffix part. `phase.hash_fanout_ms` is preferred over `phase.hashFanoutMs`. This is within Claude's discretion per CONTEXT.md.

### Safety of Renaming
Spans are no-ops until subscribed (OTel design). No public contract exists for these attribute key names yet (the project is not yet public — PROJECT.md "not yet public" north star). Zero operator dashboard queries would break. The rename is safe to ship in this phase. [VERIFIED: PROJECT.md, OBS-01 opt-in design]

---

## RankingsActivitySource Extraction (D-05, Criterion #5)

### Current State
`RankingsTickerService` (line 59-60) contains:
```csharp
private static readonly ActivitySource _activitySource =
    new("GameKit.Rankings.Ticker", "1.0.0");
```
This inline declaration is the same pattern that was in `MatchmakerTickerService` before plan 05-05 extracted it to `MatchmakingActivitySource`. [VERIFIED: RankingsTickerService.cs lines 59-60]

### Target Shape
```csharp
// src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs
public static class RankingsActivitySource
{
    public const string SourceName = GameKitTelemetry.RankingsTickerSourceName; // = "GameKit.Rankings.Ticker"
    internal static readonly ActivitySource Source = new(SourceName, GameKitTelemetry.Version);

    public static Activity? StartDrainLadderActivity() => Source.StartActivity("DrainLadder");
}
```

`RankingsTickerService` then replaces the inline `_activitySource` with calls to `RankingsActivitySource.Source.StartActivity(...)` or typed helper methods. The static readonly field on `RankingsActivitySource` is `internal` — mirrors `MatchmakingActivitySource.Source` visibility. [VERIFIED: MatchmakingActivitySource.cs line 44]

---

## Self-Hosted Observability Stack (D-08...D-11, OBS-08, Criterion #3)

### Compose Architecture: Base + Overlay Split

**`samples/TicTacToeDuel/docker-compose.yml`** (new base for sample — does NOT override repo root compose):
```yaml
# samples/TicTacToeDuel/docker-compose.yml
# Base services for running TicTacToeDuel sample locally.
# Postgres on :5433 (host :5432 reserved for developer's local Postgres — project memory).
services:
  postgres:
    image: postgres:17.9
    ports:
      - "5433:5432"          # KEY: 5433 on host, not 5432
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres_bootstrap_dev_only
      POSTGRES_DB: gamekit
    volumes:
      - tictactoe-postgres-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d gamekit"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s

  redis:
    image: redis:8.6.2
    ports:
      - "6379:6379"
    command: ["redis-server", "--appendonly", "yes", "--appendfsync", "everysec",
              "--maxmemory-policy", "noeviction"]

volumes:
  tictactoe-postgres-data:
```

**`samples/TicTacToeDuel/docker-compose.observability.yml`** (overlay):
```yaml
# Overlay: start with:
#   docker compose -f docker-compose.yml -f docker-compose.observability.yml up
services:
  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.154.0
    volumes:
      - ./observability/otel-collector-config.yml:/etc/otelcol-contrib/config.yaml:ro
    ports:
      - "4317:4317"   # OTLP gRPC — published for host-running app
      # NOTE: 8889 (Prometheus exporter) is NOT published — internal only
    networks:
      - obs-internal
    depends_on:
      - tempo

  prometheus:
    image: prom/prometheus:v3.11.2
    volumes:
      - ./observability/prometheus.yml:/etc/prometheus/prometheus.yml:ro
    # NO ports: section — Prometheus is NOT published to the host (criterion #3)
    networks:
      - obs-internal

  tempo:
    image: grafana/tempo:2.6.1
    command: ["-config.file=/etc/tempo.yaml"]
    volumes:
      - ./observability/tempo.yaml:/etc/tempo.yaml:ro
      - tempo-data:/var/tempo
    networks:
      - obs-internal

  grafana:
    image: grafana/grafana:13.0.2
    ports:
      - "3000:3000"   # Published for dev: browser access to dashboards
    environment:
      GF_AUTH_ANONYMOUS_ENABLED: "true"
      GF_AUTH_ANONYMOUS_ORG_ROLE: "Admin"
    volumes:
      - ./observability/grafana/provisioning:/etc/grafana/provisioning:ro
      - ./observability/grafana/dashboards:/var/lib/grafana/dashboards:ro
    networks:
      - obs-internal
    depends_on:
      - prometheus
      - tempo

networks:
  obs-internal:
    driver: bridge
    # No external: true — fully internal; no host-network binding for Prometheus

volumes:
  tempo-data:
```

**`samples/TicTacToeDuel/observability/otel-collector-config.yml`**:
```yaml
receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
      http:
        endpoint: 0.0.0.0:4318

exporters:
  prometheus:
    endpoint: "0.0.0.0:8889"   # Prometheus scrapes this — internal network only
  otlp/tempo:
    endpoint: "tempo:4317"
    tls:
      insecure: true

service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlp/tempo]
    metrics:
      receivers: [otlp]
      exporters: [prometheus]
```

**`samples/TicTacToeDuel/observability/prometheus.yml`**:
```yaml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'gamekit'
    static_configs:
      - targets: ['otel-collector:8889']   # Internal Docker DNS — not a host port
```

**`samples/TicTacToeDuel/observability/tempo.yaml`** (minimal):
```yaml
server:
  http_listen_port: 3200

distributor:
  receivers:
    otlp:
      protocols:
        grpc:

storage:
  trace:
    backend: local
    local:
      path: /var/tempo/blocks
    wal:
      path: /var/tempo/wal
```

### Network Isolation: Why `curl http://localhost:9090` Returns Nothing

The `prometheus` service has no `ports:` mapping. Docker Compose creates a virtual bridge network (`obs-internal`) where containers communicate by service name. Prometheus listens on `9090` within this network but that port is NOT bound to any host interface. The host OS has no listener on `:9090`. `curl http://localhost:9090` will get "connection refused". [CITED: docker.com/compose/how-tos/networking — "services can communicate with each other using service names as hostnames; services not exposed to the host have no port binding"]

The `otel-collector` port `4317` IS published to the host because the TicTacToeDuel sample app runs on the host via `dotnet run` and needs to push OTLP to the Collector.

### Grafana Provisioning

**`datasources.yml`**:
```yaml
apiVersion: 1
datasources:
  - name: Prometheus
    type: prometheus
    url: http://prometheus:9090
    isDefault: true
    access: proxy
  - name: Tempo
    type: tempo
    url: http://tempo:3200
    access: proxy
```

**Dashboard provisioning** — `dashboards.yml`:
```yaml
apiVersion: 1
providers:
  - name: GameKit
    type: file
    options:
      path: /var/lib/grafana/dashboards
```

The two dashboard JSON files contain pre-built panel definitions. Key panels:

- **matchmaking-queue-depth.json:** `matchmaking.analytics.dropped_events` counter (reason dimension), queue depth gauge. Requires Prometheus datasource with `gamekit.matchmaking.*` metrics.
- **ticker-health.json:** Ticker tick duration (histogram), lease-acquired/lost counter, pool sweep duration. These metrics are Phase 15 instrumentation — for Phase 13 the dashboards can be pre-built with placeholder queries that will populate once Phase 15 lands.

> **Dashboard JSON generation approach:** Write minimal valid Grafana dashboard JSON by hand (not via Grafana API export). The minimal structure for a Grafana 13 dashboard JSON is stable and well-documented. Planner should assign a task to write ~100-line JSON per dashboard. [ASSUMED: hand-authoring is simpler than scripted export for this scale]

### Jaeger Swap Documentation (D-08)

Document as a README note in `samples/TicTacToeDuel/observability/`:
```
# To use Jaeger instead of Tempo (Apache-2.0 vs AGPLv3):
# In docker-compose.observability.yml, replace:
#   tempo:
#     image: grafana/tempo:2.6.1
# with:
#   jaeger:
#     image: jaegertracing/all-in-one:latest  (Apache-2.0)
#     ports: ["16686:16686"]  # Jaeger UI
# And in otel-collector-config.yml, replace the otlp/tempo exporter with:
#   jaeger:
#     endpoint: "jaeger:14250"
#     tls: {insecure: true}
# In Grafana datasources.yml, replace Tempo with Jaeger datasource type.
```

### GPL/AGPL Licensing Analysis (D-08)

**Finding:** Tempo (AGPLv3) and Grafana (AGPLv3) are operator-pulled Docker containers. GameKit does not link, compile against, or distribute these containers. The GPL/AGPL "linking" obligation applies to combined works where copyleft code is linked into the same binary or distributed as part of the same artifact. Running a separately licensed process in a Docker container does not constitute linking. [CITED: grafana.com/blog/2021/04/20/grafana-loki-tempo-relicensing-to-agplv3 — "users have to share source code if they are modifying it and making it available to others"] The GameKit `docker-compose.observability.yml` REFERENCES the Tempo image by name (like linking to an external service) but does NOT distribute the image or modify it. Conclusion: no GPL/AGPL conflict. [ASSUMED: this analysis is based on the standard interpretation of AGPL copyleft — a legal review is always recommended before distribution, but for a self-hosted developer tool this is well-established practice]

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Analyzer test harness | Custom `Compilation`-based tests | `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` Verifier | The verifier handles compilation setup, diagnostic location matching, code fix verification; hand-rolling this takes 200+ lines per test |
| OTLP gRPC push from app | Custom HTTP/gRPC exporter | `OpenTelemetry.Exporter.OpenTelemetryProtocol` (already pinned 1.15.3) | Handles OTLP/gRPC and OTLP/HTTP with backpressure, retry, batching |
| Prometheus scraping in collector | Custom HTTP server on the app | OTel Collector prometheus exporter | The collector abstracts the pull model; app stays OTLP-push-only |
| Grafana datasource setup | Manual Grafana UI clicks | `grafana/provisioning/datasources/*.yml` | Provisioned-as-code loads on container start; click-ops are not reproducible |
| Span token denylist via grep | Shell script or regex in CI | Roslyn DiagnosticAnalyzer | Analyzer is AST-precise (no false positives on comments/strings/non-telemetry code), shows exact line/column, fails build immediately |

**Key insight:** The existing `GameKit.Build` project already has all the scaffolding for a Roslyn component (`IsRoslynComponent=true`, correct wiring in all src csproj files). The PII analyzer is purely additive to that project — the hardest infrastructure is already in place.

---

## Common Pitfalls

### Pitfall 1: Publishing Prometheus Port to Host (Criterion #3 Failure)
**What goes wrong:** Adding `ports: ["9090:9090"]` to the Prometheus container service makes `curl http://localhost:9090` succeed, failing the criterion and potentially exposing app metrics.
**Why it happens:** Copy-paste from generic Prometheus tutorials that assume local access.
**How to avoid:** Explicitly omit the `ports:` key from the `prometheus:` service block. The container is reachable by other containers on `obs-internal` via DNS.
**Warning signs:** CI test for criterion #3 runs `curl -f http://localhost:9090/-/healthy` expecting failure; if it succeeds, the isolation is broken.

### Pitfall 2: Analyzer Targeting netstandard2.0 (Existing Pattern Confirmed)
**What goes wrong:** Targeting `net10.0` for the `GameKit.Build` project would cause "Generator failed to initialize" at every consumer build.
**Why it happens:** Roslyn loads the analyzer assembly into the compiler host process which runs on netstandard2.0.
**How to avoid:** `GameKit.Build.csproj` already has `<TargetFramework>netstandard2.0</TargetFramework>` — do not change it. [VERIFIED: GameKit.Build.csproj]

### Pitfall 3: Analyzer Test Project Referencing Build as Analyzer
**What goes wrong:** Adding `OutputItemType="Analyzer"` to the test project's `ProjectReference` on `GameKit.Build` causes the analyzer to run against the test code itself, creating circular verification problems.
**How to avoid:** The test project references `GameKit.Build` as a plain `ProjectReference` (no `OutputItemType`). The test directly constructs the `PiiAttributeAnalyzer` type and invokes it via the test verifier.

### Pitfall 4: Magic Strings in Per-Package Telemetry Classes
**What goes wrong:** Writing `new ActivitySource("GameKit.Rankings.Ticker", "1.0.0")` directly in `RankingsActivitySource.cs` instead of referencing `GameKitTelemetry.RankingsTickerSourceName` + `GameKitTelemetry.Version`. Criterion #4 fails.
**How to avoid:** The D-02 enforcement test catches this at runtime. During implementation, always use the constants.

### Pitfall 5: Tempo 3.0.x Migration Breaking Changes
**What goes wrong:** Pinning `grafana/tempo:latest` picks up 3.0.x which has storage format changes incompatible with a fresh 2.x Tempo install's config syntax.
**How to avoid:** Pin explicitly to `grafana/tempo:2.6.1`. Document the version in the compose file with a comment. [CITED: grafana.com/docs/tempo/latest/release-notes/v2-9]

### Pitfall 6: OTel Collector Image Tag Instability
**What goes wrong:** Using `otel/opentelemetry-collector-contrib:latest` picks up a pre-release or major version with breaking config syntax changes.
**How to avoid:** Pin to `otel/opentelemetry-collector-contrib:0.154.0`. The Collector config format changes between minor versions.

### Pitfall 7: AddGameKitObservability() Taking Hard OTel SDK Dep on Shipped Packages
**What goes wrong:** Adding `<PackageReference Include="OpenTelemetry.Extensions.Hosting" />` directly to `GameKit.Core.csproj` without `PrivateAssets="all"` causes the OTel SDK to flow to every consumer who installs `GameKit.Core`, violating OBS-01.
**How to avoid:** The OTel packages are already pinned as transitive-only in `Directory.Packages.props` for CVE suppression reasons. `AddGameKitObservability()` can use them without adding them as a new `PackageReference` — they are already present in the build graph. Alternatively, make the OTel references in `GameKit.Core.csproj` `PrivateAssets="all"` to suppress flow. [ASSUMED: the exact PrivateAssets strategy is planner discretion — both approaches are valid; the key constraint is that a consumer who does NOT call `AddGameKitObservability()` must NOT pull in the OTel SDK]

### Pitfall 8: Prometheus Scrape Target Points to App Instead of Collector
**What goes wrong:** Configuring Prometheus to scrape the TicTacToeDuel app directly (e.g., `http://host.docker.internal:5000/metrics`) instead of the Collector's exporter endpoint.
**Why it happens:** Some tutorials configure the app to expose a `/metrics` Prometheus endpoint. GameKit's approach is OTLP-push only; the app has no Prometheus scrape endpoint.
**How to avoid:** Always configure `prometheus.yml` to scrape `otel-collector:8889` (the Collector's Prometheus exporter). The Collector receives OTLP from the app and converts it.

### Pitfall 9: Token-Split False Negative on PascalCase Boundaries
**What goes wrong:** `SetTag("playerCount", n)` → naive dot-split → token `["playercount"]` → matches `player` prefix but NOT whole-token → misses the block.
**How to avoid:** The tokenizer must split on case boundaries TOO, not just dots. `playerCount` → `["player", "Count"]` → `["player", "count"]` → `player` is in denylist → BLOCKED. This is the whole-token-after-case-split requirement from D-07.

---

## Code Examples

### GameKitTelemetry Constants (verified against existing pattern)
```csharp
// src/GameKit.Core/Telemetry/GameKitTelemetry.cs
// Source: mirrors MatchmakingMeter.cs + MatchmakingActivitySource.cs pattern [VERIFIED: both files]
public static class GameKitTelemetry
{
    /// <summary>Shared version for all GameKit ActivitySource + Meter instances.</summary>
    public const string Version = "1.0.0";

    /// <summary>Prefix for all GameKit OpenTelemetry source + meter names (PascalCase, per D-01).</summary>
    public const string SourcePrefix = "GameKit";

    // Activity source names (operators call AddSource with these values)
    /// <summary>ActivitySource name for the matchmaking ticker. Register with <c>AddSource(GameKitTelemetry.MatchmakingTickerSourceName)</c>.</summary>
    public const string MatchmakingTickerSourceName = "GameKit.Matchmaking.Ticker";

    /// <summary>ActivitySource name for the rankings ticker. Register with <c>AddSource(GameKitTelemetry.RankingsTickerSourceName)</c>.</summary>
    public const string RankingsTickerSourceName = "GameKit.Rankings.Ticker";

    // Meter names (operators call AddMeter with these values)
    /// <summary>Meter name for GameKit.Matchmaking analytics instruments.</summary>
    public const string MatchmakingMeterName = "GameKit.Matchmaking";

    // Low-cardinality span attribute key constants (D-04)
    public const string AttrLadderId   = "ladder.id";
    public const string AttrPoolName   = "pool.name";
    public const string AttrLadderName = "ladder.name";
    public const string AttrRegion     = "region";
    public const string AttrStatus     = "status";
    public const string AttrResult     = "result";
    public const string AttrErrorType  = "error.type";
}
```

### RankingsActivitySource (extracted pattern)
```csharp
// src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs
// Source: mirrors MatchmakingActivitySource.cs [VERIFIED: MatchmakingActivitySource.cs]
public static class RankingsActivitySource
{
    /// <summary>The OpenTelemetry source name. Operators MUST register <c>AddSource(GameKitTelemetry.RankingsTickerSourceName)</c>.</summary>
    public const string SourceName = GameKitTelemetry.RankingsTickerSourceName;

    internal static readonly ActivitySource Source = new(SourceName, GameKitTelemetry.Version);

    /// <summary>Starts a "DrainLadder" span. Returns null if no listener subscribed.</summary>
    public static Activity? StartDrainLadderActivity() => Source.StartActivity("DrainLadder");
}
```

### AddGameKitObservability() Extension
```csharp
// src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs
// Source: OTel Extensions.Hosting pattern [CITED: github.com/open-telemetry/opentelemetry-dotnet]
public static class GameKitObservabilityBuilderExtensions
{
    /// <summary>
    /// Registers all GameKit <see cref="ActivitySource"/> and <see cref="Meter"/> names with the
    /// OpenTelemetry SDK hosted in this application. Optionally configures an OTLP exporter.
    /// </summary>
    /// <remarks>
    /// <b>Operator action required:</b> This method registers source/meter names with the
    /// OpenTelemetry SDK. The OTel SDK itself must be installed (<c>OpenTelemetry.Extensions.Hosting</c>)
    /// and <c>AddOpenTelemetry()</c> must be called on the host's <see cref="IServiceCollection"/>
    /// for telemetry to flow. Without the SDK, this call is a no-op.
    /// </remarks>
    public static IGameKitBuilder AddGameKitObservability(
        this IGameKitBuilder builder,
        Action<GameKitObservabilityOptions>? configure = null)
    {
        var opts = new GameKitObservabilityOptions();
        configure?.Invoke(opts);

        builder.Services.AddOpenTelemetry()
            .WithTracing(t =>
            {
                t.AddSource(GameKitTelemetry.MatchmakingTickerSourceName);
                t.AddSource(GameKitTelemetry.RankingsTickerSourceName);
                if (opts.OtlpEndpoint is not null)
                    t.AddOtlpExporter(o => o.Endpoint = new Uri(opts.OtlpEndpoint));
            })
            .WithMetrics(m =>
            {
                m.AddMeter(GameKitTelemetry.MatchmakingMeterName);
                if (opts.OtlpEndpoint is not null)
                    m.AddOtlpExporter(o => o.Endpoint = new Uri(opts.OtlpEndpoint));
            });

        return builder;
    }
}

/// <summary>Options for <see cref="GameKitObservabilityBuilderExtensions.AddGameKitObservability"/>.</summary>
public sealed class GameKitObservabilityOptions
{
    /// <summary>OTLP endpoint URI (e.g. <c>http://localhost:4317</c>). Null = no OTLP exporter.</summary>
    public string? OtlpEndpoint { get; set; }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| OTel collector image v0.9x | v0.154.0 (contrib) | Continuous releases | Config YAML syntax stable since v0.7x |
| Prometheus v2.x | v3.x (v3.11.2) | 2025/2026 | New distroless variant; core scrape config syntax unchanged |
| Tempo v2.x | v2.6.1 (stable) | Avoid 3.0.x for now | Storage format breaking change in 3.0 requires migration |
| Grafana v10/v11 | v13.0.2 | 2026-04-14 | Provisioning YAML format unchanged; new panel types |
| Text-grep PII enforcement | Roslyn DiagnosticAnalyzer | Phase 13 | AST-precise, exact line numbers, fails build |

**Deprecated/outdated:**
- `grafana/tempo:latest` tag: Do not use for pinned stacks — it silently upgrades to 3.0.x.
- `otel/opentelemetry-collector:latest` (non-contrib): Lacks `prometheus` exporter required for this stack.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `AdditionalFiles` in `Directory.Build.props` can resolve relative-to-repo-root paths for the allow-list file | PII Lint Gate | Allow-list not loaded → analyzer errors on all intentional exceptions or skips denylist check |
| A2 | `PrivateAssets` semantics on `ProjectReference` with `OutputItemType="Analyzer"` match `PackageReference` behavior (analyzer DLL not included in packed NuGet) | AddGameKitObservability() Pattern | OTel SDK flows to all GameKit.Core consumers, violating OBS-01 |
| A3 | `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 1.1.4 is compatible with `Microsoft.CodeAnalysis.CSharp` 4.13.0 (already in GameKit.Build) | Standard Stack | Test verifier may require a different Roslyn version; version conflict in test project |
| A4 | Reflection-based enforcement test (compare const values at runtime) is sufficient to satisfy criterion #4 "single source of truth" | GameKitTelemetry Constants | A magic-string duplicate that happens to match the const value at runtime would pass; source-scan would catch it |
| A5 | Hand-authoring minimal Grafana 13 dashboard JSON (~100 lines each) is feasible without a running Grafana instance | Self-Hosted Stack | Dashboard JSON may not import correctly; may need to start Grafana first and export |
| A6 | `grafana/tempo:2.6.1` container config format (single-binary mode with `-config.file`) works with the minimal `tempo.yaml` shown | Self-Hosted Stack | Tempo may fail to start; may need additional config sections |
| A7 | GK0002 severity (warning vs error) for non-literal tag keys is planner discretion | PII Lint Gate | If too strict (Error), any dynamic tag key breaks the build even for legitimate uses |
| A8 | The OTel packages already transitively present in the build graph (due to CVE-suppression pinning in `Directory.Packages.props`) are sufficient for `AddGameKitObservability()` without explicit `PackageReference` additions to `GameKit.Core.csproj` | AddGameKitObservability() Pattern | If not available at compile time in `GameKit.Core`, explicit PackageReference needed |

---

## Open Questions

1. **GK0002 severity for non-literal keys**
   - What we know: the analyzer can't evaluate dynamic key expressions; we can warn
   - What's unclear: should it be `DiagnosticSeverity.Warning` (allows the build to pass) or `DiagnosticSeverity.Error` (forces all tags to be literal or const-foldable)?
   - Recommendation: `Warning` for non-literals initially; after the const migration is complete, upgrade to `Error`

2. **PackageReference strategy for OTel in GameKit.Core**
   - What we know: OTel 1.15.3 is already transitively pinned; `AddGameKitObservability()` needs `OpenTelemetry.Extensions.Hosting` to compile
   - What's unclear: whether transitive pins are sufficient for intellisense + compilation in `GameKit.Core.csproj`, or whether explicit `PrivateAssets="all"` PackageReferences are needed
   - Recommendation: add explicit `<PackageReference Include="OpenTelemetry.Extensions.Hosting" PrivateAssets="all" />` and `<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" PrivateAssets="all" />` to `GameKit.Core.csproj` — this makes the intent explicit without flowing the deps to consumers

3. **Tempo 2.6.1 minimal config syntax**
   - What we know: Tempo 2.x uses a YAML config file
   - What's unclear: whether the minimal config shown above is sufficient for Tempo to start in single-binary mode
   - Recommendation: test-validate the compose stack in Wave 1 before the Grafana provisioning tasks; a failing Tempo start breaks the criterion #3 verification

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker + Compose | OBS-08 compose stack | ✓ | Docker 29.5.3, Compose v5.1.4 | — |
| .NET SDK 10.0 | All C# compilation | ✓ | 10.0.109 | — |
| Host port 5433 | Sample Postgres | [ASSUMED] | — | Change to another free port |
| Host port 4317 | OTLP Collector | [ASSUMED] | — | Change collector mapping |
| Host port 3000 | Grafana UI | [ASSUMED] | — | Change port mapping |

**Missing dependencies with no fallback:** none
**Missing dependencies with fallback:** host port conflicts (5433, 4317, 3000) are low-risk; alternatives are trivial config changes.

---

## Validation Architecture

*nyquist_validation is enabled in `.planning/config.json`.*

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 (existing) |
| Config file | None (inherits repo-wide `dotnet test`) |
| Quick run command | `dotnet test tests/GameKit.Build.Tests/ --no-build -x` |
| Full suite command | `dotnet test tests/ --no-build` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| OBS-07 (criterion #1) | PII literal key blocked by analyzer | Analyzer unit | `dotnet test tests/GameKit.Build.Tests/ -x` | ❌ Wave 0 |
| OBS-07 (criterion #1) | Clean attribute key passes analyzer | Analyzer unit | `dotnet test tests/GameKit.Build.Tests/ -x` | ❌ Wave 0 |
| OBS-07 (criterion #1) | Allow-listed key passes analyzer | Analyzer unit | `dotnet test tests/GameKit.Build.Tests/ -x` | ❌ Wave 0 |
| OBS-07 (criterion #1) | Token-split false-positive prevention (`recipient.count`) | Analyzer unit | `dotnet test tests/GameKit.Build.Tests/ -x` | ❌ Wave 0 |
| OBS-07 (criterion #1) | camelCase PII key blocked (`playerCount`) | Analyzer unit | `dotnet test tests/GameKit.Build.Tests/ -x` | ❌ Wave 0 |
| OBS-01/02 (criterion #2) | `AddGameKitObservability()` compiles and registers sources | Unit (smoke) | `dotnet test tests/GameKit.Core.Tests/ -x` | ❌ Wave 0 |
| OBS-02 (criterion #4) | `GameKitTelemetry` constants referenced by per-package Telemetry classes | Unit (reflection) | `dotnet test tests/GameKit.Core.Tests/ -x` | ❌ Wave 0 |
| OBS-02 (criterion #5) | `RankingsActivitySource.SourceName` equals `GameKitTelemetry.RankingsTickerSourceName` | Unit (reflection) | `dotnet test tests/GameKit.Rankings.Tests/ -x` | ❌ Wave 0 |
| OBS-08 (criterion #3) | `curl http://localhost:9090` connection refused | Integration (manual) | `docker compose -f docker-compose.yml -f docker-compose.observability.yml up -d && curl -f http://localhost:9090 ; echo "exit=$?"` — expect non-zero | ❌ Wave 0 (manual) |
| OBS-08 (criterion #3) | Grafana reachable at :3000 | Integration (smoke) | `curl -f http://localhost:3000/api/health` | ❌ Wave 0 (manual) |

### Sampling Rate
- **Per task commit:** `dotnet test tests/GameKit.Build.Tests/ --no-build -x` (analyzer tests only — fast, < 5s)
- **Per wave merge:** `dotnet test tests/ --no-build` (full suite)
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `tests/GameKit.Build.Tests/GameKit.Build.Tests.csproj` — new project, covers OBS-07 analyzer tests
- [ ] `tests/GameKit.Build.Tests/PiiAttributeAnalyzerTests.cs` — all analyzer test cases
- [ ] Framework install: `dotnet add package Microsoft.CodeAnalysis.CSharp.Analyzer.Testing --version 1.1.4` + `...XUnit --version 1.1.2`
- [ ] `tests/GameKit.Core.Tests/Telemetry/GameKitTelemetryConstantsTests.cs` — covers criterion #4 enforcement
- [ ] `tests/GameKit.Rankings.Tests/Telemetry/RankingsActivitySourceTests.cs` — covers criterion #5

---

## Security Domain

`security_enforcement` is not explicitly set to `false` in `.planning/config.json`. Included per policy.

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | Not in scope for this phase |
| V3 Session Management | no | Not in scope |
| V4 Access Control | no | Not in scope |
| V5 Input Validation | yes (span attribute keys) | Roslyn analyzer (GK0001/GK0002) validates keys at build time; no runtime input to validate |
| V6 Cryptography | no | Not in scope |
| V10 Malicious Code | yes (partial) | Analyzer inspects ALL attribute keys in src/ before any code ships — prevents PII leakage |

### Known Threat Patterns for this Stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| PII leakage via span attributes (player_id, email in trace) | Information Disclosure | Roslyn PII analyzer (GK0001) — blocks at build time |
| OTLP endpoint unauthenticated push (localhost:4317) | Spoofing/Tampering | Dev-only stack; no mTLS needed in sample; document for production |
| Prometheus metrics exposure via host port | Information Disclosure | Do not publish Prometheus port — criterion #3 enforces this |
| Docker image supply-chain (pinned vs latest) | Tampering | Pin all image tags explicitly; note in compose file comments |

---

## Sources

### Primary (HIGH confidence)
- `src/GameKit.Build/GameKit.Build.csproj` — confirmed `IsRoslynComponent=true`, `netstandard2.0`, `ManagePackageVersionsCentrally=false`, `Microsoft.CodeAnalysis.CSharp 4.13.0` [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` — canonical ActivitySource/typed-helper pattern with `SourceName`, `Source`, typed `Start*Activity` methods [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` — canonical Meter pattern with `MeterName`, `MeterVersion`, `Meter` instance [VERIFIED: codebase]
- `src/GameKit.Rankings/Services/RankingsTickerService.cs` — inline `_activitySource` on lines 59-60 confirmed for extraction [VERIFIED: codebase]
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — all camelCase SetTag callsites confirmed [VERIFIED: codebase grep]
- `Directory.Packages.props` — OTel 1.15.3 already pinned; Docker and .NET versions confirmed [VERIFIED: codebase]
- `docker-compose.yml` (repo root) — Postgres 17.9, Redis 8.6.2 image references confirmed [VERIFIED: codebase]

### Secondary (MEDIUM confidence)
- [opentelemetry.io extending-the-sdk README](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/trace/extending-the-sdk/README.md) — library AddSource extension method pattern; "if not provided, document the ActivitySource name"
- [grafana.com/grafana/download](https://grafana.com/grafana/download) — Grafana 13.0.2 latest stable as of 2026-06-14
- [github.com/grafana/tempo/releases](https://github.com/grafana/tempo/releases) — Tempo 2.6.1 latest stable; 3.0.x breaking changes confirmed
- [github.com/prometheus/prometheus/releases](https://github.com/prometheus/prometheus/releases) — Prometheus v3.11.2 latest stable
- [github.com/open-telemetry/opentelemetry-collector-contrib/releases](https://github.com/open-telemetry/opentelemetry-collector-contrib/releases) — v0.154.0 latest stable
- [docker.com/compose/how-tos/networking](https://docs.docker.com/compose/how-tos/networking/) — confirmed bridge network isolates containers; no host binding without `ports:` key
- [grafana.com/blog/2021/04/20/grafana-loki-tempo-relicensing-to-agplv3](https://grafana.com/blog/2021/04/20/grafana-loki-tempo-relicensing-to-agplv3/) — AGPLv3 confirmed; no linking obligation for operator-pulled containers

### Tertiary (LOW confidence)
- [dotnet/roslyn-analyzers](https://github.com/dotnet/roslyn-analyzers) — `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` test harness pattern; version 1.1.4 via `dotnet package search` [ASSUMED: full API details not checked via Context7]

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all packages verified via codebase or `dotnet package search` against nuget.org
- Architecture (analyzer): HIGH — existing `GameKit.Build` project is the confirmed template; DiagnosticAnalyzer pattern is well-established
- Architecture (compose stack): MEDIUM-HIGH — compose config patterns confirmed via official Docker docs; specific image configs (Tempo minimal YAML) are ASSUMED
- Pitfalls: HIGH — pitfalls 1-3 are verified against the existing codebase; others are well-documented community patterns

**Research date:** 2026-06-14
**Valid until:** 2026-07-14 (30 days) — OTel image tags and Grafana version may advance; repin if planning is delayed more than 30 days
