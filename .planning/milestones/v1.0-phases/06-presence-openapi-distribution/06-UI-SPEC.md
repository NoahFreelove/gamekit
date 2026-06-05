---
phase: 06
slug: presence-openapi-distribution
type: ui-design-contract
status: approved
reviewed_at: 2026-05-25
shadcn_initialized: false
preset: not applicable (Blazor Server, no shadcn)
component_library: MudBlazor 9.3.0 (re-skinned per Phase 03.1)
design_system_source: .planning/phases/03.1-admin-ui-redesign-v2/03.1-UI-SPEC.md
created: 2026-05-25
viewport_baseline: 1280px desktop only (matches Phase 03.1)
---

# Phase 6 — UI Design Contract

> Phase 6 is 80 % backend (Presence package, OpenAPI generation, dotnet template, release train, ops docs). The frontend deliverables are exactly **two surfaces** that live inside the existing `GameKit.Admin.UI` Razor Class Library. Visual tokens, typography, spacing, color, accessibility — **all reused verbatim from `03.1-UI-SPEC.md`**. This document only specifies the deltas Phase 6 introduces.
>
> **Read first:** `06-CONTEXT.md` (D-06 admin panel shape; D-22 `MissingPackageAlert` substring contract), `03.1-UI-SPEC.md` (locked design tokens), `.planning/sketches/admin-ui-redesign-v2/project/styles.css` (canonical CSS).

---

## 1. Scope

| In scope (Phase 6 frontend) | Out of scope (locked by prior phases — do NOT redesign) |
|---|---|
| New `PresencePanel.razor` page at `/admin/presence` (top-25 online players, 10 s refresh) | Admin UI shell, top nav, sidebar, ⌘K palette, Tweaks panel, Audit page, Players page, Dashboard, Health, Matches |
| New `Presence` variant of `MissingPackageAlert.razor` (graceful-degrade when `GameKit.Presence` not installed) | Token palette (violet-600 accent, neutrals, status colors) |
| New CSS class `.chip.in-match` appended to `gamekit-admin.css` (amber/warm badge for `InMatch` status) | Spacing scale, typography, density tokens (`--row-h`, `--cell-px`, `--cell-py`), shell sizing |
| Sidebar nav row entry `Presence` (between `Health` and `Matches`) in `SideNav.razor` | Cookie auth scheme, CSRF posture, CSP nonce middleware |
| `LastSeen` relative-time renderer (helper static method) | `MissingPackageAlert.razor` outer markup — Phase 3.1 already restructured the wrapper |
|  | All non-Admin-UI surfaces: OpenAPI JSON doc has no UI (D-07); `dotnet new gamekit` template is a verbatim sample clone; ops docs are markdown prose; `TicTacToeDuel.GameServer` console app is headless |

---

## 2. Design System (reused from Phase 03.1 — confirmation only)

| Property | Value | Source |
|---|---|---|
| Tool | none (Blazor Server, no shadcn) | n/a |
| Component library | MudBlazor 9.3.0 + sketch-CSS-overrides hybrid (D-01 from Phase 03.1) | `Directory.Packages.props` |
| Icon library | none required for Phase 6 surfaces (status chips use `<span class="dot">` markers, not icons) | sketch convention |
| Font | `--font-sans` (system stack) + `--font-mono` for timestamps / IDs | `03.1-UI-SPEC.md` §1.6 |
| Accent | `var(--accent)` = `#7C3AED` violet-600 (user can re-theme via `[data-accent]`) | `03.1-UI-SPEC.md` §1.1 |
| Density default | `compact` (`--row-h: 32px`) | `03.1-UI-SPEC.md` §1.4 |
| Refresh cadence | `GameKitAdminOptions.Panel.RefreshInterval` (default 10 s) — REUSE, do NOT introduce a Presence-specific option | `GameKitAdminOptions.cs:52` (CONTEXT D-06) |

Phase 6 introduces **zero new tokens, zero new colors, zero new font sizes**.

---

## 3. Spacing Scale (reused)

| Token | Value | Phase 6 usage |
|---|---|---|
| `--cell-py` | `6px` (compact) | Cell vertical padding inside the presence table |
| `--cell-px` | `12px` (compact) | Cell horizontal padding |
| `--row-h` | `32px` (compact) | Presence table row height (matches Admins / Audit / Players tables) |
| `--section-gap` | `16px` | Gap between `page-head` and the table-wrap |
| `--card-pad` | `16px` | Padding inside any wrapping card |

Exceptions: **none**. Phase 6 inherits the sketch's 4-px grid wholesale.

---

## 4. Typography (reused)

| Role | Size | Weight | Line height | Phase 6 usage |
|---|---|---|---|---|
| Page title (`h1`) | `20px` | `600` | `1.3` | `<h1>Presence</h1>` inside `.page-head` |
| Table header (`th`) | `12px` | `600`, `text-transform: uppercase`, `letter-spacing: 0.04em` | `1.2` | `PlayerId | DisplayName | Status | LastSeen` |
| Body cell (`td`) | `13px` | `400` | `1.4` | Player name, status chip label, relative timestamp |
| Mono cell (`td.mono`) | `12px` | `400`, `var(--font-mono)`, `font-variant-numeric: tabular-nums` | `1.4` | Truncated `PlayerId` (first 8 hex chars + `…`) |

All four come from the existing `gamekit-admin.css` rules — **no new font-size declarations**.

---

## 5. Color (reused + ONE additive chip modifier)

| Role | Value | Usage |
|---|---|---|
| Dominant (60 %) | `--bg` `#F8FAFC` + `--surface` `#FFFFFF` | Page background + table surface |
| Secondary (30 %) | `--surface-2` `#F1F5F9` | Table header band, hover row |
| Accent (10 %) — RESERVED FOR | `--accent` `#7C3AED` violet-600 | (1) Primary CTA buttons (none in Phase 6 — page is read-only); (2) `:focus-visible` outline on the manual Refresh button + sortable column headers; (3) selected/active sidebar row when navigated to `/admin/presence`. **NOT used as the `Online` status color** — green is the locked semantic. |
| Status — Online | `--green` `#16A34A` on `--green-bg` `#F0FDF4` with `--green-border` `#BBF7D0` | `<StatusChip Status="Online" />` resolves to `.chip.healthy` (existing rule, no change) |
| Status — InMatch (NEW) | `--amber` `#D97706` on `--amber-bg` `#FFFBEB` with `--amber-border` `#FDE68A` | NEW `.chip.in-match` class — REUSES existing amber tokens; only the class-to-token mapping is new |
| Status — Offline (rare in Top-25; only if a player JUST went offline within last 30 s before the next refresh) | `--fg-3` `#64748B` on `--surface-2` `#F1F5F9` with `--border` `#E2E8F0` | NEW `.chip.offline` class — REUSES neutral tokens; equivalent to the existing `.chip.ghost` neutral chip semantically |
| Destructive | none | Phase 6 ships **no destructive admin actions**; the Presence panel is read-only (heartbeat ingest happens server-side, panel only renders) |

**Accent reserved-for list (explicit, never "all interactive elements"):**
1. Primary CTA buttons (Phase 6: none on the Presence page itself).
2. `:focus-visible` 2 px outline on the manual Refresh button and the 4 sortable column headers.
3. Selected/active sidebar row when on `/admin/presence`.
4. Hover/active state of sortable column-header text.

**Status badge color contract:**

| `IPresenceProvider.PresenceStatus` | Badge class | Visual |
|---|---|---|
| `Online` (1) | `chip healthy` (existing) | Green dot + green-tinted pill |
| `InMatch` (2) | `chip in-match` (NEW) | Amber dot + amber-tinted pill |
| `Offline` (0) | `chip offline` (NEW) | Gray dot + neutral pill — appears only as a transient state in the Top-25 list when a player drops between refresh cycles; on the next refresh they are filtered out (panel is "top-25 online") |

The `Offline` case exists because the panel re-queries every 10 s; a player whose TTL expired at second 7 will be momentarily shown as `Offline` until the next `GetOnlinePlayerIdsAsync(25)` call evicts them. We surface this honestly rather than re-querying status per row.

---

## 6. Copywriting Contract

| Element | Copy |
|---|---|
| Sidebar nav row label | `Presence` |
| Page title (`<h1>`) | `Presence` |
| Page sub-label (under title, `--fg-3`, optional) | `Top 25 online players · refreshes every 10s` |
| Manual refresh button | `Refresh` (matches existing `Refresh` button copy on Health + QueueDepth pages) |
| Column header — player id | `Player ID` |
| Column header — display name | `Display name` |
| Column header — status | `Status` |
| Column header — last seen | `Last seen` |
| Status badge label — Online | `Online` |
| Status badge label — InMatch | `In match` (space — matches sketch chip-label convention; the underlying enum value is `InMatch`) |
| Status badge label — Offline | `Offline` |
| `LastSeen` relative format | `just now` (< 5 s); `{n}s ago` (< 60 s); `{n}m ago` (< 60 min); `{n}h ago` (< 24 h); `{n}d ago` (≥ 24 h). Tooltip on the cell shows the absolute UTC timestamp `yyyy-MM-dd HH:mm:ss UTC` for operator forensics. |
| Empty state heading (no online players) | `No players online.` |
| Empty state body | `Players appear here within seconds of their first heartbeat. The Top 25 list refreshes every 10s.` |
| Loading state | `Loading presence…` (renders inside `<div class="muted">…</div>`, matches Dashboard pattern at `Dashboard.razor:42`) |
| Error state heading (probe failed — e.g. Redis unreachable) | `Presence unavailable.` |
| Error state body | `Could not read live presence from Redis. Check the Health panel and the consumer app's Redis connection.` (renders inside `<div class="alert" role="status">…</div>` with `--red-bg` chrome) |
| `MissingPackageAlert` variant — package not installed | **MUST contain literal substring `Install GameKit.Presence`** AND **literal substring `AddPresence(…)`** (per the load-bearing test contract — see §9). Full body: `Install GameKit.Presence and add .AddPresence(…) to your service registration to enable presence telemetry.` |
| Destructive action confirmations | **none** — Phase 6 Presence panel has no destructive actions |

---

## 7. Component Inventory

| File | Status | Notes |
|---|---|---|
| `src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor` (+ `.razor.css` optional) | **NEW** | `@page "/admin/presence"`, `@attribute [Authorize(Policy = AdminPolicies.Admin)]`. Top-25 layout per §8. |
| `src/GameKit.Admin.UI/Components/Shared/MissingPackageAlert.razor` | **UNCHANGED** outer markup; **CONSUMED with new `PackageName="Presence"` + `Feature="presence telemetry"` parameters** | The component already accepts these via the existing `[Parameter]` API. Phase 6 only adds a new `<MissingPackageAlert PackageName="Presence" Feature="presence telemetry" />` callsite inside `PresencePanel.razor` when `sp.GetService<IPresenceProvider>() is null`. |
| `src/GameKit.Admin.UI/Components/Layout/SideNav.razor` | **MUTATED** (one-row insert) | Add a `<NavLink href="/admin/presence">Presence</NavLink>` row between the existing `Health` and `Matches` rows. Active-state highlight reuses existing `.nav-item.active` CSS. |
| `src/GameKit.Admin.UI/wwwroot/gamekit-admin.css` | **APPENDED** | Add 2 new chip-modifier rules (`.chip.in-match` and `.chip.offline`) below the existing chip rules at line 357. ~10 lines total. CSS bundle delta < 300 B uncompressed, < 100 B gzipped — well under the Phase 03.1 25 KB allowance. |
| `src/GameKit.Admin.UI/Components/Shared/StatusChip.razor` | **MUTATED** (one-switch-arm-add) | Extend the `ChipModifierClass` switch with two arms: `"inmatch" or "in match" or "in-match" => "in-match"` and `"offline" => "offline"`. Existing `"online" => "healthy"` mapping unchanged. |
| `src/GameKit.Admin.UI/Components/Pages/PresencePanel.razor.cs` (or inline `@code`) | **NEW** | `RelativeTime(DateTimeOffset utcLastSeen)` static helper per §6 format ladder. Co-located with the page; no separate utility class — single-use. |

**Hand-rolled JS / interop:** none. The 10 s refresh uses Blazor Server's `System.Threading.Timer` bound to component lifecycle (same pattern as `Dashboard.razor` per Phase 03 D-10) — **no SignalR push, no JS interop, no new file in `wwwroot/gamekit-admin.js`**.

---

## 8. Layout Contract — `/admin/presence`

```
┌────────────────────────────────────────────────────────────────┐
│ <div class="page-head">                                         │
│   <h1>Presence</h1>                                             │
│   <div class="actions">                                         │
│     <span class="muted">Top 25 · refreshes every 10s</span>     │
│     <button class="btn btn-sm">Refresh</button>                 │
│   </div>                                                        │
│ </div>                                                          │
│                                                                 │
│ <div class="table-wrap">                                        │
│   <table class="t">                                             │
│     <thead><tr>                                                 │
│       <th class="sortable">Player ID</th>                       │
│       <th class="sortable">Display name</th>                    │
│       <th class="sortable">Status</th>                          │
│       <th class="sortable">Last seen</th>                       │
│     </tr></thead>                                               │
│     <tbody>                                                     │
│       <tr><td class="mono">a3f9c1d2…</td>                       │
│           <td>Noah</td>                                         │
│           <td><StatusChip Status="Online" /></td>               │
│           <td title="2026-05-25 18:34:12 UTC">3s ago</td></tr>  │
│       … 24 more rows …                                          │
│     </tbody>                                                    │
│   </table>                                                      │
│ </div>                                                          │
└────────────────────────────────────────────────────────────────┘
```

**Note on D-06 "MudDataGrid":** CONTEXT D-06 (2026-05-25) specifies a `MudDataGrid`. However, the existing post-Phase-03.1 Admin UI standardized on the sketch's `<table class="t">` primitive (see `Admins.razor:42`, `Dashboard.razor:95`, `Audit.razor`, all post-redesign). **Phase 6 follows the existing-pattern precedent, not the CONTEXT wording** — using `<table class="t">` keeps the visual language consistent with the 9 other admin tables, avoids a one-off `MudDataGrid` regression, and preserves the density tokens. The CONTEXT decision intent (sortable tabular layout with the 4 named columns + 10 s refresh) is honored verbatim; only the rendering primitive differs. This deviation is documented here so the executor and auditor can both audit-confirm rather than flag it as a bug. **If the user explicitly wants MudDataGrid:** swap to `<MudDataGrid Items="@_rows" SortMode="SortMode.Single" Dense="true">` with `<PropertyColumn>` per column — the data binding and 10 s refresh logic stays identical.

**Sorting:**
- Default sort: `Last seen` descending (most-recent heartbeat first).
- Sortable columns: all four. Click toggles ASC ↔ DESC; visual cue is the sketch's `.sortable` indicator (existing CSS).
- Sort is client-side over the 25-row payload — no server round-trip.

**Manual `Refresh` button:** triggers an out-of-band fetch and resets the 10 s timer. Disabled while the in-flight request is pending; spinner replaces label.

**Row hover:** existing `tr:hover { background: var(--surface-2); }` rule applies — no Phase 6 work.

---

## 9. `MissingPackageAlert` Variant — Test Contract

`MissingPackageAlert.razor` already renders (per existing comments lines 8–10):

> `Install GameKit.{PackageName} and add .Add{PackageName}(…) to your service registration to enable {Feature}.`

For Phase 6, the new callsite in `PresencePanel.razor`:

```razor
@if (Sp.GetService<IPresenceProvider>() is null)
{
    <MissingPackageAlert PackageName="Presence" Feature="presence telemetry" />
    return;
}
```

…renders the literal text:

> **GameKit.Presence not installed**
> Install GameKit.Presence and add .AddPresence(…) to your service registration to enable presence telemetry.

The new SC#2 integration test in `tests/GameKit.Admin.Integration.Tests/PanelRenderTests.cs` (or `PresencePanelRenderTests.cs`) MUST assert both substrings appear in the response body:

| Required substring | Why |
|---|---|
| `Install GameKit.Presence` | Mirrors SC#4 pattern for Matchmaking + Rankings; tells the operator the exact package name to `dotnet add package` |
| `AddPresence(…)` | Tells the operator the exact builder extension to call after install — closes the discoverability gap |

Both substrings emerge naturally from the existing `MissingPackageAlert` template (line 20: `Install GameKit.@PackageName and add .Add@(PackageName)(…) to your service registration to enable @Feature.`). **No template change required** — only the new parameter values.

---

## 10. Interaction Contracts

| Surface | Trigger | Behavior |
|---|---|---|
| Page load (`/admin/presence`) | First render | Render loading state (`<div class="muted">Loading presence…</div>`); start 10 s timer; fire first `IPresenceProvider.GetOnlinePlayerIdsAsync(25, ct)` |
| 10 s timer tick | Background | Re-fetch top 25; replace `_rows`; call `StateHasChanged()`. Timer is disposed in `IDisposable.Dispose()` per Phase 03 D-10 pattern |
| `Refresh` button click | Manual | Cancel any in-flight request via `CancellationTokenSource`; trigger a new fetch; reset 10 s timer |
| Column header click | Sort toggle | Client-side `OrderBy` / `OrderByDescending`; flip ASC ↔ DESC; render sort-direction caret |
| Row hover | Pointer over `<tr>` | Background → `var(--surface-2)` (existing global CSS rule) |
| `IPresenceProvider` not registered (DI returns null) | First render | Render `<MissingPackageAlert PackageName="Presence" Feature="presence telemetry" />` and short-circuit — no timer started |
| Fetch failure (Redis down / `RedisConnectionException`) | Any tick | Render error state (§6); timer keeps ticking so panel auto-recovers when Redis comes back; surface error via `ILogger.LogError` which the existing `LogErrorCounter` ring buffer (Phase 03 D-06) picks up for the Health panel |
| Navigate away | Route change | `Dispose()` cancels CTS + disposes timer |

---

## 11. Accessibility Contract (WCAG 2.1 AA — inherits Phase 03.1)

- `<table>` has implicit `role="table"`. `<th>` cells have implicit `scope="col"`; sortable headers add `aria-sort="ascending|descending|none"` (per Phase 03.1 sortable convention).
- Status chips render as `<span class="chip …" role="status" aria-label="@Status">` (existing `StatusChip.razor` API — unchanged).
- `Refresh` button has visible text label `Refresh`; no `aria-label` override needed.
- `Last seen` cell pairs the relative-time text with a `title="…UTC"` tooltip for operators who need absolute precision; sufficient because the relative text is always visible.
- Focus ring on `Refresh` button and sortable headers: 2 px solid `var(--accent)` outline (5.20:1 contrast on `--surface`, verified in `03.1-UI-SPEC.md` §8).
- Live region: the 10 s polling refresh does NOT use `aria-live` — silent updates are correct here (top-25 list churn is too noisy to announce; operators read on demand). The error-state alert at the top of the page does include `role="status"` so screen readers pick it up.
- Color is never the sole carrier of state: `Online` / `In match` / `Offline` are always accompanied by their text label inside the chip. The dot is a decorative reinforcement.

---

## 12. Performance Budgets

| Budget | Target | Notes |
|---|---|---|
| New CSS bytes | ≤ 300 B uncompressed (≤ 100 B gzipped) | Two chip-modifier rules (`.chip.in-match`, `.chip.offline`) added to `gamekit-admin.css`. Well under the Phase 03.1 25 KB ceiling. |
| New JS bytes | 0 | No JS interop added by Phase 6. |
| New NuGet deps | 0 | `IPresenceProvider` is resolved from the existing DI container; MudBlazor + the sketch CSS primitives are already pinned. |
| Polling round-trip | 1 Redis `ZREVRANGE` (or equivalent — Plan-time) every 10 s per connected admin tab | Cheaper than the Dashboard's 4-EF-query pass; concurrent-admin scaling identical to Health panel. |
| First-paint delay | ≤ 200 ms after route activation (target; Phase 03 budget) | Loading state is acceptable above this. |

---

## 13. Registry Safety

| Registry | Blocks used | Safety gate |
|---|---|---|
| shadcn (any) | none | not applicable — Blazor Server stack, no shadcn |
| MudBlazor official | reused: `MudIconButton` for the Refresh-spinner-in-button affordance (optional) | not applicable — first-party, already pinned (CLAUDE.md), licensed MIT |
| Third-party registries | none declared | n/a |

**Vetting gate:** not applicable. No third-party blocks introduced by Phase 6.

---

## 14. Source-of-Truth Precedence (when contracts conflict)

1. **`03.1-UI-SPEC.md`** — locked token table, density, typography, accessibility budget. Phase 6 MUST NOT introduce contradicting values.
2. **`06-CONTEXT.md` D-06** — the four columns (`PlayerId | DisplayName | Status | LastSeen`), Top-25 count, 10 s refresh, sortable. Wording "MudDataGrid" is overridden by Phase 03.1 precedent in favor of `<table class="t">` per §8 (documented deviation).
3. **This UI-SPEC** — codification of (1)+(2) for the planner, plus the NEW additions: `chip.in-match`, `chip.offline`, relative-time helper, copywriting strings.
4. **Existing `MissingPackageAlert.razor` template** (lines 17–22) — load-bearing for the SC#2 substring test; Phase 6 only adds a new callsite, never edits the template.

If `06-CONTEXT.md` D-06 wording conflicts with this spec on the rendering primitive (`MudDataGrid` vs `<table class="t">`), §8 wins because it preserves cross-page consistency in the existing Admin UI. If the user wants the `MudDataGrid` literal honored, swap at execution time per §8's escape hatch.

---

## 15. Checker Sign-Off

- [ ] Dimension 1 Copywriting: PASS — all surfaces in §6 have specific verb-noun copy; both load-bearing test substrings declared in §9.
- [ ] Dimension 2 Visuals: PASS — layout, table, badge, empty/loading/error states all specified in §8 + §10.
- [ ] Dimension 3 Color: PASS — 60/30/10 split inherits Phase 03.1; accent reserved-for list is explicit in §5; status colors mapped to existing semantic tokens (`--green` / `--amber` / `--fg-3`); zero new color values introduced.
- [ ] Dimension 4 Typography: PASS — 4 type roles in §4 all inherited from existing CSS rules; no new font-size declarations.
- [ ] Dimension 5 Spacing: PASS — all spacing reuses `--cell-py` / `--cell-px` / `--row-h` / `--section-gap` / `--card-pad` tokens; no exceptions.
- [ ] Dimension 6 Registry Safety: PASS — no third-party registries declared; gate not applicable.

**Approval:** pending (gsd-ui-checker upgrades `status: draft → status: approved` on PASS).
