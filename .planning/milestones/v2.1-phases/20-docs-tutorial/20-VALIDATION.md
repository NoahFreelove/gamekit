---
phase: 20
slug: docs-tutorial
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-23
---

# Phase 20 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | DocFX (MIT) build gate + xUnit `WebApplicationFactory` smoke test + markdown presence/link checks |
| **Quick run command** | `dotnet tool run docfx metadata docfx.json --warningsAsErrors` (the gate) |
| **Tutorial smoke** | `dotnet test tests/<smoke>.Tests --filter Tutorial` (WebApplicationFactory boots TicTacToeDuel) |
| **Estimated runtime** | docfx build: ~30–90s; smoke test: ~30–120s |

---

## Sampling Rate

- **After every task commit:** the affected gate (docfx build for docs tasks; smoke test for tutorial task)
- **After every plan wave:** full `dotnet docfx --warningsAsErrors` + the smoke test
- **Before verification:** docfx gate exits 0; tutorial smoke test green; all DOCS-05 runbook/ADR files present
- **Max feedback latency:** ~120s

---

## Per-Task Verification Map

| Task | Requirement | Secure Behavior | Test Type | Automated Command | Status |
|------|-------------|-----------------|-----------|-------------------|--------|
| DocFX manifest + docfx.json + gate | DOCS-01 | `docfx --warningsAsErrors` exits 0 (after Directory.Build.props dup-AdditionalFiles fix); CI gate on main | build gate | `dotnet tool run docfx docfx.json --warningsAsErrors` → exit 0 | ⬜ |
| Getting-started tutorial + CI smoke | DOCS-02 | guest login → enqueue 2 (poolName=null) → match forms → complete → /health/ready 200 | integration (WebApplicationFactory) | `dotnet test … --filter Tutorial` | ⬜ |
| Per-package concepts docs | DOCS-03 | docs/concepts/<pkg>.md for each src package (purpose, interfaces, lib-vs-consumer line) | docs presence | `for p in core auth …; do test -f docs/concepts/$p.md; done` | ⬜ |
| Upgrade v2.0→v2.1 guide | DOCS-04 | docs/upgrade-v2.1.md lists AddGameKitObservability/Health, MessagePack pin+audit, ILeaderLease, migration markers | docs presence + content | `test -f docs/upgrade-v2.1.md && grep -q …` | ⬜ |
| Runbooks + ADRs | DOCS-05 | docs/runbooks/rolling-deploy.md + matchmaking-outage.md added; docs/adr/ ADRs; existing runbooks cross-linked not duplicated | docs presence | `test -f docs/runbooks/rolling-deploy.md && test -d docs/adr` | ⬜ |
| Sample currency + poolName fix | DOCS-06 | matchmaking.html poolName: null (not "tictactoe"); sample v2.1-current | static check | `grep -q 'poolName' samples/.../matchmaking.html` asserts null/absent | ⬜ |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] DocFX local tool manifest (`.config/dotnet-tools.json`) + `docfx.json` + the 2-line `Directory.Build.props` dup-AdditionalFiles fix (DOCS-01) — the gate must exit 0
- [ ] Tutorial CI smoke test (DOCS-02) — WebApplicationFactory booting TicTacToeDuel, NO poolName on enqueue
- [ ] `samples/.../matchmaking.html` poolName fix (DOCS-06) — prerequisite for an honest tutorial
- [ ] `docs/runbooks/rolling-deploy.md`, `docs/runbooks/matchmaking-outage.md`, `docs/adr/` (DOCS-05)

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Tutorial completes in <15 min for a fresh dev | DOCS-02 | the "15-minute" UX claim is human-judged | A human follows the tutorial from scratch; the CI smoke test proves the path WORKS, not the timing |
| Concepts/upgrade/runbook prose accuracy | DOCS-03/04/05 | prose quality + correctness is human-reviewed | Spot-check that every cited command/flag/endpoint matches what shipped |

*The functional gates (docfx exit 0, smoke test green, file presence) are automated; only prose quality + the 15-min UX claim are manual.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 120s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
