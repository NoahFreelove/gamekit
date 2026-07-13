# Phase 13: Observability Foundations - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-14
**Phase:** 13-observability-foundations
**Areas discussed:** Naming convention + migration, Span attribute key convention, PII lint gate mechanism, Sample stack shape

---

## Naming convention + migration

| Option | Description | Selected |
|--------|-------------|----------|
| Split | Source/Meter NAMES stay PascalCase `GameKit.<Package>` (ecosystem norm + existing live sources, zero rename); metric instruments + attribute keys lowercase-dotted `gamekit.<package>.*` per OTel semconv | ✓ |
| Uniform lowercase | Everything lowercase incl. source/meter names; rename the 2 live sources + meter, update XML-doc AddSource/AddMeter strings + sample host | |
| Uniform PascalCase | Everything `GameKit.<Package>` incl. metric names; treat OBS-02/03 lowercase as illustrative | |

**User's choice:** Split (Recommended)
**Notes:** Reads OBS-02/03's `gamekit.<package>.*` as the instrument/attribute namespace, not the ActivitySource/Meter name. Avoids breaking documented operator `AddSource(...)` strings.

| Option | Description | Selected |
|--------|-------------|----------|
| Constants + enforcement test | `GameKitTelemetry` consts in Core; unit test asserts every per-package Telemetry class references the constant (no magic strings); version pinned 1.0.0 | ✓ |
| Constants only, no test | Define consts, rely on code review for drift | |

**User's choice:** Constants + enforcement test (Recommended)

---

## Span attribute key convention

| Option | Description | Selected |
|--------|-------------|----------|
| Normalize existing now | Retrofit Matchmaking camelCase tags (`ladderId`→`ladder.id`, etc.) to lowercase-dotted this phase; Rankings already compliant | ✓ |
| Foundation-only, defer retrofit | Define convention + constants + extract Rankings source, leave Matchmaking camelCase until Phase 15 | |

**User's choice:** Normalize existing now (Recommended)
**Notes:** Spans are no-op-until-subscribed and nothing's public — low-risk to normalize now and ship the foundation consistent.

| Option | Description | Selected |
|--------|-------------|----------|
| Core dimension keys | Seed consts for OBS-03 low-cardinality dims: `ladder.id`, `pool.name`, `ladder.name`, `region`, `status`, `result`, `error.type`; forbid `player.id`/`ticket.id` | ✓ |
| Minimal seed | Only prefix + naming-rule consts; each package defines its own keys | |

**User's choice:** Core dimension keys (Recommended)

---

## PII lint gate mechanism

| Option | Description | Selected |
|--------|-------------|----------|
| Roslyn analyzer, repo-build-only | AST-precise SetTag/AddTag inspection over `src/` during solution build + CI; not shipped to consumers; pairs with documented allow-list | ✓ |
| Roslyn analyzer, also shipped to consumers | Same analyzer packaged into shipped NuGet to guard game-devs' builds; more surface + false-positive burden; exceeds src/ scope | |
| CI-only regex/grep script | Shell/regex gate; simplest but brittle on multiline/non-literal args, no IDE feedback | |

**User's choice:** Roslyn analyzer, repo-build-only (Recommended)
**Notes:** Matches criterion #1's `src/` scope + "install only what you need". First task before any new instrumentation lands (GPL/GDPR landmine).

| Option | Description | Selected |
|--------|-------------|----------|
| Token-split + whole-token match + allow-list | Tokenize on dots/case-boundaries, match whole tokens vs denylist; `client.ip` blocked, `recipient.count` clean; allow-list file for exceptions | ✓ |
| Raw substring match | Literal `Contains()` per criterion wording; flags legitimate keys with `ip`/`user` substrings | |

**User's choice:** Token-split + whole-token match + allow-list (Recommended)

---

## Sample stack shape

| Option | Description | Selected |
|--------|-------------|----------|
| Tempo | Matches criterion #3 verbatim; AGPLv3 ok (independent operator-run container, not linked/distributed); Jaeger documented as swap | ✓ |
| Jaeger default, Tempo documented | Lead with Apache-2.0 Jaeger; diverges from criterion #3 literal wording | |

**User's choice:** Tempo (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| OTLP push, app stays on host | App `dotnet run` pushes OTLP to dockerized Collector `:4317`; Collector→Prometheus+Tempo; Prometheus/scrape-target internal-only; no app Dockerfile; base compose Postgres `:5433`+Redis | ✓ |
| Containerize the app into the stack | Add Dockerfile so `up` brings up app+DBs+observability together; cleanest single-command demo but adds image-build infra | |

**User's choice:** OTLP push, app stays on host (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| Provisioned-as-code, 2 dashboards | Grafana provisioning files (datasources.yml + 2 dashboard JSONs: queue depth + ticker health) auto-load on start | ✓ |
| Provision datasources only, dashboards manual | Auto-provision datasources, leave dashboards to operator | |

**User's choice:** Provisioned-as-code, 2 dashboards (Recommended)

## Claude's Discretion

- Analyzer project layout / diagnostic IDs, OTel Collector pipeline config, Prometheus scrape interval, dashboard panel composition.
- Final normalized Matchmaking attribute-key strings beyond the D-04 seed set (follow lowercase-dotted rule).

## Deferred Ideas

- Per-package instrumentation (OBS-04/05/06) → Phase 15.
- Shipping the PII analyzer to consumers → deferred (exceeds src/ scope).
- **Final-demo 3D multiplayer platformer** with GameKit matchmaking + containerized secure game server → milestone-level demo deliverable, captured to GSD backlog.
