---
phase: 21
slug: final-demo-3d-multiplayer-platformer
status: ready
nyquist_compliant: true
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

> Populated by the planner against actual PLAN task IDs (plan revision 2026-06-22).
> The PLAN `<verify><automated>` blocks are authoritative; `21-RESEARCH.md`
> § Validation Architecture has been reconciled to these exact filter strings.
> Each command below is copied verbatim from that task's `<verify><automated>` /
> `<acceptance_criteria>` block (all carry `-p:NuGetAudit=false` per the pre-existing
> MessagePack NU1903 memory).

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 21-01 T1 | 21-01 | 1 | R11 (enabler) | T-21-SC | `reuse` CLI present for license gate | host tooling | `command -v reuse && reuse --version` | ❌ W0 | ⬜ pending |
| 21-01 T2 | 21-01 | 1 | R1 | T-21-01/02/03 | new projects build; TicTacToeDuel + src/ untouched | build gate | `dotnet build samples/Platformer3D/Platformer3D.csproj -p:NuGetAudit=false && dotnet build samples/TicTacToeDuel/TicTacToeDuel.csproj -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-01 T3 | 21-01 | 1 | R1, R11 | T-21-SC | test projects build; SPDX/reuse baseline green | build + lint | `dotnet test tests/GameKit.Platformer3D.Tests/ -p:NuGetAudit=false && reuse lint 2>&1 \| tail -3` | ❌ W0 | ⬜ pending |
| 21-02 T1 | 21-02 | 2 | R6 (D-09/D-10/D-11) | T-21-04/06 | fixed-delta Win/Loss/Draw; exact-tie draw symmetric; batched-only | unit | `dotnet test tests/GameKit.Platformer3D.Tests/ --filter "FullyQualifiedName~TimeMarginRankingAlgorithm" -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-02 T2 | 21-02 | 2 | R5 (D-06/D-07/D-08) | T-21-05 | Name != elo-range; in/out window; cold-start; stateless | unit | `dotnet test tests/GameKit.Platformer3D.Tests/ --filter "FullyQualifiedName~BestTimeMatchmakingStrategy" -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-03 T1 | 21-03 | 2 | R11 | T-21-10 | three.js vendored MIT; reuse lint; version string consistent NOTICES↔REUSE.toml | lint + grep | `reuse lint 2>&1 \| tail -5; test -f .../three.module.js && grep -q 'three.js' THIRD-PARTY-NOTICES.md && grep -q 'three.module.js' REUSE.toml` + `TAG=$(curl -s .../releases/latest \| grep -oP '"tag_name":\s*"\K[^"]+'); grep -qF "$TAG" THIRD-PARTY-NOTICES.md && grep -qF "$TAG" REUSE.toml` | ❌ W0 | ⬜ pending |
| 21-03 T2 | 21-03 | 2 | R2, R8 | T-21-07/08/09 | no CDN/analytics egress; guest button; run-summary frame; no PII | grep gate | `! grep -rEiq 'https?://(cdn\|unpkg\|cdnjs\|fonts\.googleapis\|jsdelivr\|google-analytics\|googletagmanager)' samples/Platformer3D/wwwroot/ && grep -q 'btn-guest' .../index.html && grep -q '/auth/login/guest' .../game.js && grep -q 'run_finish' .../game.js` | ❌ W0 | ⬜ pending |
| 21-04 T1 | 21-04 | 3 | R4, R5, R6 | T-21-11..15 | admin mounted; ladder Algorithm=time-margin; A3 services.Replace; WS after auth | build + grep | `dotnet build samples/Platformer3D/Platformer3D.csproj -p:NuGetAudit=false` (+ `grep -q 'Replace(ServiceDescriptor.Singleton<IMatchmakingStrategy, BestTimeMatchmakingStrategy>' samples/Platformer3D/Program.cs`) | ❌ W0 | ⬜ pending |
| 21-04 T2 | 21-04 | 3 | R7 (D-03) | T-21-12 | run-summary monotonic/plausible/one-finish sanity validation | unit | `dotnet test tests/GameKit.Platformer3D.Tests/ --filter "RunSummary" -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-04 T3 | 21-04 | 3 | R7, R9 (D-04/D-05/D-13) | T-21-13/14/15 | in-process token (revoke-then-issue); idempotent service-token completion; no leak | build + source review | `dotnet build samples/Platformer3D.GameServer/Platformer3D.GameServer.csproj -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-04 T4 | 21-04 | 3 | R7 (D-05/D-10) | T-21-13 | **Docker-free** duplicate-key → AlreadyCompletedCached → one outcome; tie→Draw mapping | unit (mocked) | `dotnet test tests/GameKit.Platformer3D.Tests/ --filter "IdempotentCompletion" -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-05 T1 | 21-05 | 4 | R3 (D-14) | T-21-18 | multi-stage image builds (or publish fallback); NuGetAudit=false; no CDN | build gate | `docker build -f samples/Platformer3D/Dockerfile -t platformer3d:planverify --build-arg NUGET_AUDIT=false .. 2>&1 \| tail -20 \|\| dotnet publish samples/Platformer3D/Platformer3D.csproj -c Release -o /tmp/claude-1000/p3dpub -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-05 T2 | 21-05 | 4 | R3, R11 | T-21-16/17/19 | only app port published; offline tarball documented; reuse lint green | lint + compose parse | `reuse lint 2>&1 \| tail -3` (compose-port asserted by 21-06 T1 ComposePort test) | ❌ W0 | ⬜ pending |
| 21-06 T1 | 21-06 | 5 | R5, R7, R8, R3 (ports) | T-21-20/23/24 | resolved strategy is custom; guest no-PII; player-JWT 401/403; pg/redis no host ports | integration + file-parse | `dotnet test tests/GameKit.Platformer3D.Integration.Tests/ --filter "Resolution\|Guest\|PlayerJwt\|ComposePort" -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-06 T2 | 21-06 | 5 | R9, R10, R7 | T-21-21/22 | party→1v1 + abort zero-tickets; full loop + double-post idempotent + re-run + concurrent parties | integration (Docker-gated) | `dotnet test tests/GameKit.Platformer3D.Integration.Tests/ --filter "LobbyToMatch\|FullLoop\|Concurrent\|ReadyCheck" -p:NuGetAudit=false` | ❌ W0 | ⬜ pending |
| 21-06 T3 | 21-06 | 5 | R2, R3, R4 | — | browser renders playable level; admin shows live data + empty states; offline stack healthy | human-verify (blocking) | manual: `docker compose -f samples/Platformer3D/docker-compose.yml up --build` → play → `/admin` → offline `docker save`/`load` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*
*File Exists is ❌ W0 (Wave-0 pending) until execution creates the artifacts; execution flips these as tasks land.*

---

## Wave 0 Requirements

> Reconciled to the actual PLAN test file/class names (plan revision 2026-06-22).

- [ ] `tests/GameKit.Platformer3D.Tests/GameKit.Platformer3D.Tests.csproj` — unit project (strategy + algorithm + GameServer) [21-01 T3]
- [ ] `tests/GameKit.Platformer3D.Tests/Strategy/BestTimeMatchmakingStrategyTests.cs` — bracket math, cold-start (RD≥300), queue-time widening, `Name != "elo-range"` (scaffold 21-01 T3 → real 21-02 T2)
- [ ] `tests/GameKit.Platformer3D.Tests/Rankings/TimeMarginRankingAlgorithmTests.cs` — fixed-delta Win/Loss/Draw, exact-tie draw edge, batched-only, `Name != "glicko2"` (scaffold 21-01 T3 → real 21-02 T1; also hosts RunSummary validator tests 21-04 T2)
- [ ] `tests/GameKit.Platformer3D.Tests/GameServer/IdempotentCompletionUnitTests.cs` — **Docker-free** R7: duplicate Idempotency-Key → AlreadyCompletedCached → one outcome; tie→Draw mapping [21-04 T4]
- [ ] `tests/GameKit.Platformer3D.Integration.Tests/GameKit.Platformer3D.Integration.Tests.csproj` + `PlatformerIntegrationFixture.cs` — Testcontainers (Postgres + Redis) [21-01 T3]
- [ ] `…/Strategy/BestTimeStrategyResolutionTests.cs` — resolved IMatchmakingStrategy is custom + match forms (R5) [21-06 T1]
- [ ] `…/Auth/GuestOnboardingTests.cs` — guest → matchmaking, no PII (R8) [21-06 T1]
- [ ] `…/Auth/PlayerJwtRejectedTests.cs` — negative: player JWT → 401/403 on session-complete (R7) [21-06 T1]
- [ ] `…/Packaging/ComposePortMappingTests.cs` — pg/redis no host ports (must-NOT; no Testcontainers) [21-06 T1]
- [ ] `…/Lobby/LobbyToMatchTests.cs` — party → ready-check → 1v1; decline/timeout/disconnect → zero tickets, party intact (R9) [21-06 T2]
- [ ] `…/Smoke/EndToEndSmokeTests.cs` — full loop, idempotency double-post (Docker-gated), re-run, concurrent parties (R10) [21-06 T2]
- [ ] `reuse` CLI — not installed on this host; Wave 0 must `pipx install reuse` (or `pip install --user reuse`) for the R11 lint gate [21-01 T1]

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Browser renders the 3D level and a run is completable | R2 | WebGL render fidelity is not unit-assertable | Open the served client, complete one timed run start→checkpoints→finish |
| Admin console surfaces live demo players/matches/sessions | R3/R4 | Blazor UI acceptance | Open `/admin`, observe live demo activity after playing a match |

*Automated gates cover the protocol/idempotency/auth/packaging surface; the two visual behaviors above are the only manual checks.*

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies (Per-Task Verification Map populated; 21-06 T3 is the single blocking human-verify, allowed for R2/R3/R4 visual+offline checks)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references (incl. `reuse` CLI install)
- [x] No watch-mode flags
- [x] Feedback latency < 30s (unit tier)
- [x] `nyquist_compliant: true` set in frontmatter
- [ ] `wave_0_complete: true` (set by execution once Wave-0 scaffolds land)

**Approval:** map complete + consistent with PLAN `<verify>` blocks; `nyquist_compliant` set. Awaiting execution.
