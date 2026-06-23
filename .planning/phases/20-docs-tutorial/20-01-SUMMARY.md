---
phase: 20-docs-tutorial
plan: "01"
subsystem: sample-app
tags: [sample, matchmaking, docs, tutorial-prereq]
status: complete

dependency_graph:
  requires: []
  provides:
    - matchmaking.html enqueues into default pool (poolName null)
    - TicTacToeDuel sample exposes public partial Program type
  affects:
    - samples/TicTacToeDuel/wwwroot/matchmaking.html
    - samples/TicTacToeDuel/Program.cs

tech_stack:
  added: []
  patterns:
    - public partial class Program pattern for WebApplicationFactory affordance

key_files:
  modified:
    - samples/TicTacToeDuel/wwwroot/matchmaking.html
    - samples/TicTacToeDuel/Program.cs

decisions:
  - "poolName null (not \"tictactoe\") routes enqueue POST into the default pool — the only pool the tictactoe ladder pairs tickets in"
  - "public partial class Program carries a XML doc comment to satisfy -warnaserror:CS1591"

metrics:
  duration: "~5 minutes"
  completed: "2026-06-23T14:04:20Z"
  tasks_completed: 2
  tasks_total: 2
  files_modified: 2
---

# Phase 20 Plan 01: DOCS-06 Sample Currency — poolName fix + public partial Program Summary

**One-liner:** Fixed matchmaking.html to send `poolName: null` so Find Match actually forms a match in the default pool; added `public partial class Program` to TicTacToeDuel for future WAF-based integration test affordance.

## Tasks Completed

| # | Task | Commit | Files |
|---|------|--------|-------|
| 1 | Fix poolName bug in matchmaking.html (DOCS-06) | a9b78ee | `samples/TicTacToeDuel/wwwroot/matchmaking.html` |
| 2 | Expose public partial Program type in sample | 2c6ba79 | `samples/TicTacToeDuel/Program.cs` |

## What Was Built

### Task 1: poolName Bug Fix

Changed line 240 of `matchmaking.html` from:
```js
body: JSON.stringify({ ladderId, poolName: "tictactoe", partyId: null }),
```
to:
```js
body: JSON.stringify({ ladderId, poolName: null, partyId: null }),
```

**Why this mattered:** `EnqueueRequest.PoolName` defaults to `null`, which routes the ticket into the `"default"` pool. The TicTacToeDuel ladder is named `"tictactoe"` but its tickets are matched within the `"default"` pool. Sending `poolName: "tictactoe"` created tickets in a named pool that was never paired — silently preventing matches from ever forming. Two browser tabs clicking "Find Match" would accumulate queue depth in the "tictactoe" pool with zero proposals emitted. This fix is a DOCS-02 tutorial prerequisite.

### Task 2: public partial class Program

Appended after `app.Run()` in `samples/TicTacToeDuel/Program.cs`:
```csharp
/// <summary>
/// Exposes the compiler-synthesized top-level <c>Program</c> type as a publicly referenceable
/// partial class so that future <c>WebApplicationFactory&lt;Program&gt;</c>-based integration
/// tests can name it as the generic argument without reflection hacks.
/// This declaration has no runtime behaviour — the entry point remains the top-level statements above.
/// </summary>
public partial class Program { }
```

The XML doc comment is required because `Directory.Build.props` treats CS1591 (missing doc comment on public type) as a warning-as-error. The Release build confirms 0 warnings, 0 errors.

Note: Plan 20-03's DOCS-02 smoke test does NOT consume this type — it uses a hand-rolled in-process host (mirroring `OpenApiTestApp`) rather than `WebApplicationFactory<Program>`. This affordance is proactively exposed for future WAF-based tests.

## Verification Results

| Check | Result |
|-------|--------|
| `grep -q 'poolName: null' matchmaking.html` | PASS |
| `! grep -q 'poolName: "tictactoe"' matchmaking.html` | PASS |
| `grep -q 'public partial class Program' Program.cs` | PASS |
| `dotnet build TicTacToeDuel.csproj -c Release -warnaserror` | PASS — 0 warnings, 0 errors |

## Deviations from Plan

None — plan executed exactly as written.

## Threat Surface Scan

No new network endpoints, auth paths, file access patterns, or schema changes introduced. The `public partial class Program` is a compile-time type visibility change with no runtime behaviour. Threat register items T-20-01-01 and T-20-01-02 both accepted; existing localStorage XSS warning in the HTML is preserved (not removed).

## Known Stubs

None. Both changes are complete functional fixes with no placeholder values.

## Self-Check: PASSED

- `samples/TicTacToeDuel/wwwroot/matchmaking.html` — modified and verified (`poolName: null` present, `poolName: "tictactoe"` absent)
- `samples/TicTacToeDuel/Program.cs` — modified and verified (`public partial class Program` present)
- Commit `a9b78ee` — exists (fix(20-01): send poolName null)
- Commit `2c6ba79` — exists (feat(20-01): expose public partial Program type)
- Release build — 0 warnings, 0 errors, -warnaserror passed
