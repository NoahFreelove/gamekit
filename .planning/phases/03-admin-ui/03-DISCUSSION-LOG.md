# Phase 3: Admin UI — Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-18
**Phase:** 03-admin-ui
**Areas discussed:** Admin auth scheme + ban enforcement, Role model + bootstrap, Panel behavior, Admin UI look + host integration

---

## Admin auth scheme + ban enforcement

### How do operators log into /admin?

| Option | Description | Selected |
|--------|-------------|----------|
| Form login + cookie (Recommended) | HttpOnly secure cookie via ASP.NET Core cookie auth | ✓ |
| HTTP Basic | Native browser prompt; ugly UX, no logout | |
| Admin-token header | Bearer-style; hostile to browser users | |
| Pluggable `IAdminAuthScheme` | Abstraction layer; overkill for v1 | |

**User's choice:** Form login + cookie
**Notes:** Browser-friendly, integrates with anti-CSRF + Blazor Server SignalR circuit.

### When admin panel is mounted in Production but no admin exists — what happens at app startup?

| Option | Description | Selected |
|--------|-------------|----------|
| Fail fast: throw at startup (Recommended) | `ValidateOnStart` pipeline throws; app fails to start | ✓ |
| Log warning, mount in read-only mode | Soft degradation with banner | |
| Auto-provision from env var | Zero-touch for docker-compose | |

**User's choice:** Fail fast at startup
**Notes:** Anchors ROADMAP SC #2.

### Where should Player.IsBanned be enforced in the auth pipeline?

| Option | Description | Selected |
|--------|-------------|----------|
| Login (Recommended) | Block new sessions at `IOAuthProvider.CompleteLoginAsync` | ✓ |
| Refresh (Recommended) | Revoke refresh family on `RefreshTokenService.RotateAsync` | ✓ |
| Middleware (every authenticated request) | DB round-trip per authenticated request | |

**User's choice:** Login + Refresh (multiSelect)
**Notes:** Existing access token self-expires within TTL (15m default) — acceptable revocation latency. Per-request middleware would be overkill given refresh coverage.

---

## Role model + bootstrap CLI

### Which role model for admins?

| Option | Description | Selected |
|--------|-------------|----------|
| Flat 'admin' only (Recommended) | Simplest; single role; v2-room for more | |
| admin vs superadmin | Two-tier; destructive actions gated on superadmin | ✓ |
| Role + permissions (RBAC) | Full RBAC; overkill for v1 | |

**User's choice:** admin vs superadmin
**Notes:** Non-default choice; triggered a follow-up to nail down which actions require superadmin.

### Which actions require superadmin?

| Option | Description | Selected |
|--------|-------------|----------|
| Create/delete other admin accounts | Peer-abuse mitigation | ✓ |
| GDPR delete player | Irreversible PII scrub (CORE-16) | ✓ |
| Manual rank adjustment | Competitive integrity (Phase 4) | ✓ |
| Rotate JWT signing key | Logs everyone out | ✓ |

**User's choice:** All four (multiSelect)
**Notes:** Full destructive/infrastructure bucket goes to superadmin.

### How should the first-admin bootstrap CLI behave?

| Option | Description | Selected |
|--------|-------------|----------|
| Interactive + flags (Recommended) | Flags optional, prompts fill in missing; password via `Console.ReadKey(intercept)` | ✓ |
| Pure-flag CLI | CI-only; flags required; password in shell history | |
| Print one-time setup URL (Grafana-style) | Slickest UX; needs second rendered page + token store | |

**User's choice:** Interactive + flags
**Notes:** Chicken-and-egg solved: first admin with zero existing admins auto-promotes to superadmin regardless of `--role`.

### Should banning enforce a reason length minimum?

| Option | Description | Selected |
|--------|-------------|----------|
| Required + min 3 chars (Recommended) | FluentValidation both client + server | ✓ |
| Required, any length ≥ 1 | NotEmpty only | |
| Freeform dropdown (categories) + notes | Structured for analytics | |

**User's choice:** Required + min 3 chars
**Notes:** Free-form; no taxonomy.

---

## Panel behavior

### How should Health + queue-depth panels update?

| Option | Description | Selected |
|--------|-------------|----------|
| Polling + Refresh button (Recommended) | 10s Timer + manual button; zero extra dep | ✓ |
| SignalR push (live) | Matchmaking wires into an admin hub | |
| Static + explicit Refresh only | No auto-update | |

**User's choice:** Polling + Refresh button
**Notes:** Avoids integration surface with Phase 5 Matchmaking.

### Player search UX — one box or typed tabs?

| Option | Description | Selected |
|--------|-------------|----------|
| Unified search box (Recommended) | Auto-detects UUID / `provider:external_id` / display-name | ✓ |
| Separate tabs per type | Radio tabs with tailored inputs | |

**User's choice:** Unified search box

### Pagination model for result lists?

| Option | Description | Selected |
|--------|-------------|----------|
| Keyset / cursor (Recommended) | Stable under concurrent inserts; indexed hot-path | ✓ |
| Offset + page number | Familiar but slow on large tables | |
| Virtualized infinite scroll | Slick UX but complex for an admin panel | |

**User's choice:** Keyset / cursor
**Notes:** "Load more" button. Default page size 50.

---

## Admin UI look + host integration

### Should Admin UI ship its own visual shell or plug into consumer's layout?

| Option | Description | Selected |
|--------|-------------|----------|
| Own shell, isolated CSS (Recommended) | GameKit ships <html> + layout + scoped CSS | ✓ |
| Consumer provides layout (slot model) | Admin.UI ships components only | |
| Own shell + theme hooks (CSS custom properties) | Bonus --gk-* overrides | |

**User's choice:** Own shell, isolated CSS
**Notes:** No theme hooks in v1; v2 can add if customers ask.

### Blazor component library dependency?

| Option | Description | Selected |
|--------|-------------|----------|
| Hand-rolled, no component-lib dep (Recommended) | 5-10 custom components; zero dep | |
| MudBlazor | MIT-licensed; ~1.8 MB; M3-ish design system | ✓ |
| Radzen.Blazor | MIT; enterprise styling | |

**User's choice:** MudBlazor
**Notes:** User took the non-default path — consciously chose 5-10 components worth of dev time savings over the transitive dep cost. Flagged for explicit acknowledgment in CLAUDE.md stack-table update + 03-01 SUMMARY.

### CSP policy for admin pages — how strict?

| Option | Description | Selected |
|--------|-------------|----------|
| Strict + nonce-based scripts (Recommended) | `default-src 'self'; script-src 'self' 'nonce-*'; frame-ancestors 'none'` | ✓ |
| Moderate: self + inline allowed | Easier dev; slightly weaker XSS defense | |
| Report-only mode first | Ship report-only; tighten later | |

**User's choice:** Strict + nonce-based scripts
**Notes:** Per-request nonce via middleware; Blazor Server layout threads nonce into `<script>` tags.

---

## Claude's Discretion

Areas where the user deferred implementation details to the planner / researcher / executor:

- `admin_users` schema column details (beyond role column)
- Migration pattern (Phase 2 precedent; Admin advisory-lock key and history table name locked)
- Blazor component split + page routing
- Health panel data-source mechanics (ping + Redis status + error ring buffer)
- Match history panel data source (EF query vs new endpoint)
- Admin login cookie lifetime + remember-me (suggested 8h sliding, 30d remember-me)
- Admin login form UX specifics
- CSP violation reporting (likely none in v1)
- Player delete (GDPR) panel UX details

## Deferred Ideas

- RBAC beyond admin/superadmin
- SSO for admin login
- Admin UI localization
- Audit log retention/archival policy
- Mobile-responsive admin UI
- Dark mode + theme hooks
- Self-service admin password reset
- CSP violation reporting endpoint
- WebAuthn / passkey admin login

---
