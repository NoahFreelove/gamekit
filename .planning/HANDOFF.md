# GameKit — Handoff (current state)

**Written:** 2026-06-27 · **Branch:** `master` (main repo `/home/noah/Desktop/projects/gamekit`) · **Latest release:** `v0.1.0` (published)

## TL;DR
Phase 21 (Final Demo — 3D Multiplayer Platformer) is **complete and merged to master** (merge commit `e1acdeb`). A **GitHub release pipeline** is live, and **`v0.1.0` is published**: the demo server container image is on GHCR, the `GameKit.*` libraries are on GitHub Packages, and a GitHub Release exists — all built by GitHub Actions on the tag.

> The old `phase-21-demo` git worktree was deleted. All work now happens on `master` in the main repo. Do **not** look for `.claude/worktrees/phase-21-demo` — it no longer exists.

## What GameKit is
A self-hostable, GPL .NET 10 game-services library (NuGet packages: `GameKit.Core`, `Auth`, `Rankings`, `Matchmaking`, `Lobby`, `Presence`, `OpenApi`, `Admin.UI`, …). It is **not** a standalone server — but the `samples/Platformer3D` demo **is** a runnable reference server (GameKit + a fully-customized 3D multiplayer platformer), and that is what the release image ships.

## Release pipeline (live)
- **Workflow:** `.github/workflows/release.yml` — triggers **only on `v*` tags**. Three jobs: build+push the demo image to GHCR; pack+push `GameKit.*` to GitHub Packages; create the GitHub Release.
- **Image:** `ghcr.io/noahfreelove/gamekit:<version>` (and `:latest`).
- **NuGet:** GitHub Packages feed `https://nuget.pkg.github.com/NoahFreelove/index.json`.
- **Versioning:** MinVer, git-tag-driven, `v` prefix (tag `v0.1.0` → version `0.1.0`).
- **Admin-only rule:** repository ruleset `Release tags — admin only` (id `18202270`, active) restricts **creation + deletion of `v*` tags to repo admins** → only you can cut releases → no Action-spam surface.

### Cut a new release
```bash
git tag vX.Y.Z && git push origin vX.Y.Z      # only admins can; the workflow does the rest
```

## Run the demo server (easy, pull-and-run — no checkout/build)
```bash
curl -fsSL https://raw.githubusercontent.com/NoahFreelove/gamekit/v0.1.0/samples/Platformer3D/docker-compose.release.yml -o docker-compose.yml
GAMEKIT_TAG=v0.1.0 docker compose up
```
Open http://localhost:8080. Admin console `/admin` → `root` / `platformer-demo-admin`. Two players = two browser **profiles** (guest identity is per-localStorage device id).

## Local dev
- Build: `dotnet build GameKit.sln` (master has the Phase-18 MessagePack pin, so `NuGetAudit` is fine here — the old `-p:NuGetAudit=false` workaround belonged to the deleted worktree).
- Run the demo locally (builds from source): `docker compose -f samples/Platformer3D/docker-compose.yml up -d --build`.
- Two-player browser e2e: `node tests/e2e-browser.mjs` (Playwright `chrome-headless-shell` + `--no-sandbox`; one-time `npx playwright install chromium` + `npm i playwright-core`). See [headless-browser memory].

## What shipped in Phase 21 (squashed commits `46d0db0` / `10e8e89` / `84422d2`)
- **Inter-party 1v1** via matchmaking self-match — a 2-member party plays a 1v1 between its members (authorized `GameKit.*` package changes: `MatchmakerTickerService`, `TeamAssignmentService`, `ProposalService` + demo `BestTimeMatchmakingStrategy`).
- **No-elo / anti-abuse:** inter-party matches are created **unranked** (null `LadderId`) so "party up + friend AFKs → free elo" is impossible.
- **Demo functional overhaul:** menu-driven client (sign-in → menu → ranked quick-match / friend party / solo / leaderboard → searching → game → results), results screen with rating delta, real leaderboard, `/ws/game` WebSocket auth fix (was anonymous → matches never completed), GameServer DNF timeout, responsive rankings.

## Open follow-ups
1. **`ci.yml` fails at 0s on every push** — pre-existing, unrelated to the release work (I didn't touch `ci.yml`). Needs a separate look (likely a missing secret or an invalid trigger/condition in the workflow).
2. **Smoke-test `docker-compose.release.yml`** now that the image is published — it uses a single Postgres superuser (no init SQL) for clean pull-and-run; verify it boots end-to-end (couldn't test pre-publish).
3. **Public NuGet?** Packages are on GitHub Packages (consumers need that source configured). If you want friction-free `dotnet add package`, add a nuget.org publish step (needs a `NUGET_API_KEY` repo secret).
4. The minor uncommitted files in the repo (`.planning/config.json`, a `.gitkeep`) were never touched this session — deal with separately if desired.

## Key references
- Phase 21 detail: `.planning/phases/21-final-demo-3d-multiplayer-platformer/` (`21-inter-party-1v1-SUMMARY.md`, `21-demo-functional-overhaul-SUMMARY.md`).
- Release infra: `.github/workflows/release.yml`, `samples/Platformer3D/docker-compose.release.yml`.
- Demo run/build gotchas + the `e2e-browser.mjs` harness: see the project memory.
