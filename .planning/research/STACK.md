# Stack Research

**Domain:** .NET game-services library — v2.1 operability + hardening additions only
**Researched:** 2026-06-08
**Confidence:** HIGH (all NuGet versions verified on nuget.org; licenses verified from official repos/pages)

---

## Scope

This document covers ONLY the new tools and NuGet dependencies required for v2.1 features.
The pinned v1.0/v2.0 stack (EF Core 10.0.6, Npgsql 10.0.1, StackExchange.Redis 2.8.41,
FluentValidation 12.1.1, Scrutor 7, Polly 8, MinVer 7, xUnit + Testcontainers 4.11,
MudBlazor 9.3.0, BCrypt.Net-Next, Isopoh Argon2, all aspnet-contrib / Google / SignalR
packages from v2.0, etc.) is unchanged — do not re-pin or re-research any prior dependency.

---

## 1. Observability — OTel Packages (NuGet)

### 1.1 Core OTel SDK + OTLP Exporter (opt-in, SDK-level additions)

Core already declares `ActivitySource`/`Meter` primitives plus the API-level packages
(`OpenTelemetry` 1.10.x, `OpenTelemetry.Extensions.Hosting` 1.10.x) as opt-in seams.
v2.1 promotes those to the SDK + OTLP exporter and adds per-package instrumentation.

| Package | Version | License | net10.0 TFM | Purpose |
|---------|---------|---------|-------------|---------|
| `OpenTelemetry` | **1.15.3** | Apache-2.0 | ✅ (ships net8.0+net10.0+netstandard2.0) | Core SDK; reference implementation of OTel API |
| `OpenTelemetry.Extensions.Hosting` | **1.15.3** | Apache-2.0 | ✅ (ships net8.0+netstandard2.0; computed net10.0) | Hosting integration — `AddOpenTelemetry()` extension; lifecycle management |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | **1.15.3** | Apache-2.0 | ✅ (ships net8.0+netstandard2.0; computed net10.0) | OTLP exporter — sends traces + metrics to OTel Collector / Tempo / Prometheus |

All three are **stable GA releases** (released 2026-04-21). These go into `GameKit.Core`'s opt-in
`AddGameKitObservability()` extension method and are pulled only when the consumer opts in.

**Why OTLP over Prometheus-direct:** The Prometheus exporter package
(`OpenTelemetry.Exporter.Prometheus.AspNetCore`) is **1.15.3-beta.1 — pre-release** (as of
2026-04-21). The recommended production path is OTLP to an OTel Collector sidecar, which then
scrapes/forwards to Prometheus. This avoids a pre-release dependency in GameKit packages; the
Collector is an infrastructure concern in the sample's docker-compose, not a library dependency.

### 1.2 Instrumentation Packages

| Package | Version | License | net10.0 TFM | GA? | Where Used |
|---------|---------|---------|-------------|-----|------------|
| `OpenTelemetry.Instrumentation.AspNetCore` | **1.15.2** | Apache-2.0 | ✅ (ships net8.0+netstandard2.0; computed net10.0) | **YES — stable** | `GameKit.Core` opt-in extension; instruments HTTP server spans |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | **1.15.1-beta.1** | Apache-2.0 | ✅ (ships net8.0+net10.0+netstandard2.0) | **NO — beta** | Sample app only (not a GameKit package dep) |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` | **1.15.1-beta.2** | Apache-2.0 | ✅ (ships net8.0+net10.0+netstandard2.0) | **NO — beta** | Sample app only (not a GameKit package dep) |

**Critical:** EF Core and StackExchange.Redis instrumentation packages are **still beta** as of
2026-06-08 (released 2026-04-21 and 2026-05-27 respectively). They MUST NOT be hard dependencies
of any shipped `GameKit.*` NuGet package — include them only in
`samples/TicTacToeDuel` (composition-root, not shipped). The OTel semantic conventions for
these two are marked "Experimental" upstream, meaning breaking changes are possible.

`OpenTelemetry.Instrumentation.AspNetCore` 1.15.2 IS GA and can be a hard dependency of
the `GameKit.Core` opt-in observability extension method.

### 1.3 Self-Hosted Observability Backend (docker-compose — NOT NuGet deps)

These are infrastructure components for `samples/TicTacToeDuel/docker-compose.observability.yml`.
No NuGet pin needed; they are Docker images.

| Component | Docker Image | License | Role |
|-----------|-------------|---------|------|
| OpenTelemetry Collector | `otel/opentelemetry-collector-contrib:latest` | Apache-2.0 | Receives OTLP on :4317(gRPC)/:4318(HTTP); fans out to Prometheus + Tempo |
| Prometheus | `prom/prometheus:latest` | Apache-2.0 | Metrics scrape target; scraped by OTel Collector's Prometheus exporter |
| Grafana Tempo | `grafana/tempo:latest` | **AGPLv3** | Distributed trace storage; receives OTLP from Collector |
| Grafana | `grafana/grafana-oss:latest` | **AGPLv3** | Unified dashboard — queries Prometheus (metrics) + Tempo (traces) |

**AGPLv3 note on Grafana + Tempo:** Grafana and Tempo were relicensed from Apache-2.0 to
AGPLv3 in April 2021. AGPLv3 is OSI-approved and GPL-family compatible. The key AGPLv3
restriction — sharing source modifications when running the software as a network service —
applies to operators modifying and distributing Grafana/Tempo itself, **not** to GameKit as a
library or to GameKit operators who run unmodified Grafana/Tempo. For a self-hosted game backend
operator running unmodified `grafana/grafana-oss` and `grafana/tempo` via docker-compose, this is
fully acceptable. The sample docker-compose does not modify or redistribute these images.

**Jaeger as alternative to Tempo:** Jaeger (Apache-2.0, current v2.13.0/1.76.0) is a simpler
self-hosted trace backend with native OTLP ingestion since v1.35. It is Apache-2.0 (no AGPLv3
concerns) and supports an all-in-one docker image (`jaegertracing/all-in-one`). The tradeoff is
that Grafana does not have built-in Tempo-style tight integration with Jaeger's trace search; Tempo
is the preferred default for the Grafana LGTM stack. The sample docker-compose should document
Jaeger as an Apache-2.0 alternative.

**Stack data flow:**
```
GameKit app (OTLP gRPC :4317)
    → OTel Collector
        → Prometheus (metrics scrape)
        → Grafana Tempo (traces, OTLP push)
    → Grafana (dashboards: Prometheus + Tempo datasources)
```

---

## 2. Health & Readiness — NuGet Packages

### 2.1 Core Health Check Framework

`Microsoft.Extensions.Diagnostics.HealthChecks` (version **10.0.8**, released 2026-05-12,
MIT, net10.0 + netstandard2.0 + net462) is part of `Microsoft.AspNetCore.App`. Do NOT add it
as an explicit NuGet pin — it resolves from the shared framework. Use
`IHealthChecksBuilder.AddCheck<T>()` and `MapHealthChecks()` directly.

### 2.2 EF Core DbContext Health Check (Microsoft first-party)

| Package | Version | License | net10.0 TFM | Purpose |
|---------|---------|---------|-------------|---------|
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | **10.0.8** | MIT | ✅ (net10.0 primary + computed variants) | `AddDbContextCheck<TContext>()` — probes DB connectivity via EF Core DbContext; released 2026-05-12 |

This is the correct first-party package for the `GameKitDbContext` readiness probe. It checks
database reachability via the existing `DbContext` without pulling in any external dependency.
Pending-migration detection (`GetPendingMigrationsAsync`) requires a custom `IHealthCheck`
implementation (GameKit-authored, ~15 LOC) wrapping EF Core's existing API — no additional
NuGet package needed for that.

### 2.3 Community Health Check Packages (Xabaril / AspNetCore.Diagnostics.HealthChecks)

| Package | Version | License | net10.0 TFM | Purpose |
|---------|---------|---------|-------------|---------|
| `AspNetCore.HealthChecks.NpgSql` | **9.0.0** | Apache-2.0 | ✅ (computed net10.0 via net8.0+netstandard2.0 assets) | Raw Npgsql connection probe — useful for the **liveness** check (no EF overhead) |
| `AspNetCore.HealthChecks.Redis` | **9.0.0** | Apache-2.0 | ✅ (computed net10.0 via net8.0+netstandard2.0 assets) | Redis liveness probe: `PING` round-trip; uses `IConnectionMultiplexer` |

Both are from the Xabaril `AspNetCore.Diagnostics.HealthChecks` project (Apache-2.0; 2,400+
commits; actively maintained through .NET 8 and computed compatible with net10.0). Released
2024-12-19 at version 9.0.0 — the version number tracks the .NET target they were built for
(v9 = .NET 9). They run fine on net10.0 via computed compatibility.

**Design guidance — separate probes per package:**
- `/health/live` — liveness: basic process + memory check only (no external deps; never
  takes the process out of rotation due to Postgres/Redis blip)
- `/health/ready` — readiness: `AspNetCore.HealthChecks.NpgSql` + `AspNetCore.HealthChecks.Redis`
  + `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` DbContext probe + custom
  pending-migration check
- Startup filter: block traffic until all readiness probes pass on first boot

**Why NOT use only `AspNetCore.HealthChecks.NpgSql` for Postgres (without the EF package):**
The Npgsql package probes the connection string directly — useful for liveness. The EF package
probes through the same `GameKitDbContext` the application uses — useful for readiness because
it validates the connection pool is healthy in the same code path. Use both: Npgsql for liveness,
EF + migration check for readiness.

---

## 3. Load / Performance Testing

### 3.1 HTTP + SignalR Load Testing: k6 (Grafana)

**Tool:** k6 (CLI binary, not a NuGet package)
**Version:** v2.0.0 (released 2026-05-11)
**License:** **AGPLv3** (open-source; same AGPLv3 caveat as Grafana/Tempo above — applies to
those modifying and redistributing k6 itself, not to operators running it)
**Self-hosted:** Yes — runs as a local binary or in CI; no SaaS required; results can be written
to InfluxDB / Prometheus / stdout
**SignalR support:** Yes — k6 supports WebSocket natively; SignalR Core's WebSocket transport
is testable via k6's `ws.connect()` API (k6 does not speak the SignalR Hub Protocol natively
but the WebSocket framing is sufficient for load-testing connection density and fan-out)
**Why k6 over alternatives:** AGPL binary means no dependency license risk inside the GameKit
NuGet packages (k6 is a CLI tool, not a library dep); writes well-understood JavaScript
scenarios; has first-class Grafana integration for results visualization in the same
docker-compose observability stack; actively maintained by Grafana Labs.

**NBomber explicitly excluded:** NBomber v5+ requires a commercial license for organizational use
(FREE only for personal use). The license page explicitly states "You can't use FREE version for
an organization" and organizational licensing starts at $99/user/month. This is incompatible with
GameKit's GPL self-hosted ethos — a contributor or adopter who wants to run the load tests would
need a commercial license. NBomber v4 was Apache-2.0 but is no longer maintained as a current
release.

### 3.2 Micro-benchmarking: BenchmarkDotNet (NuGet)

For internal throughput measurement (matchmaking ticker cycle time, auth token issuance
latency, EF Core query performance), BenchmarkDotNet is the correct .NET-native tool.

| Package | Version | License | net10.0 TFM | Purpose |
|---------|---------|---------|-------------|---------|
| `BenchmarkDotNet` | **0.15.8** | MIT | ✅ (net8.0+net9.0; computed net10.0) | Microbenchmark harness — `[Benchmark]` methods, statistical analysis, memory diagnoser |

**0.15.8 released 2025-11-30.** MIT, .NET Foundation project. Used by .NET Runtime, EF Core,
and most serious .NET perf work. BenchmarkDotNet lives in a separate `benchmarks/` project
(never shipped as a NuGet package dep — dev tool only).

**k6 + BenchmarkDotNet together:** k6 covers end-to-end HTTP/WebSocket load (what operators
care about: RPS, p99 latency, connection fan-out). BenchmarkDotNet covers internal algorithmic
performance (what maintainers care about: Glicko-2 batch throughput, matchmaking tick overhead).
These are complementary, not redundant.

---

## 4. Documentation Generator: DocFX

**Tool:** DocFX (dotnet global tool, not a runtime NuGet dep)
**NuGet package:** `docfx` (global tool)
**Version:** **2.78.5** (released 2026-02-24)
**License:** MIT (.NET Foundation project)
**net10.0:** Explicit net10.0 target framework support added in 2.78.5 (release notes confirmed)
**Generates:** Static HTML from .NET XML doc comments + Markdown conceptual docs; outputs to
`_site/` folder suitable for any static hosting (GitHub Pages, nginx, Caddy, S3 — all self-hosted)
**Self-hosted:** Yes — fully static output; no server required beyond a static file server

**Why DocFX over alternatives:**
- **Statiq.Docs** (1.0.0-beta.17, last NuGet update 2024-01-09): Dual-licensed — the Statiq
  Framework is MIT but Statiq Web and Statiq Docs carry a custom "public license" that restricts
  commercial use to <10 employees and <$100K revenue. This is a GPL-incompatible commercial
  restriction for a widely-adopted library. **Excluded.**
- **Docusaurus** (Meta/Facebook, MIT): Excellent for conceptual docs but requires Node.js tooling;
  does not natively consume .NET XML doc comments (needs a plugin + manual wiring). Extra
  ecosystem dependency without benefit for a .NET library project.
- **MkDocs + Material** (MIT/MIT): Same Node.js/Python tooling concern; no native .NET XML
  comment ingestion. Good for conceptual docs only; does not solve API reference generation.
- **DocFX** is the only .NET-native, MIT-licensed, static-output tool that natively ingests both
  XML doc comments (API reference) and Markdown (conceptual docs) without external language runtimes.
  It is a .NET Foundation project, actively maintained, and now supports net10.0.

**Installation (dev tool, not a package dep):**
```bash
dotnet tool install -g docfx
```

DocFX is installed globally or as a local tool manifest entry in `docs/`; it is never added to
`Directory.Packages.props` or any `GameKit.*` `.csproj`.

---

## 5. Dependency / CVE Scanning

### 5.1 Built-in: NuGetAudit (SDK 8+, enhanced in .NET 10)

**No additional tool install required.** `NuGetAudit` has been built into the .NET SDK since
SDK 8.0.100 (NuGet 6.8). In .NET 10, `NuGetAuditMode` defaults to `all` — both direct AND
transitive dependencies are scanned against the GitHub Advisory Database on every `dotnet restore`.

Configuration in `Directory.Build.props`:
```xml
<PropertyGroup>
  <NuGetAudit>true</NuGetAudit>
  <NuGetAuditMode>all</NuGetAuditMode>   <!-- .NET 10 default; explicit for clarity -->
  <NuGetAuditLevel>moderate</NuGetAuditLevel>
</PropertyGroup>
```

**Offline note:** NuGetAudit fetches the advisory feed from NuGet.org during restore. It requires
internet connectivity for up-to-date data. For air-gapped CI: pre-cache the vulnerability feed
or supplement with Trivy (see below).

### 5.2 Container + SBOM Scanning: Trivy

**Tool:** Trivy (CLI binary)
**License:** Apache-2.0 (Aqua Security — open source, not dual-licensed)
**Offline:** Yes — downloads vulnerability DB once, caches locally; `trivy image --skip-update`
for air-gapped environments
**Scope for GameKit v2.1:** Scan the `samples/TicTacToeDuel` Docker image (which bundles the
app + all NuGet deps) for known CVEs in both .NET NuGet packages and OS-level packages in the
base image. Also generates CycloneDX/SPDX SBOM.
**Why Trivy over OWASP Dependency-Check:** Both are Apache-2.0 and self-hostable. Trivy is
preferred because: (a) it scans container images end-to-end (OS + language packages), covering
the full operator attack surface; (b) it is faster and simpler to invoke in CI; (c) it generates
machine-readable SBOM natively. OWASP Dependency-Check is excellent for .NET NuGet-only scans
and is a valid addition, but Trivy covers a strict superset.

**CI integration (not a NuGet dep):**
```bash
trivy image --severity HIGH,CRITICAL --exit-code 1 gamekit-sample:latest
```

### 5.3 Summary of Scanning Strategy

| Tool | What It Covers | License | Offline? |
|------|----------------|---------|---------|
| NuGetAudit (SDK built-in) | Direct + transitive NuGet CVEs at restore time | N/A (SDK) | Partial (cached feed) |
| Trivy | Container OS packages + NuGet + SBOM generation | Apache-2.0 | Yes |
| `dotnet list package --vulnerable` | Ad-hoc NuGet vuln check (subset of NuGetAudit) | N/A (SDK) | No |

OWASP Dependency-Check (Apache-2.0, self-hostable with local NVD cache) is documented as an
**optional supplement** in the ops guide but not a required CI step — NuGetAudit + Trivy cover
the same ground.

---

## 6. Backup / DR — No New NuGet Dependencies

Backup, restore, and DR for GameKit's Postgres + Redis infrastructure is an **operational
concern**, not a library concern. GameKit v2.1 delivers a DR runbook doc and sample scripts
in `docs/runbooks/`; no new NuGet packages are required.

| Tool | License | Purpose | Notes |
|------|---------|---------|-------|
| `pg_dump` / `pg_restore` | PostgreSQL License (MIT-like, open source) | Logical backup/restore of Postgres schemas and data | Ships with Postgres; invoke via `docker exec` or a cron `BackgroundService` trigger |
| `pg_basebackup` | PostgreSQL License | Physical base backup; works with WAL archiving for PITR | Ships with Postgres |
| pgBackRest (optional) | MIT | Full/differential/incremental backups + WAL archiving; S3/local storage | External tool; recommended in runbook for production; not a .NET dep |
| WAL-G (optional) | Apache-2.0 | WAL archiving to local storage / S3-compatible | External tool for PITR setups |
| Redis RDB snapshots | N/A (Redis config) | Point-in-time Redis state via `BGSAVE` / scheduled RDB | Config in `redis.conf` — `save 900 1`, `save 300 10` |
| Redis AOF | N/A (Redis config) | Append-only file for durable Redis persistence | Config: `appendonly yes`, `appendfsync everysec` |

**EF Core migration dry-run / rollback ergonomics:** No new NuGet package. Use
`dotnet ef migrations script --idempotent` (produces an idempotent SQL script reviewable before
execution) and `dotnet ef database update <PreviousMigration>` for rollback. Document the
per-package advisory lock key and `__ef_migrations_<pkg>` table naming pattern in the runbook.

---

## 7. Summary: New Additions to Directory.Packages.props

| Package | Version | Used By | License | GPL Compatible | net10.0 TFM |
|---------|---------|---------|---------|----------------|-------------|
| `OpenTelemetry` | `1.15.3` | `GameKit.Core` (opt-in) | Apache-2.0 | Yes | ✅ |
| `OpenTelemetry.Extensions.Hosting` | `1.15.3` | `GameKit.Core` (opt-in) | Apache-2.0 | Yes | ✅ |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.15.3` | `GameKit.Core` (opt-in) | Apache-2.0 | Yes | ✅ |
| `OpenTelemetry.Instrumentation.AspNetCore` | `1.15.2` | `GameKit.Core` (opt-in) | Apache-2.0 | Yes | ✅ |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | `1.15.1-beta.1` | **Sample app only** | Apache-2.0 | Yes | ✅ |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` | `1.15.1-beta.2` | **Sample app only** | Apache-2.0 | Yes | ✅ |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` | `10.0.8` | `GameKit.Core` | MIT | Yes | ✅ |
| `AspNetCore.HealthChecks.NpgSql` | `9.0.0` | `GameKit.Core` | Apache-2.0 | Yes | ✅ (computed) |
| `AspNetCore.HealthChecks.Redis` | `9.0.0` | `GameKit.Core` | Apache-2.0 | Yes | ✅ (computed) |
| `BenchmarkDotNet` | `0.15.8` | `benchmarks/` (dev only, not shipped) | MIT | Yes | ✅ (computed) |

**Packages that do NOT need a `Directory.Packages.props` entry:**
- `Microsoft.Extensions.Diagnostics.HealthChecks` — resolved from `Microsoft.AspNetCore.App` shared framework
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` — pre-release; **excluded from all GameKit packages**

---

## 8. What NOT to Add

| Package / Tool | Why Excluded | What to Do Instead |
|---------------|-------------|-------------------|
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` | Pre-release (1.15.3-beta.1); breaking changes possible before stable | Use OTLP exporter + OTel Collector sidecar to route metrics to Prometheus |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | Pre-release (1.15.1-beta.1); experimental semantic conventions | Sample app only; document as opt-in with "experimental" caveat |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` | Pre-release (1.15.1-beta.2); experimental semantic conventions | Sample app only; same caveat |
| **NBomber v5+** | Commercial license for org use ($99/user/month); "FREE only for personal use — cannot use for an organization" — incompatible with GPL self-hosted ethos and contributor experience | Use k6 (AGPL, CLI tool — no library license concern) |
| **Statiq.Docs** | Custom license restricting commercial use to <10 employees and <$100K revenue — GPL-incompatible commercial restriction | Use DocFX (MIT, .NET Foundation, net10.0 support confirmed in 2.78.5) |
| `Microsoft.Azure.SignalR` | Cloud-only managed service — hard GPL/self-hosted exclusion (v2.0 decision, unchanged) | Redis backplane already in place |
| Any AI/LLM SDK | Explicitly out of scope (PROJECT.md constraint) | — |
| Hangfire / Quartz.NET | Adds customer-DB tables (already decided in v1.0; unchanged) | `BackgroundService` + Polly |
| `Microsoft.Crank` | Primarily an ASP.NET team internal benchmarking orchestrator (not packaged for general use; requires agent infrastructure); no SignalR Hub Protocol support | k6 (HTTP/WebSocket) + BenchmarkDotNet (micro) |
| k6 Cloud / Grafana Cloud | SaaS — violates zero-cloud constraint | k6 CLI (self-hosted; results to Prometheus/InfluxDB) |
| Zipkin / `OpenTelemetry.Exporter.Zipkin` | Deprecated; OTel project will stop updates December 2026; Zipkin ecosystem is stagnant | OTLP exporter → OTel Collector |
| Datadog / Honeycomb / New Relic exporters | Commercial SaaS — hard excluded by GPL/self-hosted constraint | OTLP exporter → self-hosted OTel Collector + Grafana/Prometheus/Tempo |

---

## 9. Integration with Existing OTel Seams

The existing `GameKit.Core` OTel seam (v1.0 decision #8) declares `ActivitySource` and `Meter`
instances per-package using the established naming convention `GameKit.<Package>`. v2.1 wires
these to the full SDK as follows:

```csharp
// Consumer's Program.cs — opt-in
builder.Services.AddGameKit()
    // ... other GameKit modules ...
    .AddObservability(otel => otel
        .WithTracing()        // wires all GameKit ActivitySources
        .WithMetrics()        // wires all GameKit Meters
        .ExportToOtlp("http://otel-collector:4317"));
```

The `AddObservability` extension method lives in `GameKit.Core` and adds:
- `OpenTelemetry` SDK
- `OpenTelemetry.Extensions.Hosting` (lifecycle)
- `OpenTelemetry.Instrumentation.AspNetCore` (HTTP server spans — GA, safe as hard dep)
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` (OTLP push)

The beta instrumentation packages (EF Core, Redis) are registered in the **sample app's**
`Program.cs` with a comment marking them as "experimental — may have breaking changes."

The existing Admin health panel (Phase 3/3.1 — Postgres+Redis ping + Redis INCRBY error counter)
is NOT replaced — it remains the lightweight in-process health indicator. The new
`/health/live` and `/health/ready` endpoints are complementary, purpose-built for k8s/load-balancer
integration.

---

## 10. Alternatives Considered

| Feature | Chosen | Alternative | Why Not |
|---------|--------|-------------|---------|
| Trace backend (docker-compose) | Grafana Tempo (AGPLv3) | Jaeger (Apache-2.0) | Tempo is tighter Grafana integration; Jaeger documented as Apache-2.0 fallback in runbook |
| Metrics export path | OTLP → OTel Collector → Prometheus | `OpenTelemetry.Exporter.Prometheus.AspNetCore` | Prometheus exporter is still beta; OTLP+Collector is the recommended GA path |
| Load testing | k6 (AGPLv3) | NBomber | NBomber is commercial for org use; k6 is free AGPL CLI tool |
| Load testing | k6 + BenchmarkDotNet | Microsoft.Crank | Crank has no SignalR Hub Protocol support; not a published package; k6+BDN covers both use cases |
| Docs generator | DocFX 2.78.5 (MIT) | Statiq.Docs | Statiq has a commercial restriction license incompatible with GPL; DocFX is MIT, .NET Foundation |
| Docs generator | DocFX 2.78.5 (MIT) | Docusaurus, MkDocs/Material | These don't natively ingest .NET XML doc comments; require Node.js/Python toolchain |
| CVE scanning | NuGetAudit + Trivy | OWASP Dependency-Check alone | Trivy covers OS packages + SBOM; Dependency-Check NuGet-only; Trivy is a strict superset |
| Postgres health probe | `AspNetCore.HealthChecks.NpgSql` + EF package | `AspNetCore.HealthChecks.NpgSql` alone | EF DbContext check validates the actual code path; Npgsql alone validates the connection string — both serve different probe tiers |
| Postgres backup | `pg_dump` + runbook | EF-managed migration rollback only | EF migrations are schema-only; `pg_dump` is data backup — different concerns |

---

## 11. Version Compatibility Notes

| Combination | Status | Notes |
|-------------|--------|-------|
| `OpenTelemetry` 1.15.3 + net10.0 | ✅ | Ships net8.0+netstandard2.0; computed net10.0 confirmed on nuget.org |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3 + net10.0 | ✅ | Same TFM matrix; confirmed GA 2026-04-21 |
| `OpenTelemetry.Instrumentation.AspNetCore` 1.15.2 + net10.0 | ✅ GA | Ships net8.0+netstandard2.0; computed net10.0; confirmed stable |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` 1.15.1-beta.1 + net10.0 | ✅ (compat) but **beta** | Ships net10.0 TFM explicitly; experimental semantic conventions |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` 1.15.1-beta.2 + StackExchange.Redis 2.8.41 | ✅ (compat) but **beta** | Ships net10.0 TFM explicitly; Redis client already pinned at 2.8.41 |
| `Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore` 10.0.8 + EF Core 10.0.6 | ✅ | Same Microsoft release train; net10.0 primary TFM |
| `AspNetCore.HealthChecks.NpgSql` 9.0.0 + Npgsql 10.0.1 + net10.0 | ✅ | Computed net10.0 via net8.0 asset; Xabaril packages use Npgsql directly — version is separate from the EF provider |
| `AspNetCore.HealthChecks.Redis` 9.0.0 + StackExchange.Redis 2.8.41 + net10.0 | ✅ | Same computed compatibility; Redis client already pinned |
| `BenchmarkDotNet` 0.15.8 + net10.0 | ✅ | Confirmed .NET 10 support; dev tool only |
| `docfx` 2.78.5 + net10.0 target projects | ✅ | net10.0 TFM support explicitly added in 2.78.5 release |
| `OpenTelemetry.Exporter.Prometheus.AspNetCore` 1.15.3-beta.1 | ⚠️ PRE-RELEASE | Do NOT use in any shipped `GameKit.*` package |

---

## Sources

- [NuGet: OpenTelemetry 1.15.3](https://www.nuget.org/packages/OpenTelemetry) — version, TFMs, license verified 2026-06-08 — HIGH
- [NuGet: OpenTelemetry.Exporter.OpenTelemetryProtocol 1.15.3](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol) — version, TFMs, license verified 2026-06-08 — HIGH
- [NuGet: OpenTelemetry.Extensions.Hosting 1.15.3](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting) — version, license verified 2026-06-08 — HIGH
- [NuGet: OpenTelemetry.Instrumentation.AspNetCore 1.15.2](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AspNetCore) — GA stable, TFMs verified 2026-06-08 — HIGH
- [NuGet: OpenTelemetry.Instrumentation.EntityFrameworkCore 1.15.1-beta.1](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.EntityFrameworkCore) — pre-release confirmed 2026-06-08 — HIGH
- [NuGet: OpenTelemetry.Instrumentation.StackExchangeRedis 1.15.1-beta.2](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.StackExchangeRedis) — pre-release confirmed 2026-06-08 — HIGH
- [NuGet: OpenTelemetry.Exporter.Prometheus.AspNetCore 1.15.3-beta.1](https://www.nuget.org/packages/OpenTelemetry.Exporter.Prometheus.AspNetCore) — pre-release confirmed 2026-06-08 — HIGH
- [OpenTelemetry .NET Exporters docs](https://opentelemetry.io/docs/languages/dotnet/exporters/) — OTLP recommended over Prometheus-direct — HIGH
- [NuGet: Microsoft.Extensions.Diagnostics.HealthChecks 10.0.8](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.HealthChecks) — version, MIT license, net10.0 TFM verified — HIGH
- [NuGet: Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore 10.0.8](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore) — version, MIT, net10.0 primary TFM confirmed — HIGH
- [NuGet: AspNetCore.HealthChecks.NpgSql 9.0.0](https://www.nuget.org/packages/AspNetCore.HealthChecks.NpgSql) — Apache-2.0, net10.0 computed compat confirmed — HIGH
- [NuGet: AspNetCore.HealthChecks.Redis 9.0.0](https://www.nuget.org/packages/AspNetCore.HealthChecks.Redis) — Apache-2.0, net10.0 computed compat confirmed — HIGH
- [GitHub: Xabaril/AspNetCore.Diagnostics.HealthChecks](https://github.com/Xabaril/AspNetCore.Diagnostics.HealthChecks) — Apache-2.0 license, maintenance status confirmed — HIGH
- [NBomber License page](https://nbomber.com/docs/getting-started/license/) — "FREE only for personal use; cannot use for organization" — HIGH
- [GitHub: grafana/k6](https://github.com/grafana/k6) — AGPLv3, v2.0.0 released 2026-05-11, WebSocket support confirmed — HIGH
- [NuGet: BenchmarkDotNet 0.15.8](https://www.nuget.org/packages/benchmarkdotnet/) — MIT, released 2025-11-30, net10.0 support confirmed — HIGH
- [NuGet: docfx 2.78.5](https://www.nuget.org/packages/docfx) — MIT, net10.0 TFM, released 2026-02-24 — HIGH
- [DocFX GitHub Releases](https://github.com/dotnet/docfx/releases) — net10.0 support added in 2.78.5 confirmed — HIGH
- [Statiq Framework LICENSE-FAQ](https://github.com/statiqdev/Statiq.Framework/blob/main/LICENSE-FAQ.md) — commercial restriction (<10 employees / <$100K revenue) confirmed — HIGH
- [NuGet: Statiq.Docs 1.0.0-beta.17](https://www.nuget.org/packages/Statiq.Docs) — beta status, last updated 2024-01-09 — HIGH
- [Grafana relicensing announcement](https://grafana.com/blog/grafana-loki-tempo-relicensing-to-agplv3/) — Grafana + Tempo → AGPLv3 in April 2021 — HIGH
- [GitHub: grafana/tempo](https://github.com/grafana/tempo) — AGPLv3 license confirmed — HIGH
- [GitHub: jaegertracing/jaeger](https://github.com/jaegertracing/jaeger) — Apache-2.0, v2.13.0 current — HIGH
- [Trivy GitHub: aquasecurity/trivy](https://github.com/aquasecurity/trivy) — Apache-2.0, offline mode, SBOM confirmed — HIGH
- [OWASP Dependency-Check](https://owasp.org/www-project-dependency-check/) — Apache-2.0, offline capable (local NVD cache) — HIGH
- [NuGetAudit 2.0 .NET Blog post](https://devblogs.microsoft.com/dotnet/nugetaudit-2-0-elevating-security-and-trust-in-package-management/) — NuGetAuditMode=all default on net10.0 confirmed — HIGH
- [MS Learn: NuGet package auditing](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages) — configuration options confirmed — HIGH
- [MS Learn: ASP.NET Core Health Checks (aspnetcore-10.0)](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0) — liveness/readiness separation pattern — HIGH

---

*Stack research for: GameKit v2.1 — Operability & Hardening (new additions only)*
*Researched: 2026-06-08*
