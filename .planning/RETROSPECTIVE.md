# Project Retrospective

*A living document updated after each milestone. Lessons feed forward into future planning.*

## Milestone: v2.1 — Operability & Hardening

**Shipped:** 2026-07-13
**Phases:** 9 (13–21) | **Plans:** 49 | **Timeline:** 28 days (2026-06-14 → 2026-07-12)

### What Was Built
- Full opt-in OpenTelemetry story: `AddGameKitObservability()`, per-package ActivitySources/Meters, W3C trace propagation through Redis tickets, self-hosted Grafana/Prometheus/Tempo stack with provisioned dashboards
- K8s probe contract (`/health/live`, `/health/ready`) with six per-package migration-readiness reporters and Degraded-only leader probes
- Multi-replica correctness: unified `ILeaderLease`, shutdown-surviving lease release with CI grep gate, idempotent session completion, split-brain and graceful-drain integration proofs
- Backup/DR + migration ops: `gamekit migrations list`/`apply --dry-run`, `gamekit db backup/restore`, runbooks, sealed `Down()` migrations, CI-verified DR round-trip
- Sealed security audit (NuGetAudit mode=all/level=high + transitive pins), k6 load baselines + benchmark regression gate, DocFX docs + tutorial smoke gate, Platformer3D 3D-multiplayer capstone demo
- Shipped alongside the milestone: nuget.org Trusted Publishing (2026-06-27), Apache-2.0 relicense (2026-07-12), marketing site at gamekit.noahfreelove.com

### What Worked
- **Evidence-first UAT closure**: replacing conversational UAT with headless-browser e2e (Playwright chrome-headless-shell + DB assertions) collapsed 13 outstanding UAT items into 1 product question + 2 credential-blocked items in a single day
- **Milestone audit before close**: `/gsd-audit-milestone` caught that the real ship blockers were 3 CI-enforcement wiring gaps (SCALE-02/03, DR-03), not missing functionality — all closed same-day via a parallel workflow
- **Sabotage-proof gate verification**: every new CI gate (lease-release grep, DR round-trip, benchmark regression) was proven by intentionally breaking it and watching it fail
- **Backlog promotion discipline**: Phase 21 (Platformer3D) entered as backlog item 999.1, got promoted with full provenance, and shipped as the capstone

### What Was Inefficient
- **Stale planning metadata accumulated silently**: 3 pre-convention quick tasks lacked `status:` frontmatter and a resolved debug session sat marked `diagnosed` for 7 weeks — all surfaced only at milestone close and had to be forensically closed
- **REQUIREMENTS.md checkbox drift**: the traceability table was authoritative (47/47) but definition checkboxes went stale (15/47), requiring reconciliation at close
- **Worktree-isolation base mismatch**: workflow-spawned worktrees fork from origin/master; with unpushed local commits this caused duplicate-pin merges that needed manual dedup — push cadence should be tighter next milestone
- **Site deployed before relicense landed**: the live site showed GPL badges for a day because deploy and relicense raced in separate sessions

### Patterns Established
- CI gates as first-class requirements (SCALE/DR/PERF gates are REQ-IDs, not habits)
- `CancellationToken.None` on cleanup paths that must survive shutdown, enforced by grep gate
- Per-package `IMigrationReadinessReporter` + latch as the standard readiness contribution
- Transitive CVE pins live in Directory.Packages.props with the GHSA ID in a comment
- Headless-browser e2e as the default UAT mechanism for anything browser-facing

### Key Lessons
1. Run the artifact audit (`audit-open`) at phase boundaries, not just milestone close — stale metadata is cheap to fix fresh and expensive to archaeology later
2. When a verify gate can be wired but not proven, sabotage it: a gate that has never failed is not yet a gate
3. Publishing early (nuget.org during hardening) worked because Trusted Publishing made releases cheap — the "defer publish until perfect" decision was safely reversible
4. Keep origin in sync with local master when using worktree-isolated agents; their base is origin/HEAD, not local HEAD

### Cost Observations
- Model mix: opus for planning/judging, sonnet for execution (per config profile `balanced`)
- Sessions: multiple parallel sessions (relicense ran concurrently with UAT closure on 2026-07-12)
- Notable: the fix-gaps + website build ran as one parallel workflow (judge-panel site design + 3 gap closures) — wall-clock cost of the slowest branch, not the sum

---

## Cross-Milestone Trends

### Process Evolution

| Milestone | Phases | Key Change |
|-----------|--------|------------|
| v1.0 | 6 (+3.1) | Established GSD phase discipline, per-package migrations, coordinated release train |
| v2.0 | 6 | Backlog → promotion flow; isolated high-risk work (account merge) into its own phase |
| v2.1 | 9 | CI-enforcement-as-requirement; evidence-first UAT (headless browser); milestone audit before close |

### Top Lessons (Verified Across Milestones)

1. Integration tests against real Postgres/Redis (Testcontainers, no skip-fallbacks) catch what mocks never will — held across all three milestones
2. Full affected-package suites after each phase, not just phase-scoped tests — sibling regressions hide otherwise (learned v1.0, enforced v2.0+)
3. Automated verification beats conversational UAT for both speed and evidence quality (v2.0 lobby bug found via live sample exercise; v2.1 closed its UAT backlog headlessly)
