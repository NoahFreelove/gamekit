# gamekit-site

The marketing/landing site for [GameKit](https://github.com/NoahFreelove/gamekit),
served via Cloudflare Pages under a `noahfreelove.com` CNAME.

Deliberately tiny: one hand-written `index.html`, one stylesheet, one ~30-line
vanilla JS file (copy-to-clipboard), an SVG favicon. No framework, no build
step, no external requests of any kind — the page phones home exactly as much
as GameKit does (not at all).

The aesthetic is "engine room": near-black charcoal, one phosphor-green accent,
monospace display type, hairline blueprint grid, terminal-prompt motifs, sharp
corners.

## Layout

```
site/
├── public/            # everything here is served verbatim (pages_build_output_dir)
│   ├── index.html
│   ├── styles.css
│   ├── site.js        # copy-button only; CSP-friendly (no inline script)
│   ├── favicon.svg
│   ├── robots.txt
│   └── _headers       # strict CSP + security headers (must live IN the served dir)
├── wrangler.toml
└── package.json
```

## Develop

```bash
npm install            # just pulls in wrangler
npm run dev            # serves public/ locally with the _headers applied
```

(For a quick look without wrangler: `python3 -m http.server -d public` — the
only thing that won't apply is the `_headers` CSP.)

## Deploy

```bash
npm install
npx wrangler deploy    # or: npm run deploy (= wrangler pages deploy)
```

First deploy creates the `gamekit-site` Pages project. Then, **once, manually,
in the Cloudflare dashboard**: Pages → gamekit-site → Custom domains → add the
chosen `<sub>.noahfreelove.com` subdomain. Cloudflare provisions the CNAME;
point the DNS record for the subdomain at the `gamekit-site.pages.dev` host if
it isn't already.

## Editing copy

All text lives in `public/index.html`; all styling/colors in
`public/styles.css` (see the `:root` token block at the top). The code samples
on the page are real GameKit API — if the builder surface changes
(`AddGameKit`, `AddAuth`, `AddMatchmaking`, `IMatchmakingStrategy`, …), update
the `#compose` section to match. If you ever add an inline script or external
asset, loosen the matching directive in `public/_headers` — the CSP is
intentionally strict.
