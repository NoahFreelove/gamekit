---
phase: quick-260712-t3r-admin-screenshot-showcase
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - site/public/img/admin-players.webp
  - site/public/img/admin-audit.webp
  - site/public/img/admin-queues.webp
  - site/public/img/admin-health.webp
  - site/public/index.html
  - site/public/styles.css
autonomous: true
requirements: [SITE-SHOWCASE-01]

must_haves:
  truths:
    - "index.html renders a new admin-console showcase section with 4 screenshots in a 2-column grid that collapses to 1 column on mobile, each with a short caption."
    - "All 4 images load from same-origin img/ paths; the page makes zero new external resource requests (CSP img-src 'self' posture preserved, _headers untouched)."
    - "Every img has loading=\"lazy\", explicit width=\"1600\" height=\"620\", descriptive alt text, and scales responsively to its container width."
    - "New styles reuse the existing CSS custom properties (--bg-inset, --line, --line-bright, --accent, --muted, --mono) and the .term/.term-bar/.term-title frame pattern — no hardcoded duplicate color values."
    - "site/wrangler.toml, site/package.json, site/public/_headers, and site/public/site.js are untouched; no files outside site/public/ change except planning docs. No deploy is performed."
  artifacts:
    - site/public/img/admin-players.webp
    - site/public/img/admin-audit.webp
    - site/public/img/admin-queues.webp
    - site/public/img/admin-health.webp
    - site/public/index.html
    - site/public/styles.css
  key_links:
    - "img src attributes in the new section <-> site/public/img/*.webp filenames (case-exact, relative paths like the existing favicon/styles refs)."
    - ".shots grid class in index.html <-> .shots rule in styles.css AND the 1-column override added to the existing @media (max-width: 960px) block."
    - "sec-num renumbering: new showcase = 03; compose 03 -> 04; production 04 -> 05 (aria-hidden decorative spans, must stay sequential)."
---

<objective>
Add an admin-console screenshot showcase section to the GameKit marketing site
(site/public/): copy 4 pre-captured 1600x620 WebP screenshots into
site/public/img/, insert a new numbered section into index.html directly after
the module/features grid, and style it in styles.css to match the established
"engine room" aesthetic (near-black, phosphor-green, monospace, terminal frames).

Purpose: The modules grid *tells* visitors GameKit.Admin.UI exists; this section
*shows* the console (players/ban/GDPR ops, audit log, live queue telemetry,
health probes) — proof the ops story is real, placed right where the Admin.UI
card plants the idea.

Output: 4 image assets + one new HTML section + supporting CSS, in one atomic
commit. Static site, no build step, no deploy.
</objective>

<execution_context>
@$HOME/.claude/gsd-core/workflows/execute-plan.md
@$HOME/.claude/gsd-core/templates/summary.md
</execution_context>

<context>
@site/public/index.html
@site/public/styles.css

# Verified scouting facts (from planner) — treat as authoritative:
#
# - Source screenshots exist and are correct (all 1600x620 WebP, ~138 KB total):
#     /tmp/claude-1000/-home-noah-Desktop-projects-gamekit/925e29b0-f3be-4848-b957-397669321bf8/scratchpad/shots/web/
#       admin-players.webp  admin-audit.webp  admin-queues.webp  admin-health.webp
#   If this directory is missing, STOP and report — do NOT attempt to regenerate
#   screenshots.
#
# - site/public/img/ does not exist yet — create it.
#
# - Page section order (working tree): hero (install commands live here) ->
#   fact strip -> 01 what (#what) -> 02 modules (#modules, the features grid,
#   contains the GameKit.Admin.UI card) -> 03 compose (#compose) ->
#   04 production (#production) -> footer. There is NO separate
#   install/quickstart section — the requested "between features and install"
#   boundary maps to: immediately after the #modules section, before compose.
#
# - PRE-EXISTING WORKING-TREE EDITS: site/public/index.html already differs
#   from HEAD by an intentional 7-line deletion (hero .eyebrow paragraph and
#   footer .badges block removed). Build on the CURRENT WORKING TREE — do not
#   restore those elements. Your commit of index.html will include these
#   pre-existing deletions; that is expected and correct.
#
# - CSP: site/public/_headers already enforces img-src 'self'. Same-origin
#   images need no header change. Do not touch _headers.
#
# - .term has max-width: 40rem — the screenshot frames need a max-width: none
#   override (same pattern as the existing .term-code class).
</context>

<tasks>

<task type="auto">
  <name>Task 1: Copy screenshots, add showcase section to index.html, style in styles.css</name>
  <files>site/public/img/admin-players.webp, site/public/img/admin-audit.webp, site/public/img/admin-queues.webp, site/public/img/admin-health.webp, site/public/index.html, site/public/styles.css</files>
  <action>
1. **Copy images.** `mkdir -p site/public/img` then copy the 4 WebP files from
   `/tmp/claude-1000/-home-noah-Desktop-projects-gamekit/925e29b0-f3be-4848-b957-397669321bf8/scratchpad/shots/web/`
   into `site/public/img/` keeping the exact filenames (admin-players.webp,
   admin-audit.webp, admin-queues.webp, admin-health.webp).

2. **Insert the showcase section in site/public/index.html** between the closing
   `</section>` of the modules section (`id="modules"`) and the
   `<!-- ============ COMPOSE ============ -->` banner comment. Follow the
   existing section conventions exactly: a banner comment
   (`<!-- ============ ADMIN CONSOLE ============ -->`), then
   `<section class="section" id="console">` wrapping a `div.wrap`, an `h2` with
   a `span.sec-num` (aria-hidden="true") reading `03`, and a `p.section-sub`.
   Use `&mdash;`/`&rsquo;` entities as the rest of the file does.

   - Heading text: `Ops console included`
   - section-sub text: `GameKit.Admin.UI mounts a Blazor Server console inside
     your own app &mdash; player ops, audit trail, live queues, health. No
     separate deployment.`
   - Body: `<div class="shots" role="list">` containing 4
     `<figure class="shot" role="listitem">` blocks. Each figure wraps the
     image in the site's terminal-frame pattern:
     `<div class="term term-shot">` containing
     `<div class="term-bar"><span class="term-title">admin &mdash; {players|audit|queues|health}</span></div>`
     followed directly by the `<img>` (no `.term-body`/`pre` wrapper — that is
     for code), then a `<figcaption>` after the frame div, inside the figure.
   - Every img: `src="img/admin-{name}.webp"`, `width="1600"`, `height="620"`,
     `loading="lazy"`, descriptive alt. Use these alt texts:
     - admin-players: `GameKit admin console &mdash; Players page: master-detail player list with search, ban, and GDPR-delete actions`
     - admin-audit: `GameKit admin console &mdash; Audit log: chronological admin actions with action-type filter chips`
     - admin-queues: `GameKit admin console &mdash; Queue depth: live matchmaking pool depths with the leader lease indicator`
     - admin-health: `GameKit admin console &mdash; Health page: Postgres, Redis, and error-rate probe cards all reporting OK`
   - Captions (verbatim, with `&mdash;`):
     - `Players &mdash; search, ban, GDPR-delete`
     - `Audit log &mdash; every admin action recorded`
     - `Queue depth &mdash; live matchmaking telemetry with leader lease`
     - `Health &mdash; Postgres/Redis/error-rate probes`
   - Order: players, audit, queues, health.
   - Only relative same-origin URLs in this section; add no external resource
     references of any kind (fonts, scripts, images) anywhere in the page.

3. **Renumber downstream sec-nums in index.html** so numbering stays
   sequential: in the compose section change `>03<` to `>04<` inside its
   `span.sec-num`, and in the production section change `>04<` to `>05<`.
   Touch nothing else in those sections. Do not add a topnav link (not
   requested).

4. **Style in site/public/styles.css.** Insert a new commented block
   `/* ---------- admin console shots ---------- */` between the
   `/* ---------- module grid ---------- */` block and the
   `/* ---------- compose ---------- */` block (CSS file order mirrors page
   order). Reuse existing custom properties — no new hex colors:
   - `.shots`: 2-column grid `repeat(2, minmax(0, 1fr))`, `gap: 1.5rem`
     (matches .code-cols), `margin-top: 2.25rem` (matches .ops-grid rhythm).
   - `.term-shot { max-width: none; }` (mirrors .term-code so frames fill
     their grid cell).
   - `.shot img { display: block; width: 100%; max-width: 100%; height: auto; }`
     — with the fixed width/height attributes this gives responsive scaling
     with zero layout shift.
   - `.shot figcaption`: `font-family: var(--mono)`, small size (~0.78rem),
     `color: var(--muted)`, slight letter-spacing, `margin-top: 0.6rem` — in
     the same voice as .term-title/.card-tags.
   - In the EXISTING `@media (max-width: 960px)` block, add
     `.shots { grid-template-columns: minmax(0, 1fr); }` alongside the other
     grid collapses (1 column on mobile; nothing further needed at 620px).

5. **Scope guard:** modify ONLY the 6 files listed in `<files>`. Do not touch
   site/wrangler.toml, site/package.json, site/public/_headers,
   site/public/site.js, or anything outside site/public/. Do not deploy. Note
   that index.html's working tree already contains the intentional pre-existing
   deletion of the hero eyebrow and footer badges — keep it as-is.

6. **Commit** everything as one atomic commit (images + HTML + CSS are
   inseparable — the page 404s or renders unstyled if split):
   `feat(quick-260712-t3r): add admin console screenshot showcase to site`.
   The pre-existing index.html deletions ride along in this commit by design.
  </action>
  <verify>
    <automated>cd /home/noah/Desktop/projects/gamekit && ls site/public/img/admin-players.webp site/public/img/admin-audit.webp site/public/img/admin-queues.webp site/public/img/admin-health.webp && [ "$(grep -c 'loading="lazy"' site/public/index.html)" = "4" ] && [ "$(grep -c 'width="1600" height="620"' site/public/index.html)" = "4" ] && [ "$(grep -c 'src="img/admin-' site/public/index.html)" = "4" ] && [ "$(grep -c 'src="http' site/public/index.html)" = "0" ] && [ "$(grep -c '\.shots' site/public/styles.css)" -ge 2 ] && grep -q 'id="console"' site/public/index.html && grep -q '>05<' site/public/index.html && { python3 -m http.server 8931 --directory site/public >/dev/null 2>&1 & SRV=$!; sleep 1; ok=1; for f in admin-players admin-audit admin-queues admin-health; do code=$(curl -s -o /dev/null -w '%{http_code}' "http://127.0.0.1:8931/img/$f.webp"); [ "$code" = "200" ] || { echo "FAIL $f -> $code"; ok=0; }; done; kill $SRV 2>/dev/null; [ "$ok" = "1" ] && echo OK; }</automated>
  </verify>
  <done>
    site/public/img/ contains the 4 WebP screenshots; index.html has the new
    #console section (sec-num 03) between #modules and the compose section with
    4 lazy-loaded, dimension-attributed, alt-texted images in a .shots grid with
    the 4 specified captions; compose/production renumbered to 04/05; styles.css
    styles the grid using existing custom properties with a 1-column collapse at
    960px; a local static serve returns 200 for all 4 images; no external
    resource URLs added; only the 6 listed files changed; one commit created.
  </done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| visitor browser -> Cloudflare Pages static assets | Public page delivery; only static HTML/CSS/JS/images cross here |

## STRIDE Threat Register

| Threat ID | Category | Component | Severity | Disposition | Mitigation Plan |
|-----------|----------|-----------|----------|-------------|-----------------|
| T-quick-t3r-01 | Tampering | new img/ asset references | low | mitigate | Same-origin `img/` paths only; existing `_headers` CSP (`img-src 'self'`) stays untouched and continues to block any injected external image; verify gate asserts zero `src="http` occurrences |
| T-quick-t3r-02 | Information Disclosure | screenshot contents | low | accept | Screenshots are pre-captured from the local dev sample app with demo data — no real player PII; publishing them is the point of the task |
| T-quick-t3r-SC | Tampering | npm/pip/cargo installs | low | accept | No package installs occur in this task (static assets + HTML/CSS edits only) |
</threat_model>

<verification>
- All must_haves truths hold (see frontmatter).
- `git diff HEAD~1 --name-only` after the commit lists only the 6 files in
  `files_modified` (index.html's pre-existing eyebrow/badges deletions are part
  of the same file and expected).
- Page still makes zero external resource requests (only `href` navigation
  links to GitHub/NuGet remain, which the CSP posture permits).
</verification>

<success_criteria>
- Visiting the locally-served site shows a "03 / Ops console included" section
  after the module grid: 4 terminal-framed screenshots, 2-up on desktop, 1-up
  under 960px, each with its mono caption, in the existing green-on-near-black
  aesthetic.
- No layout shift on image load (explicit dimensions + height:auto scaling).
- wrangler.toml / package.json / _headers / site.js byte-identical to before.
- Exactly one new commit; no deploy performed.
</success_criteria>

<output>
Create `.planning/quick/260712-t3r-add-admin-screenshot-showcase-to-site/SUMMARY.md` when done.
</output>
