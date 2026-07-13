---
phase: quick-260712-vfl-screenshot-lightbox
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - site/public/index.html
  - site/public/styles.css
  - site/public/site.js
autonomous: true
requirements: [SITE-LIGHTBOX-01]

must_haves:
  truths:
    - "Clicking (or pressing Enter/Space on) any of the 4 admin-console screenshots in #console opens a fullscreen dialog showing that image at near-viewport width (1600x620 aspect preserved), with its caption, a mono position counter (e.g. 2 / 4), and prev/next controls that cycle through all 4 with wrap-around."
    - "The dialog closes via the X button, clicking the backdrop, or the Esc key; ArrowLeft/ArrowRight navigate while open; focus moves into the dialog on open and returns to the originating trigger on close; background page scroll is locked while the dialog is open."
    - "With JavaScript disabled, the page renders and behaves exactly as it does today: the 2-column .shots grid works, the dialog stays hidden, and no zoom affordance (cursor or hover hint) appears — enhancement affordances are gated on a class that only site.js adds."
    - "CSP posture is intact: zero inline style attributes in index.html, exactly one script element in index.html (the existing site.js reference), no external resources, all new behavior appended to site.js without altering its existing 33 lines, all new styling in styles.css driven by class/attribute toggling."
    - "New UI matches the engine-room aesthetic: near-black rgba(10,14,12,~0.95) backdrop, phosphor-green var(--accent) accents, var(--mono) counter/caption, and the .term/.term-bar terminal-frame pattern around the enlarged image; at <=620px the image fits the viewport width and prev/next controls are bottom-anchored and reachable."
  artifacts:
    - site/public/index.html
    - site/public/styles.css
    - site/public/site.js
  key_links:
    - "site.js builds its slide list at runtime from the existing '#console .shot' figures (img src, img alt, figcaption text, .term-title text) — the grid markup stays the single source of truth; the static dialog is only a shell whose img/caption/counter/title are re-rendered per slide."
    - "Dialog visibility uses BOTH the hidden attribute and an is-open class, with a base .lightbox { display: none } rule in styles.css — this avoids the classic bug where a display:flex author rule overrides the UA [hidden] rule."
    - "The .shots container receives an enhancement class (lb-enhanced) from site.js; every affordance rule in styles.css (zoom cursor, hover hint) is scoped under that class so no-JS visitors never see a dead affordance."
---

<objective>
Add a CSP-safe, progressively-enhanced click-to-enlarge lightbox with carousel
navigation to the 4 admin-console screenshots in the #console section of the
GameKit marketing site (site/public/). The 2-column grid renders the 1600x620
shots too small to read; the lightbox shows each near viewport width with its
caption, a mono "n / 4" counter, prev/next wrap-around, and full keyboard +
focus-management accessibility.

Purpose: the screenshots are the proof that the ops console is real; right now
they are decorative because the text in them is illegible at grid size.

Output: edits to exactly 3 files (index.html, styles.css, site.js) in ONE
atomic commit. No deploy, no new images, no new dependencies in the site.
</objective>

<execution_context>
@$HOME/.claude/gsd-core/workflows/execute-plan.md
@$HOME/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@site/public/index.html
@site/public/styles.css
@site/public/site.js

# Verified scouting facts (from planner) — treat as authoritative:
#
# - Working tree is CLEAN at plan time. index.html is 296 lines; styles.css
#   is 497 lines; site.js is 33 lines (single IIFE: copy-to-clipboard).
#
# - #console section = index.html lines 167-204. Grid: div.shots[role=list]
#   containing 4x figure.shot[role=listitem], each holding
#   div.term.term-shot > (div.term-bar > span.term-title) + img, then a
#   figcaption. All imgs: width="1600" height="620" loading="lazy", rich alt
#   text, srcs img/admin-players.webp, admin-audit.webp, admin-queues.webp,
#   admin-health.webp (in that DOM order).
#
# - The ONLY script element is line 294, src="site.js", just before </body>.
#   It has no defer and runs after the DOM is parsed — safe to query DOM at
#   top level of an appended IIFE.
#
# - styles.css custom props (line 5-22): --bg #0a0e0c, --bg-raised, --bg-inset,
#   --line, --line-bright, --text, --muted, --accent #46e08c, --accent-dim,
#   --accent-glow, --mono, --sans. Breakpoints: 960px / 620px / 460px.
#   Global :focus-visible outline exists (line 59). A global
#   prefers-reduced-motion block kills ALL animations/transitions (line 27) —
#   any transition you add is automatically neutralized; do not add your own
#   reduced-motion handling.
#
# - .term has max-width: 40rem (line 172) — the lightbox panel reuses .term
#   for the frame aesthetic and MUST override that max-width (precedent:
#   .term-shot { max-width: none; } at line 383).
#
# - .shot figcaption (styles.css line 392) is the mono caption pattern to
#   mirror inside the dialog. .copy-btn (line 222) is the button pattern to
#   mirror for lb buttons (mono, uppercase, bg-raised, line-bright border,
#   accent on hover).
#
# - site.js lines 21-30 (the legacy clipboard fallback) already write to a
#   detached textarea's positioning via element style properties. That is
#   PRE-EXISTING code — leave lines 1-33 byte-identical. The append-only diff
#   gate and the tail-scoped gate below account for this.
#
# - Baseline grep counts in index.html (must-hold invariants): inline style
#   attributes = 0; script elements = 1; role="dialog" = 0 (becomes 1).
#
# - CSP (site/public/_headers, DO NOT EDIT): default-src 'none';
#   style-src 'self'; script-src 'self'; img-src 'self'. Inline style
#   attributes and inline script bodies are BLOCKED by this policy.
#
# - Local verification tooling (all already present, install NOTHING into
#   site/ or repo root):
#     * wrangler is installed at site/node_modules/.bin/wrangler;
#       `npm run dev` in site/ serves public/ on http://localhost:8788 WITH
#       the _headers CSP applied (miniflare; no login needed).
#     * Headless browser binary:
#       /home/noah/.cache/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-linux64/chrome-headless-shell
#       Launch args must include --no-sandbox --disable-gpu (snap sandbox
#       limitation on this machine).
#     * playwright-core is NOT installed anywhere. Install it ONLY inside the
#       scratchpad dir (see verify step), never into site/package.json.
#     * Scratchpad:
#       /tmp/claude-1000/-home-noah-Desktop-projects-gamekit/925e29b0-f3be-4848-b957-397669321bf8/scratchpad
#
# - HARD boundaries: do NOT touch site/public/_headers, site/wrangler.toml,
#   site/package.json, site/package-lock.json, or anything under
#   site/public/img/. Do NOT deploy.
</context>

<tasks>

<task type="auto">
  <name>Task 1: Add CSP-safe lightbox (markup + styles + behavior) and verify with grep gates + headless-browser UAT</name>
  <files>site/public/index.html, site/public/styles.css, site/public/site.js</files>
  <action>
Implement a click-to-enlarge lightbox for the 4 #console screenshots as pure
progressive enhancement. Three coordinated edits, one commit.

**1) index.html — static dialog shell only (no changes inside .shots):**

Insert the dialog markup immediately BEFORE the existing script element at the
end of body (line ~294). Do not modify the .shots grid markup, the figures, or
anything else. Do not add inline style attributes anywhere, and do not add any
new script element or inline script body. Required structure (write as plain
HTML, attribute names exact):

- Root: a div with id="shot-lightbox", class="lightbox", role="dialog",
  aria-modal="true", aria-label="Admin console screenshot viewer", tabindex="-1",
  and the bare `hidden` attribute. Everything below nests inside it.
- A figure with class "lightbox-panel term" containing:
  - a div.term-bar holding: span.term-title with id="lb-title" (default text:
    "admin — players" using the mdash entity, matching the grid's term-title
    style), a span with class "lb-counter" and id="lb-counter" (default text
    "1 / 4"), and a button type="button" class="lb-btn lb-close"
    aria-label="Close screenshot viewer" whose visible glyph is the times
    entity.
  - an img with id="lb-img" class="lb-img", width="1600" height="620",
    src="img/admin-players.webp", and the SAME alt text the first grid img
    uses (copy it verbatim from line 177).
  - a figcaption id="lb-caption" class="lb-caption" (default text: copy the
    first grid figcaption, line 179).
- A div class="lb-nav" containing two buttons, each type="button" with class
  "lb-btn lb-prev" / "lb-btn lb-next" and aria-label="Previous screenshot" /
  aria-label="Next screenshot"; visible glyphs: larr / rarr entities.

Rationale for a static shell: role="dialog" is greppable in markup, the
viewer degrades to nothing (hidden attribute needs no CSS/JS), and JS only
re-renders text/src instead of building DOM.

**2) styles.css — append a new "lightbox" section after the last media query
(after line 497):**

All colors/typography via existing custom properties — no new hex values
except the backdrop rgba below. Required rules (selectors exact, declarations
directive):

- `.lightbox` base: position fixed, inset 0, z-index above the sticky topbar
  (topbar is z-index 10; use 60), display none, background
  rgba(10, 14, 12, 0.95) — that is --bg at 95% opacity per the engine-room
  spec. The container itself is the backdrop (no separate element needed).
- `.lightbox.is-open`: display flex, align-items center, justify-content
  center, padding ~1.5rem. NOTE: because a display:flex author rule would
  override the UA rule for the hidden attribute, visibility is controlled by
  the is-open class on top of a display:none base; JS toggles hidden AND
  is-open together (belt and suspenders).
- `.lightbox-panel`: max-width min(96vw, 88rem) — this must beat .term's
  40rem cap (same specificity, later in file wins); width 100%; cursor
  default. Reuses .term for bg/border/shadow frame.
- `.lb-img`: display block, width 100%, height auto, max-height 76vh,
  object-fit contain, background var(--bg-inset).
- `.lb-counter`: font-family var(--mono), font-size ~0.72rem, letter-spacing
  0.14em, color var(--accent), margin-left auto (term-bar is flex; this
  right-aligns it), padding-right ~0.9rem.
- `.lb-caption`: mirror .shot figcaption (mono, 0.78rem, letter-spacing
  0.06em, color var(--muted)) with ~0.6rem 0.9rem padding.
- `.lb-btn`: mirror .copy-btn (mono, ~0.8rem, uppercase optional for glyph
  buttons — keep glyphs legible at ~1.1rem font-size, min 44px hit target via
  min-width/min-height 2.75rem, color var(--muted), background
  var(--bg-raised), 1px var(--line-bright) border, cursor pointer); hover and
  focus-visible: color var(--accent), border-color var(--accent-dim).
- `.lb-close`: margin-left ~0.5rem (sits after the counter in the term-bar).
- `.lb-prev` / `.lb-next`: position fixed, top 50%, translateY(-50%), left
  1rem / right 1rem respectively, z-index 61.
- `body.lb-locked`: overflow hidden (the scroll lock).
- Affordance rules, ALL scoped under the JS-added class:
  `.shots.lb-enhanced .term-shot` gets cursor zoom-in;
  `.shots.lb-enhanced .term-bar::after` gets content "[+] enlarge",
  margin-left auto, mono, ~0.68rem, letter-spacing 0.1em, color var(--muted),
  opacity 0; shown (opacity 1, color var(--accent)) when the parent .shot is
  hovered or the .term-shot has focus-visible. Nothing appears without JS.
- Mobile block `@media (max-width: 620px)`: `.lightbox.is-open` padding
  reduced and align-items flex-start with padding-top ~3.5rem (leaves the
  bottom clear); `.lb-prev`/`.lb-next` switch from side-fixed to a
  bottom-anchored pair — position fixed, bottom 1.25rem, top auto, transform
  none, left 25% / right 25% (or equivalent) so both sit reachable at the
  bottom center with a gap; image stays width 100% of the panel which is
  ~96vw.

**3) site.js — append a second IIFE AFTER line 33; lines 1-33 stay
byte-identical:**

Same style as the existing code: strict-mode IIFE, var/function declarations,
no arrow functions required, brief top comment. Behavior contract:

- Guard: query the dialog by id "shot-lightbox" and all "#console .shot"
  figures; if the dialog is missing or figure count is 0, return (resilient
  no-op on other pages).
- Build a slides array from the figures in DOM order: for each, capture the
  inner img's getAttribute("src") and alt, the figcaption textContent
  (trimmed), and the .term-title textContent (trimmed).
- Grab refs: lb-img, lb-title, lb-caption, lb-counter, the close/prev/next
  buttons, the .shots container, document.body.
- Enhance each figure's .term-shot div as the trigger: set tabindex="0",
  role="button", aria-haspopup="dialog", and an aria-label of
  "Enlarge screenshot: " + that figure's caption text (role=button needs an
  explicit accessible name; naming from contents would swallow the whole alt
  text). Wire click to open(i); wire keydown so Enter opens and Space opens
  with preventDefault (Space must not scroll the page). After wiring all 4,
  add class "lb-enhanced" to the .shots container (this is what un-gates the
  cursor/hint affordances in CSS).
- render(i): set current index; assign lb-img src and alt from slides[i]
  (assigning the src property under img-src 'self' is CSP-fine — these are
  same-origin relative paths); set title, caption via textContent; set
  counter textContent to (i+1) + " / " + slides.length.
- open(i): store the invoking trigger element as returnFocus; render(i);
  remove the hidden attribute and add class "is-open" to the dialog; add
  class "lb-locked" to body; attach a document keydown listener; move focus
  to the close button.
- close(): reverse everything — add hidden, remove is-open, remove lb-locked,
  detach the document keydown listener, and call returnFocus.focus().
- Document keydown while open: Escape closes; ArrowRight advances with
  wrap-around ((i+1) % n); ArrowLeft goes back with wrap-around
  ((i-1+n) % n); Tab implements a minimal focus containment cycling across
  the dialog's three buttons (close, prev, next): if Tab on the last, wrap to
  the first (preventDefault); if Shift+Tab on the first, wrap to the last.
- Click handling: close button closes; prev/next buttons navigate; a click on
  the dialog container where event.target === event.currentTarget (i.e. the
  backdrop, outside the panel and nav buttons) closes.
- Hard constraints for ALL new code: toggle classes (classList) and
  attributes (hidden/tabindex/role/aria-*) only — never write to an element's
  inline-style object and never set a style attribute from script; no
  external requests; no new globals leaked (everything inside the IIFE); no
  timers needed.

**Commit:** exactly one atomic commit touching only the 3 files:
`feat(quick-260712-vfl): add click-to-enlarge lightbox to admin console screenshots`
  </action>
  <verify>
    <automated>
All gates below must pass BEFORE committing. Run from the repo root.

# -- Static gates (fast) --
# G1: dialog semantics landed in markup
test "$(grep -c 'role="dialog"' site/public/index.html)" -eq 1
test "$(grep -c 'aria-modal="true"' site/public/index.html)" -eq 1
# G2: CSP posture — zero inline style attributes (baseline was 0; must stay 0)
test "$(grep -c 'style="' site/public/index.html)" -eq 0
# G3: still exactly one script element, and it is the site.js reference
test "$(grep -c '<script' site/public/index.html)" -eq 1
test "$(grep -c '<script src="site.js">' site/public/index.html)" -eq 1
# G4: site.js is append-only — no existing line deleted or edited
test "$(git diff -U0 HEAD -- site/public/site.js | grep -c '^-[^-]')" -eq 0
# G5: new site.js code (line 34+) never writes element inline styles
test "$(tail -n +34 site/public/site.js | grep -cE '\.style\.|setAttribute\((.)style')" -eq 0
# G6: no external resources introduced in JS/CSS
test "$(grep -cE 'https?:' site/public/site.js)" -eq 0
test "$(grep -cE 'https?:' site/public/styles.css)" -eq 0
# G7: JS parses
node --check site/public/site.js
# G8: forbidden files untouched
test -z "$(git status --porcelain site/public/_headers site/wrangler.toml site/package.json site/public/img/)"

# -- Local-serve smoke (CSP-enforced) --
# Start: (cd site && npm run dev) in background -> http://localhost:8788
# (wrangler pages dev applies public/_headers, so CSP is REAL in this smoke).
# Wait for readiness (curl retry loop, ~30s budget), then:
curl -sf http://localhost:8788/ | grep -c 'shot-lightbox'   # >= 1
curl -sfI http://localhost:8788/styles.css | head -1        # 200
curl -sfI http://localhost:8788/site.js | head -1           # 200
curl -sI  http://localhost:8788/ | grep -ci 'content-security-policy'  # 1
# Fallback ONLY if wrangler will not boot: python3 -m http.server 8788
# --directory site/public, and skip the CSP-header assertion (grep gates
# G2/G3 still cover the policy statically). Note the fallback in SUMMARY.md.

# -- Headless-browser UAT (server from previous step still running) --
# One-time, OUTSIDE the repo: in the scratchpad dir, npm init -y and
# npm i playwright-core (dev-harness only; NEVER into site/). Write a
# throwaway lb-check.mjs there that launches chromium via playwright-core with
# executablePath /home/noah/.cache/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-linux64/chrome-headless-shell
# and args --no-sandbox --disable-gpu, then asserts ALL of the following,
# printing one PASS/FAIL line each and exiting non-zero on any failure:
#  A1 (desktop 1280x800) page loads; ZERO console errors and ZERO messages
#     mentioning Content Security Policy across the whole session.
#  A2 #shot-lightbox is not visible on load.
#  A3 click 2nd .term-shot -> dialog visible; #lb-counter text "2 / 4";
#     #lb-img src ends admin-audit.webp; #lb-caption matches the 2nd grid
#     figcaption; getComputedStyle(document.body).overflow === "hidden";
#     document.activeElement is inside the dialog.
#  A4 ArrowRight -> "3 / 4" (admin-queues); two more ArrowRight -> wraps to
#     "1 / 4" (admin-players); ArrowLeft -> wraps to "4 / 4" (admin-health).
#  A5 Escape -> dialog hidden; body overflow no longer "hidden";
#     document.activeElement === the 2nd trigger (focus returned).
#  A6 keyboard path: focus() the 1st trigger, press Enter -> opens at
#     "1 / 4"; press Tab 4 times -> activeElement still inside the dialog
#     (containment); then click near the top-center of the backdrop
#     (e.g. x=640,y=8) -> dialog closes.
#  A7 no-JS context (javaScriptEnabled:false): 4 figures render, dialog not
#     visible, computed cursor on a .term-shot is NOT "zoom-in".
#  A8 mobile context (390x844): open 1st shot; #lb-img bounding box width
#     <= 390; prev AND next buttons each fully inside the viewport with >=
#     40px min dimension; clicking next -> "2 / 4".
# Required final output: "ALL LIGHTBOX CHECKS PASSED". Kill the dev server
# afterwards.
    </automated>
  </verify>
  <done>
All static gates G1-G8 pass; the CSP-enforced local serve returns the page,
styles.css, and site.js with the lightbox shell present; the headless-browser
UAT prints ALL LIGHTBOX CHECKS PASSED covering open/counter/carousel
wrap-around, Esc/backdrop/X close, focus in-and-return, scroll lock, keyboard
operability + focus containment, no-JS inertness (no affordance, grid intact),
and mobile reachability; exactly one commit exists touching only
site/public/index.html, site/public/styles.css, site/public/site.js.
  </done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| browser -> static page | Public marketing page; only inputs are clicks/keys. No forms, no data ingress. |
| dev harness -> scratchpad | playwright-core installed in scratchpad only; never enters the shipped site or repo manifests. |

## STRIDE Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-vfl-01 | Tampering | index.html / site.js (CSP regression via inline style or script) | medium | mitigate | Grep gates G2/G3/G5 + smoke served through wrangler with the real _headers CSP; any inline style/script would surface as a CSP console violation and fail assertion A1. |
| T-vfl-02 | Information Disclosure | site.js (external request / phone-home regression) | low | mitigate | Gate G6 (no protocol-qualified URLs in JS/CSS); slides sourced only from same-origin img/ paths already in the DOM. |
| T-vfl-SC | Tampering | npm install of playwright-core (dev harness) | low | accept | playwright-core is a well-known Microsoft-published package, installed ONLY in the session scratchpad as a verification driver; it is not a runtime, build, or repo dependency and ships in no artifact. |
</threat_model>

<verification>
- Working tree after execution: only the 3 declared files changed, one commit.
- Page with JS on: lightbox fully functional per A1-A8; page with JS off:
  indistinguishable from pre-change behavior.
- CSP invariants preserved (0 inline style attributes, 1 script element,
  0 external resources, _headers untouched).
</verification>

<success_criteria>
- All must_haves truths hold, demonstrated by the automated gates (no manual
  browser step required — verification is fully automated per the developer's
  UAT-automation preference).
- No deploy performed; wrangler used strictly as a local dev server.
</success_criteria>

<output>
Create `.planning/quick/260712-vfl-screenshot-lightbox/SUMMARY.md` when done
(follow the summary template; note the wrangler-vs-python fallback if used).
</output>
