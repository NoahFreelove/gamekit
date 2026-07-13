# Phase 14: Health & Readiness - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-06-14
**Phase:** 14-health-readiness
**Mode:** `--auto` (recommended option auto-selected per gray area — no interactive prompts)
**Areas discussed:** Health-check framework, Live/Ready separation, Migration-readiness design, Dependency gating on Core-only installs, Leader-lock probe semantics, Payload safety + Admin.UI delegation

---

## Health-check framework

| Option | Description | Selected |
|--------|-------------|----------|
| Built-in ASP.NET Core HealthChecks | `Microsoft.Extensions.Diagnostics.HealthChecks` from the shared framework — zero NuGet pin, three-state, `HealthCheckService` is the SoT HLTH-06 names | ✓ |
| Xabaril `AspNetCore.HealthChecks.*` | Community packages with prebuilt Npgsql/Redis checks | |
| Hand-rolled probe service | Extend the existing Admin.UI `HealthProbeService` model into Core | |

**User's choice:** Built-in ASP.NET Core HealthChecks (recommended default).
**Notes:** Honors "install only what you need" — the BCL already provides `IHealthCheck` + tag filtering + three-state. Xabaril adds third-party deps for no gain; hand-rolling duplicates framework plumbing.

---

## Live/Ready separation

| Option | Description | Selected |
|--------|-------------|----------|
| Tag-based filtering, one registration | `/health/live` `Predicate=_=>false` (process-only), `/health/ready` `Predicate=Tags.Contains("ready")` | ✓ |
| Two separate check registrations | Independent check sets per endpoint | |

**User's choice:** Tag-based filtering (recommended default).
**Notes:** Liveness running zero checks is what makes `/health/live` stay 200 with Postgres stopped (criterion #1). Degraded→200, Unhealthy→503.

---

## Migration-readiness design

| Option | Description | Selected |
|--------|-------------|----------|
| `IMigrationReadinessReporter` per package, query pending migrations | Six reporters; each checks `GetPendingMigrationsAsync()==empty`, latched; one Core aggregate check | ✓ |
| Each HostedService flips a flag after its own apply | Readiness tied to in-process apply only | |
| Single Core check queries all history tables directly | Core reaches into sibling tables | |

**User's choice:** Per-package reporter querying pending migrations (recommended default).
**Notes:** Querying pending (not remembering own apply) makes readiness correct under `AutoMigrate=false` / out-of-band `gamekit migrate`. Six reporters = Core, Auth, Admin.UI, Rankings, Matchmaking, Lobby (Presence has no migrations).

---

## Dependency gating on Core-only installs

| Option | Description | Selected |
|--------|-------------|----------|
| Postgres always gates; Redis gates only when configured (down→503) | HLTH-02-faithful: absent Redis never blocks; configured-but-down → Unhealthy | ✓ |
| Redis-down → Degraded (stays in rotation) | Softer; avoids shared-Redis fleet drain | |

**User's choice:** Conditional Redis gate, down→503 per HLTH-02 (recommended default).
**Notes:** HLTH-02 says "out of rotation until every dependency passes." The fleet-wide-drain softening is captured as a Phase-16 deferred tradeoff, not overridden here.

---

## Leader-lock probe semantics

| Option | Description | Selected |
|--------|-------------|----------|
| Degraded-only check in Matchmaking, non-acquiring read of holder+TTL | Self-registers into shared builder; `QueryLeaseAsync()` reads `GET`+`PTTL` without taking the lock | ✓ |
| Check lives in Core | Core would need a Matchmaking reference | |
| Probe acquires the lock to test it | Would perturb leader election | |

**User's choice:** Degraded-only, ships in Matchmaking, non-acquiring (recommended default).
**Notes:** Preserves no-reverse-dependency architecture. Follower → Degraded → stays in rotation (criterion #3). Surfaces `InstanceId` (`MachineName:Guid`) + TTL (criterion #3 / HLTH-04).

---

## Payload safety + Admin.UI delegation

| Option | Description | Selected |
|--------|-------------|----------|
| Custom whitelist `ResponseWriter` + `HealthProbeService` delegates to `HealthCheckService` | Emit `{status, checks:[{name,status,description}]}` only; delete duplicated probes; leak test | ✓ |
| Default HealthChecks JSON writer | Serializes Exception/Data — leaks host:port | |
| Keep Admin.UI probing independently | Violates HLTH-06 (no duplication) | |

**User's choice:** Custom writer + Admin delegation (recommended default).
**Notes:** Default writer leaks Npgsql exception host:port — forbidden by HLTH-05. Admin error-rate tile stays Admin-local (not a readiness dependency). Replica `InstanceId` is the replica's own id, not an infra host — flagged for the security auditor.

---

## Claude's Discretion

- Namespace/file layout for Core health types; `IGameKitBuilder` vs `IServiceCollection`
  extension surface; check-name/tag constants; `LockQueryAsync` vs raw `GET`+`PTTL`;
  migration-latch caching; sibling-package discovery of the shared `IHealthChecksBuilder`.

## Deferred Ideas

- Shared-Redis fleet-drain softening (Redis-down → Degraded for non-matchmaking replicas) → Phase 16.
- K8s `liveness/readiness/startupProbe` YAML + probe-tuning docs → docs phase / DOCS-04.
- Combined `/health` aggregate endpoint → out of scope.
- Per-package OTel spans/metrics → Phase 15; leader-churn/SIGTERM-drain/storm correctness → Phase 16.
