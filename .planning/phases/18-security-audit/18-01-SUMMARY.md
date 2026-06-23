---
phase: 18-security-audit
plan: "01"
subsystem: build/ci
tags: [security, cve, nuget-audit, messagepack, supply-chain]
dependency_graph:
  requires: []
  provides: [clean-build-with-audit-gate, messagepack-cve-eliminated, ci-vulnerability-scan]
  affects: [all-packages, ci-pipeline]
tech_stack:
  added: []
  patterns: [NuGetAuditMode=all, CentralPackageTransitivePinningEnabled, transitive-pin]
key_files:
  modified:
    - Directory.Packages.props
    - Directory.Build.props
    - .github/workflows/ci.yml
decisions:
  - "Transitive-pin MessagePack 3.1.7 (not suppress) — CVE eliminated by upgrade, GHSA-hv8m-jj95-wg3x resolved"
  - "NuGetAuditMode=all + NuGetAuditLevel=high added to Directory.Build.props as MSBuild gate"
  - "Explicit dotnet list package --vulnerable --include-transitive CI step added after Restore; exits non-zero on High/Critical"
  - "-p:NuGetAudit=false workaround is now obsolete — do not use in any downstream Phase 18 plans or MEMORY.md references"
metrics:
  duration: "~6 minutes"
  completed: "2026-06-23"
  tasks_completed: 2
  tasks_total: 2
  files_modified: 3
status: complete
---

# Phase 18 Plan 01: CVE Supply-Chain Gate + MessagePack Pin Summary

**One-liner:** MessagePack 3.1.7 transitive pin eliminates GHSA-hv8m-jj95-wg3x (HIGH); NuGetAuditMode=all gate + explicit CI vulnerable-scan step enforces supply-chain hygiene going forward.

## What Was Built

### Task 1: Transitive-pin MessagePack 3.1.7 + enable NuGet audit gate

**`Directory.Packages.props`** — Added two transitive pins immediately after the SignalR StackExchangeRedis pin (for locality/context):

```xml
<!-- SEC-07 (Phase 18-01): Transitive pin to resolve GHSA-hv8m-jj95-wg3x
     (MessagePack 2.5.187 HIGH severity CVE).
     Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8 requires >= 2.5.187;
     3.1.7 satisfies the constraint and is clean of the advisory.
     CentralPackageTransitivePinningEnabled=true propagates this pin to all transitive uses. -->
<PackageVersion Include="MessagePack" Version="3.1.7" />
<PackageVersion Include="MessagePack.Annotations" Version="3.1.7" />
```

**`Directory.Build.props`** — Added audit gate properties to the common `<PropertyGroup>`:

```xml
<!-- SEC-07 (Phase 18-01): NuGet supply-chain audit gate. -->
<NuGetAuditMode>all</NuGetAuditMode>
<NuGetAuditLevel>high</NuGetAuditLevel>
```

Both changes landed in a single commit (`fe47f30`) — atomic per RESEARCH Pitfall 4 (enabling the gate without the pin would have broken restore for all 15 SignalR-transitive projects).

**Commit:** `fe47f30`

### Task 2: Prove the gate is live — add explicit CI vulnerability scan step

**`.github/workflows/ci.yml`** — Added `Vulnerability scan (SEC-07 gate)` step placed after Restore and before Build:

```yaml
- name: Vulnerability scan (SEC-07 gate)
  run: |
    dotnet list package --vulnerable --include-transitive 2>&1 | tee /tmp/vuln-report.txt
    if grep -qiE '^\s*(High|Critical)' /tmp/vuln-report.txt; then
      echo "ERROR: HIGH or CRITICAL vulnerability found in dependency graph:" >&2
      grep -iE '^\s*(High|Critical)' /tmp/vuln-report.txt >&2
      exit 1
    fi
    echo "Vulnerability scan passed — no HIGH or CRITICAL advisories found."
```

No step in the workflow uses `-p:NuGetAudit=false`. The explicit step satisfies ROADMAP Phase 18 success criterion #1.

**Commit:** `5a495ff`

---

## Deliverable Proof

### Clean-build proof (NuGetAuditMode=all active, no suppression)

```
dotnet build GameKit.sln --configuration Release -warnaserror

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:16.86
```

### Restore proof (0 NU1903)

```bash
dotnet restore GameKit.sln 2>&1 | grep -c NU1903
0
```

### Vulnerability scan proof (all 40 projects clean)

```
The given project `GameKit.Lobby` has no vulnerable packages given the current sources.
The given project `GameKit.Matchmaking` has no vulnerable packages given the current sources.
[... 38 more — all clean ...]
Vulnerability scan passed — no HIGH or CRITICAL advisories found.
```

### Lobby integration test proof (MessagePack 3.1.7 runtime compat with SignalR)

```
Passed!  - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 7s
```

17/17 Lobby integration tests pass — MessagePack 3.1.7 is fully runtime-compatible with `Microsoft.AspNetCore.SignalR.StackExchangeRedis 10.0.8` (RESEARCH Assumption A1 confirmed).

---

## Deviations from Plan

None — plan executed exactly as written.

The two changes (Directory.Packages.props pin + Directory.Build.props gate) were committed atomically in a single commit per RESEARCH Pitfall 4. The CI step matches the pattern specified in RESEARCH §SEC-07 and the PLAN action description.

---

## Important Downstream Note: `-p:NuGetAudit=false` Is NOW OBSOLETE

The MEMORY.md note "Pre-existing MessagePack NU1903" instructing to build affected packages with `-p:NuGetAudit=false` is **no longer applicable**. The GHSA-hv8m-jj95-wg3x advisory has been eliminated by upgrade to MessagePack 3.1.7 — not suppressed.

**All subsequent Phase 18 plans and any new build commands must NOT use `-p:NuGetAudit=false`.**

This includes:
- Per-task verification builds in plans 18-02 through 18-08
- Local development builds
- CI steps (none currently use it)
- Any documentation or MEMORY.md references going forward

---

## Threat Mitigations Applied

| Threat ID | Status | Mitigation |
|-----------|--------|-----------|
| T-18-01-SC | RESOLVED | MessagePack 3.1.7 pin eliminates GHSA-hv8m-jj95-wg3x from all 15 affected projects |
| T-18-01-02 | RESOLVED | NuGetAuditMode=all + CI scan step gates all future HIGH/CRITICAL CVEs |
| T-18-01-03 | RESOLVED | Zero NuGetAuditSuppress entries added; upgrade path is auditable in Directory.Packages.props |

---

## Self-Check: PASSED

- [x] `Directory.Packages.props` contains `MessagePack 3.1.7` pin — FOUND
- [x] `Directory.Build.props` contains `NuGetAuditMode=all` — FOUND
- [x] `.github/workflows/ci.yml` contains `--vulnerable --include-transitive` step — FOUND
- [x] `git log --oneline` contains `fe47f30` (Task 1) and `5a495ff` (Task 2) — VERIFIED
- [x] `dotnet restore` → 0 NU1903 — VERIFIED
- [x] `dotnet build -warnaserror` → Build succeeded, 0 errors — VERIFIED
- [x] `dotnet list package --vulnerable` → no HIGH/CRITICAL — VERIFIED
- [x] Lobby integration tests → 17/17 PASSED — VERIFIED
