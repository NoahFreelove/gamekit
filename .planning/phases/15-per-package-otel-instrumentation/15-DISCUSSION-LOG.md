# Phase 15: Per-Package OTel Instrumentation - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-15
**Phase:** 15-per-package-otel-instrumentation
**Areas discussed:** HTTP-layer metrics strategy, W3C trace linking (fan-in), Background-job + lobby instrument types, Package coverage scope

---

## HTTP-layer metrics strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Lean on ASP.NET Core built-in | Host's `AddAspNetCoreInstrumentation()` provides `http.server.request.duration` (RED); GameKit emits its own `gamekit.*` spans/metrics only at domain + async/background boundaries. No hand-rolled per-endpoint counters. | ✓ |
| Hand-roll GameKit RED per endpoint | Every GameKit endpoint emits its own `gamekit.<package>.request.*` counters/histograms; duplicates `http.server.*` and risks route cardinality. | |
| Hybrid: built-in + custom spans only | Built-in for HTTP RED metrics; GameKit adds named custom spans (not metrics) around notable handler operations. | |

**User's choice:** Lean on ASP.NET Core built-in
**Notes:** Lowest cardinality, no duplication, idiomatic. The phase goal's "every HTTP handler path emits RED metrics" is read as satisfied by the built-in `http.server.*` metrics scraped through the sample stack.

---

## W3C trace linking (multi-ticket fan-in)

| Option | Description | Selected |
|--------|-------------|----------|
| Parent = first ticket, links = rest | Match-formation span's parent is the first/initiating ticket's restored `traceparent`; other co-matched tickets attached as OTel span links. | ✓ |
| All tickets as links, new root | Match-formation starts a new root trace, all N enqueue traces attached as links; no parent-child. | |
| Defer link model to research | Lock store+restore of `traceparent`; let researcher pick parent-vs-links semantics. | |

**User's choice:** Parent = first ticket, links = rest
**Notes:** Satisfies criterion #2 "descendant of the enqueue span" literally for one trace while preserving causal visibility to all participants — the idiomatic OTel fan-in pattern.

---

## Background-job + lobby instrument types (OBS-04 / OBS-05)

| Option | Description | Selected |
|--------|-------------|----------|
| Idiomatic OTel mix (recommended) | ObservableGauge (queue depth per pool, connected clients); Histogram (ticker lag, decay duration); Counter (leader-lock failures, lobby messages, ready-check started/completed). Rates derived in Grafana. | ✓ |
| Counters + gauges only (no histograms) | Emit ticker lag + decay duration as ObservableGauge (last value); avoids histogram bucket cost but loses latency percentiles. | |
| Defer shapes to research | Lock the metric list; let researcher choose instrument types + assess observable-callback Redis cost. | |

**User's choice:** Idiomatic OTel mix (recommended)
**Notes:** Matches OTel semantic conventions and the existing `dropped_events` counter shape; ObservableGauge callback reading Redis on scrape flagged for researcher cost-check.

---

## Package coverage scope

| Option | Description | Selected |
|--------|-------------|----------|
| Criteria packages + built-in for rest | Full GameKit spans/metrics for Matchmaking, Rankings, Lobby; Auth/Presence/Core/Admin rely on built-in HTTP instrumentation. PII tag-key test on all packages. | ✓ |
| All packages get custom spans/metrics | Add GameKit-specific spans + a domain metric to every package including Auth/Presence/Core/Admin this phase. | |
| Criteria three + Auth + Presence | Matchmaking/Rankings/Lobby fully, plus Auth (login/token spans) and Presence (heartbeat metrics). | |

**User's choice:** Criteria packages + built-in for rest
**Notes:** Tightest scope that still satisfies all four success criteria; criterion #1's per-package PII tag-key test still runs in every package.

---

## Claude's Discretion

- Exact instrument names under each `gamekit.<package>.*` namespace, histogram bucket boundaries,
  ObservableGauge polling cost vs push-on-tick fallback, new `Telemetry/` class layout for Lobby +
  the new `RankingsMeter`, whether `tracestate` rides alongside `traceparent`, and how the ticker
  reconstructs `ActivityContext` from the stored string.
- Whether Lobby uses a separate `ActivitySource`/`Meter` pair or a single meter.

## Deferred Ideas

- Custom instruments for Auth/Presence/Core/Admin.UI (literal "every package" reading) — not required.
- Lobby + Rankings Grafana dashboards — criterion #4 only requires the two matchmaking dashboards.
- Multi-replica leader-churn / SIGTERM-drain / split-brain correctness → Phase 16.
- Shipping the GK0001 PII analyzer to consumers → still out of scope (deferred since Phase 13).
