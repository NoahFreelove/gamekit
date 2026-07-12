---
phase: quick-260712-n7f-relicense
plan: 01
subsystem: infra
tags: [license, spdx, reuse, nuget, apache-2.0]

requires: []
provides:
  - "Repo-wide relicense from GPL-3.0-or-later to Apache-2.0 (LICENSE text, REUSE/SPDX machinery, NuGet metadata, header test, prose)"
affects: [any future phase touching LICENSE, REUSE.toml, NuGet packaging metadata, or license prose]

tech-stack:
  added: []
  patterns: ["scripted token-only SPDX rewrite via git grep + perl -pi, pathspec-excluding .planning/ and LICENSES/"]

key-files:
  created:
    - LICENSES/Apache-2.0.txt
  modified:
    - LICENSE
    - Directory.Build.props
    - Directory.Packages.props
    - tests/GameKit.Core.Tests/LicenseHeaderTests.cs
    - REUSE.toml
    - CLAUDE.md
    - README.md
    - docker/README.md
    - docs/adr/0004-aspnet-contrib-oauth.md
    - docs/adr/0006-scrutor-msdi-di.md
    - docs/adr/0007-fluentvalidation-explicit.md
    - docs/adr/0008-bcrypt-default-argon2-optin.md
    - docs/ops/multi-replica.md
    - docs/runbooks/postgres-backup-restore.md
    - samples/TicTacToeDuel/observability/README.md
    - tests/k6/README.md
    - site/public/index.html
    - templates/GameKit.Templates/README.md
    - templates/GameKit.Templates/content/GameKit.SampleGame/README.md
    - src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs
    - "963 SPDX-headered files across src/**, tests/**, samples/**, benchmarks/**, templates/**, docs/**, .github/workflows/**"
    - "12 src/GameKit.*.csproj (PackageTags NuGet metadata, discovered via Gate B sweep, not in plan's explicit file list)"

key-decisions:
  - "Deleted LICENSES/GPL-3.0-or-later.txt via git rm (reuse lint flags unused license files); LICENSES/Apache-2.0.txt fetched via `reuse download Apache-2.0`"
  - "Directory.Build.props: PackageLicenseFile -> PackageLicenseExpression=Apache-2.0 (mutually exclusive NuGet properties, NU5033/NU5039)"
  - "12 src/GameKit.*.csproj PackageTags entries literally tagged 'gpl' on NuGet — fixed to 'apache-2.0' as an in-scope Rule 1 auto-fix, found via the Gate B repo-wide grep sweep rather than the plan's static file list"
  - "CLAUDE.md:169's citation of the archived .planning/ directory name '...ops-defaults-gpl' left untouched — it is a historical directory-name reference, not a GameKit license claim, and renaming .planning/ paths is explicitly out of scope per the plan"

requirements-completed: [RELICENSE-01]

coverage:
  - id: D1
    description: "Root LICENSE replaced with official Apache License 2.0 text; LICENSES/Apache-2.0.txt added; LICENSES/GPL-3.0-or-later.txt removed"
    requirement: "RELICENSE-01"
    verification:
      - kind: other
        ref: "head -5 LICENSE | grep 'Apache License'; test -f LICENSES/Apache-2.0.txt; test ! -f LICENSES/GPL-3.0-or-later.txt"
        status: pass
    human_judgment: false
  - id: D2
    description: "Directory.Build.props declares PackageLicenseExpression=Apache-2.0 (PackageLicenseFile removed); 12 csproj PackageTags corrected from 'gpl' to 'apache-2.0'"
    requirement: "RELICENSE-01"
    verification:
      - kind: other
        ref: "grep PackageLicenseExpression Directory.Build.props; XML well-formed check on all 12 touched csproj files; dotnet build src/GameKit.Core succeeded"
        status: pass
    human_judgment: false
  - id: D3
    description: "Zero GPL-3.0-or-later SPDX tokens remain outside .planning/ and LICENSES/ (963 files rewritten via scripted perl pass); Glicko2 files read BSD-3-Clause AND Apache-2.0"
    requirement: "RELICENSE-01"
    verification:
      - kind: other
        ref: "git grep -nI 'GPL-3.0-or-later' -- ':!.planning' ':!LICENSES' (zero matches); git grep 'BSD-3-Clause AND Apache-2.0' src/GameKit.Rankings/Glicko2/Rating.cs"
        status: pass
    human_judgment: false
  - id: D4
    description: "LicenseHeaderTests.cs asserts Apache-2.0 headers (renamed test method, updated doc comments and dual-license branch); test passes"
    requirement: "RELICENSE-01"
    verification:
      - kind: unit
        ref: "tests/GameKit.Core.Tests/LicenseHeaderTests.cs#Every_CSharp_Source_File_Has_SPDX_Apache_Header"
        status: pass
    human_judgment: false
  - id: D5
    description: "Non-token GPL prose relicensed across CLAUDE.md, README.md, docs/adr/*, docs/ops, docs/runbooks, Directory.Packages.props, site/public/index.html, templates READMEs; AGPLv3 third-party descriptions (k6, Redis, Grafana/Tempo) preserved factually"
    requirement: "RELICENSE-01"
    verification:
      - kind: other
        ref: "git grep -nIiE 'GPL' -- ':!.planning' ':!LICENSES' | grep -viE 'AGPL' — only false-positive substring matches remain (see Deviations)"
        status: pass
    human_judgment: false

duration: 8min
completed: 2026-07-12
status: complete
---

<!-- REUSE-IgnoreStart -->

# Quick Task 260712-n7f: Relicense GameKit from GPL-3.0-or-later to Apache-2.0 Summary

**Full repo relicense to Apache-2.0: root LICENSE swap, 963-file scripted SPDX header rewrite, REUSE/LICENSES/ sync, NuGet PackageLicenseExpression + PackageTags metadata, LicenseHeaderTests reassertion, and non-token prose cleanup across docs/site/templates while preserving factual AGPLv3 third-party statements.**

## Performance

- **Duration:** 8 min
- **Started:** 2026-07-12T16:57:00-04:00
- **Completed:** 2026-07-12T17:03:25-04:00
- **Tasks:** 3
- **Files modified:** 994 total (LICENSE swap: 5 files; scripted token pass: 963 files; prose + PackageTags pass: 28 files — some overlap between task boundaries)

## Accomplishments
- Root `LICENSE` is now the official Apache License 2.0 text; `LICENSES/Apache-2.0.txt` added via `reuse download`; `LICENSES/GPL-3.0-or-later.txt` removed
- `Directory.Build.props` declares `<PackageLicenseExpression>Apache-2.0</PackageLicenseExpression>` (mutually-exclusive `PackageLicenseFile` removed) — applies to all 6 shipped NuGet packages
- `tests/GameKit.Core.Tests/LicenseHeaderTests.cs` rewritten to assert Apache-2.0 SPDX headers (renamed test method `Every_CSharp_Source_File_Has_SPDX_Apache_Header`); passes
- Scripted `git grep | perl -pi` pass rewrote the exact token `GPL-3.0-or-later` -> `Apache-2.0` across 963 git-tracked files (SPDX headers, `REUSE.toml` annotations, compound `BSD-3-Clause AND ...` / `MIT AND ...` expressions, Glicko2 prose parenthetical, exact-token README/CONTRIBUTING/docs prose)
- Non-token GPL prose relicensed in CLAUDE.md, README.md, `Directory.Packages.props`, four ADRs, `docs/ops/multi-replica.md`, `docs/runbooks/postgres-backup-restore.md`, `docker/README.md`, samples observability README, `tests/k6/README.md`, `site/public/index.html`, and both templates READMEs — GameKit's own license claim now reads Apache-2.0 everywhere, while factual third-party AGPLv3 descriptions (k6, Redis image option, Grafana/Tempo) are preserved
- Auto-fixed 12 `src/GameKit.*.csproj` `PackageTags` entries that literally tagged packages `gpl` on NuGet — discovered via the Gate B repo-wide grep sweep, not in the plan's static file list, but directly in scope for relicensing NuGet package metadata

## Task Commits

Each task was committed atomically (Task 1 split across two commits because a `git add` pathspec error mid-command left the `LICENSES/GPL-3.0-or-later.txt` deletion staged separately from the rest of the file swap):

1. **Task 1: Swap license files, NuGet metadata, and the license-header test to Apache-2.0** - `54cbf1e` (feat, deletion) + `5e93e16` (feat, remaining file swap)
2. **Task 2: Scripted bulk SPDX token rewrite GPL-3.0-or-later -> Apache-2.0** - `943f279` (feat)
3. **Task 3: Relicense non-token prose + full verification** - `5e8d38e` (feat)
4. **Follow-up (orchestrator-sanctioned): make `reuse lint` exit 0** - `8f83f84` (fix) — REUSE.toml overrides for `.planning/**` (embedded SPDX examples in process docs) + `site/**` aggregate coverage (pre-existing gap on master); REUSE-Ignore markers around the plan doc's quoted SPDX strings (PLAN.md edit sanctioned by orchestrator override)

_No plan-metadata commit — orchestrator handles the docs commit in Step 8._

## Files Created/Modified
- `LICENSE` - Official Apache License 2.0 text (was GPL-3.0)
- `LICENSES/Apache-2.0.txt` - New, fetched via `reuse download Apache-2.0`
- `LICENSES/GPL-3.0-or-later.txt` - Deleted
- `Directory.Build.props` - `PackageLicenseExpression=Apache-2.0` replaces `PackageLicenseFile`
- `tests/GameKit.Core.Tests/LicenseHeaderTests.cs` - Asserts Apache-2.0 SPDX headers
- `REUSE.toml` + 963 SPDX-headered source/doc/config files - Token rewrite `GPL-3.0-or-later` -> `Apache-2.0`
- `CLAUDE.md`, `README.md`, `Directory.Packages.props`, `docs/adr/0004/0006/0007/0008`, `docs/ops/multi-replica.md`, `docs/runbooks/postgres-backup-restore.md`, `docker/README.md`, `samples/TicTacToeDuel/observability/README.md`, `tests/k6/README.md`, `site/public/index.html`, `templates/GameKit.Templates/README.md`, `templates/GameKit.Templates/content/GameKit.SampleGame/README.md`, `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs` - Non-token GPL prose relicensed
- 12 `src/GameKit.*.csproj` files - `PackageTags` `;gpl` -> `;apache-2.0`

## Decisions Made
- Deleted `LICENSES/GPL-3.0-or-later.txt` via `git rm` after Task 2's token pass, since `reuse lint` flags unused license files
- `PackageLicenseFile` and `PackageLicenseExpression` are mutually exclusive NuGet properties (NU5033/NU5039) — removed the former entirely rather than keeping both
- Fixed 12 csproj `PackageTags` (`gpl` -> `apache-2.0`) as an in-scope Rule 1 auto-fix discovered by the Task 3 Gate B sweep, since it's stale NuGet license metadata directly within the relicensing objective
- Left CLAUDE.md's citation of the archived `.planning/phases/01-foundation-core-migrations-ops-defaults-gpl/` directory name untouched — it's a historical path reference (not a GameKit license claim) and renaming `.planning/` directories is explicitly out of scope per the plan

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] 12 src/GameKit.*.csproj PackageTags entries literally tagged the package `gpl`**
- **Found during:** Task 3 (Gate B repo-wide grep sweep, run in addition to the plan's static file list)
- **Issue:** `<PackageTags>gamekit;...;gpl</PackageTags>` in `GameKit.Admin.UI`, `GameKit.Auth`, `GameKit.Auth.Apple`, `GameKit.Auth.Argon2`, `GameKit.Auth.Epic`, `GameKit.Auth.Google`, `GameKit.Core`, `GameKit.Lobby`, `GameKit.Matchmaking`, `GameKit.OpenApi`, `GameKit.Presence`, `GameKit.Rankings` — a stale NuGet.org search tag claiming GPL after the license actually changed to Apache-2.0
- **Fix:** `perl -pi -e 's/;gpl<\/PackageTags>/;apache-2.0<\/PackageTags>/'` across all 12 files
- **Files modified:** the 12 csproj files listed above
- **Verification:** All 12 files verified well-formed XML via `python3 -c "xml.dom.minidom.parse(...)"`; `dotnet build src/GameKit.Core` succeeded
- **Committed in:** `5e8d38e` (Task 3 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — bug)
**Impact on plan:** Necessary correctness fix for NuGet package metadata within the relicensing objective. No scope creep — every other change matches the plan's explicit file list verbatim.

## Issues Encountered

- `git add <deletion-path> <other-files>` failed atomically when one pathspec (`LICENSES/GPL-3.0-or-later.txt`, already staged by an earlier `git rm`) no longer matched a working-tree file — git refused to stage any of the listed paths. Resolved by splitting Task 1 into two commits: the pre-staged deletion landed alone in `54cbf1e`, and the remaining LICENSE/Directory.Build.props/LicenseHeaderTests.cs/Apache-2.0.txt changes landed in `5e93e16`. No functional impact — both commits are within Task 1's scope.
- The plan's Task 3 automated verify command chains `Gate A && Gate B && dotnet test && reuse-fallback` with `&&`, so a literal read requires Gate B (case-insensitive `GPL` grep, AGPL-excluded) to return zero matches project-wide. After all substantive fixes, 4 categories of residual matches remain — all confirmed non-license-related:
  - `samples/Platformer3D/wwwroot/js/three.core.js` / `three.module.js` — vendored MIT three.js code; `clippingPlanes`/`NUM_CLIPPING_PLANES` identifiers contain the substring "gPl" coincidentally. Vendored file; out of scope to rename identifiers.
  - `src/GameKit.Rankings/Services/RankingsTickerService.cs` — `existingPlayerIds` variable name contains "gPl" coincidentally.
  - `tests/GameKit.Core.Tests/Services/PlayerDisplayNameResolverTests.cs` — `ExistingPlayer` test-name segment contains "gPl" coincidentally.
  - `CLAUDE.md:169` — cites the archived `.planning/phases/01-foundation-core-migrations-ops-defaults-gpl/` directory name; a historical path reference, not a license claim. Renaming `.planning/` directories is explicitly out of scope per the plan's own constraint ("Do NOT touch .planning/").

  None of these are GameKit license claims. The plan's own `<verification>` prose and `<success_criteria>` (not the literal grep) define the actual bar: "no stale GameKit-GPL claim; AGPLv3 third-party descriptions preserved" — which is met. `reuse lint` initially failed for two out-of-scope reasons (the plan file's own quoted grep-command text under `.planning/`, and `site/public/*` files that predate this task and were never REUSE-annotated); an orchestrator-sanctioned follow-up (`8f83f84`) closed both via REUSE.toml annotations (`.planning/**` override, `site/**` aggregate) plus REUSE-Ignore markers in the plan doc — `reuse lint` now exits 0 fully compliant (Missing licenses: 0, Invalid SPDX Expressions: 0, Used licenses: Apache-2.0/BSD-3-Clause/MIT only).
- `dotnet test tests/GameKit.Core.Tests --filter LicenseHeaderTests` passes (1/1); `dotnet build src/GameKit.Core` succeeds with 0 warnings/errors, confirming no functional breakage from the metadata-only and prose-only changes.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- Relicense complete and verified: LICENSE, LICENSES/, REUSE.toml, all 963 SPDX headers, NuGet PackageLicenseExpression + PackageTags, and prose all read Apache-2.0
- `LicenseHeaderTests` is the durable regression gate for future contributions — any new `.cs` file without an `Apache-2.0` header will fail CI
- No blockers. The 4 confirmed-false-positive grep matches (documented above) do not require follow-up; they are substring coincidences and an out-of-scope archived directory name

---
*Phase: quick-260712-n7f-relicense*
*Completed: 2026-07-12*

## Self-Check: PASSED

- FOUND: LICENSE
- FOUND: LICENSES/Apache-2.0.txt
- FOUND: Directory.Build.props
- FOUND: tests/GameKit.Core.Tests/LicenseHeaderTests.cs
- CONFIRMED DELETED: LICENSES/GPL-3.0-or-later.txt
- FOUND commit: 54cbf1e
- FOUND commit: 5e93e16
- FOUND commit: 943f279
- FOUND commit: 5e8d38e
- FOUND commit: 8f83f84 (follow-up: reuse lint green)
- reuse lint exit 0 — REUSE 3.3 compliant

<!-- REUSE-IgnoreEnd -->
