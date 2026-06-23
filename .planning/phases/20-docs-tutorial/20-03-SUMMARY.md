---
phase: 20-docs-tutorial
plan: "03"
subsystem: docs-and-ci
tags: [docs, ci, smoke-test, matchmaking, tutorial, DOCS-02]
dependency_graph:
  requires: ["20-01", "20-02"]
  provides: [tutorial-smoke-test, ci-gate-docs-02, getting-started-tutorial]
  affects: [tests/GameKit.Tutorial.SmokeTests, .github/workflows/ci.yml, docs/tutorial]
tech_stack:
  added:
    - "tests/GameKit.Tutorial.SmokeTests: new xUnit integration-test project (mirrors OpenApiTestApp)"
    - "TutorialSmokeTestApp: hand-rolled in-process host with in-process matchmaking ticker"
    - "TutorialRuntimeModelCustomizer: applies all package entity configs (FOLLOW-UP-02-03-01)"
  patterns:
    - "Testcontainers Postgres + Redis fixtures (PostgresFixture / RedisFixture)"
    - "Ephemeral RSA 2048 PEM keypair under %TEMP% for JWT issuance"
    - "Fresh per-host DB (5 migration passes: Core → Auth → Admin → Rankings → Matchmaking)"
    - "StartupLadderUpserter seeds tictactoe ladder Guid at host startup"
    - "Deadline-bounded status poll (10s, Assert.Fail — never hangs)"
key_files:
  created:
    - tests/GameKit.Tutorial.SmokeTests/GameKit.Tutorial.SmokeTests.csproj
    - tests/GameKit.Tutorial.SmokeTests/CollectionDefinitions.cs
    - tests/GameKit.Tutorial.SmokeTests/TutorialRuntimeModelCustomizer.cs
    - tests/GameKit.Tutorial.SmokeTests/TutorialSmokeTestApp.cs
    - tests/GameKit.Tutorial.SmokeTests/TutorialHostBootTests.cs
    - tests/GameKit.Tutorial.SmokeTests/TutorialSmokeTests.cs
    - docs/tutorial/getting-started.md
  modified:
    - src/GameKit.Auth/AssemblyInfo.cs (added InternalsVisibleTo GameKit.Tutorial.SmokeTests)
    - src/GameKit.Rankings/AssemblyInfo.cs (added InternalsVisibleTo GameKit.Tutorial.SmokeTests)
    - src/GameKit.Matchmaking/AssemblyInfo.cs (added InternalsVisibleTo GameKit.Tutorial.SmokeTests)
    - src/GameKit.Admin.UI/AssemblyInfo.cs (added InternalsVisibleTo GameKit.Tutorial.SmokeTests)
    - .github/workflows/ci.yml (added Pre-pull images + Tutorial smoke test steps)
    - GameKit.sln (added GameKit.Tutorial.SmokeTests project)
    - README.md (added Getting Started link to tutorial)
decisions:
  - "Hand-rolled in-process host (TutorialSmokeTestApp) not WebApplicationFactory — mirrors OpenApiTestApp, avoids internal Program type + on-disk JWT key dependency"
  - "poolName null in EnqueueRequest — routes to 'default' pool; named pool 'tictactoe' never forms a match in TicTacToeDuel"
  - "10s deadline-bounded status poll with Assert.Fail — never hangs CI; 100ms poll interval (ticker fires at 500ms)"
  - "CI smoke step gated to push:main only — PR gate already covered by solution-wide integration step"
  - "Pre-pull postgres:17.9 + redis:8.6.2 verbatim from fixtures to bound first-run latency"
metrics:
  duration: "~20 minutes"
  completed: "2026-06-23"
  tasks_completed: 4
  files_created: 7
  files_modified: 7
status: complete
---

# Phase 20 Plan 03: Tutorial Smoke Test + Getting-Started Tutorial Summary

**One-liner:** DOCS-02 delivered — in-process TutorialSmokeTestApp proves the tutorial happy-path (guest login x2 → enqueue with poolName null → proposal forms → both accepts confirmed matched → /health/ready 200) with a dedicated CI gate on push:main.

## Tasks Completed

| # | Task | Status | Commit |
|---|------|--------|--------|
| 1 | TutorialSmokeTestApp + host-boot gate (DOCS-02) | PASSED | a46a92d |
| 2 | Tutorial happy-path smoke test — non-null ProposalId through both accepts | PASSED | 736e6f7 |
| 3 | Wire smoke test into CI (DOCS-02 dedicated step) | DONE | 5da87c0 |
| 4 | Write getting-started tutorial prose (docs/tutorial/getting-started.md) | DONE | d4f0978 |

## Test Results

Both smoke tests pass against live Testcontainers (zero cloud credentials):

```
Passed DOCS-02: tutorial host boots with a running matchmaking ticker [2 s]
Passed DOCS-02: tutorial happy-path forms a match and reaches readiness [971 ms]

Test Run Successful.
Total tests: 2
     Passed: 2
 Total time: 6.5 Seconds
```

**Match genuinely formed:** the in-process ticker produced proposal `7f4745de-...` within the
first 500 ms cycle; ProposalId was non-null; second accept returned `status: "matched"` with the
same ProposalId; `/health/ready` returned 200.

## Key Deliverables

### TutorialSmokeTestApp (Task 1)

- Mirrors `OpenApiTestApp.cs` exactly: ephemeral RSA 2048 keypair under `%TEMP%`, fresh per-host
  Postgres DB via `PostgresFixture`, Redis via `RedisFixture`
- Runs 5 migration passes (Core → Auth → Admin → Rankings → Matchmaking) before host startup
- Registers full Add* chain: AddAuth, AddRankings().AddLadder("tictactoe"), AddMatchmaking(500ms
  tick).AddLadder("tictactoe"), AddPresence, AddGameKitAdmin, AddGameKitHealthChecks
- StartupLadderUpserter seeds the tictactoe Ladder row during host startup; TicTacToeLadderId
  resolves by querying `db.Set<Ladder>().Where(l => l.Name == "tictactoe")` after startup
- `CreateClient(deviceId)` returns an HttpClient with `X-GameKit-Device` pre-set
- `TutorialRuntimeModelCustomizer` applies Auth + Admin + Rankings + Matchmaking entity configs
  to bypass the FOLLOW-UP-02-03-01 ApplicationServiceProvider issue

### Smoke Test (Task 2)

- `GuestLoginAsync`: POST /auth/login/guest, asserts 200, extracts non-empty access token
- `EnqueueAsync`: POST /api/mm/queue with explicit `poolName: null` (comment explains why)
- `PollUntilProposedAsync`: deadline-bounded 10s loop, 100ms poll interval, Assert.Fail on timeout
- First accept: `Status: "queued"` with ProposalId (one player pending)
- Second accept: asserts `Status == "matched"` and `ProposalId == proposalId` (round-trip proof)
- Final GET /health/ready asserts 200

### CI Integration (Task 3)

Added to `.github/workflows/ci.yml` in `build-and-test` job, gated to push:main:

1. "Pre-pull Testcontainers images (DOCS-02)": `docker pull postgres:17.9` + `docker pull redis:8.6.2`
   (tags verbatim from PostgresFixture / RedisFixture — no invented tags)
2. "Tutorial smoke test (DOCS-02)": `dotnet test tests/GameKit.Tutorial.SmokeTests/...
   --no-build -c Release --filter "Category=Integration"`

No existing CI gate was altered or weakened. No `-p:NuGetAudit=false` introduced.

### Getting-Started Tutorial (Task 4)

`docs/tutorial/getting-started.md` — 304 lines, 9 steps:
- Step 1: local template install (`dotnet new install ./templates/GameKit.Templates` + `dotnet new gamekit`)
- Step 2: `docker compose -f samples/TicTacToeDuel/docker-compose.yml up -d`
- Step 3: `bash samples/TicTacToeDuel/scripts/gen-test-rsa-pem.sh`
- Step 4: explicit port-5433 env-var override (connection string correction)
- Step 5: `dotnet run --project samples/TicTacToeDuel`
- Step 6: Play as Guest (POST /auth/login/guest + X-GameKit-Device; XSS callout echoed)
- Step 7: two-tab Find Match with `poolName: null` + accept x2 + GET /health/ready 200
- Step 8: optional Admin console (`dotnet gamekit admin create`)
- Step 9: optional observability stack (docker-compose.observability.yml → Grafana :3000)

Every command/endpoint/flag verified against the real CLI + endpoints. poolName null explicitly
documented (not a named pool). localStorage XSS callout echoed from matchmaking.html.

README.md updated with a "Getting Started" link pointing at the tutorial.

## Deviations from Plan

None — plan executed exactly as written.

The TutorialSmokeTestApp required InternalsVisibleTo grants to Auth, Rankings, Matchmaking,
and Admin.UI assemblies (matching the OpenApiTestApp pattern). These were added to all four
AssemblyInfo.cs files as planned.

## Threat Flags

None. No new network endpoints, auth paths, or trust-boundary changes introduced. The smoke
test and CI step operate within the existing Testcontainers security boundary. The tutorial
echoes the existing localStorage XSS warning rather than introducing a new one.

## Known Stubs

None. The smoke test drives a real match to completion (not a mock). The tutorial prose
documents the actual shipped endpoints and commands.

## Self-Check: PASSED

All created files exist:
- [x] tests/GameKit.Tutorial.SmokeTests/GameKit.Tutorial.SmokeTests.csproj
- [x] tests/GameKit.Tutorial.SmokeTests/TutorialSmokeTestApp.cs
- [x] tests/GameKit.Tutorial.SmokeTests/TutorialHostBootTests.cs
- [x] tests/GameKit.Tutorial.SmokeTests/TutorialSmokeTests.cs
- [x] docs/tutorial/getting-started.md
- [x] .github/workflows/ci.yml contains "GameKit.Tutorial.SmokeTests"

All commits exist in git log:
- [x] a46a92d: feat(20-03): add TutorialSmokeTestApp + host-boot gate (DOCS-02 Task 1)
- [x] 736e6f7: feat(20-03): tutorial happy-path smoke test (DOCS-02 Task 2)
- [x] 5da87c0: feat(20-03): wire tutorial smoke test into CI (Task 3)
- [x] d4f0978: docs(20-03): write getting-started tutorial + README link (Task 4)
