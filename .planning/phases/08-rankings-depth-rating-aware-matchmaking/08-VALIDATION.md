---
phase: 8
slug: rankings-depth-rating-aware-matchmaking
status: planned
nyquist_compliant: true
wave_0_complete: false
created: 2026-06-05
---

# Phase 8 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11 (Postgres + Redis) + Moq |
| **Config file** | none — tests added to existing `tests/GameKit.Rankings.*Tests` + `tests/GameKit.Matchmaking.*Tests` |
| **Quick run command** | `dotnet test <project> --nologo` |
| **Full suite command** | `dotnet test GameKit.sln --nologo` |
| **Estimated runtime** | unit < 30s; integration (Testcontainers PG+Redis) ~1-3 min |

---

## Sampling Rate

- **After every task commit:** quick (unit) command for the affected project
- **After every plan wave:** full suite
- **Before verify:** full suite green
- **Max feedback latency:** ~30s unit / minutes integration

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | Status |
|---------|------|------|-------------|-----------|-------------------|--------|
| 08-01 T1 | 08-01 | 1 | RANK-15, RANK-16 | build | `dotnet build src/GameKit.Rankings/GameKit.Rankings.csproj --nologo` | ⬜ pending |
| 08-01 T2 | 08-01 | 1 | RANK-15 | unit | `dotnet test tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj --filter "FullyQualifiedName~Glicko2Inactivity" --nologo` | ⬜ pending |
| 08-01 T3 | 08-01 | 1 | RANK-16 | integration (PG) | `dotnet test tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj --filter "FullyQualifiedName~SchemaFreeze" --nologo` | ⬜ pending |
| 08-02 T1 | 08-02 | 2 | RANK-15 | build | `dotnet build src/GameKit.Rankings/GameKit.Rankings.csproj --nologo` | ⬜ pending |
| 08-02 T2 | 08-02 | 2 | RANK-15 | build | `dotnet build src/GameKit.Rankings/GameKit.Rankings.csproj --nologo` | ⬜ pending |
| 08-02 T3 | 08-02 | 2 | RANK-15 | integration (PG+Redis) | `dotnet test tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj --filter "FullyQualifiedName~RankDecay" --nologo` | ⬜ pending |
| 08-03 T1 | 08-03 | 2 | RANK-16 | integration (PG) | `dotnet test tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj --filter "FullyQualifiedName~PlacementMatch" --nologo` | ⬜ pending |
| 08-03 T2 | 08-03 | 2 | RANK-17 | unit | `dotnet test tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj --filter "FullyQualifiedName~RankingsRatingSourceRegistration" --nologo` | ⬜ pending |
| 08-03 T3 | 08-03 | 2 | RANK-17 | integration (PG) | `dotnet test tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj --filter "FullyQualifiedName~RankingsRatingSource" --nologo` | ⬜ pending |
| 08-04 T1 | 08-04 | 3 | MATCH-17 | unit | `dotnet test tests/GameKit.Matchmaking.Tests/GameKit.Matchmaking.Tests.csproj --filter "FullyQualifiedName~EloRangeGuardrail" --nologo` | ⬜ pending |
| 08-04 T2 | 08-04 | 3 | MATCH-16 | build | `dotnet build src/GameKit.Matchmaking/GameKit.Matchmaking.csproj --nologo` | ⬜ pending |
| 08-04 T3 | 08-04 | 3 | MATCH-16, MATCH-17 | integration (PG+Redis) | `dotnet test tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj --filter "FullyQualifiedName~RatingAwareEnqueue" --nologo` | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

> Sampling continuity check: no run of 3 consecutive tasks lacks an automated verify. Every task above has an
> automated `dotnet build` or `dotnet test` gate. Build-only tasks (08-02 T1/T2, 08-04 T2) are immediately
> followed by an integration test in the same plan/wave (08-02 T3, 08-04 T3).

---

## Wave 0 Requirements

Wave 0 test scaffolds are folded into the first/affected task of each plan (each test file pairs with the
production code in the same package and is created alongside it). Coverage:

- [x] Rankings migration determinism + advisory-lock-reuse + schema-freeze test → 08-01 T3 (`SchemaFreezeTests`)
- [x] Glickman inactivity-formula unit test (÷173.7178/×173.7178; rating unchanged) → 08-01 T2 (`Glicko2InactivityTests`)
- [x] EloRange MaxBracketWidth cap + MinPoolDepthBeforeBracketExpansion guard unit tests → 08-04 T1 (`EloRangeGuardrailTests`)
- [x] RankingsRatingSource maps player_ranks → PlayerRatingValue; omits players with no rank row → 08-03 T2/T3

---

## Manual-Only Verifications

*All Phase 8 behaviors have automated verification (Testcontainers Postgres + Redis). None require external credentials.*

---

## Success-Criterion → Test Map

| ROADMAP SC | Test | Plan |
|------------|------|------|
| SC#1 (inactive → RD inflates, rating constant) | `Glicko2InactivityTests` + `RankDecayTests.Decay_InflatesRD_LeavesRatingConstant_StampsLastDecayAt` | 08-01 T2, 08-02 T3 |
| SC#2 (placement hides rank; atomic decrement) | `SchemaFreezeTests` + `PlacementMatchTests` | 08-01 T3, 08-03 T1 |
| SC#3 (WithRatingsFrom → real ratings; omit → fallback) | `RankingsRatingSourceTests` + `RatingAwareEnqueueTests.Enqueue_WritesRealRating_IntoTicketHash` | 08-03 T3, 08-04 T3 |
| SC#4 (bracket stops at MaxBracketWidth regardless of pool depth) | `EloRangeGuardrailTests` + `RatingAwareEnqueueTests.BracketExpansion_StopsAt_MaxBracketWidth_RegardlessOfPoolDepth` | 08-04 T1, 08-04 T3 |
| SC#5 (player_ranks schema finalized/frozen) | `SchemaFreezeTests` | 08-01 T3 |

---

## Validation Sign-Off

- [x] All tasks have automated verify or Wave 0 dependencies
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** planned 2026-06-05
