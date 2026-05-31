# ROADMAP: GameKit

**Project:** GameKit — GPL, self-hostable, composable .NET 10 game-services library
**Current status:** v1.0 shipped 2026-05-30. No active milestone.

## Milestones

- ✅ **v1.0 — Initial 6-Phase Build-Out** (2026-04-15 → 2026-05-26, shipped 2026-05-30) — 7 NuGet packages (Core, Auth, Rankings, Matchmaking, Presence, Admin.UI, OpenApi) + CLI + template; full auth, rankings (Glicko-2), crash-safe matchmaking, Blazor admin UI, presence, OpenAPI, and a 9-file ops guide. 92/92 requirements. → [milestones/v1.0-ROADMAP.md](milestones/v1.0-ROADMAP.md)

## Next

No active milestone. Run `/gsd:new-milestone` to define v1.x / v2 (questioning → research → requirements → roadmap).

**Carried-forward backlog for the next milestone** (from the [v1.0 audit](v1.0-MILESTONE-AUDIT.md)):
- Wire Rankings → Matchmaking ratings (EloRange is rating-blind in v1.0).
- Replace the Admin "Rank adjust" sidebar stub page (functional path already works via dialog).
- v2 candidates already noted in the requirements archive: Argon2 hasher (`GameKit.Auth.Argon2`), Google/Apple/Epic OAuth, account merge, rank decay, placement matches, richer parties/lobby, backfill, regional pools, multi-replica Admin UI.

---
*v1.0 roadmap detail archived at `.planning/milestones/v1.0-ROADMAP.md`.*
