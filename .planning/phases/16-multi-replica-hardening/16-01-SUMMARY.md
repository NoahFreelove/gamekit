---
phase: 16-multi-replica-hardening
plan: "01"
subsystem: core-services/lease-abstraction
tags: [scale, distributed-systems, leader-election, redis, refactor]
status: complete

dependency_graph:
  requires:
    - "Phase 14: IMatchmakerLease (now alias-forward of ILeaderLease)"
    - "Phase 15: OTel instrumentation (no coupling; parallel dependency)"
  provides:
    - "GameKit.Core.Services.ILeaderLease — unified leader-lease abstraction"
    - "GameKit.Core.Services.LeaseStatus — sealed record moved from Matchmaking"
    - "SCALE-01 grep invariant — every LockTakeAsync caller implements ILeaderLease"
  affects:
    - "GameKit.Matchmaking.Services.IMatchmakerLease (extends ILeaderLease)"
    - "GameKit.Matchmaking.Services.MatchmakerLeaseHelper (implements both)"
    - "GameKit.Matchmaking.Services.RedisMatchmakerLease (implements both; gains RenewLeaseAsync stub)"
    - "GameKit.Rankings.Services.RankDecayLeaseHelper (gains ILeaderLease + QueryLeaseAsync)"
    - "GameKit.Rankings.Services.RankingsTickerLeaseHelper (gains ILeaderLease + QueryLeaseAsync)"
    - "tests/GameKit.Core.Tests/Services/LeaderLeaseContractTests.cs (new contract test)"

tech_stack:
  added: []
  patterns:
    - "alias-forward interface pattern (IMatchmakerLease : ILeaderLease empty body)"
    - "reflected assembly load without project reference (Assembly.LoadFrom + FindRepoRoot)"
    - "QueryLeaseAsync Lua snippet (GET + PTTL) copied to Rankings helpers"

key_files:
  created:
    - src/GameKit.Core/Services/ILeaderLease.cs
    - src/GameKit.Core/Services/LeaseStatus.cs
    - tests/GameKit.Core.Tests/Services/LeaderLeaseContractTests.cs
  modified:
    - src/GameKit.Matchmaking/Services/IMatchmakerLease.cs
    - src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs
    - src/GameKit.Matchmaking/Services/RedisMatchmakerLease.cs
    - src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs
    - src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs

decisions:
  - "IMatchmakerLease becomes empty alias-forward (IMatchmakerLease : ILeaderLease {}); all existing DI and health-check references compile unchanged"
  - "LeaseStatus moved to GameKit.Core.Services — Matchmaking helpers add using GameKit.Core.Services to resolve it"
  - "RedisMatchmakerLease.RenewLeaseAsync returns Task.FromResult(false) stub — documented as minimal-impl that does not support renewal"
  - "Rankings helpers gain QueryLeaseAsync via Lua GET+PTTL script — identical to MatchmakerLeaseHelper pattern with lock-key substitution"
  - "Test uses Assembly.LoadFrom+FindRepoRoot instead of project reference to avoid unwanted coupling"

metrics:
  duration_minutes: 7
  completed_date: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_created: 3
  files_modified: 5
---

# Phase 16 Plan 01: ILeaderLease Abstraction (SCALE-01) Summary

**One-liner:** `ILeaderLease` interface extracted to `GameKit.Core.Services` with `LeaseStatus`; all four lease helpers unified behind it; `IMatchmakerLease` becomes an empty alias-forward; contract test verifies the invariant via reflection.

## Tasks Completed

| # | Task | Commit | Key Files |
|---|------|--------|-----------|
| 1 | Create ILeaderLease + LeaseStatus in GameKit.Core.Services; IMatchmakerLease extends ILeaderLease | 97487ee | ILeaderLease.cs, LeaseStatus.cs, IMatchmakerLease.cs |
| 2 | Adapt all four lease helpers to implement ILeaderLease | dacdd5d | MatchmakerLeaseHelper.cs, RedisMatchmakerLease.cs, RankDecayLeaseHelper.cs, RankingsTickerLeaseHelper.cs |
| 3 | Add Core contract test proving every LockTakeAsync caller implements ILeaderLease | fd9c584 | LeaderLeaseContractTests.cs |

## What Was Built

### ILeaderLease Interface (`src/GameKit.Core/Services/ILeaderLease.cs`)

Five-member interface in `GameKit.Core.Services`:
- `string InstanceId { get; }` — fencing token (MachineName:Guid)
- `Task<bool> TryAcquireLeaseAsync(CancellationToken)` — acquire Redis lock
- `Task<bool> RenewLeaseAsync(CancellationToken)` — extend TTL; false = lease lost, caller MUST stop
- `Task ReleaseLeaseAsync(CancellationToken)` — Lua-script-verified release; shutdown paths use non-cancelling token
- `Task<LeaseStatus> QueryLeaseAsync(CancellationToken)` — non-acquiring read for health checks

All members carry full XML doc comments per CLAUDE.md public-API rule.

### LeaseStatus Record (`src/GameKit.Core/Services/LeaseStatus.cs`)

`public sealed record LeaseStatus(string? HolderInstanceId, TimeSpan? Ttl)` — moved verbatim from `GameKit.Matchmaking.Services.IMatchmakerLease`. No behavior change; only namespace changed.

### IMatchmakerLease Alias-Forward (`src/GameKit.Matchmaking/Services/IMatchmakerLease.cs`)

Reduced to an empty alias:
```csharp
public interface IMatchmakerLease : ILeaderLease { }
```
All existing DI registrations (`TryAddSingleton<IMatchmakerLease, RedisMatchmakerLease>`, `Replace<IMatchmakerLease>(...)`), health-check constructor injection, and reconciler/retention-sweep `_lease` fields continue to compile unchanged.

### Four Helpers — ILeaderLease Implementation

| Helper | Changes |
|--------|---------|
| `MatchmakerLeaseHelper` | Added `, ILeaderLease` to class declaration; already had all five members |
| `RedisMatchmakerLease` | Added `, ILeaderLease`; added `RenewLeaseAsync` stub returning `false` |
| `RankDecayLeaseHelper` | Added `: ILeaderLease`; added `QueryLeaseAsync` + `ParseLeaseStatus` using `Decay.LockKey` |
| `RankingsTickerLeaseHelper` | Added `: ILeaderLease`; added `QueryLeaseAsync` + `ParseLeaseStatus` using `Ticker.LockKey` |

### Contract Test (`tests/GameKit.Core.Tests/Services/LeaderLeaseContractTests.cs`)

Two `[Fact]` tests — pure reflection, no Testcontainers, no I/O:
1. `ILeaderLease_ExposesExactlyFiveExpectedMembers` — asserts exactly the five expected member names via `GetProperties` + `GetMethods(IsSpecialName=false)`
2. `IMatchmakerLease_IsAssignableToILeaderLease` — loads `GameKit.Matchmaking` via `Assembly.LoadFrom` (no project reference added) and asserts assignability

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] GetMembers() includes compiler-generated accessors**
- **Found during:** Task 3 (RED phase)
- **Issue:** `iface.GetMembers(DeclaredOnly)` returns both `InstanceId` (property) and `get_InstanceId` (accessor), making the five-member assertion fail
- **Fix:** Changed to `GetProperties(DeclaredOnly)` + `GetMethods(DeclaredOnly).Where(!IsSpecialName)` to collect logical member names only
- **Files modified:** `tests/GameKit.Core.Tests/Services/LeaderLeaseContractTests.cs`
- **Commit:** fd9c584 (in same commit)

**2. [Rule 1 - Bug] Type.GetType fails for non-loaded assembly**
- **Found during:** Task 3 (RED phase)
- **Issue:** `Type.GetType("..., GameKit.Matchmaking")` returns null when the assembly is not in the test process's AppDomain (no project reference)
- **Fix:** Added `Assembly.LoadFrom` fallback with `FindRepoRoot` helper that walks parent directories to locate the Matchmaking DLL in `src/GameKit.Matchmaking/bin/Debug/net10.0/`
- **Files modified:** `tests/GameKit.Core.Tests/Services/LeaderLeaseContractTests.cs`
- **Commit:** fd9c584 (in same commit)

## Verification Results

### Build
- `GameKit.Core`: Build succeeded, 0 warnings, 0 errors
- `GameKit.Matchmaking`: Build succeeded, 0 warnings, 0 errors
- `GameKit.Rankings`: Build succeeded, 0 warnings, 0 errors

### SCALE-01 Grep Invariant
```
for f in $(grep -rl "LockTakeAsync" src/ --include="*.cs"); do
  grep -ql "ILeaderLease\|IMatchmakerLease" "$f" || echo "VIOLATION: $f"
done
```
Result: **no output** (no violations)

### Contract Tests
```
dotnet test tests/GameKit.Core.Tests --filter LeaderLeaseContractTests -p:NuGetAudit=false
```
Result: **Passed: 2, Failed: 0**

## Known Stubs

`RedisMatchmakerLease.RenewLeaseAsync` returns `Task.FromResult(false)` unconditionally. This is intentional — it is a minimal implementation that doesn't support renewal. Documented in XML doc comment. Future plan 16-03 (`ReleaseLeaseAsync(CancellationToken.None)` fix) is out of scope for this plan.

## Threat Flags

None. No new network endpoints, auth paths, or schema changes introduced.

The Lua script in `QueryLeaseAsync` (`GET + PTTL`) is a fixed string constant — no string interpolation of caller input; keys passed via `RedisKey[]` array per T-16-01-01 mitigation. SCALE-01 grep gate (T-16-01-02) verified above.

## Self-Check: PASSED

Files exist:
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Services/ILeaderLease.cs` — FOUND
- `/home/noah/Desktop/projects/gamekit/src/GameKit.Core/Services/LeaseStatus.cs` — FOUND
- `/home/noah/Desktop/projects/gamekit/tests/GameKit.Core.Tests/Services/LeaderLeaseContractTests.cs` — FOUND

Commits exist:
- `97487ee` — feat(16-01): add ILeaderLease + LeaseStatus — FOUND
- `dacdd5d` — feat(16-01): adapt all four lease helpers — FOUND
- `fd9c584` — test(16-01): add LeaderLeaseContractTests — FOUND
