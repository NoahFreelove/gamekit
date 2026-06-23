---
phase: 21
slug: final-demo-3d-multiplayer-platformer
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-06-22
---

# Phase 21 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> Source of truth for the R1–R11 → test map: `21-RESEARCH.md` § Validation Architecture.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Moq 4.20.72 |
| **Integration** | Testcontainers 4.11.0 (Postgres + Redis) |
| **Config file** | `tests/GameKit.Platformer3D.Tests/*.csproj` + `tests/GameKit.Platformer3D.Integration.Tests/*.csproj` (Wave 0: create) |
| **Quick run command** | `dotnet test tests/GameKit.Platformer3D.Tests/` (unit only) |
| **Full suite command** | `dotnet test tests/GameKit.Platformer3D.Tests/ tests/GameKit.Platformer3D.Integration.Tests/` |
| **Estimated runtime** | ~30s unit; integration dominated by Testcontainers spin-up |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/GameKit.Platformer3D.Tests/` (unit, < 30s)
- **After every plan wave:** Run the full suite (unit + Testcontainers integration)
- **Before `/gsd-verify-work`:** Full suite green, `reuse lint` passes, `docker compose up` smoke passes
- **Max feedback latency:** ~30 seconds (unit tier)

---

## Per-Task Verification Map

> Populated by the planner against actual PLAN task IDs. The authoritative
> requirement→test mapping is `21-RESEARCH.md` § Validation Architecture
> (R1–R11 with concrete `dotnet test --filter` commands). Each PLAN task's
> `<acceptance_criteria>` must carry the matching automated command from that map.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| _(planner fills per task)_ | | | | | | | | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/GameKit.Platformer3D.Tests/*.csproj` — unit project (strategy + algorithm)
- [ ] `tests/GameKit.Platformer3D.Tests/Strategy/BestTimeProximityStrategyTests.cs` — bracket math, cold-start (RD≥300), queue-time widening, `Name != "elo-range"`
- [ ] `tests/GameKit.Platformer3D.Tests/Rankings/*AlgorithmTests.cs` — fixed-delta Win/Loss/Draw, exact-tie draw edge, batched-only, `Name != "glicko2"`
- [ ] `tests/GameKit.Platformer3D.Integration.Tests/*.csproj` — Testcontainers (Postgres + Redis)
- [ ] `…/Smoke/EndToEndSmokeTests.cs` — full loop (guest → party → matchmake → result → leaderboard), idempotency double-post, concurrent parties
- [ ] `…/Auth/PlayerJwtRejectedTests.cs` — negative: player JWT → 401/403 on session-complete
- [ ] `…/Lobby/LobbyToMatchTests.cs` — party → ready-check → 1v1; decline/timeout/disconnect → zero tickets, party intact
- [ ] `reuse` CLI — not installed on this host; Wave 0 must `pip install reuse` (or `pipx install reuse`) for the R11 lint gate

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Browser renders the 3D level and a run is completable | R2 | WebGL render fidelity is not unit-assertable | Open the served client, complete one timed run start→checkpoints→finish |
| Admin console surfaces live demo players/matches/sessions | R3/R4 | Blazor UI acceptance | Open `/admin`, observe live demo activity after playing a match |

*Automated gates cover the protocol/idempotency/auth/packaging surface; the two visual behaviors above are the only manual checks.*

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references (incl. `reuse` CLI install)
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s (unit tier)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
