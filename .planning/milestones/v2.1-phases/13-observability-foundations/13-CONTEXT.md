# Phase 13: Observability Foundations - Context

**Gathered:** 2026-06-14
**Status:** Ready for planning

<domain>
## Phase Boundary

Establish a **PII-safe observability foundation** — naming conventions locked,
`AddGameKitObservability()` wired in Core, and a self-hosted sample stack running —
**before any per-package instrumentation is written**. The actual per-package spans/metrics
(OBS-04 background-job metrics, OBS-05 lobby SignalR metrics, OBS-06 trace-context
propagation) are **Phase 15** and explicitly out of scope here.

In scope (maps to OBS-01, OBS-02, OBS-03, OBS-07, OBS-08):
- `GameKitTelemetry` constants in Core as the single source of truth for source/meter name
  prefixes + span attribute key names (no magic strings).
- `AddGameKitObservability()` on `IGameKitBuilder` in `GameKit.Core` — registers all known
  GameKit `ActivitySource`/`Meter` sources, no forced OTel SDK on consumers who omit the call.
- The PII/secret span-attribute lint gate (the **first** task — GPL/GDPR landmine).
- Extract the Rankings inline `_activitySource` into a canonical `Telemetry/` class matching
  the Matchmaking pattern.
- The self-hosted sample observability stack (`docker-compose.observability.yml`) +
  pre-provisioned Grafana dashboards in `samples/TicTacToeDuel`.

Out of scope: new per-handler spans, RED metrics emission across all packages, W3C
trace-context propagation through async/Redis/SignalR paths — all Phase 15.

</domain>

<decisions>
## Implementation Decisions

### Naming Convention (OBS-02, OBS-03 — success criterion #4)
- **D-01: Split naming convention.** `ActivitySource`/`Meter` **names** stay PascalCase
  namespace-style `GameKit.<Package>` — this matches the .NET OTel ecosystem norm
  (`Microsoft.AspNetCore`, `StackExchange.Redis`, `Npgsql`) **and** the existing live
  sources (`GameKit.Matchmaking.Ticker`, meter `GameKit.Matchmaking`,
  `GameKit.Rankings.Ticker`), so **zero source renames** and no broken operator
  `AddSource(...)`/`AddMeter(...)` strings. **Metric instrument names + span attribute
  keys** use lowercase-dotted `gamekit.<package>.*` / `ladder.id` per OTel semantic
  conventions. OBS-02/03's `gamekit.<package>.*` is read as the *instrument/attribute*
  namespace, not the source name.
- **D-02: `GameKitTelemetry` constants + enforcement test.** Core exposes the prefix and
  each canonical source/meter name as `const`s; meter/source version pinned `"1.0.0"`
  centrally. A unit test asserts every per-package `Telemetry/` class references the Core
  constant (satisfies criterion #4 "single source of truth, no magic strings").

### Span Attribute Keys (OBS-03 — success criteria #4, #5)
- **D-03: Lowercase-dotted attribute keys; normalize existing now.** Retrofit the
  Matchmaking camelCase tags to the standard in **this** phase
  (`ladderId`→`ladder.id`, `poolName`→`pool.name`, `candidatesEvaluated`→
  `candidates.evaluated`, `matchesFormed`→`matches.formed`, etc.). Rankings is already
  dotted-compliant. Spans are no-op-until-subscribed and nothing is public, so renaming
  attribute keys is low-risk — ship the foundation consistent rather than mixed-convention.
- **D-04: Core dimension key constants (the low-cardinality allow-list).** Seed
  `GameKitTelemetry` with the cross-cutting low-cardinality dimensions named in OBS-03:
  `ladder.id`, `pool.name`, `ladder.name`, `region`, `status`, `result`, `error.type`.
  High-cardinality identifiers (`player.id`, `ticket.id`) are **FORBIDDEN** as tags.
  Per-package-specific keys are added in Phase 15 as that instrumentation lands.
- **D-05: Extract `RankingsActivitySource`** from the inline `_activitySource` in
  `RankingsTickerService` into a canonical `Telemetry/RankingsActivitySource.cs`, mirroring
  `MatchmakingActivitySource` (criterion #5).

### PII / Secret Lint Gate (OBS-07 — success criterion #1, the FIRST task)
- **D-06: Roslyn analyzer, repo-build-only.** A source analyzer inspects the first argument
  of `SetTag`/`AddTag` calls in `src/` during GameKit's own solution build + CI (AST-precise,
  exact line diagnostics, fails the build). **Not** shipped in the consumer NuGet packages —
  matches criterion #1's `src/` scope and the "install only what you need" constraint. This
  is the first task before any new instrumentation lands.
- **D-07: Token-split + whole-token match + allow-list.** Tokenize the attribute key on dots
  and case-boundaries, then match whole tokens against the denylist
  `{player, user, email, token, ip, fingerprint}`. `client.ip`→`[client, ip]`→**blocked**;
  `recipient.count`→`[recipient, count]`→**clean** (avoids the naive `Contains("ip")`
  false-positive on recipient/description/zip). A committed allow-list file documents any
  intentional exceptions — this doubles as OBS-08's "documented attribute allow-list".

### Sample Observability Stack (OBS-08 — success criterion #3)
- **D-08: Tempo default; Jaeger documented swap.** Ship Tempo (matches criterion #3
  verbatim). AGPLv3 is fine here — Tempo runs as an independent operator-pulled container;
  GameKit neither links nor distributes it, so there is no GPL-compatibility issue. Document
  Jaeger (Apache-2.0) as a one-line overlay swap per OBS-08.
- **D-09: OTLP push, app stays on host.** The sample app runs via `dotnet run` (current
  workflow) and pushes OTLP to the dockerized Collector's receiver (host-published `:4317`).
  The Collector fans out to Prometheus (metrics) + Tempo (traces); Grafana reads both.
  Prometheus and its scrape target (the Collector's Prometheus exporter) stay on the
  **internal Docker network only** — satisfies the isolation in criterion #3 (host
  `curl :9090` does not reach app metrics) with **no app Dockerfile**.
- **D-10: Sample-local compose pair.** Add `samples/TicTacToeDuel/docker-compose.yml`
  (base: Postgres + Redis for the sample) and `docker-compose.observability.yml` (overlay:
  OTel Collector + Prometheus + Grafana + Tempo) so the criterion command runs verbatim from
  the sample dir. Map the sample Postgres to host **`:5433`** (host `:5432` is owned by the
  developer's local Postgres — see project memory).
- **D-11: Grafana provisioned-as-code, 2 dashboards.** Commit Grafana provisioning files
  (`datasources.yml` + 2 dashboard JSONs: matchmaking **queue depth** and **ticker health**)
  that auto-load on container start — zero click-ops, reproducible. Wire to the Collector's
  Prometheus + Tempo datasources.

### Claude's Discretion
- Exact analyzer project layout / diagnostic IDs, OTel Collector pipeline config shape,
  Prometheus scrape interval, and dashboard panel composition — researcher/planner decide.
- The precise final attribute-key strings for the normalized Matchmaking tags beyond the
  D-04 seed set (follow the lowercase-dotted rule).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase scope & requirements
- `.planning/ROADMAP.md` — Phase 13 section (goal + 5 success criteria)
- `.planning/REQUIREMENTS.md` — OBS-01, OBS-02, OBS-03, OBS-07, OBS-08 (and OBS-04/05/06
  which are **Phase 15**, listed only to mark the boundary)
- `.planning/PROJECT.md` — v2.1 milestone goal + "not yet public" north star (informs the
  low-cost-rename calculus)

### Canonical telemetry pattern to mirror
- `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` — the reference
  `ActivitySource` + typed-helper pattern; source name `GameKit.Matchmaking.Ticker`
- `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` — the reference `Meter` +
  instrument pattern; meter name `GameKit.Matchmaking`, instrument
  `matchmaking.analytics.dropped_events`

### Code that changes in this phase
- `src/GameKit.Rankings/Services/RankingsTickerService.cs` — inline `_activitySource`
  (`GameKit.Rankings.Ticker`) to extract into `Telemetry/` (D-05, criterion #5); tags
  `ladder.id`/`ladder.name`/`result`/`error` already dotted-compliant
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — camelCase tags to
  normalize (D-03): `candidatesEvaluated`, `matchesFormed`, `paused`, `reaped`, `budgetBail`,
  `matchCapBail`, `phase.*`
- `src/GameKit.Core/Builder/IGameKitBuilder.cs` — the `AddGameKitObservability()` extension
  integration point (criterion #2)

### Infra / deps
- `docker-compose.yml` (repo root) — dev DB stack shape (Postgres 17.9 :5432 + Redis 8.6.2);
  reference for the new sample-local base compose (which uses :5433)
- `Directory.Packages.props` — OTel **1.15.3** already pinned (`OpenTelemetry`,
  `OpenTelemetry.Api`, `.Exporter.OpenTelemetryProtocol`, `.Extensions.Hosting`,
  `.Instrumentation.AspNetCore`); these stay **opt-in** — no shipped package takes a hard
  OTel SDK dependency (OBS-01)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `MatchmakingActivitySource` / `MatchmakingMeter` (`src/GameKit.Matchmaking/Telemetry/`) —
  the canonical shape `GameKitTelemetry` + the extracted `RankingsActivitySource` must match
  (static class, `SourceName`/`MeterName` const, version `"1.0.0"`, typed `StartXActivity`
  helpers, operator-action XML-doc remarks).
- Root `docker-compose.yml` — Postgres/Redis service shape, healthchecks, named volumes to
  reuse for the sample base compose.

### Established Patterns
- **Opt-in OTel everywhere.** Spans/meters are no-ops unless the host registers
  `AddSource(...)`/`AddMeter(...)`; XML docs already repeat this "operator action required"
  guidance. `AddGameKitObservability()` must preserve this — it registers names, it does not
  force the SDK on consumers who skip the call.
- **Per-package `Telemetry/` folder** with a static source/meter class — extend this layout
  to Rankings; reference Core `GameKitTelemetry` constants from each.

### Integration Points
- `AddGameKitObservability()` is an extension on `IGameKitBuilder` (returned by
  `services.AddGameKit(...)`), living in `GameKit.Core`.
- The PII analyzer hooks GameKit's solution build / CI (repo-build-only), scanning `src/`.
- The sample stack connects via OTLP from the host-run app → dockerized Collector `:4317`.

</code_context>

<specifics>
## Specific Ideas

- Existing operator-facing source/meter names are **deliberately preserved** (D-01) — the
  PascalCase `GameKit.*` source names are the ecosystem norm; do not "lowercase" them.
- The PII gate is the **first** thing built this phase (OBS-07) — it must be green before any
  attribute normalization (D-03) or extraction (D-05) lands, so the new/changed tags are
  guarded as they're written.
- Sample Postgres maps to **`:5433`** to coexist with the developer's host Postgres on
  `:5432`.

</specifics>

<deferred>
## Deferred Ideas

- **Per-package instrumentation (Phase 15):** OBS-04 background-job metrics (ticker lag,
  queue depth, rank-decay duration, leader-lock failures), OBS-05 lobby SignalR metrics,
  OBS-06 W3C trace-context propagation through Redis/async/SignalR paths. Foundation only
  here.
- **Shipping the PII analyzer to consumers:** considered (would guard game-devs' own tags)
  but deferred — exceeds criterion #1's `src/` scope and adds consumer-facing analyzer
  surface. Revisit if self-hosters ask for it.
- **Final-demo 3D multiplayer platformer:** a small 3D multiplayer platformer (convenient
  engine) with GameKit hosting matchmaking, a real containerized game server doing secure
  server↔GameKit communication, packaged for easy demoing. **Milestone-level demo
  deliverable — not Phase 13.** Captured to the GSD backlog for milestone planning.

</deferred>

---

*Phase: 13-observability-foundations*
*Context gathered: 2026-06-14*
