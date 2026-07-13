---
phase: 20-docs-tutorial
plan: "02"
subsystem: docs-ci
tags: [docfx, api-reference, ci-gate, docs-01]
dependency_graph:
  requires: [20-01]
  provides: [DOCS-01]
  affects: [.github/workflows/ci.yml, Directory.Build.props, .config/dotnet-tools.json, docfx.json]
tech_stack:
  added:
    - docfx 2.78.5 (local tool manifest)
  patterns:
    - dotnet local tool manifest (.config/dotnet-tools.json)
    - DocFX metadata+build CI gate (--warningsAsErrors)
key_files:
  created:
    - .config/dotnet-tools.json
    - docfx.json
  modified:
    - Directory.Build.props
    - .gitignore
    - .github/workflows/ci.yml
    - docs/performance-tuning.md
decisions:
  - "Removed 2 duplicate <AdditionalFiles> includes for AnalyzerReleases.Shipped/Unshipped.md from Directory.Build.props; Roslyn SDK auto-discovery is sufficient"
  - "docfx.json metadata src glob scoped to src/**/*.csproj only (excludes samples/ and tests/)"
  - "Fixed broken docfx cross-ref in docs/performance-tuning.md (unresolvable ../benchmarks/BASELINES.md file link)"
metrics:
  duration: "~14 minutes"
  completed: "2026-06-23"
  tasks_completed: 3
  tasks_total: 3
  files_modified: 6
status: complete
---

# Phase 20 Plan 02: DocFX API-Reference Gate (DOCS-01) Summary

DocFX local tool manifest, repo-root `docfx.json`, Directory.Build.props fix, and CI gate so `dotnet docfx docfx.json --warningsAsErrors` exits 0 on every push.

## What Was Built

**DOCS-01** — The strict DocFX API-reference gate is operational. Three deliverables:

1. **Directory.Build.props fix** — Removed 2 explicit `<AdditionalFiles>` includes for `AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md` under the `AssemblyName == 'GameKit.Build'` condition. The Roslyn SDK auto-discovers these files via its own `Exists()` condition, so the explicit includes caused docfx to see each file twice ("Duplicate source file" warnings, exit 255). With the duplicates removed, `docfx metadata --warningsAsErrors` exits 0. The `pii-allowlist.txt` include and all other props are untouched. GameKit.Build analyzer builds clean: 0 warnings, 0 errors, -warnaserror.

2. **docfx local tool manifest + docfx.json** — `.config/dotnet-tools.json` pins docfx 2.78.5 so CI runs `dotnet tool restore` + `dotnet docfx` with no global install. `docfx.json` at repo root scopes metadata to `src/**/*.csproj` only (excludes `samples/` and `tests/`), outputs API YAML to `api/`, and builds the site to `_site/`. Both output directories are added to `.gitignore`; `docs/` is not gitignored.

3. **CI gate** — Two steps added to the `build-and-test` job in `.github/workflows/ci.yml` after the existing Build step and before Unit tests: `dotnet tool restore` and `dotnet docfx docfx.json --warningsAsErrors`. The gate runs on every PR and push to main. No `-p:NuGetAudit=false` was added.

## Proof

```
$ dotnet tool restore && dotnet docfx docfx.json --warningsAsErrors
Tool 'docfx' (version '2.78.5') was restored. Available commands: docfx
Restore was successful.
...
Build succeeded.
    0 warning(s)
    0 error(s)

EXIT=0
```

```
$ dotnet build src/GameKit.Build/GameKit.Build.csproj -c Release -warnaserror
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Fixed broken docfx cross-reference in docs/performance-tuning.md**
- **Found during:** Task 2 — first docfx run after adding docfx.json
- **Issue:** `docs/performance-tuning.md` line 8 had a markdown link `[benchmarks/BASELINES.md](../benchmarks/BASELINES.md)`. DocFX resolves this relative to the docset root; `benchmarks/*.md` is not in the docfx build content (only `docs/**/*.md` is included), so docfx emitted an `InvalidFileLink` warning: "Invalid file link:(~/benchmarks/BASELINES.md)." This caused `--warningsAsErrors` to exit 255.
- **Fix:** Changed the hyperlink `[text](../benchmarks/BASELINES.md)` to a code-span `` `benchmarks/BASELINES.md` ``. The referenced file exists on disk and is still navigable via the filesystem/GitHub; the docset just cannot hyperlink to files outside its content set.
- **Files modified:** `docs/performance-tuning.md`
- **Commit:** 7964f1d (included with Task 2)

## Known Stubs

None. All deliverables are fully wired: tool manifest pins a real version, docfx.json targets real project files, CI gate runs the real command.

## Self-Check: PASSED

- `.config/dotnet-tools.json` exists: FOUND
- `docfx.json` exists: FOUND
- `_site/` in `.gitignore`: FOUND
- `api/` in `.gitignore`: FOUND
- `docs/` NOT gitignored: CONFIRMED
- Task 1 commit da1aebd: FOUND (`fix(20-02): remove 2 duplicate AdditionalFiles lines`)
- Task 2 commit 7964f1d: FOUND (`feat(20-02): add docfx local tool manifest, docfx.json, and gitignore entries`)
- Task 3 commit 7065276: FOUND (`feat(20-02): wire DocFX --warningsAsErrors gate into CI build-and-test job`)
- `docfx.json --warningsAsErrors` in ci.yml: FOUND
- `dotnet tool restore` step in ci.yml: FOUND
- No active `NuGetAudit=false` in step commands: CONFIRMED (only in comments)
- `AnalyzerReleases` NOT in Directory.Build.props: CONFIRMED
- `pii-allowlist.txt` still in Directory.Build.props: CONFIRMED
- GameKit.Build build with -warnaserror: 0 warnings, 0 errors
