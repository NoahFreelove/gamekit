# Phase 13: Observability Foundations - Pattern Map

**Mapped:** 2026-06-14
**Files analyzed:** 12 (7 new, 4 modified, 1 new project)
**Analogs found:** 12 / 12

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` | constants / utility | n/a (static consts) | `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` + `MatchmakingActivitySource.cs` | exact |
| `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` | builder extension | request-response | `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` | exact |
| `src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs` | telemetry utility | event-driven | `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` | exact |
| `src/GameKit.Build/PiiAttributeAnalyzer.cs` | Roslyn DiagnosticAnalyzer | n/a (compile-time) | `src/GameKit.Build/GameKitVersionGenerator.cs` | role-match (same project, same wiring, different Roslyn base class) |
| `src/GameKit.Build/pii-allowlist.txt` | config / data file | n/a | none (first of kind) | no analog |
| `tests/GameKit.Build.Tests/GameKit.Build.Tests.csproj` | test project config | n/a | `tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj` | exact |
| `tests/GameKit.Build.Tests/PiiAttributeAnalyzerTests.cs` | test | unit | `tests/GameKit.Core.Tests/Builder/GameKitApplicationBuilderExtensionsTests.cs` | role-match |
| `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` | MODIFY | event-driven | self (camelCase tag rename to dotted) | n/a |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | MODIFY | event-driven | self (SetTag key renames per D-03) | n/a |
| `samples/TicTacToeDuel/docker-compose.yml` | infra config | n/a | `docker-compose.yml` (repo root) | exact |
| `samples/TicTacToeDuel/docker-compose.observability.yml` | infra config | n/a | `docker-compose.yml` (repo root) | role-match |
| `samples/TicTacToeDuel/observability/*` | infra config | n/a | none (first of kind) | no analog (use RESEARCH.md examples) |

---

## Pattern Assignments

### `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` (constants utility)

**Analog:** `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` (lines 1–62) + `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` (lines 1–80)

**File header + namespace pattern** (MatchmakingMeter.cs lines 1–6):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Diagnostics.Metrics;

namespace GameKit.Matchmaking.Telemetry;
```

**Deviation for GameKitTelemetry:** namespace is `GameKit.Core.Telemetry`; no `using` statements needed (only string consts). Class is `public static` not `internal static`.

**MeterName / MeterVersion const pattern** (MatchmakingMeter.cs lines 37–40):
```csharp
/// <summary>The Matchmaking meter name. Operators must register <c>AddMeter</c> with this exact value.</summary>
public const string MeterName = "GameKit.Matchmaking";

/// <summary>The meter version, pinned to <c>1.0.0</c> for v1 wire compatibility.</summary>
public const string MeterVersion = "1.0.0";
```

**SourceName const pattern** (MatchmakingActivitySource.cs lines 34–37):
```csharp
/// <summary>
/// The OpenTelemetry source name. Operators MUST register
/// <c>AddSource("GameKit.Matchmaking.Ticker")</c> in their OTel SDK setup to subscribe.
/// </summary>
public const string SourceName = "GameKit.Matchmaking.Ticker";
```

**Operator-action-required XML doc pattern** (MatchmakingActivitySource.cs lines 16–23 — the `<remarks>` block pattern):
```csharp
/// <remarks>
/// <para>
/// <b>Operator action required (Pitfall §7):</b> spans emitted via this source are no-ops
/// unless the host registers
/// <c>AddSource("GameKit.Matchmaking.Ticker")</c> in its OpenTelemetry SDK setup. ...
/// </para>
/// </remarks>
```

**What GameKitTelemetry adds beyond the analogs:** a single class collects all `SourceName` / `MeterName` values from all packages as `const`s plus the shared `Version = "1.0.0"` and the D-04 attribute key constants (`AttrLadderId`, `AttrPoolName`, etc.). Per-package classes then reference these via `= GameKitTelemetry.MatchmakingTickerSourceName` instead of duplicating the string literal.

---

### `src/GameKit.Core/Builder/GameKitObservabilityBuilderExtensions.cs` (builder extension)

**Analog:** `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` (lines 1–133)

**Imports + namespace pattern** (MatchmakingBuilderExtensions.cs lines 1–12):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Builder;
// ...
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Matchmaking.Builder;
```

**Deviation for GameKitObservabilityBuilderExtensions:** namespace is `GameKit.Core.Builder`. Additional usings needed: `OpenTelemetry`, `OpenTelemetry.Metrics`, `OpenTelemetry.Trace`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` (for `AddOtlpExporter`), `GameKit.Core.Telemetry`.

**Extension method on IGameKitBuilder pattern** (MatchmakingBuilderExtensions.cs lines 61–64):
```csharp
public static IGameKitMatchmakingBuilder AddMatchmaking(
    this IGameKitBuilder builder,
    Action<GameKitMatchmakingOptions>? configure = null)
{
    ArgumentNullException.ThrowIfNull(builder);
    // ...
    return matchmakingBuilder;
}
```

**Deviation:** `AddGameKitObservability` returns `IGameKitBuilder` (not a sub-builder), uses `Action<GameKitObservabilityOptions>? configure = null`, and the body calls `builder.Services.AddOpenTelemetry().WithTracing(...).WithMetrics(...)`.

**Options class pattern** — copy the inline options class pattern from any existing builder (MatchmakingBuilderExtensions uses `GameKitMatchmakingOptions` as a separate file; for the observability extension a co-located `GameKitObservabilityOptions` sealed class is appropriate given its small surface).

**OTel PackageReference strategy (Pitfall §7):** Add `<PackageReference Include="OpenTelemetry.Extensions.Hosting" PrivateAssets="all" />` and `<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" PrivateAssets="all" />` to `src/GameKit.Core/GameKit.Core.csproj` — `PrivateAssets="all"` prevents the OTel SDK from flowing to consumers who skip `AddGameKitObservability()`.

---

### `src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs` (telemetry utility)

**Analog:** `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` (lines 1–80) — copy this file almost verbatim.

**Full structure to mirror** (MatchmakingActivitySource.cs lines 31–80):
```csharp
public static class MatchmakingActivitySource
{
    public const string SourceName = "GameKit.Matchmaking.Ticker";

    internal static readonly ActivitySource Source = new(SourceName, "1.0.0");

    public static Activity? StartTickActivity() => Source.StartActivity("Tick");

    public static Activity? StartPoolActivity(Guid ladderId, string poolName)
    {
        var activity = Source.StartActivity("PoolSweep");
        if (activity is not null)
        {
            activity.SetTag("ladderId", ladderId.ToString());
            activity.SetTag("poolName", poolName);
        }
        return activity;
    }

    public static Activity? StartProposalSweepActivity() => Source.StartActivity("ProposalSweep");
}
```

**Deviations for RankingsActivitySource:**
1. Class name: `RankingsActivitySource`, namespace `GameKit.Rankings.Telemetry`.
2. `SourceName = GameKitTelemetry.RankingsTickerSourceName` (references Core constant — satisfies D-02/criterion #4).
3. `Source = new(SourceName, GameKitTelemetry.Version)` (version via Core constant).
4. Single typed helper: `StartDrainLadderActivity()` (the only span in RankingsTickerService — maps to the `_activitySource.StartActivity("DrainLadder")` call on line 213 of RankingsTickerService.cs).
5. No `SetTag` calls inside the factory — the tags (`ladder.id`, `ladder.name`, `result`, `error.type`) are applied by the caller in RankingsTickerService; this matches the current MatchmakingActivitySource pattern for `StartPoolActivity` which sets tags inline.

**Extraction site in RankingsTickerService.cs:**
- Remove: lines 59–60 (`private static readonly ActivitySource _activitySource = new("GameKit.Rankings.Ticker", "1.0.0");`)
- Replace all `_activitySource.StartActivity(...)` with `RankingsActivitySource.Source.StartActivity(...)` OR the typed `RankingsActivitySource.StartDrainLadderActivity()` helper.
- Add `using GameKit.Rankings.Telemetry;` to the file's using block.

---

### `src/GameKit.Build/PiiAttributeAnalyzer.cs` (Roslyn DiagnosticAnalyzer)

**Analog:** `src/GameKit.Build/GameKitVersionGenerator.cs` (lines 1–75) — same project, same file header, same namespace.

**File header + namespace pattern** (GameKitVersionGenerator.cs lines 1–7):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Microsoft.CodeAnalysis;

namespace GameKit.Build;
```

**Deviation for PiiAttributeAnalyzer:** additional usings — `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.CSharp.Syntax`, `Microsoft.CodeAnalysis.Diagnostics`, `System.Collections.Immutable`, `System.Linq`.

**Class-level attribute pattern** (GameKitVersionGenerator.cs line 27–28):
```csharp
[Generator]
public sealed class GameKitVersionGenerator : IIncrementalGenerator
```

**Deviation:** `[DiagnosticAnalyzer(LanguageNames.CSharp)]` attribute, `public sealed class PiiAttributeAnalyzer : DiagnosticAnalyzer`.

**Project wiring (no csproj changes needed):** The `GameKit.Build.csproj` already has `IsRoslynComponent=true`, `netstandard2.0`, `EnforceExtendedAnalyzerRules=true`, `ManagePackageVersionsCentrally=false`, `IsPackable=false`. The new file drops into the same project without any `.csproj` modification. All `src/GameKit.*.csproj` files already wire `GameKit.Build` via `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` — the PII analyzer participates automatically once its class is compiled into `GameKit.Build.dll`.

**Allow-list wiring in Directory.Build.props:** add one `<AdditionalFiles>` entry pointing to `src/GameKit.Build/pii-allowlist.txt` so the file is passed to every analyzer invocation across all `src/` projects.

---

### `tests/GameKit.Build.Tests/GameKit.Build.Tests.csproj` (test project)

**Analog:** `tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj` (full file)

**Structure to copy:**
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>GameKit.Rankings.Tests</RootNamespace>
    <AssemblyName>GameKit.Rankings.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
    <WarningsAsErrors />
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Moq" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\GameKit.Rankings\GameKit.Rankings.csproj" />
    <ProjectReference Include="..\GameKit.TestFixtures\GameKit.TestFixtures.csproj" />
  </ItemGroup>
</Project>
```

**Deviations for GameKit.Build.Tests:**
1. `RootNamespace` / `AssemblyName` → `GameKit.Build.Tests`.
2. No `FrameworkReference Include="Microsoft.AspNetCore.App"` — analyzer tests do not need the ASP.NET Core shared framework.
3. No `Moq` or `Microsoft.EntityFrameworkCore.InMemory` — not needed.
4. No `GameKit.TestFixtures` reference.
5. `ProjectReference` to `src/GameKit.Build/GameKit.Build.csproj` as a **plain** reference — **NO** `OutputItemType="Analyzer"` (Pitfall §3 from RESEARCH.md: referencing as an analyzer would make it run against the test code, causing circular problems).
6. Additional `PackageReference` entries for `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` (1.1.4) and `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing.XUnit` (1.1.2) — these opt OUT of CPM (`ManagePackageVersionsCentrally=false` is NOT needed for the test project; versions must be added to `Directory.Packages.props`).
7. The `tests/Directory.Build.props` already provides `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector` — no need to repeat them.

---

### `tests/GameKit.Build.Tests/PiiAttributeAnalyzerTests.cs` (analyzer unit tests)

**Analog:** `tests/GameKit.Core.Tests/Builder/GameKitApplicationBuilderExtensionsTests.cs` (lines 1–40)

**File header + namespace + using pattern** (lines 1–10):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Reflection;
using GameKit.Core.Builder;
using Xunit;

namespace GameKit.Core.Tests.Builder;
```

**Deviation for PiiAttributeAnalyzerTests:** namespace `GameKit.Build.Tests`; usings swap in `Microsoft.CodeAnalysis.Testing`, `Microsoft.CodeAnalysis.CSharp.Testing`, `Microsoft.CodeAnalysis.CSharp.Testing.XUnit`, `GameKit.Build`. Replace `[Fact]` method body pattern with `CSharpAnalyzerVerifier<PiiAttributeAnalyzer>.VerifyAnalyzerAsync(source, expectedDiagnostics)` calls (see RESEARCH.md §Testing the Analyzer for the exact verifier pattern).

**Test class pattern** (lines 13–15):
```csharp
public class GameKitApplicationBuilderExtensionsTests
{
    [Fact]
    public void SomeName_Does_Something()
    {
```

**Deviation:** async test methods (`public async Task`), not sync `void` — `VerifyAnalyzerAsync` returns `Task`.

---

### MODIFY `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs`

**Current state** (lines 37, 44, 67–69):
```csharp
public const string SourceName = "GameKit.Matchmaking.Ticker";
internal static readonly ActivitySource Source = new(SourceName, "1.0.0");
// ...
activity.SetTag("ladderId", ladderId.ToString());
activity.SetTag("poolName", poolName);
```

**Required changes (D-03):**
1. `SourceName` stays as-is (it's the source *name*, not an attribute key — PascalCase is correct per D-01).
2. `new(SourceName, "1.0.0")` → `new(SourceName, GameKitTelemetry.Version)` — use Core constant (add `using GameKit.Core.Telemetry;`).
3. `SetTag("ladderId", ...)` → `SetTag(GameKitTelemetry.AttrLadderId, ...)` — i.e., `"ladder.id"`.
4. `SetTag("poolName", ...)` → `SetTag(GameKitTelemetry.AttrPoolName, ...)` — i.e., `"pool.name"`.
5. Update the `<remarks>` doc comment to reflect the new tag names (`ladder.id`, `pool.name`).

---

### MODIFY `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs`

**SetTag callsites to rename (D-03) — located via grep:**

| Line | Old key | New key | Constant to use |
|------|---------|---------|-----------------|
| 209 | `"paused"` | `"paused"` | no change (single word) |
| 261 | `"reaped"` | `"reaped"` | no change (single word) |
| 362 | `"candidatesEvaluated"` | `"candidates.evaluated"` | inline string or new `GameKitTelemetry.AttrCandidatesEvaluated` (Phase 15) |
| 395 | `"phase.hashFanoutMs"` | `"phase.hash_fanout_ms"` | inline string |
| 434 | `"budgetBail"` | `"budget.bail"` | inline string |
| 439 | `"matchCapBail"` | `"match_cap.bail"` | inline string |
| 496 | `"matchesFormed"` | `"matches.formed"` | inline string or new const |
| 497 | `"phase.matchLoopMs"` | `"phase.match_loop_ms"` | inline string |
| 498 | `"phase.totalMs"` | `"phase.total_ms"` | inline string |

The D-04 allow-list constants (`AttrLadderId`, `AttrPoolName`, etc.) cover the cross-package keys. The matchmaking-specific keys (`candidates.evaluated`, `matches.formed`, etc.) are written as inline string literals for Phase 13; Phase 15 will promote them to `GameKitTelemetry` constants when the full instrumentation lands.

---

### `samples/TicTacToeDuel/docker-compose.yml` (base compose)

**Analog:** `docker-compose.yml` (repo root, lines 1–58) — copy service shape exactly.

**Service shape to copy** (root docker-compose.yml lines 9–55):
```yaml
services:
  postgres:
    image: postgres:17.9
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres_bootstrap_dev_only
      POSTGRES_DB: postgres
    volumes:
      - gamekit-postgres-data:/var/lib/postgresql/data
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d gamekit"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s
    shm_size: "256mb"
  redis:
    image: redis:8.6.2
    command:
      - "redis-server"
      - "--appendonly"
      - "yes"
      - "--appendfsync"
      - "everysec"
      - "--maxmemory-policy"
      - "noeviction"
      - "--save"
      - "3600 1 300 100 60 10000"
    volumes:
      - gamekit-redis-data:/data
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 3s
      retries: 5
volumes:
  gamekit-postgres-data:
  gamekit-redis-data:
```

**Deviations for the sample base compose:**
1. Postgres `ports` → `"5433:5432"` (D-10: host `:5432` owned by developer's local Postgres — confirmed in project memory).
2. Remove `container_name` and `restart: unless-stopped` — sample-local compose does not need named containers or restart policy.
3. Remove `shm_size: "256mb"` — fine for dev sample.
4. Remove `./docker/postgres/init` volume mount — sample has no init scripts.
5. Volume names: `tictactoe-postgres-data` / `tictactoe-redis-data` to avoid collision with the repo-root dev stack.
6. Redis: omit `--save` args for simplicity in sample.

---

### `samples/TicTacToeDuel/docker-compose.observability.yml` (overlay compose)

**Analog:** `docker-compose.yml` (repo root) — borrow the structural conventions (YAML comments, `services:` block, `volumes:` block).

**Key deviations (no direct analog — use RESEARCH.md §Self-Hosted Observability Stack verbatim):**
- Four new services: `otel-collector`, `prometheus`, `tempo`, `grafana`.
- `prometheus` has NO `ports:` key (criterion #3 isolation).
- `otel-collector` publishes ONLY `:4317` (OTLP gRPC for host app).
- `grafana` publishes `:3000` for browser access.
- All four services on a `obs-internal` bridge network with no `external: true`.
- Pinned image versions: `otel/opentelemetry-collector-contrib:0.154.0`, `prom/prometheus:v3.11.2`, `grafana/grafana:13.0.2`, `grafana/tempo:2.6.1`.

---

### `samples/TicTacToeDuel/observability/*` (OTel Collector + Prometheus + Grafana + Tempo config)

**No analog in codebase.** Use RESEARCH.md §Self-Hosted Observability Stack code examples verbatim:
- `otel-collector-config.yml` — OTLP receiver on `:4317`/`:4318`, Prometheus exporter on `0.0.0.0:8889` (internal), OTLP exporter to `tempo:4317`.
- `prometheus.yml` — `scrape_interval: 15s`, single job `gamekit` scraping `otel-collector:8889`.
- `tempo.yaml` — minimal single-binary Tempo config with local block storage.
- `grafana/provisioning/datasources/datasources.yml` — Prometheus datasource (`http://prometheus:9090`, isDefault) + Tempo datasource (`http://tempo:3200`).
- `grafana/provisioning/dashboards/dashboards.yml` — file provider pointing to `/var/lib/grafana/dashboards`.
- `grafana/dashboards/matchmaking-queue-depth.json` + `ticker-health.json` — hand-authored minimal Grafana 13 JSON (~100 lines each).

---

## Shared Patterns

### License Header
**Source:** Every `src/` file in the repo, e.g., `src/GameKit.Build/GameKitVersionGenerator.cs` lines 1–2
**Apply to:** All new `.cs` files
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### Operator-Action-Required XML Doc Remarks
**Source:** `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` lines 16–29; `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` lines 22–27
**Apply to:** `RankingsActivitySource.cs`, `GameKitTelemetry.cs`, `GameKitObservabilityBuilderExtensions.cs`

Pattern: every telemetry class's class-level `<remarks>` block must include a `<b>Operator action required:</b>` paragraph explaining which `AddSource(...)` or `AddMeter(...)` call the host needs, and that without it instruments are no-ops.

### GameKit.Build Analyzer Wiring
**Source:** `src/GameKit.Core/GameKit.Core.csproj` lines 37–46
**Apply to:** All existing `src/GameKit.*.csproj` files already carry this — no changes needed; new `PiiAttributeAnalyzer.cs` is automatically included by virtue of being in `GameKit.Build`.

```xml
<ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

### `ArgumentNullException.ThrowIfNull` guard
**Source:** `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` line 65; `src/GameKit.Core/Builder/GameKitServiceCollectionExtensions.cs` line 32
**Apply to:** `GameKitObservabilityBuilderExtensions.cs` — guard the `builder` parameter.

### Test Project Inherits from tests/Directory.Build.props
**Source:** `tests/Directory.Build.props` — provides `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector` to every test project.
**Apply to:** `tests/GameKit.Build.Tests/GameKit.Build.Tests.csproj` — do NOT duplicate these packages.

---

## No Analog Found

| File | Role | Data Flow | Reason |
|------|------|-----------|--------|
| `src/GameKit.Build/pii-allowlist.txt` | config data | n/a | First AdditionalFiles text allow-list in the repo; format is plain text (one key per line) |
| `samples/TicTacToeDuel/observability/otel-collector-config.yml` | infra config | n/a | No OTel Collector config exists in the repo yet |
| `samples/TicTacToeDuel/observability/prometheus.yml` | infra config | n/a | No Prometheus config exists in the repo yet |
| `samples/TicTacToeDuel/observability/tempo.yaml` | infra config | n/a | No Tempo config exists in the repo yet |
| `samples/TicTacToeDuel/observability/grafana/provisioning/datasources/datasources.yml` | infra config | n/a | No Grafana provisioning exists in the repo yet |
| `samples/TicTacToeDuel/observability/grafana/dashboards/*.json` | dashboard | n/a | No Grafana dashboard JSON exists in the repo yet |

For all "no analog" files, use RESEARCH.md §Self-Hosted Observability Stack code examples as the primary template.

---

## Metadata

**Analog search scope:** `src/GameKit.Build/`, `src/GameKit.Core/Builder/`, `src/GameKit.Matchmaking/Telemetry/`, `src/GameKit.Matchmaking/Builder/`, `src/GameKit.Rankings/Services/`, `tests/GameKit.Rankings.Tests/`, `tests/GameKit.Core.Tests/`, `docker-compose.yml` (repo root)
**Files read:** 14
**Pattern extraction date:** 2026-06-14

---

## PATTERN MAPPING COMPLETE

**Phase:** 13 - observability-foundations
**Files classified:** 12
**Analogs found:** 9 / 12 (3 infra config files have no analog — use RESEARCH.md)

### Coverage
- Files with exact analog: 5 (`GameKitTelemetry.cs`, `RankingsActivitySource.cs`, `GameKitObservabilityBuilderExtensions.cs`, `GameKit.Build.Tests.csproj`, `docker-compose.yml`)
- Files with role-match analog: 4 (`PiiAttributeAnalyzer.cs`, `PiiAttributeAnalyzerTests.cs`, `docker-compose.observability.yml`, both MODIFY targets)
- Files with no analog: 3 (OTel Collector config, Prometheus config, Grafana provisioning/dashboards — all use RESEARCH.md verbatim)

### Key Patterns Identified
- All telemetry classes follow the static class + `SourceName`/`MeterName` const + `internal static readonly` instance + typed `Start*Activity()` helpers shape from `MatchmakingActivitySource` / `MatchmakingMeter`.
- `GameKitTelemetry` is the aggregator class: per-package `SourceName` consts reference it (e.g., `= GameKitTelemetry.RankingsTickerSourceName`), not duplicate string literals.
- `GameKitObservabilityBuilderExtensions.AddGameKitObservability()` follows the `this IGameKitBuilder builder, Action<TOptions>? configure = null` → `ArgumentNullException.ThrowIfNull(builder)` → body → `return builder` shape used by all sibling builder extensions.
- `PiiAttributeAnalyzer` drops into the existing `GameKit.Build` project unchanged — `IsRoslynComponent=true`, `netstandard2.0`, `ManagePackageVersionsCentrally=false`; zero new `.csproj` files in `src/`.
- Sample compose pair mirrors the repo-root `docker-compose.yml` service/healthcheck/volume shape with Postgres remapped to `:5433`.

### File Created
`/home/noah/Desktop/projects/gamekit/.planning/phases/13-observability-foundations/13-PATTERNS.md`

### Ready for Planning
Pattern mapping complete. Planner can now reference analog patterns in PLAN.md files.
