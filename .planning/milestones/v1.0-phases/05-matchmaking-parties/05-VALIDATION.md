---
phase: 05
slug: matchmaking-parties
status: draft
nyquist_compliant: true
wave_0_complete: true
created: 2026-05-16
---

# Phase 05 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution. Mirrors Phase 4's three-tier model (unit / integration / load) but adds a **load test phase gate** (SC#3) and a **chaos test phase gate** (SC#2) that Phase 4 did not have.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.9.2 + Testcontainers 4.11 (Postgres + Redis) + Moq 4.20.72 |
| **Config file** | Inherits from `Directory.Build.props` (xUnit auto-detected) |
| **Quick run command** | `dotnet test tests/GameKit.Matchmaking.Tests/ --nologo --verbosity quiet` |
| **Full suite command** | `dotnet test tests/GameKit.Matchmaking.Tests/ tests/GameKit.Matchmaking.Integration.Tests/ --nologo` |
| **Load test command** (phase gate) | `dotnet test tests/GameKit.Matchmaking.LoadTests/ --no-build --nologo` |
| **Estimated runtime** | unit ~5s · integration ~90s (Testcontainer warm) · load test ~10–12 min (1k tickets, 10 min sustain) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/GameKit.Matchmaking.Tests/ --nologo --verbosity quiet`
- **After every plan wave:** Run the full suite (unit + integration)
- **Before `/gsd:verify-work`:** Full suite green + load test green + chaos test green + leader-election test green
- **Max feedback latency:** ~5s for unit, ~90s for integration after the first warm run

**Load test policy:** The load test is a phase gate — it does NOT run on every task commit (cost). It runs:
1. Once at the end of the final integration plan to validate SC#3
2. On any plan that modifies the Lua claim script, the lease helper, the channel-drain service, or the Npgsql pool configuration
3. On request via `dotnet test tests/GameKit.Matchmaking.LoadTests/`

---

## Per-Task Verification Map

> Populated by `gsd-planner` as PLAN.md files are produced. Each task gets a row with its automated command (or a Wave 0 reference if no fixture exists yet) and the SC / pitfall it anchors.

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 05-01-T01 | 01 | 0 | MATCH-01, MATCH-15 | T-05-01-SC | N/A (scaffolding) | build | `dotnet build tests/GameKit.Matchmaking.Tests tests/GameKit.Matchmaking.Integration.Tests tests/GameKit.Matchmaking.LoadTests --nologo` | ✅ (this task creates) | ⬜ pending |
| 05-01-T02 | 01 | 0 | MATCH-01, MATCH-15 | — | N/A (scaffolding) | build | `dotnet build tests/GameKit.Matchmaking.Tests --nologo` | ✅ (this task creates) | ⬜ pending |
| 05-01-T03 | 01 | 0 | MATCH-15 | — | Advisory-lock-key distinct from prior packages (defense-in-depth) | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter FullyQualifiedName~MatchmakingKey_Is_Distinct --nologo --no-build \|\| echo "EXPECTED — integration build gated on Plan 05-02"` | ✅ (this task creates) | ⬜ pending |
| 05-02-T01 | 02 | 1 | MATCH-01, MATCH-02, MATCH-03, MATCH-15 | T-05-02-01 | Integer enum storage (no HasConversion<string>); CITEXT party_code (Pitfall §9) | build | `dotnet build src/GameKit.Matchmaking --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-02-T02 | 02 | 1 | MATCH-15 | T-05-02-01 | Per-package migration boundary — ExcludeFromMigrations for 16 prior-package types | build | `dotnet build src/GameKit.Matchmaking --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-02-T03 | 02 | 1 | MATCH-15 | T-05-02-01, T-05-02-02, T-05-02-03 | Advisory-lock distinct (T-05-02-02); CITEXT party_code (T-05-02-03); migration boundary (T-05-02-01) | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter "MigrationDeterminism\|AdvisoryLockKey" --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-02-T04 | 02 | 1 | MATCH-15 | T-05-02-01 | Human inspection of migration boundary | human-verify | (none — checkpoint:human-verify; resume-signal "approved") | N/A | ⬜ pending |
| 05-03-T01 | 03 | 1 | MATCH-07, MATCH-10, MATCH-11 | T-05-03-01 | Fail-fast IValidateOptions rejects degenerate config at host startup (T-05-03-01) | unit | `dotnet test tests/GameKit.Matchmaking.Tests --filter MatchmakingOptionsValidation --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-03-T02 | 03 | 1 | MATCH-10 | T-05-03-02 | Case-insensitive ladder dedup at host config time (T-05-03-02) | unit | `dotnet test tests/GameKit.Matchmaking.Tests --filter LadderConfigDefaults --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-03-T03 | 03 | 1 | MATCH-01, MATCH-14 | — | N/A (DI surface — security delegated to per-service tasks) | build | `dotnet build src/GameKit.Matchmaking --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-04-T01 | 04 | 2 | MATCH-09, MATCH-10 | — | N/A (pure strategy math — stateless, thread-safe per XML doc) | unit | `dotnet test tests/GameKit.Matchmaking.Tests --filter "BracketFlexMath\|GlickoWeighted\|EloRangeStrategy" --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-04-T02 | 04 | 2 | MATCH-03 | T-05-04-03, T-05-04-04, T-05-04-05, T-05-04-06 | Cryptographic party code (T-05-04-04); SERIALIZABLE single-active-party (T-05-04-05); owner-only dissolve (T-05-04-06); citext case-insensitive (Pitfall §9) | unit + integration | `dotnet test tests/GameKit.Matchmaking.Tests --filter PartyCodeGeneration --nologo && dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter PartyService --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-04-T03 | 04 | 2 | MATCH-04, MATCH-05 | T-05-04-01, T-05-04-02 | Lua fencing-token check FIRST step (T-05-04-01); atomic claim prevents double-match (T-05-04-02); EVALSHA fast-path | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter AtomicClaimScript --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-05-T01 | 05 | 3 | MATCH-07, MATCH-08 | T-05-05-01, T-05-05-03 | Lease helper Polly retry; InstanceId fencing (T-05-05-01); lock expiry recovery (T-05-05-03) | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter MatchmakerLeaseHelper --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-05-T02 | 05 | 3 | MATCH-04, MATCH-05, MATCH-07 | T-05-05-02, T-05-05-04 | Per-pool RenewLease bail (Pitfall §2 — T-05-05-02); ProposalSweeper preserves queuedAt (T-05-05-04 — Pitfall §10); SCAN not KEYS | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter ProposalSweep --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-05-T03 | 05 | 3 | MATCH-08 | T-05-05-01, T-05-05-02 | SC#4 phase gate: exactly-one-leader semantics; forced failover within LockTtl with no double-matching | integration (phase gate) | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter MatchmakingLeaderElection --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-06-T01 | 06 | 3 | MATCH-02 | T-05-06-05 | UTC-only via IClock (Pitfall §4 — T-05-06-05); escalating cooldown per D-08 | unit + integration | `dotnet test tests/GameKit.Matchmaking.Tests --filter "DeclineCooldownEscalation\|TeamAssignment" --nologo && dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter CooldownEnforcement --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-06-T02 | 06 | 3 | MATCH-04, MATCH-05, MATCH-09 | T-05-06-01, T-05-06-02, T-05-06-03, T-05-06-04 | ticketId-in-proposal verify (Spoofing T-05-06-01); Lua atomic SADD+SCARD complete-check (T-05-06-02); decline writes DeclineHistory before re-ZADD (T-05-06-03); late-accept idempotent (T-05-06-04) | build (paired with T03 integration) | `dotnet build src/GameKit.Matchmaking --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-06-T03 | 06 | 3 | MATCH-02, MATCH-05 | T-05-06-01, T-05-06-04 | End-to-end accept happy-path + decline-requeue with D-09 queuedAt preservation | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter "ProposalAcceptHappyPath\|ProposalDeclineRequeue" --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-07-T01 | 07 | 3 | MATCH-02 | T-05-07-02, T-05-07-03, T-05-07-05 | Connection-per-batch (Pitfall §8 — T-05-07-02); OTel counter on drop (T-05-07-03, T-05-07-05); BoundedChannelFullMode.DropNewest (D-15) | unit + integration | `dotnet test tests/GameKit.Matchmaking.Tests --filter TicketEventChannelDrop --nologo && dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter AnalyticsDrainService --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-07-T02 | 07 | 3 | MATCH-06, MATCH-15 | T-05-07-01, T-05-07-04 | Reconciler zero Redis writes (Pitfall §1 — T-05-07-01); leader-gated retention (T-05-07-04); orphan-session audit row | integration | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter "ReconcilerSweep\|RetentionCleanup" --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-07-T03 | 07 | 3 | MATCH-02 | — | Channel<TicketEvent> singleton REPLACES Plan 05-04 placeholder with options-driven instance | build | `dotnet build src/GameKit.Matchmaking --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-08-T01 | 08 | 4 | MATCH-01, MATCH-04, MATCH-14 | T-05-08-07 | SC#6: ZCARD live source of truth; depth survives Postgres row deletion (T-05-08-07); ToUnixTimeMilliseconds per Pitfall §6 | integration (phase gate SC#6) | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter MatchmakingObservability --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-08-T02a | 08 | 4 | MATCH-01, MATCH-03, MATCH-11 | T-05-08-04 | Per-IP rate limit on /api/parties/join (T-05-08-04 mitigation against code enumeration); sliding-window not fixed (T-05-08-03) | build | `dotnet build src/GameKit.Matchmaking --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-08-T02b | 08 | 4 | MATCH-01, MATCH-03 | T-05-08-01, T-05-08-02, T-05-08-06 | Pitfall §5 long-poll abort: linked CTS + finally Unsubscribe (T-05-08-06); ticket-ownership verify (T-05-08-01, T-05-08-02) | integration (Pitfall §5 phase gate) | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter "PartyEndpoint\|LongPollStatus" --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-08-T04 | 08 | 4 | MATCH-02, MATCH-05, MATCH-07, MATCH-09, MATCH-10, MATCH-14 | T-05-08-03, T-05-08-05 | Admin cookie auth + Superadmin + antiforgery (T-05-08-05); SC#5 rate-limit 429 (T-05-08-03); SC#1 bracket flex end-to-end | integration (phase gates SC#1 + SC#5) | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter "MatchmakingHappyPath\|MatchmakingRateLimit" --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-09-T01 | 09 | 5 | MATCH-04, MATCH-06 | T-05-09-01 | TryAddSingleton<IChaosInterceptor, NullChaosInterceptor> default (T-05-09-01); test-only AbortingChaosInterceptor in tests project | build | `dotnet build src/GameKit.Matchmaking tests/GameKit.Matchmaking.Integration.Tests --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-09-T02 | 09 | 5 | MATCH-04, MATCH-06, MATCH-12 | T-05-09-01 | SC#2 phase gate: 4 invariants (no dup sessions, no ghost ticket keys, no player in 2 active sessions, stale tickets expired) | integration (phase gate SC#2) | `dotnet test tests/GameKit.Matchmaking.Integration.Tests --filter MatchmakingChaosTests --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-09-T03 | 09 | 5 | MATCH-01 | T-05-09-02, T-05-09-03 | Human-verify UAT: 1v1 happy path via two browser tabs (CONTEXT.md: no party UI in sample) | human-verify | (none — checkpoint:human-verify; resume-signal "approved") | N/A | ⬜ pending |
| 05-10-T01 | 10 | 6 | MATCH-07, MATCH-13 | T-05-10-02 | LoadTestFixture Testcontainer isolation (T-05-10-02); Maximum Pool Size=25 Pitfall §8 constraint | build | `dotnet build tests/GameKit.Matchmaking.LoadTests --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-10-T02 | 10 | 6 | MATCH-04, MATCH-07, MATCH-13 | T-05-10-01, T-05-10-03 | Opt-in via `[Trait("Category", "LoadTest")]` (T-05-10-01); descriptive histogram on budget violation (T-05-10-03) | build (run is human-verify) | `dotnet build tests/GameKit.Matchmaking.LoadTests --nologo` | ✅ (Wave 0 ready) | ⬜ pending |
| 05-10-T03 | 10 | 6 | MATCH-13 | T-05-10-03 | SC#3 phase gate (operator runs 10-min sustain): MaxIterationMs ≤ 50ms, zero pool exhaustion, zero dropped events, ≥1000 matches | human-verify (10-min load run) | (none — checkpoint:human-verify; resume-signal "approved") | N/A | ⬜ pending |

*Status legend: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

The following fixtures and test projects MUST exist before any Wave 1+ task can claim an automated verification. The planner's first plan (Wave 0) creates all of them.

- [ ] `tests/GameKit.Matchmaking.Tests/GameKit.Matchmaking.Tests.csproj` — unit test project (xUnit + Moq)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj` — integration test project (xUnit + Testcontainers.PostgreSql + Testcontainers.Redis)
- [ ] `tests/GameKit.Matchmaking.LoadTests/GameKit.Matchmaking.LoadTests.csproj` — load test project (xUnit + Testcontainers; long-running; not part of default run)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` — `[CollectionDefinition("Matchmaking")]` composing `PostgresFixture` + `RedisFixture` (mirrors `RankingsCollection`)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestModelCustomizer.cs` — `RelationalModelCustomizer` override that applies `MatchmakingModelBuilderExtension` so cross-package contexts see matchmaking entities (Pitfall §3 carry-forward)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/Fixtures/MatchmakingIntegrationFixture.cs` — shared per-class fixture: builds a `WebApplicationFactory<Program>` with a Testcontainer Postgres + Redis, applies migrations, seeds a deterministic `StepClock`
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/Fixtures/StepClock.cs` — copy or share-via-`GameKit.TestFixtures` of the `Glicko2ConvergenceTests` step clock (advances `IClock.UtcNow` deterministically)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs` — Wave 0 mandatory: live-verify `SELECT hashtext('gamekit.matchmaking.migrations')::bigint` matches the C# constant and is distinct from Core / Auth / Admin / Rankings advisory keys (mirrors the Phase 4 pattern that caught a real bug)
- [ ] (optional) `tests/GameKit.Matchmaking.LoadTests/Fixtures/LoadTestFixture.cs` — long-lived Testcontainer pair with `MaxPoolSize=25` Npgsql config (so the load test verifies Pitfall §8 mitigation)

---

## Per-Success-Criterion Test Mapping

> Authoritative mapping from ROADMAP.md Success Criteria → test classes. The planner MUST reference these by name in each plan's verification section.

| SC | Assertion | Test Class | Project |
|----|-----------|-----------|---------|
| SC#1 | Party of 1-N enqueues; `EloRangeMatchmakingStrategy.Match()` produces a match whose bracket widened 100→500 over ~40s; `matchmaking_tickets` rows written async to Postgres while Redis remains live source of truth | `MatchmakingHappyPathTests` (uses `StepClock` to advance `queuedAt` by 40s, asserts bracket at t=0/10/20/30/40) | Integration |
| SC#2 | Chaos: 100 parties enqueued, matcher runs, app process killed mid-match, restart, reconciliation runs → assert no duplicate `game_sessions`, no ghost `mm:ticket:{id}` keys, expired leases returned, no player in 2 active sessions | `MatchmakingChaosTests` (uses `IChaosInterceptor` abort hook + reconciler sweep + post-restart assertion suite) | Integration (phase gate) |
| SC#3 | 1k concurrent queued tickets for 10 minutes against a single Redis + Postgres pair; no matchmaker iteration exceeding configured budget; no Npgsql pool exhaustion | `MatchmakingLoadTests.SustainedThousandTicketLoad_HoldsBudget` (Stopwatch per tick asserts ≤ budget; Npgsql pool wait-event count = 0) | LoadTests (phase gate) |
| SC#4 | 2 matcher replicas share one Redis → exactly one holds lock at any time; forced failover transfers leadership within lease TTL with no double-matching | `MatchmakingLeaderElectionTests` (mirrors `RankingsTickerLeaderElectionTests` — spin two `WebApplicationFactory<Program>` instances sharing one Redis Testcontainer, force `LockRelease`, assert failover within TTL) | Integration (phase gate) |
| SC#5 | Per-player enqueue rate-limit returns 429 on spam; no duplicate tickets enter queue | `MatchmakingRateLimitTests` (rapid-fire POST `/api/mm/queue` from same JWT, assert 429 after threshold, assert single ticket in Redis) | Integration |
| SC#6 | Admin queue-depth + health panels show live Redis state (queue counts, lease count, leader identity) — NOT from Postgres reconciliation mirrors | `MatchmakingObservabilityTests` (enqueue N tickets, call `IMatchmakingObservability.GetQueueStatsAsync`, assert ZCARD per pool matches; flush reconciler mirror and confirm panel value unchanged) | Integration |

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Admin UI live panels render queue depth / leader identity in browser | MATCH-14 | Visual integration with Phase 3 Blazor `MainLayout`; component-level snapshot tests are brittle and Phase 3 set the precedent of UAT for admin chrome | UAT: log in as admin → navigate to `/admin/matchmaking/health` → enqueue 3 tickets via `curl POST /api/mm/queue` → confirm panel updates within polling interval (default 2s) |
| `pause-queue` / `drain-queue` admin command-palette verbs | MATCH-14 | Same — chrome interaction not worth a UI test harness investment | UAT: open admin command palette (`⌘K`) → type "pause queue" → confirm dialog → confirm Redis SET `mm:queue:paused=1` → enqueue returns 503 |
| `TicTacToeDuel` sample app 1v1 happy path | MATCH-01..15 (sample) | End-to-end demo behavior; the sample's value is showing integration works, not unit-testable | UAT: run sample → two browsers → both log in → both `POST /api/mm/queue` → poll status → assert "matched" appears in both → assert TicTacToe board renders |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify OR a Wave 0 dependency that creates the fixture
- [ ] Sampling continuity: no 3 consecutive tasks without an automated verify
- [ ] Wave 0 covers all MISSING references (8 items above)
- [ ] No `--watch` / `dotnet watch` flags in any verify command (long-running, can't be sampled)
- [ ] Feedback latency: < 5s unit, < 90s integration (warm)
- [ ] Load test (SC#3) is the final wave's verification, not a per-task one
- [ ] Chaos test (SC#2) and leader-election test (SC#4) are each tied to the plan that ships the relevant component
- [ ] `nyquist_compliant: true` set in frontmatter once the planner fills the per-task map

**Approval:** pending
