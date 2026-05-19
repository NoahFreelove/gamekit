---
slug: phase-031-verification-gaps
type: quick
created: 2026-05-15
status: in-progress
source: .planning/phases/03.1-admin-ui-redesign-v2/03.1-VERIFICATION.md
---

# Close Phase 03.1 verification gaps

## Problem

`03.1-VERIFICATION.md` reports score 4.5/6 with one unresolved BLOCKER and one PARTIAL
that cannot flip to VERIFIED without code changes:

- **BLOCKER-GAP-01** — `PlayerDetailPane.LoadAsync` resolves the banning admin's name
  through `IPlayerDisplayNameResolver`, which only queries the `players` table.
  AdminUser IDs never exist in `players`, so every human-issued ban renders the
  configured tombstone ("Deleted Player") instead of the admin's username.
- **Anti-pattern** introduced by the same plan: two `ConfigureAwait(false)` calls in
  a Blazor Server component whose continuation calls `StateHasChanged()`.
- **INFO-GAP-03** — `applyAttrs(loadTweaks())` runs at script-bundle init, before
  Blazor mounts `TweaksPanel`, so the aria-checked reflection finds 0 buttons on
  first paint.

## Fixes

### Fix 1 — `src/GameKit.Admin.UI/Components/Shared/PlayerDetailPane.razor`

Replace the resolver call (line 273) with a direct `admin_users` lookup, and remove
both `ConfigureAwait(false)` calls (lines 270, 273).

The page already has a scoped `GameKitDbContext Db` injection — we just query
`Db.Set<AdminUser>()` directly with `AsNoTracking`. Add the `using
GameKit.Admin.UI.Entities;` import. Drop the now-unused
`IPlayerDisplayNameResolver` inject if nothing else in the component uses it.

### Fix 2 — `src/GameKit.Admin.UI/wwwroot/gamekit-admin.js`

Refresh aria-checked from `openTweaks()` so the buttons reflect the persisted
selection at the moment the panel becomes visible. The initial `applyAttrs` call
at line 422 still runs (it sets the `<html>` attributes that prevent FOUC — those
work because `<html>` is always in DOM), but the button-level reflection is
deferred until the panel is opened.

### Fix 3 — Regression test

Add a bUnit test to `PlayersWorkspaceTests` (or a sibling) that:
- Seeds an `AdminUser` row in the InMemory DB (the existing
  `TestDbContextFactory.InMemoryTestModelCustomizer` already registers the entity).
- Seeds a banned `Player` row.
- Seeds an `AdminAuditLog` row with `Action = "admin.player.ban"`,
  `TargetId = player.Id`, `ActorId = admin.Id`.
- Renders `PlayerDetailPane` with that player id.
- Asserts the rendered markup contains the admin's username and does NOT contain
  the tombstone string `"Deleted Player"`.

## Bookkeeping

- Update `.planning/phases/03.1-admin-ui-redesign-v2/03.1-VERIFICATION.md`:
  flip SC#1 / SC#2 / SC#6 to VERIFIED, add a re-verification section noting the
  fixes, update the score to 6/6.
- Update `.planning/STATE.md` to mark Phase 03.1 complete and progress to 100%.
- Append entry to STATE.md "Quick Tasks Completed" table.

## Atomic commit

One commit covering all three fixes + tests + bookkeeping:
`fix(03.1): resolve BLOCKER-GAP-01 (admin lookup) + INFO-GAP-03 (tweaks aria timing)`
