---
phase: 04-rankings-sessions-gdpr
plan: "01"
subsystem: rankings-test-infrastructure
tags:
  - license-compliance
  - test-scaffolding
  - glicko2
  - vendor-attribution
dependency_graph:
  requires: []
  provides:
    - THIRD-PARTY-NOTICES.md (BSD-3-Clause attribution for Glicko-2 vendoring)
    - tests/GameKit.Rankings.Tests csproj
    - tests/GameKit.Rankings.Integration.Tests csproj
    - tests/GameKit.TestFixtures/RankingsFixture.cs
    - tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json
  affects:
    - GameKit.sln (two new test project entries)
    - REUSE.toml (Glicko2/*.cs BSD-3-Clause AND GPL-3.0-or-later annotation)
tech_stack:
  added:
    - BSD-3-Clause vendored Glicko-2 attribution (MaartenStaa/glicko2-csharp commit 59033eec)
  patterns:
    - Composite fixture pattern (RankingsFixture mirrors AuthIntegrationFixture shape)
    - Testcontainers integration test pattern (PostgresFixture + RedisFixture)
    - REUSE.toml override annotation for dual-licensed vendored source
key_files:
  created:
    - THIRD-PARTY-NOTICES.md
    - tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj
    - tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj
    - tests/GameKit.TestFixtures/RankingsFixture.cs
    - tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json
    - tests/GameKit.Rankings.Tests/Glicko2/Fixtures/THIRD-PARTY-NOTICES.md
  modified:
    - REUSE.toml
    - GameKit.sln
decisions:
  - "Verified BSD-3-Clause (not MIT) for MaartenStaa/glicko2-csharp — user approved bsd-3"
  - "RankingsFixture omits WireMockFixture — Rankings has no outbound HTTP"
  - "Glickman_Worked_Example.json is the deterministic anchor for plan 04-03 RANK-05"
metrics:
  duration: "~15min"
  completed: "2026-05-16T03:00:48Z"
  tasks: 3
  files: 8
requirements:
  - RANK-01
  - RANK-05
  - RANK-06
  - RANK-14
---

# Phase 04 Plan 01: License Verification + Test Infrastructure Summary

**One-liner:** BSD-3-Clause attribution locked for MaartenStaa/glicko2-csharp vendoring; Rankings unit + integration test csprojs scaffolded; Glickman §3.1 worked-example fixture committed.

## What Was Built

### Task 1 — License Attribution (83ca444)
Verified the MaartenStaa/glicko2-csharp upstream LICENSE by reading directly from the cloned repository at commit `59033eeca27a49a444897430dc0a63a33bc99870`. The license is **BSD-3-Clause** — the three-clause (non-endorsement) variant. See correction note below.

Files created/modified:
- `THIRD-PARTY-NOTICES.md` (new at repo root): verbatim BSD-3-Clause license text byte-for-byte, upstream URL + commit SHA, SPDX identifier, and the per-file vendoring header that plan 04-03 must use.
- `REUSE.toml`: new `[[annotations]]` block with `precedence = "override"` for `src/GameKit.Rankings/Glicko2/*.cs`, declaring `SPDX-License-Identifier = "BSD-3-Clause AND GPL-3.0-or-later"`.
- `tests/GameKit.Rankings.Tests/Glicko2/Fixtures/THIRD-PARTY-NOTICES.md` (new): per-folder REUSE-compliant re-statement of the same attribution.

### Task 2 — Test Project Scaffolding (d7bd6d5)
Created two csproj files mirroring the Auth analog pair exactly:
- `tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj`: unit tests, references `GameKit.Rankings` + `GameKit.TestFixtures` + Moq + EF InMemory.
- `tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj`: integration tests, references `GameKit.Rankings` + `GameKit.TestFixtures` + Npgsql + StackExchange.Redis + `Microsoft.AspNetCore.Mvc.Testing` + Testcontainers.PostgreSql + Testcontainers.Redis.
- Both added to `GameKit.sln` via `dotnet sln add`. Both build with 0 warnings and 0 errors.

### Task 3 — RankingsFixture + Glickman JSON Fixture (65054c6)
- `tests/GameKit.TestFixtures/RankingsFixture.cs`: composite of `PostgresFixture` + `RedisFixture` (WireMock excluded — Rankings has no outbound HTTP). GPL-3.0-or-later SPDX header present.
- `tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json`: Glickman (2012) §3.1 worked example fixture — player(1500,200,0.06) vs opp1(1400,30,0.06), opp2(1550,100,0.06), opp3(1700,300,0.06); outcomes win/loss/loss; algorithm params tau=0.5, initVolatility=0.06; expected outputs rating=1464.05 (±0.5), rd=151.52 (±0.5), volatility=0.05999 (±0.0001).

## Critical: License Variant Correction for Plan 04-03

**CLAUDE.md says:** "MIT" for MaartenStaa/glicko2-csharp
**04-CONTEXT.md says:** "MIT" for this dependency
**ACTUAL LICENSE (verified by git clone at commit 59033eec):** `BSD-3-Clause`

The three-clause BSD differs from two-clause BSD by the presence of the non-endorsement clause:
> "Neither the name of glicko2-csharp nor the names of its contributors may be used to endorse or promote products derived from this software without specific prior written permission."

Plan 04-03 MUST use the following per-file header verbatim in all `src/GameKit.Rankings/Glicko2/*.cs` files:

```csharp
// SPDX-License-Identifier: BSD-3-Clause AND GPL-3.0-or-later
// Original work Copyright (c) 2015, Maarten Staa (BSD-3-Clause)
// https://github.com/MaartenStaa/glicko2-csharp commit 59033eec
// Modified work Copyright (c) 2026 GameKit contributors (GPL-3.0-or-later)
```

**Action required (phase close-out):** CLAUDE.md and `04-CONTEXT.md` both contain incorrect "MIT" references that must be corrected to "BSD-3-Clause" during phase 04 close-out (plan 04-08 or a dedicated cleanup plan).

## Deviations from Plan

### Auto-fixed Issues

None — plan executed exactly as written (after the human-approved checkpoint for license variant).

### CLAUDE.md-Driven Adjustments

None. No CLAUDE.md directives were contradicted by plan instructions.

## Verification Results

| Check | Result |
|-------|--------|
| `grep -E "(BSD-3-Clause)" THIRD-PARTY-NOTICES.md` | PASS — exactly one match |
| `grep -q "1464.05" Glickman_Worked_Example.json` | PASS |
| `grep -q "PostgresFixture" RankingsFixture.cs` | PASS |
| `grep -q "RedisFixture" RankingsFixture.cs` | PASS |
| `dotnet build GameKit.Rankings.Tests` | PASS — 0 warnings, 0 errors |
| `dotnet build GameKit.Rankings.Integration.Tests` | PASS — 0 warnings, 0 errors |
| `dotnet sln list \| grep Rankings` (test entries) | PASS — 2 entries |
| `dotnet build GameKit.TestFixtures` | PASS — 0 warnings, 0 errors |

## Commit Log

| Task | Commit | Message |
|------|--------|---------|
| 1 | 83ca444 | docs(04-01): vendor BSD-3-Clause attribution for MaartenStaa/glicko2-csharp |
| 2 | d7bd6d5 | feat(04-01): scaffold GameKit.Rankings.Tests + GameKit.Rankings.Integration.Tests csprojs |
| 3 | 65054c6 | feat(04-01): add RankingsFixture composite and Glickman worked-example JSON fixture |

## Known Stubs

None.

## Threat Flags

None — no new network endpoints, auth paths, or schema changes introduced in this plan. The vendoring attribution (T-04-01-SC) was mitigated by Task 1 as planned.

## Self-Check: PASSED

- THIRD-PARTY-NOTICES.md: EXISTS
- REUSE.toml updated: VERIFIED (BSD-3-Clause AND GPL-3.0-or-later annotation present)
- tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj: EXISTS
- tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj: EXISTS
- tests/GameKit.TestFixtures/RankingsFixture.cs: EXISTS
- tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json: EXISTS (contains 1464.05)
- tests/GameKit.Rankings.Tests/Glicko2/Fixtures/THIRD-PARTY-NOTICES.md: EXISTS
- Commits 83ca444, d7bd6d5, 65054c6: VERIFIED in git log
