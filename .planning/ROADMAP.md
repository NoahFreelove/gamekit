# Roadmap: GameKit

## Milestones

- ✅ **v1.0 — Initial 6-Phase Build-Out** — Phases 1–6 (shipped 2026-05-30) — [archive](milestones/v1.0-ROADMAP.md)
- ✅ **v2.0 — Expansion: Providers, Lobby & Rating-Aware Play** — Phases 7–12 (shipped 2026-06-07) — [archive](milestones/v2.0-ROADMAP.md)
- ✅ **v2.1 — Operability & Hardening** — Phases 13–21 (shipped 2026-07-13) — [archive](milestones/v2.1-ROADMAP.md)

## Phases

<details>
<summary>✅ v1.0 — Initial 6-Phase Build-Out (Phases 1–6) — SHIPPED 2026-05-30</summary>

7 composable GPL NuGet packages (Core, Auth, Rankings, Matchmaking, Presence, Admin.UI, OpenApi) + CLI + template. 92/92 requirements. Full detail: [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md).

</details>

<details>
<summary>✅ v2.0 — Expansion: Providers, Lobby & Rating-Aware Play (Phases 7–12) — SHIPPED 2026-06-07</summary>

- [x] Phase 7: Core Rating Seam + Stateless Auth Packages (6/6 plans) — 2026-06-05
- [x] Phase 8: Rankings Depth + Rating-Aware Matchmaking (4/4 plans) — 2026-06-06
- [x] Phase 9: Regional Matchmaking Pools + Backfill (4/4 plans) — 2026-06-06
- [x] Phase 10: Account Merge (Isolated High-Risk) (4/4 plans) — 2026-06-06
- [x] Phase 11: GameKit.Lobby (New Package) (4/4 plans) — 2026-06-07
- [x] Phase 12: Admin Multi-Replica + Distribution Close-Out (4/4 plans) — 2026-06-07

29/29 requirements. Full detail: [milestones/v2.0-ROADMAP.md](milestones/v2.0-ROADMAP.md). Audit: [milestones/v2.0-MILESTONE-AUDIT.md](milestones/v2.0-MILESTONE-AUDIT.md).

</details>

<details>
<summary>✅ v2.1 — Operability & Hardening (Phases 13–21) — SHIPPED 2026-07-13</summary>

- [x] **Phase 13: Observability Foundations** — PII lint gate + Core OTel conventions + `AddGameKitObservability()` + sample observability stack (completed 2026-06-14)
- [x] **Phase 14: Health & Readiness** — `MapGameKitHealth()` + `IMigrationReadinessReporter` across all packages + three-probe model + Admin.UI delegation (completed 2026-06-15)
- [x] **Phase 15: Per-Package OTel Instrumentation** — Activity spans + RED metrics + W3C trace propagation wired into every package (completed 2026-06-22)
- [x] **Phase 16: Multi-Replica Hardening** — `ILeaderLease` abstraction + SIGTERM drain + idempotency + split-brain CI gate (completed 2026-06-22)
- [x] **Phase 17: Backup / DR + Migration Ops** — CLI commands + Postgres/Redis runbooks + DR round-trip CI test + migration-ops docs (completed 2026-06-23)
- [x] **Phase 18: Security Audit** — JWT/admin/GDPR/egress/rate-limit audit tests + CVE CI gate + security checklist (completed 2026-06-23)
- [x] **Phase 19: Load / Performance Testing** — BenchmarkDotNet micro-benchmarks + k6 load scenarios + CI regression gate + tuning guide (completed 2026-06-23)
- [x] **Phase 20: Docs & Tutorial** — DocFX API reference + getting-started tutorial + upgrade guide + runbook library (completed 2026-06-23)
- [x] **Phase 21: Final Demo — 3D Multiplayer Platformer** — single loadable image (admin server + GameKit + fully-customized example) you `docker load`/run, then play a 3D multiplayer game in the web browser (completed 2026-06-23)

47/47 requirements. Full detail: [milestones/v2.1-ROADMAP.md](milestones/v2.1-ROADMAP.md). Audit: [milestones/v2.1-MILESTONE-AUDIT.md](milestones/v2.1-MILESTONE-AUDIT.md).

</details>

## Progress

| Phase | Milestone | Plans | Status | Completed |
|-------|-----------|-------|--------|-----------|
| 1–6. (v1.0 build-out) | v1.0 | 60 | Complete | 2026-05-30 |
| 7–12. (v2.0 expansion) | v2.0 | 26 | Complete | 2026-06-07 |
| 13–21. (v2.1 operability & hardening) | v2.1 | 49 | Complete | 2026-07-13 |

---

## Backlog

Candidate work not yet promoted into an active phase. Promote via `/gsd:review-backlog`.

### Phase 999.1: Final Demo — 3D Multiplayer Platformer (✅ PROMOTED → Phase 21 on 2026-06-22)

> **Promoted to active roadmap as Phase 21** (v2.1 capstone) on 2026-06-22. See the Phase 21 detail entry above. Kept here for provenance.

**Goal:** A small 3D multiplayer platformer that showcases GameKit end-to-end as a live demo — GameKit hosts matchmaking; a real, containerized game server establishes secure server↔GameKit communication; the whole thing runs with a simple `docker compose up` so it can be demoed easily.

**Captured:** 2026-06-14 (user request during Phase 13 discussion)
**Milestone:** TBD — likely a v2.1 capstone or its own demo milestone (substantial: real game + game-server + secure server-to-GameKit auth + container packaging).
**Sketch of scope:**

- [ ] Tiny 3D multiplayer platformer client — whichever engine is convenient (Godot / Unity / three.js — pick for demo speed, mind GPL compatibility of any bundled engine bits).
- [ ] Real game server (not a sample stub) that authenticates to GameKit and drives matchmaking via the GameKit HTTP API.
- [ ] Secure server↔GameKit communication (service-to-service auth — confirm the right primitive: dedicated server credential / JWT scope / mutual TLS).
- [ ] Containerized so the full demo (game server + GameKit backend + Postgres + Redis) comes up with one `docker compose up`.

**Why backlog, not Phase 13:** This is a milestone-level demo deliverable, well outside Phase 13's observability-foundations scope. Recorded so it isn't lost; sequence it during milestone planning.

---
*v1.0 + v2.0 shipped. v2.1 roadmap created 2026-06-08 — 47/47 requirements mapped across Phases 13–20.*
