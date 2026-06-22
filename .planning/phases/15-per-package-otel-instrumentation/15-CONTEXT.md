# Phase 15: Per-Package OTel Instrumentation - Context

**Gathered:** 2026-06-15
**Status:** Ready for planning

<domain>
## Phase Boundary

Wire **actual** OpenTelemetry spans + RED metrics into the GameKit packages and thread
**W3C trace context** through the async paths — turning the Phase-13 foundation (locked naming,
`GameKitTelemetry` constants, the GK0001 PII analyzer, the sample Collector→Prometheus+Tempo→
Grafana stack, and the two pre-provisioned dashboards) into a system that emits real telemetry.

In scope (maps to OBS-04, OBS-05, OBS-06):
- **OBS-04 — background-job metrics** in `GameKit.Matchmaking` + `GameKit.Rankings`: matchmaking
  ticker lag, queue depth per pool, rank-decay job run duration, leader-lock acquisition failures.
- **OBS-05 — Lobby SignalR metrics** in `GameKit.Lobby`: connected clients, messages/sec,
  ready-check completion rate, under the `gamekit.lobby.*` namespace.
- **OBS-06 — W3C trace-context propagation** through async paths: `traceparent` stored in the
  Redis ticket hash at enqueue and restored at match-formation in the ticker. The lobby
  ready-check span is parented to the initiating hub invocation's ambient `Activity` (server-side
  capture). The rank-decay `BackgroundService` is timer-triggered with **no inbound context**, so
  its span starts a **fresh root** trace (see D-03a).
- Per-package **`MeterListener` PII tag-key assertion test** in every package (criterion #1).
- Make the Phase-13 matchmaking dashboards (queue depth + ticker health) **render real data**
  against the sample stack (criterion #4).

Out of scope: hand-rolled per-HTTP-endpoint RED metrics (we lean on built-in ASP.NET Core
instrumentation — see D-01); custom GameKit instruments in Auth/Presence/Core/Admin.UI this
phase (D-04); multi-replica leader-churn / SIGTERM-drain correctness (Phase 16); K8s probe-tuning
docs (docs phase). The foundation itself (naming, analyzer, sample stack, `AddGameKitObservability`)
shipped in Phase 13 and is **not** re-built here.

</domain>

<decisions>
## Implementation Decisions

### HTTP-layer metrics strategy (phase goal "every HTTP handler path … RED metrics")
- **D-01: Lean on built-in ASP.NET Core instrumentation for HTTP RED — do NOT hand-roll
  per-endpoint counters.** The host's `AddAspNetCoreInstrumentation()` already emits
  `http.server.request.duration` (rate/errors/duration per route + status) — the RED triad for
  every GameKit endpoint. GameKit emits its **own** `gamekit.<package>.*` spans/metrics **only**
  at domain + async/background/SignalR boundaries (match formation, ticker, decay, lobby). No
  duplicate `gamekit.<pkg>.http.*` request counters — lowest cardinality, no duplication, idiomatic.
  The phase goal's "every HTTP handler path emits … RED metrics" is satisfied **by the built-in
  `http.server.*` metrics** scraped through the sample stack, not by bespoke per-route instruments.

### W3C trace-context propagation across the Redis fan-in (OBS-06 — criterion #2)
- **D-02: Store `traceparent` in the ticket hash at enqueue, restore in the ticker.** At enqueue
  the HTTP handler writes the current `Activity`'s W3C `traceparent` (and `tracestate` if present)
  string into the Redis ticket hash. When the ticker forms a match it reads those values back and
  reconstructs the parent `ActivityContext` so the match-formation span is a **descendant** of the
  originating enqueue trace (the literal criterion #2 requirement) — the full lifecycle is one
  causal trace in Tempo.
- **D-03: Multi-ticket fan-in → parent = first/initiating ticket, links = the rest.** A match
  forms from N tickets, each with its own enqueue trace. The match-formation span's **parent** is
  the first/initiating ticket's restored `traceparent`; every other co-matched ticket is attached
  as an OTel **span link**. This keeps one clean parent chain (satisfies "descendant of the enqueue
  span") while preserving causal visibility to all participants — the idiomatic OTel pattern for
  fan-in.
- **D-03a (clarification — store-then-restore applies only where a real inbound context exists).**
  The lobby ready-check broadcast reuses the same idea: its `ReadyCheck` span is parented to
  `Activity.Current` captured **server-side** at the SignalR hub invocation (never from client
  input). The rank-decay `BackgroundService`, however, fires on a timer with **no inbound client
  request and nothing to restore**, so it correctly originates its **own fresh-root** trace —
  idiomatic OTel: a periodic background job is a trace originator, not a continuation. Forcing a
  synthetic parent would add no causal value. [reconciled 2026-06-22 per plan-checker Blocker #2 —
  the earlier "same store-then-restore mechanism for the rank-decay BackgroundService" wording
  over-generalized; a timer-triggered job has no traceparent to restore.]

### Background-job + lobby metric instrument shapes (OBS-04, OBS-05 — criterion #3)
- **D-04: Instrument the three criteria packages; built-in HTTP for the rest.** Full GameKit
  spans + metrics land in **Matchmaking, Rankings, Lobby** (the packages named in the success
  criteria). **Auth, Presence, Core, Admin.UI** rely on built-in ASP.NET Core HTTP instrumentation
  + their existing spans — **no new hand-rolled instruments** this phase (keeps cardinality and
  surface tight; not required by any criterion). The **`MeterListener` PII tag-key test runs in
  every package** regardless (criterion #1 says "each package").
- **D-05: Idiomatic OTel instrument-type mix.**
  - **ObservableGauge** (polled callback on collect): `gamekit.matchmaking.*` queue **depth per
    pool** (tag `pool.name`), `gamekit.lobby.*` **connected clients**. The callback reads
    Redis/hub state on scrape — researcher to confirm the per-scrape Redis read cost is acceptable
    and bounded (see Discretion).
  - **Histogram**: matchmaking **ticker lag**, rank-decay **job run duration** — gives p50/p95/p99
    in Grafana via `histogram_quantile`.
  - **Counter**: **leader-lock acquisition failures**, lobby **messages** (messages/sec derived via
    `rate()` in Grafana), ready-check **started/completed** pair (completion rate derived
    downstream — not a pre-computed ratio). Mirrors the existing
    `matchmaking.analytics.dropped_events` counter shape.

### Dashboards (criterion #4)
- **D-06: Make the existing 2 matchmaking dashboards render real data; lobby/rankings dashboards
  are optional.** Criterion #4 requires only the Phase-13 **matchmaking queue depth + ticker
  health** dashboards to render correct data against the sample stack once the metrics exist.
  Adding lobby/rankings Grafana dashboards is **discretion / nice-to-have**, not required —
  don't expand scope chasing them.

### Claude's Discretion
- Exact instrument names under each namespace (follow D-01 lowercase-dotted `gamekit.<package>.*`
  rule + the Phase-13 allow-list), histogram bucket boundaries, ObservableGauge polling cost vs a
  push-on-tick alternative if per-scrape Redis reads prove expensive, the precise `Telemetry/`
  class layout for the new Lobby meter + Matchmaking/Rankings additions, whether `tracestate` is
  carried alongside `traceparent`, and how the ticker reconstructs `ActivityContext` from the
  stored string — researcher/planner decide.
- Whether to add a Lobby `ActivitySource`/`Meter` pair vs a single meter (Lobby currently has
  **no** Telemetry/ folder — it is built from scratch following the Matchmaking pattern).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase scope & requirements
- `.planning/ROADMAP.md` — Phase 15 section (goal + 4 success criteria)
- `.planning/REQUIREMENTS.md` — OBS-04, OBS-05, OBS-06 (lines 20–22); OBS-07/08 (lines 23–24)
  marked done in Phase 13, listed to fix the foundation boundary
- `.planning/PROJECT.md` — v2.1 "Operability & Hardening" milestone goal + "not yet public"
  north star
- `.planning/phases/13-observability-foundations/13-CONTEXT.md` — the locked foundation: D-01
  split naming convention, D-04 low-cardinality attribute allow-list, D-06/07 PII analyzer,
  D-08/09/10/11 sample stack + dashboards. **This phase builds on every one of those decisions.**

### Phase-13 foundation to build on (the canonical telemetry pattern + constants)
- `src/GameKit.Core/Telemetry/GameKitTelemetry.cs` — single source of truth for source/meter
  names, the `"1.0.0"` version, `SourcePrefix = "GameKit"`, and the D-04 attribute key constants
  (`ladder.id`, `pool.name`, `ladder.name`, `region`, `status`, `result`, `error.type`). New
  per-package source/meter names + new low-cardinality attribute keys get added **here** as
  instrumentation lands, and the reflection enforcement test (`GameKitTelemetryConstantsTests`)
  must stay green.
- `src/GameKit.Matchmaking/Telemetry/MatchmakingActivitySource.cs` — reference `ActivitySource`
  + typed `StartXActivity` helper pattern; source `GameKit.Matchmaking.Ticker`.
- `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs` — reference `Meter` + instrument pattern
  (`internal static`, `InternalsVisibleTo` for the MeterListener tests); existing
  `matchmaking.analytics.dropped_events` counter is the shape to mirror for new counters.
- `src/GameKit.Rankings/Telemetry/RankingsActivitySource.cs` — extracted in Phase 13; Rankings
  has a source but **no Meter yet** — the decay-duration histogram needs a new `RankingsMeter`.

### Code instrumented / extended this phase
- `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` — ticker lag histogram, queue
  depth gauge, leader-lock acquisition-failure counter; the match-formation span that restores
  the stored `traceparent` (D-02/D-03).
- `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` + `Services/MatchmakingService.cs` — the
  enqueue path that writes `traceparent` into the ticket hash.
- `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` — ticket hash key layout; add the
  `traceparent`/`tracestate` hash fields here.
- `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` / `RedisMatchmakerLease` — leader-lock
  acquisition-failure counter site.
- `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` — decay-duration histogram + trace
  propagation through the decay job.
- `src/GameKit.Lobby/Hubs/LobbyHub.cs` + `Services/ILobbyService.cs` — connected-clients gauge,
  message counter, ready-check started/completed counters; ready-check broadcast trace propagation.
  Lobby has **no `Telemetry/` folder yet** — create one following the Matchmaking pattern.

### Sample stack + dashboards (criterion #4)
- `samples/TicTacToeDuel/Program.cs` — where `AddAspNetCoreInstrumentation()` + `AddSource`/
  `AddMeter` registrations are wired; confirm the new sources/meters are registered for the
  sample stack to scrape (D-01 relies on built-in ASP.NET Core instrumentation being enabled here).
- `samples/TicTacToeDuel/observability/dashboards/` — the two Phase-13 dashboards (matchmaking
  queue depth + ticker health) that must render real data once metrics exist (criterion #4).
- `samples/TicTacToeDuel/docker-compose.observability.yml` — the Collector→Prometheus+Tempo→Grafana
  stack the metrics/traces flow into.

### Deps
- `Directory.Packages.props` — OTel **1.15.3** already pinned (opt-in, no shipped package takes a
  hard SDK dependency, per OBS-01). Confirm **no new pin** is needed.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **`MatchmakingMeter` + `MatchmakingActivitySource`** — the exact `internal static` meter/source
  pattern (const name, `"1.0.0"` version, typed helpers, `InternalsVisibleTo` for MeterListener
  tests) to copy when adding the new Matchmaking instruments, the new `RankingsMeter`, and the
  brand-new Lobby telemetry classes.
- **`matchmaking.analytics.dropped_events` counter** — a working `Counter<long>` with a `reason`
  tag; the shape for the new leader-lock-failure and lobby counters.
- **`GameKitTelemetry` constants + `GameKitTelemetryConstantsTests`** — extend the constants for
  new sources/meters/attribute keys; the reflection test catches drift between Core constants and
  per-package class values.
- **GK0001 PII analyzer** — already guards `SetTag`/`AddTag` keys in `src/`; new span tags written
  this phase are auto-checked against the `{player, user, email, token, ip, fingerprint}` denylist.

### Established Patterns
- **Opt-in everywhere** — instruments are no-ops until the host registers `AddSource`/`AddMeter`;
  `AddGameKitObservability()` (Phase 13) registers the names. New sources/meters must be added to
  that registration so the sample stack picks them up.
- **Per-package `Telemetry/` folder** with a static source/meter class referencing Core constants.
  Lobby gets a new one; Rankings gains a `Meter` alongside its existing source.
- **Composable, no reverse deps** — instrumentation lives in each package; Core only holds the
  shared constants.

### Integration Points
- Enqueue HTTP handler → writes `traceparent` to Redis ticket hash → ticker reads + restores it
  into the match-formation `Activity` (D-02/D-03).
- Built-in `AddAspNetCoreInstrumentation()` in the sample app provides HTTP RED metrics (D-01);
  GameKit registers only its custom sources/meters on top.
- ObservableGauge callbacks read Redis (queue depth) / hub state (connected clients) on scrape.

</code_context>

<specifics>
## Specific Ideas

- **Built-in HTTP instrumentation is the RED source** — GameKit deliberately does NOT re-emit
  per-endpoint request metrics (D-01). Verify the matchmaking-enqueue trace in criterion #2 still
  shows the HTTP server span (from ASP.NET Core) as the root above the GameKit enqueue span.
- **Fan-in trace model is parent-first + links** — when N tickets form a match, the first ticket
  is the parent, the rest are span links (D-03). Criterion #2's single enqueue→formation example
  works because that ticket is the parent.
- **Lobby is greenfield for telemetry** — no existing `Telemetry/` folder; everything under
  `gamekit.lobby.*` is new, built to the Matchmaking pattern.
- **Rates are derived in Grafana** — messages/sec and ready-check completion rate are emitted as
  raw counters; Grafana computes the rate (`rate()`) and ratio. Don't pre-compute ratios in code.

</specifics>

<deferred>
## Deferred Ideas

- **Custom instruments for Auth/Presence/Core/Admin.UI** — considered (a literal reading of "every
  package"), deferred (D-04). Not required by the success criteria; built-in HTTP instrumentation
  covers their RED needs this phase. Revisit if a specific package gains an observability gap.
- **Lobby + Rankings Grafana dashboards** — criterion #4 only requires the two matchmaking
  dashboards to render real data (D-06). Dedicated lobby/rankings dashboards are nice-to-have for a
  later docs/polish pass.
- **Multi-replica leader-churn / SIGTERM-drain / split-brain correctness (→ Phase 16).** This phase
  emits the leader-lock acquisition-failure *metric*; fleet-wide correctness under churn is Phase 16.
- **Shipping the GK0001 PII analyzer to consumers** — deferred in Phase 13; still out of scope.

### Reviewed Todos (not folded)
None — `todo.match-phase 15` returned zero matches.

</deferred>

---

*Phase: 15-per-package-otel-instrumentation*
*Context gathered: 2026-06-15*
