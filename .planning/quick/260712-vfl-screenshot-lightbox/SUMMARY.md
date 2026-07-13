---
phase: quick-260712-vfl-screenshot-lightbox
plan: 01
subsystem: marketing-site
tags: [site, lightbox, progressive-enhancement, csp, accessibility]
requires: []
provides:
  - "Click-to-enlarge lightbox with carousel for the 4 #console admin screenshots"
affects: []
tech-stack:
  added: []
  patterns:
    - "Progressive enhancement: dialog shell hidden by default; all affordances gated on JS-added .lb-enhanced class"
    - "Dialog visibility via hidden attribute + .is-open class over a display:none base (author flex rule can never beat UA [hidden])"
key-files:
  created: []
  modified:
    - site/public/index.html
    - site/public/styles.css
    - site/public/site.js
decisions:
  - "Slides built at runtime from '#console .shot' figures — grid markup stays the single source of truth; the static dialog is only a re-rendered shell"
  - "preventDefault on Enter in the trigger keydown handler — without it the browser's default Enter activation clicks the close button that open() just focused, closing the dialog in the same keystroke"
metrics:
  duration: "~9 min"
  completed: "2026-07-13T02:56:00Z"
  tasks: 1
  files: 3
status: complete
---

# Quick Task 260712-vfl: Screenshot Lightbox Summary

CSP-safe, progressively-enhanced click-to-enlarge lightbox with wrap-around
carousel, full keyboard/focus accessibility, and mobile bottom-anchored nav
for the 4 admin-console screenshots — verified end-to-end with a
wrangler-served (real CSP) headless-browser UAT.

## What Was Built

- **index.html** — static dialog shell (`#shot-lightbox`, `role="dialog"`,
  `aria-modal="true"`, `hidden`) inserted immediately before the single
  `site.js` script element. `.shots` grid markup untouched.
- **styles.css** — appended lightbox section: fixed rgba(10,14,12,0.95)
  backdrop at z-index 60, `.term`-framed panel with `max-width: min(96vw, 88rem)`
  overriding the 40rem cap, mono accent counter, 44px `.lb-btn` hit targets,
  `body.lb-locked` scroll lock, `.shots.lb-enhanced`-gated zoom-in cursor and
  "[+] enlarge" hover hint, and a `<=620px` block that bottom-anchors
  prev/next at 25%/25%.
- **site.js** — second IIFE appended after line 33 (lines 1-33 byte-identical):
  builds 4 slides from the grid figures, enhances each `.term-shot` as a
  `role="button"` trigger (Enter/Space/click), open/close with focus move-in
  and return, document-level Escape/ArrowLeft/ArrowRight with wrap-around,
  minimal Tab containment across the 3 dialog buttons, backdrop-click close.
  Class/attribute toggling only — zero inline-style writes.

## Verification Results

**Static gates (all PASS):**
- G1 role="dialog"=1, aria-modal="true"=1
- G2 inline style attributes in index.html = 0
- G3 exactly 1 script element, it is `<script src="site.js">`
- G4 site.js append-only (0 deleted/edited HEAD lines)
- G5 new site.js code (line 34+) has 0 inline-style writes
- G6 0 protocol-qualified URLs in site.js / styles.css
- G7 `node --check site/public/site.js` clean
- G8 _headers, wrangler.toml, package.json, img/ untouched

**CSP-enforced local serve (wrangler pages dev on :8788 — no python fallback needed):**
page served with `content-security-policy` header present, `shot-lightbox`
in markup, styles.css and site.js both 200.

**Headless-browser UAT (chrome-headless-shell via playwright-core, scratchpad-only):**
```
PASS A1 — 0 console errors, 0 CSP messages across the session
PASS A2 — dialog not visible on load
PASS A3 — click 2nd shot: "2 / 4", admin-audit.webp, caption match, scroll locked, focus in dialog
PASS A4 — ArrowRight 3/4 -> wraps 1/4; ArrowLeft wraps 4/4 (correct images each step)
PASS A5 — Escape closes, scroll unlocked, focus returned to 2nd trigger
PASS A6 — Enter opens at 1/4, Tab x4 stays contained, backdrop click closes
PASS A7 — no-JS: 4 figures, dialog hidden, no zoom-in cursor
PASS A8 — 390x844: img 364px wide, prev/next 44x44 fully in viewport, next -> 2/4
ALL LIGHTBOX CHECKS PASSED
```

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Enter-to-open instantly self-closed the dialog**
- **Found during:** Task 1 UAT (assertion A6 failed: containment=false)
- **Issue:** The trigger's Enter keydown handler opened the dialog and moved
  focus to the close `<button>` during dispatch; the browser's default Enter
  activation (which runs after dispatch, against the now-focused element)
  then fired a click on that close button, closing the dialog in the same
  keystroke. MutationObserver trace confirmed open->close within one event.
- **Fix:** `e.preventDefault()` on the Enter branch of the trigger keydown
  handler (Space already had it).
- **Files modified:** site/public/site.js
- **Commit:** 566f035 (same atomic commit)

No other deviations — plan executed as written.

## Known Stubs

None — all UI is fully wired to the existing grid content.

## Threat Flags

None — no new network surface; T-vfl-01/T-vfl-02 mitigations verified by
G2/G3/G5/G6 plus A1 under the real wrangler-applied CSP.

## Commits

| Commit | Message |
| ------ | ------- |
| 566f035 | feat(quick-260712-vfl): add click-to-enlarge lightbox to admin console screenshots |

## Notes

- wrangler dev server used for the CSP-enforced smoke (no python fallback needed);
  killed after UAT. Ephemeral `site/.wrangler/` state dir removed post-run.
- playwright-core + chrome-headless-shell used from the session scratchpad only;
  nothing installed into site/ or the repo.

## Self-Check: PASSED

- site/public/index.html — FOUND
- site/public/styles.css — FOUND
- site/public/site.js — FOUND
- Commit 566f035 — FOUND in git log
- Static gates G1-G8 — all PASS (re-run after the A6 fix)
- Headless UAT — ALL LIGHTBOX CHECKS PASSED (A1-A8)
