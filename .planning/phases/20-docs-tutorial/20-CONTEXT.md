# Phase 20: Docs & Tutorial - Context

**Gathered:** 2026-06-23
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

A developer with only Docker and the .NET SDK can complete the getting-started tutorial in under 15 minutes and reach a working first match with traces visible in Grafana; the DocFX API reference CI gate ensures XML doc coverage never regresses.

**Requirements:** DOCS-01..DOCS-06
**Depends on:** Phase 15, Phase 17, Phase 18, Phase 19 (API surface + all runbooks stable)
**UI hint:** no — docs + tutorial + CI phase (DocFX generates a static site but it's not a hand-built frontend). Plan with `--skip-ui`.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices at Claude's discretion (discuss skipped).

### Requirements (authoritative text)
- **DOCS-01** DocFX (MIT, net10.0) site from the EXISTING XML doc comments (per-package API reference) with a `docfx build --warningsAsErrors` CI gate; built/verified IN-REPO, NOT published to a public site this milestone.
- **DOCS-02** Getting-started tutorial (`dotnet new gamekit` → first authenticated player + first completed match in ~15 min), runnable against the sample with `docker-compose up`, zero cloud credentials; a CI smoke test executes the tutorial path.
- **DOCS-03** Per-package concepts docs (what the package does, the interfaces it exposes, the library-vs-consumer responsibility line).
- **DOCS-04** Upgrade/compatibility guide v2.0 → v2.1 (config additions, new health/observability wiring, any migration-order changes).
- **DOCS-05** Runbook library under `docs/runbooks/` (backup/restore, rolling deploy, migration apply, matchmaking-outage incident response) + ADRs capturing key v1/v2 decisions.
- **DOCS-06** The sample app (`samples/TicTacToeDuel`) kept current with all v2.1 features (observability stack, health endpoints) — both tutorial target AND integration harness.

</decisions>

<code_context>
## Existing Code Insights

- **Already exists (DOCS-05 partial):** `docs/runbooks/postgres-backup-restore.md` + `redis-backup-restore.md` (Phase 17), `docs/migration-ops.md` (Phase 17), `docs/security-checklist.md` (Phase 18), `docs/performance-tuning.md` (Phase 19), plus a rich `docs/ops/` tree (disaster-recovery, multi-replica, migrations-runbook, jwt-keys, air-gapped, container, bare-metal, postgres-roles, redis-aof). Phase 20 ADDS the **rolling-deploy** + **matchmaking-outage incident-response** runbooks, captures **ADRs** for key v1/v2 decisions, and reconciles docs/runbooks vs docs/ops (avoid duplicating — cross-link).
- **Template (DOCS-02):** `templates/GameKit.Templates/content/GameKit.SampleGame/.template.config/template.json` exists. VERIFY the template short-name is `gamekit` (DOCS-02 says `dotnet new gamekit`); if it's `gamekit-sample` or similar, reconcile the tutorial text with the real short-name (or add the `gamekit` short-name).
- **Sample (DOCS-06):** `samples/TicTacToeDuel` already wired with v2.1 health (Phase 14) + observability (Phase 13/15). Verify it's CURRENT (AddGameKitObservability, MapGameKitHealth, the observability compose) and the tutorial steps match reality.
- **DocFX (DOCS-01):** install as a COMMITTED local tool manifest (`.config/dotnet-tools.json` via `dotnet new tool-manifest` + `dotnet tool install docfx`) so CI runs `dotnet docfx`. A global docfx is being installed to warm the cache. `docfx build --warningsAsErrors` is STRICT — it fails on any missing XML doc / broken `<see cref>` cross-reference across ALL src/ packages. Expect to FIX XML-doc gaps surfaced by the strict build (the project constraint is "XML doc on every public API — no exceptions", so most should hold; broken cross-refs are the likely finds).

### CRITICAL for the DOCS-02 tutorial smoke test (from project memory)
- **Port 5433:** the host already runs Postgres on :5432, so the sample/tutorial must run the gamekit DB on **:5433** (the TicTacToeDuel sample) — the tutorial + CI smoke test must use the port the sample actually binds.
- **Matchmaking default pool:** TicTacToeDuel only pairs tickets in the `default` pool — enqueue with **NO poolName** to form a real match (a `poolName` example never matches). The tutorial's "first completed match" step + the CI smoke test MUST enqueue with no poolName, or the match never forms and the smoke test hangs/fails.
- **Dev admin creds:** sample-app `root` login exists (see memory) for any admin-console tutorial step.

</code_context>

<specifics>
## Specific Ideas

- The CI smoke test (DOCS-02) is the highest-risk deliverable: it must `docker-compose up` the sample, create/authenticate a player, enqueue two tickets (NO poolName) to form a match, complete it, and assert `/health/ready` (and ideally a trace in the stack). Reuse existing Testcontainers/sample harnesses. It must be reproducible offline (zero cloud creds).
- DocFX config (`docfx.json`) should scope metadata to `src/**` packages, output in-repo (e.g. `_site/` gitignored, or `docs/api/`), and the CI gate runs `dotnet docfx docfx.json --warningsAsErrors`.
- Build clean WITHOUT `-p:NuGetAudit=false` (Phase 18 fixed the CVE; gate is on).
- Keep docs honest: every command/flag in the tutorial + concepts docs must reference what ACTUALLY shipped (verify against the real CLI, endpoints, and options).

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped. (Public docs-site publishing is explicitly out of scope this milestone per DOCS-01.)

</deferred>
