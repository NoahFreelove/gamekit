---
phase: 04
slug: rankings-sessions-gdpr
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-05-15
---

# Phase 4 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.
> Anchored to `04-RESEARCH.md` §Validation Architecture (lines 1207–1267).

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11.0 + Moq 4.20.72 |
| **Config file** | `tests/Directory.Build.props` (exists, drives every test csproj) |
| **Quick run command** | `dotnet test --no-build --filter "FullyQualifiedName~GameKit.Rankings.Tests"` |
| **Full suite command** | `dotnet test` |
| **Live-DB integration** | `dotnet test --filter "FullyQualifiedName~Integration"` (requires Docker) |
| **Estimated runtime** | ~1s unit, ~3–5 min full suite with Testcontainers cold-start |

---

## Sampling Rate

- **After every task commit:** `dotnet test --no-build --filter "FullyQualifiedName~GameKit.Rankings.Tests"` (unit only — ~1 s)
- **After every plan wave:** `dotnet test` (full suite, including integration — ~3–5 min)
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** ~5 s for unit; ~300 s for integration (Testcontainers cold-start dominant)

---

## Per-Requirement Verification Map

*Per-task rows materialize when planner emits 04-NN-PLAN.md files. Below is the requirement→test anchor map seeded from RESEARCH.md.*

| Req ID | Behavior | Test Type | Test Class | Automated Command | File Exists | Status |
|--------|----------|-----------|------------|-------------------|-------------|--------|
| RANK-01 | Package at `src/GameKit.Rankings/GameKit.Rankings.csproj` with `net10.0` TFM | smoke | n/a | `dotnet build src/GameKit.Rankings` | ❌ W0 | ⬜ pending |
| RANK-02 | `ladders` table exists in `gamekit` schema | schema introspection | `SchemaTypeAssertions.Ladders_Table_Exists` | quick | ❌ W0 | ⬜ pending |
| RANK-03 | rating/RD/volatility columns are `double precision` (SC#3) | schema introspection | `SchemaTypeAssertions.Rating_Columns_Are_DoublePrecision` | quick | ❌ W0 | ⬜ pending |
| RANK-04 | `IRankingAlgorithm.Apply(state, batch)` interface — batched only | unit (reflection) | `Glicko2AlgorithmContractTests.IRankingAlgorithm_Has_Only_Apply_Batch_Method` | quick | ❌ W0 | ⬜ pending |
| RANK-05 | Default `Glicko2Algorithm` matches Glickman worked example | unit | `Glicko2WorkedExampleTests.Glickman_Worked_Example_Matches_Within_Tolerance` | quick | ❌ W0 | ⬜ pending |
| RANK-06 | 1000-match convergence (SC#1) | integration | `Glicko2ConvergenceTests.Two_Populations_Converge_Within_Tolerance` | integration | ❌ W0 | ⬜ pending |
| RANK-07 | Lazy rank creation on first match | integration | `LazyRankCreationTests.Rank_Row_Created_On_First_Match_Drain` | integration | ❌ W0 | ⬜ pending |
| RANK-08 | Leaderboard top-N + around-me | integration | `LeaderboardServiceTests.TopAsync_Returns_Sorted_By_Rating_Desc`, `LeaderboardServiceTests.AroundAsync_Returns_Window_Centered_On_Player` | integration | ❌ W0 | ⬜ pending |
| RANK-09 | `AddLadder(name, config)` upserts at startup | integration | `LadderUpsertOnStartupTests.AddLadder_Inserts_Row_Idempotently` | integration | ❌ W0 | ⬜ pending |
| RANK-10 | Seasonal reset + archival (SC#4) | integration | `SeasonArchiveLeaderboardTests.Archive_Preserves_Previous_Season_TopN`, `SeasonArchiveLeaderboardTests.SoftRegress_Reduces_Rating_Toward_Default` | integration | ❌ W0 | ⬜ pending |
| RANK-11 | Session-complete 5× retry → exactly one delta (SC#2) | integration | `SessionCompleteIdempotencyTests.Retry_Five_Times_Applies_Delta_Once` | integration | ❌ W0 | ⬜ pending |
| RANK-12 | Rank-adjust audit atomicity (SC#6) | integration | `AdminRankAdjustTransactionTests.UpdateAndAudit_RollBack_Together_On_Failure` | integration | ❌ W0 | ⬜ pending |
| RANK-13 | GDPR export contract (SC#5) | contract | `GdprExportContractTests.Response_Has_All_Documented_Top_Level_Keys` | integration | ❌ W0 | ⬜ pending |
| RANK-14 | Per-package migration under `__ef_migrations_rankings` | integration | `RankingsMigrationDeterminismTests.Apply_Then_ReApply_Produces_No_Diff`, `RankingsAdvisoryLockKeyTests.RankingsKey_Is_Distinct_From_Core_Auth_Admin_Keys` | integration | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Success Criteria → Test Anchors

| SC | What must be TRUE | Test Class | Fixture |
|----|--------------------|-----------|---------|
| SC#1 | 1000-match convergence within Glickman tolerance | `Glicko2ConvergenceTests` (integration) — seeds two 50-player populations, 1000 paired matches, 100 rating periods, asserts mean-rating within 50 of true skill | Glickman PDF fixture + `Random(42)` |
| SC#2 | `/sessions/{id}/complete` 5× retry → exactly one rating delta | `SessionCompleteIdempotencyTests` — WebApplicationFactory + Testcontainers Postgres + Redis; mints service token; 5× POST same Idempotency-Key | `RankingsFixture` (new: PostgresFixture + RedisFixture) |
| SC#3 | Rating columns are `double precision` | `SchemaTypeAssertions` — queries `information_schema.columns`, asserts six columns (`player_ranks.{rating,rating_deviation,volatility}` + `session_participants.{rating_before,rating_after,rating_delta}`) are `double precision` | `PostgresFixture` |
| SC#4 | Seasonal archive preserves prior-season top-N + around-me | `SeasonArchiveLeaderboardTests` — seeds 10 players, triggers `EndSeasonService.EndAsync`, asserts `season_rank_archive` rows + archived-season leaderboard ordering | `RankingsFixture` + admin auth helper |
| SC#5 | `/export` returns documented JSON bundle | `GdprExportContractTests` — seeds player+identities+credentials+sessions+ratings, asserts top-level keys exactly `{player, identities, credentials_metadata, sessions, rating_history, exported_at}`; asserts no `password_hash`; asserts identities use `external_id_hash`; asserts ≤ 25 MB | `RankingsFixture` |
| SC#6 | Admin rank-adjust writes before/after atomically | `AdminRankAdjustTransactionTests` — faulty `IAdminAuditWriter` throws after UPDATE; asserts UPDATE rolled back and rating unchanged | `AdminIntegrationFixture` extended for Rankings |

---

## Wave 0 Requirements

- [ ] `tests/GameKit.Rankings.Tests/GameKit.Rankings.Tests.csproj` — unit test project
- [ ] `tests/GameKit.Rankings.Integration.Tests/GameKit.Rankings.Integration.Tests.csproj` — integration test project
- [ ] `tests/GameKit.TestFixtures/RankingsFixture.cs` — composes `PostgresFixture` + new `RedisFixture`
- [ ] `tests/GameKit.TestFixtures/RedisFixture.cs` — `Testcontainers.Redis` fixture (Phase 5 will reuse)
- [ ] `tests/GameKit.Rankings.Tests/Glicko2/Fixtures/Glickman_Worked_Example.json` — deterministic input + expected output for RANK-05
- [ ] `src/GameKit.Rankings/GameKit.Rankings.csproj` — populated csproj (currently empty stub from Phase 1)
- [ ] Glicko-2 license verification (Pitfall §1 close) — `git clone MaartenStaa/glicko2-csharp && cat LICENSE` before any vendored source is committed; attribution header pattern must match the actual BSD-2 vs BSD-3 result

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Service-token raw secret printed exactly once on `dotnet gamekit service-token issue` | D-06 / RANK-11 | CLI stdout side effect; integration tests assert `service_tokens.token_hash` row but not the human-visible print | Run `dotnet gamekit service-token issue --name e2e-test`; verify raw token appears in stdout; re-run `service-token list` and verify the raw token is NOT shown |
| Glicko-2 attribution header in vendored source files | RANK-05 / Pitfall §1 | Source-file inspection, not test-runnable | After Wave 0 license-verify task, grep each vendored `.cs` file for the `// Original work copyright (c) Maarten Staa, BSD-{2,3}-Clause` header |
| Admin palette `rank-adjust` and `end-season` verbs open dialogs that POST to the new endpoints | RANK-12 + D-11 | bUnit + WebApplicationFactory coverage is in scope but cross-package E2E (Phase 3 UI calling Phase 4 endpoint) is verified manually in the sample app | Boot `samples/TicTacToeDuel`; log in as superadmin; open palette; trigger `rank-adjust` on a player and `end-season` on the default ladder; verify audit rows + dialog UX |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 5 s (unit) / < 300 s (integration)
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
