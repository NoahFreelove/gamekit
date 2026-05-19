# GameKit.Matchmaking.LoadTests

**Phase 5 SC#3 phase-gate harness — 1 000 concurrent tickets / 10-minute sustain.**

This project contains a single load test
(`MatchmakingLoadTests.SustainedThousandTicketLoad_HoldsBudget`) that exercises the
matchmaker end-to-end under sustained 1 k-concurrent-ticket load and asserts the SC#3
budget/pool/dropped-event invariants from
`.planning/phases/05-matchmaking-parties/05-VALIDATION.md` §Per-Success-Criterion Test Mapping.

The test is **opt-in**. It does NOT run as part of the default `dotnet test` sweep — the
fact decorator `[Trait("Category", "LoadTest")]` plus a 15-minute timeout keep it out of
rapid CI loops. The project's `<IsPackable>false</IsPackable>` keeps it out of `dotnet pack`.

---

## When to run it

| Trigger | Why |
|---------|-----|
| **Phase 5 close-out** | Mandatory SC#3 gate per ROADMAP §Phase 5 + `05-CONTEXT.md` lines 56-58. |
| Changes to `AtomicClaimScript.LuaSource` (Plan 05-04) | The Lua script runs once per match; budget regressions surface here first. |
| Changes to `MatchmakerLeaseHelper` (Plan 05-05) | Polly retry/timeout knobs affect the tick budget tail. |
| Changes to `MatchmakingAnalyticsDrainService` (Plan 05-07) | Pitfall §8 — drain holding a connection across Polly sleep is the canonical pool-exhaustion regression. |
| Changes to `GameKitMatchmakingTickerOptions` / `Analytics.ChannelCapacity` defaults | The defaults are the load-test sufficiency bar; changing them needs a re-run. |
| Operator-requested smoke | Any time before a release. |

`05-VALIDATION.md` §Sampling Rate codifies this as the "load test policy":

> 1. Once at the end of the final integration plan to validate SC#3
> 2. On any plan that modifies the Lua claim script, the lease helper, the channel-drain
>    service, or the Npgsql pool configuration
> 3. On request via `dotnet test tests/GameKit.Matchmaking.LoadTests/`

---

## How to run it

**Pre-flight:**

```bash
docker info        # ≥4 GB free
docker compose ps  # no port-bound containers blocking 5432/6379 (we use random ports anyway)
```

**Build:**

```bash
dotnet build tests/GameKit.Matchmaking.LoadTests --nologo
```

**Run** (10 minutes sustain + ~2 minutes warm-up + ~30 s tail = ~12-13 minutes total):

```bash
dotnet test tests/GameKit.Matchmaking.LoadTests \
  --filter Category=LoadTest \
  --no-build \
  --logger "console;verbosity=detailed"
```

**Halfway report (~5-minute mark)** prints to stdout via `ITestOutputHelper`:

```
[HH:MM:SS.fff] [halfway] TicksObserved=600 MaxIterationMs=27 p99=18.40
              PoolExhaustionEvents=0 PoolWaitEvents=0 DroppedEvents=0
[HH:MM:SS.fff] [halfway] matchmaking_tickets.Status=Matched count: 523
```

**Final report** (always printed; precedes pass/fail):

```
[HH:MM:SS.fff] ===== SC#3 FINAL =====
  Test duration:       00:12:42.183
  Tick observations:   1247
  MaxIterationMs:      31 (budget 50)
  p50 / p90 / p99 ms:  4.20 / 12.10 / 22.80
  Pool exhaustion:     0
  Pool waits >100ms:   0
  Dropped events:      0
  Matched tickets:     1289
  Enqueue errors:      0
```

---

## What each assertion guarantees

| Assertion | Source | What it catches |
|-----------|--------|-----------------|
| `Budget.AssertBudgetHeld(50)` | `TickerBudgetObserver` subscribed to `MatchmakingActivitySource("GameKit.Matchmaking.Ticker")` | Ticker per-iteration wall-time within 50 ms (RESEARCH §Decision 13). Failure surfaces with a histogram (p50/p90/p99/max) + likely-cause hints (Lua perf regression, strategy iteration overhead). |
| `Pool.AssertNoPoolExhaustion()` | `NpgsqlPoolObserver` subscribed to `EventSource("Npgsql")` | Npgsql pool not exhausted during the run (Pitfall §8). Triggers if any service holds a connection across a Polly retry sleep or the 25-connection cap is genuinely insufficient. |
| `dropped == 0` | `MeterListener` on `matchmaking.analytics.dropped_events` (D-15 / D-16) | Bounded channel capacity (default 10 000) sufficient for sustained 1 k-concurrent load. Failure indicates `Analytics.ChannelCapacity` should be raised or the drain `BatchSize/Interval` retuned. |
| `matchedCount >= 1000` | `SELECT COUNT(*) FROM matchmaking_tickets WHERE Status=5` | The matchmaker actually formed matches (not just enqueued). 10 min × ~100 matches/min minimum = 1 000+ matched tickets. |

The Postgres pool observer uses two detection paths (RESEARCH §A6 defense-in-depth):

1. **Primary — EventSource subscription.** Listens for events on `EventSource("Npgsql")`
   at `EventLevel.Informational` and filters event names/messages containing
   `pool` + (`exhaust` | `wait` | `timeout`).
2. **Fallback — exception message inspection.** `Pool.RecordExceptionFallback(ex.ToString())`
   is called from the test driver's `catch` blocks; any `DbException.Message` containing
   `"pool"` plus exhaust/timeout/size keywords increments the exhaustion counter.

If a future Npgsql version changes its EventSource semantics, the fallback path is the
backstop — neither false-positives nor false-negatives are silent.

---

## Failure-mode triage

| Failure | Likely cause | Remediation |
|---------|--------------|-------------|
| `MaxIterationMs > 50` | Lua claim script perf regression OR strategy iteration overhead grew OR per-pool SCAN became dominant | Inspect the histogram. Re-run with profiler. Confirm Lua script hasn't gained branches. Consider an in-memory ladder registry if SCAN dominates. |
| `Pool exhaustion > 0` | Drain/reconciler/retention holding a connection across Polly retry sleeps | Inspect `MatchmakingAnalyticsDrainService.FlushBatch` (Plan 05-07). The Npgsql connection must close per batch — the `using var ctx = ...` scope must NOT outlive the await on the Polly retry. |
| `Dropped events > 0` | Channel capacity (default 10 000) insufficient at sustained throughput | Increase `Analytics.ChannelCapacity` OR document the drop count + rate as accepted (loss is best-effort per D-15/D-16). |
| `Matched tickets < 1000` | Matcher not making forward progress | Check for ticker deadlocks. Check Redis connectivity logs. The other observers should fire first; this assertion is the sanity backstop. |
| Test times out (>15 min) | Deadlock — typically Pitfall §5 long-poll subscription leakage | Inspect `LongPollStatusHandler` (Plan 05-08). Verify the linked CTS + `finally Unsubscribe` chain is intact. |

---

## OQ-4 implicit verification

The fixture deliberately does NOT pause the reconciler (default 30-second tick) or the
retention sweep (startup + 24-hour interval) during the run. Both services run their
natural schedules concurrently with the drain + ticker, contending for the 25-connection
Npgsql pool.

If reconciler + retention contention is a real problem at the SC#3 throughput level,
either the per-tick budget will exceed 50 ms (because the drain's INSERTs starve waiting
on the pool, blocking forward progress in the analytics path) OR the pool observer will
fire exhaustion events. The load test catches both modes.

`05-09-SUMMARY.md` §OQ-4 (Reconciler + Retention concurrency under load) recorded this
deferral; this test closes it.

---

## CI integration (operator wires the job)

The recommendation for CI:

- Default `dotnet test` runs on every PR/push — load test SKIPPED (no
  `--filter Category=LoadTest`).
- Dedicated nightly / on-demand `dotnet test --filter Category=LoadTest` job —
  load test RUN. Sample GitHub Actions step:

```yaml
- name: SC#3 load test (nightly)
  if: github.event_name == 'schedule' || inputs.run_load_test == 'true'
  timeout-minutes: 20
  run: |
    dotnet test tests/GameKit.Matchmaking.LoadTests \
      --filter Category=LoadTest \
      --no-build \
      --logger "console;verbosity=detailed" \
      --logger "trx;LogFileName=loadtest.trx"
```

CI YAML wiring is **out of scope for v1** — this README documents the recipe; the
operator's pipeline configuration is downstream. (`05-VALIDATION.md` §Sampling Rate
explicitly notes "CI runs it as a separate job".)

---

## See also

- `.planning/phases/05-matchmaking-parties/05-10-PLAN.md` — this plan
- `.planning/phases/05-matchmaking-parties/05-RESEARCH.md` §Decision 13 — load-test harness shape
- `.planning/phases/05-matchmaking-parties/05-RESEARCH.md` §Pitfall §8 — drain connection lifetime constraint
- `.planning/phases/05-matchmaking-parties/05-RESEARCH.md` §A6 — Npgsql EventSource fallback
- `.planning/phases/05-matchmaking-parties/05-VALIDATION.md` §SC#3 + §Sampling Rate
- `.planning/phases/05-matchmaking-parties/05-CONTEXT.md` lines 56-58 — SC#3 as phase gate
