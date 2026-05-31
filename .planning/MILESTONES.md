# MILESTONES: GameKit

A running index of shipped milestones. Newest first.

| Version | Name | Shipped | Duration | Requirements | Archive |
|---------|------|---------|----------|--------------|---------|
| v1.0 | Initial 6-Phase Build-Out | 2026-05-30 | ~6 weeks (2026-04-15 → 2026-05-26) | 92/92 | [roadmap](milestones/v1.0-ROADMAP.md) · [requirements](milestones/v1.0-REQUIREMENTS.md) · [audit](v1.0-MILESTONE-AUDIT.md) |

## v1.0 — Initial 6-Phase Build-Out

Shipped GameKit as 7 composable GPL NuGet packages on .NET 10 (Core, Auth, Rankings, Matchmaking, Presence, Admin.UI, OpenApi) plus a CLI, a build-time version-stamp source generator, and a `dotnet new gamekit` template. Self-hosted on Postgres + Redis only; every algorithm a DI-swappable interface; no cloud, no telemetry.

- **Phases:** 7 · **Plans:** 60 · **Commits:** 152 · **Source:** ~34.3k LOC · **Tests:** ~29.6k LOC (18 projects)
- **Status:** ✅ Complete — audit `tech_debt` (no blockers; 2 documented integration warnings carried to v1.x)
