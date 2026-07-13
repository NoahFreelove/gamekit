---
phase: quick-260712-t3r-admin-screenshot-showcase
plan: 01
subsystem: site
tags: [marketing-site, static-assets, admin-ui, showcase]
requires: []
provides:
  - "Admin console screenshot showcase section (#console) on the marketing site"
affects: []
tech-stack:
  added: []
  patterns:
    - "Reused .term/.term-bar/.term-title terminal-frame pattern for screenshot frames"
    - ".term-shot max-width:none override mirrors existing .term-code pattern"
key-files:
  created:
    - site/public/img/admin-players.webp
    - site/public/img/admin-audit.webp
    - site/public/img/admin-queues.webp
    - site/public/img/admin-health.webp
  modified:
    - site/public/index.html
    - site/public/styles.css
key-decisions:
  - "Placed showcase as section 03 immediately after #modules (where the Admin.UI card plants the idea); renumbered compose->04, production->05"
  - "Same-origin img/ paths only - CSP img-src 'self' posture preserved, _headers untouched"
metrics:
  duration: ~3 minutes
  completed: 2026-07-13
status: complete
---

# Quick Task 260712-t3r: Admin Console Screenshot Showcase Summary

**One-liner:** 4 same-origin 1600x620 WebP admin screenshots in a new "03 / Ops console included" section with terminal-framed 2-up grid (1-up under 960px), lazy-loaded with explicit dimensions for zero layout shift.

## What Was Done

- Created `site/public/img/` and copied 4 pre-captured screenshots (admin-players, admin-audit, admin-queues, admin-health; all 1600x620 WebP, ~138 KB total) from the planner's scratchpad - no regeneration.
- Inserted `<section class="section" id="console">` between `#modules` and the compose section, following existing conventions: banner comment, `div.wrap`, `h2` with aria-hidden `span.sec-num` `03`, `p.section-sub`, `&mdash;` entities. Body is `div.shots[role=list]` with 4 `figure.shot[role=listitem]` blocks, each wrapping the image in a `.term.term-shot` frame (`.term-bar` + `.term-title` "admin - {page}") followed by a mono `figcaption`.
- Every img: `src="img/admin-*.webp"`, `width="1600" height="620"`, `loading="lazy"`, descriptive alt text per plan spec.
- Renumbered downstream sec-nums: compose 03->04, production 04->05.
- Added `/* ---------- admin console shots ---------- */` CSS block between the module-grid and compose blocks: `.shots` 2-col grid (gap 1.5rem, margin-top 2.25rem), `.term-shot { max-width: none; }`, responsive `.shot img` (block, width 100%, height auto), mono muted `.shot figcaption` (0.78rem, letter-spacing). Added `.shots { grid-template-columns: minmax(0, 1fr); }` to the existing `@media (max-width: 960px)` block. No new hex colors - only existing custom properties.

## Verification Results

All plan verify gates passed:

- 4 WebP files present in `site/public/img/` - PASS
- `loading="lazy"` count = 4, `width="1600" height="620"` count = 4, `src="img/admin-` count = 4 - PASS
- `src="http` count = 0 (zero external resource requests; CSP `img-src 'self'` posture preserved) - PASS
- `.shots` appears >= 2 times in styles.css (base rule + 960px collapse) - PASS
- `id="console"` present; `>05<` present (renumbering complete) - PASS
- Local static serve (`python3 -m http.server`) returned HTTP 200 for all 4 images - PASS
- `git diff HEAD~1 HEAD --name-only` lists exactly the 6 files in `files_modified` - PASS
- `git diff --diff-filter=D HEAD~1 HEAD` - no file deletions - PASS
- wrangler.toml / package.json / _headers / site.js byte-identical to before (never modified) - PASS
- No deploy performed - PASS

## Deviations from Plan

None - plan executed exactly as written.

Note (expected, documented in plan): the index.html commit carries the pre-existing intentional working-tree deletions of the hero `.eyebrow` paragraph and footer `.badges` block (post-relicense cleanup). These rode along by design.

## Known Stubs

None - all 4 images are real captured assets wired to real files; no placeholders.

## Threat Flags

None - static same-origin assets only; no new endpoints, auth paths, or external requests. T-quick-t3r-01 mitigation held (zero `src="http"` occurrences; `_headers` untouched).

## Commits

| Commit | Message |
| ------ | ------- |
| 8c1cd91 | feat(quick-260712-t3r): add admin console screenshot showcase to site |

## Self-Check: PASSED
