---
phase: 03
phase_name: Admin UI
gathered: 2026-04-18
status: Ready for planning
---

# Phase 3: Admin UI — Context

**Gathered:** 2026-04-18
**Status:** Ready for planning

<domain>
## Phase Boundary

Ships `GameKit.Admin.UI` as a Blazor Server Razor Class Library NuGet package. Consumers call `services.AddGameKitAdmin(...)` and `app.MapGameKitAdmin("/admin")` to mount a self-contained admin console at a chosen path. The console authenticates operators via a scheme distinct from player JWTs, offers player search + ban/unban + audit-log viewer + match-history viewer + health panel + rank-adjust / queue-depth placeholders, and writes every mutation to the existing Core `admin_audit_log`. In Production, `/admin` returns 404 (not 401) on unauthenticated access.

Scope covers ADMIN-01 through ADMIN-12. Out of scope (deferred to later phases): the functional rank-adjust integration lives with Phase 4 Rankings; functional queue-depth live data lives with Phase 5 Matchmaking; RBAC beyond the admin/superadmin split; admin UI localization; multi-tenant admin; SSO integration with corporate IdPs (Entra / Okta / Keycloak).

</domain>

<decisions>
## Implementation Decisions

### Admin Auth Scheme (ADMIN-04)

- **D-01:** Admins log in via **form + HttpOnly secure cookie** scheme. `/admin/login` renders a Blazor Server login page; on submit, ASP.NET Core cookie auth issues a secure `SameSite=Lax` HttpOnly cookie; `/admin/logout` clears it. Browser-friendly UX, plays with anti-CSRF, integrates with Blazor Server's SignalR circuit without token refresh gymnastics. Chose cookie over HTTP Basic (ugly native prompt, no logout) and over admin-token header (hostile to browser operators, Blazor Server really wants a cookie).
- **D-02:** The admin auth scheme is registered under a distinct scheme name — `GameKitAdmin` — so it cannot be confused with, nor authenticate into, any player-facing endpoint. An integration test asserts that a valid player JWT Bearer token hitting any `/admin/*` endpoint returns 404/403 (ROADMAP SC #6).
- **D-03:** **Player `is_banned` is enforced at two auth checkpoints in `GameKit.Auth`:**
  - **Login path:** every `IOAuthProvider.CompleteLoginAsync` checks `Player.IsBanned` and returns 403 with a banned-reason-hash error; prevents new sessions.
  - **Refresh path:** `RefreshTokenService.RotateAsync` checks `Player.IsBanned` and revokes the entire refresh-token family when banned; the banned player's existing access token self-expires within the configured access-token TTL (default 15 min).
  - **Not** enforced per-authenticated-request via middleware — the DB round-trip per request is overkill given refresh-path coverage.

### Startup Fail-Fast (ADMIN-03)

- **D-04:** When hosting environment is **Production** and zero rows exist in `admin_users` with `role = 'superadmin'`, `AddGameKitAdmin` throws a loud `InvalidOperationException` at app startup with a message pointing operators at `dotnet gamekit admin create`. The exception is thrown from the `ValidateOnStart()` pipeline so the app fails to start rather than running degraded. This is the ROADMAP SC #2 "startup assertion fails fast" anchor.
- **D-05:** In **Development** and **Staging**, startup does not throw — a warning is logged and the panel reports the missing-admin state inline so operators can still exercise the UI during build-out.

### Role Model (ADMIN-04)

- **D-06:** **Two-tier role model** — `admin` (baseline permissions) and `superadmin` (elevated). Stored as a `role text CHECK (role IN ('admin','superadmin'))` column on `admin_users`.
  - **`admin` can:** ban/unban players (with mandatory reason), view admin_audit_log, view match history, view the health panel, see the rank-adjust + queue-depth placeholders.
  - **`superadmin` can additionally:** create/delete other admin accounts, trigger GDPR player delete (CORE-16 irreversible flow), perform manual rank adjustments (Phase 4 surface), rotate JWT signing keys (Phase 2 operational surface).
- **D-07:** Startup assertion **requires at least one superadmin in Production** (per D-04). Regular `admin` accounts alone are not enough to satisfy the gate — ensures there is always at least one operator who can recover from lockout by provisioning more admins.

### First-Admin Bootstrap CLI (ADMIN-11)

- **D-08:** `dotnet gamekit admin create` is the bootstrap flow. **Interactive + flag-driven hybrid:**
  - Accepts `--username <u>` and `--password <p>` as optional flags for CI / docker-entrypoint friendliness.
  - If any required flag is missing, prompts on stdin. Password prompt uses `Console.ReadKey(intercept: true)` so the password never echoes and never lands in shell history.
  - Accepts `--role admin|superadmin` (default: `admin`). **Exception:** when zero `admin_users` rows exist, the first admin created is **auto-promoted to superadmin** regardless of the `--role` flag — closes the "how does the first admin exist" chicken-and-egg.
  - On success, prints: username, role, hashed-credential prefix (for auditability), and a one-line confirmation.

### Ban Reason Policy (ADMIN-06)

- **D-09:** Ban reason is **required**, minimum **3 characters**, maximum **512 characters**. Validated by FluentValidation both client-side (Blazor form) and server-side (endpoint filter). Stored verbatim in `admin_audit_log.after_json->>'ban_reason'`. Free-form; no dropdown or taxonomy in v1.

### Panel Behavior (ADMIN-07..10)

- **D-10:** **Health panel + queue-depth panel refresh via polling + manual Refresh button.** Default interval: 10s, configurable via `GameKitAdminOptions.PanelRefreshInterval`. No SignalR push from sibling packages (avoids integration surface with Matchmaking in Phase 5). Blazor Server components use a `System.Threading.Timer` bound to component lifecycle.
- **D-11:** **Player search uses a unified search box.** The box auto-detects input type: 36-char UUID → id lookup; `provider:external_id` (e.g. `steam:7656...`) → `player_identities` lookup; otherwise → `display_name` prefix match (case-insensitive via `citext`). Single input, single endpoint.
- **D-12:** **Keyset / cursor pagination** on all list views (player search results, match history, audit log). Default page size 50 rows. "Load more" button appends the next page. Indexed hot paths: `(id DESC)` for players, `(created_at DESC, id DESC)` for audit log. No offset/limit, no infinite scroll.

### UI Shell + Component Library (ADMIN-01)

- **D-13:** GameKit.Admin.UI ships its **own Blazor layout + minimal shell CSS** (top nav, sidebar, content area). CSS scoped via Blazor's built-in CSS isolation (`.razor.css`) so consumer app styles do not leak in. No theme hooks in v1 — consumers accept the default GameKit admin look. Adding `--gk-*` custom-property overrides is a v2 consideration if customers ask.
- **D-14:** **MudBlazor** (MIT license, GPL-compatible) is the Blazor component library dependency. Acknowledged tradeoff:
  - **Pros:** ~5-10 hand-rolled components worth of work avoided (DataGrid, Dialog, Snackbar, Form, Autocomplete, Navigation). Operators get familiar MD3-ish component behavior out of the box. Actively maintained.
  - **Cons:** Adds a transitive dependency to every consumer's app (~1.8 MB static assets). Admin.UI tracks MudBlazor's upgrade cadence. Phase 3 research MUST verify MudBlazor 8.x exposes a `net10.0` TFM before pinning; fall back to the latest `net9.0`-compatible version under a compatibility shim if not yet GA. CLAUDE.md stack table requires an update in 03-01 to add MudBlazor to the per-package dependency list.

### CSP + Anti-CSRF (ADMIN-12)

- **D-15:** **Strict CSP with per-request nonce.** Default policy shipped by GameKit.Admin.UI:
  `default-src 'self'; script-src 'self' 'nonce-<per-request>'; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'`.
  The nonce is generated per-request via an `IStartupFilter`-style middleware and threaded into `<script>` tags from the Blazor layout. Integration test asserts the `Content-Security-Policy` header is present on every response from `/admin/*`.
- **D-16:** **Anti-CSRF token required on all mutations** via `Microsoft.AspNetCore.Antiforgery`. All POST/DELETE/PATCH admin endpoints call `IAntiforgery.ValidateRequestAsync`. Integration test asserts a mutation without the token returns 400.

### Audit Writer Pattern (ADMIN-06, CORE-09)

- **D-17:** Admin mutations write to `admin_audit_log` via an `IAdminAuditWriter` service (Scoped, mirrors the Phase 2 `IAuthAuditWriter` shape). Called by the admin endpoint handlers, not by Blazor components directly. Actions are namespaced: `admin.player.ban`, `admin.player.unban`, `admin.admin.create`, `admin.admin.delete`, `admin.player.gdpr_delete`, `admin.player.rank_adjust`, `admin.signing_key.rotate`. Before/after JSON captures the mutated field diff.

### Admin Login Rate Limit

- **D-18:** `/admin/login` uses a dedicated rate-limit policy `gamekit:admin:login` — default **5 attempts / minute / IP**, sliding window. Mitigates credential-stuffing against the admin surface. Rate-limit registration lives in GameKit.Admin.UI and reuses the existing `IGameKitRateLimitPolicies` from Core.

### Claude's Discretion

The planner / researcher / executor decide without further user input:

- **`admin_users` schema** — `id uuid PK`, `username citext UNIQUE`, `password_hash text`, `role text CHECK (role IN ('admin','superadmin'))`, `created_at timestamptz`, `last_login_at timestamptz NULL`. Password hashing reuses the Phase 2 `IPasswordHasher` (BCrypt default, Argon2 sibling possible). Feel free to add columns for defense-in-depth (failed-login counter, locked_until) if needed.
- **Migration pattern** — follow Phase 2 precedent. History table `__ef_migrations_admin` in `gamekit` schema, advisory lock key from `hashtext('gamekit.admin.migrations')::bigint` verified against live Postgres, migrations assembly = `GameKit.Admin.UI`. Register an `IModelBuilderExtension` for the admin entities.
- **Panel component structure** — planner picks Blazor page routing, component split, DataGrid bindings. MudBlazor's DataGrid is the default choice for tabular data; a plain `<table>` is fine for audit log if DataGrid feels heavy.
- **Health panel data sources** — Postgres connectivity via `NpgsqlConnection.Ping`-equivalent; Redis connectivity via `StackExchange.Redis.IConnectionMultiplexer.GetStatus()`; recent error rate from an in-memory ring buffer populated by a log filter (planner picks the exact mechanism — no OTel hard dep per Phase 1 decision).
- **Match history panel data source** — direct EF query against `game_sessions` + `session_participants` joined to `players`. Reuse query patterns from the GDPR export service (CORE-16). No new endpoint unless needed for pagination.
- **Admin login cookie lifetime** — planner picks. Suggestion: 8-hour sliding expiration, signed with `DataProtection`. Remember-me checkbox extends to 30 days.
- **Admin login form UX details** — planner picks layout. Include "forgot password" as a stub that just says "Contact a superadmin" (Phase 3 doesn't ship password reset).
- **CSP reporting** — planner decides whether to also emit `Content-Security-Policy-Report-Only` alongside the enforce header and where to route violation reports (likely no reporter in v1 since we don't want to phone home).
- **Player delete panel (GDPR, superadmin-only)** — surface in the UI that calls the existing CORE-16 `IGdprDeleteService`. Confirmation dialog required.

### Folded Todos

None — no pending todos surfaced for this phase.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project-level (read first)
- `CLAUDE.md` — Stack pin table. Phase 3 research must update it to add MudBlazor (pending TFM verification), and add the GameKit.Admin.UI per-package section.
- `.planning/ROADMAP.md` § Phase 3 — 12 ADMIN requirements + 6 success criteria (SC #1–#6 anchor the test matrix).
- `.planning/REQUIREMENTS.md` — ADMIN-01 through ADMIN-12.
- `.planning/STATE.md` — Current locked decisions; Phase 1 + Phase 2 "Decisions Locked" tables are load-bearing for Phase 3.

### Phase 2 Carryover
- `.planning/phases/02-authentication/02-CONTEXT.md` § D-03 — player JWT claims do NOT include admin roles (confirms Phase 3 must use a separate scheme).
- `.planning/phases/02-authentication/02-CONTEXT.md` § D-14 — SERIALIZABLE pattern for race-sensitive admin writes (e.g. two superadmins creating the same username concurrently).
- `src/GameKit.Auth/Services/IPasswordHasher.cs` + `BCryptPasswordHasher.cs` — reuse for admin credentials.
- `src/GameKit.Auth/Http/RateLimiting/AuthRateLimitRegistrations.cs` — pattern to mirror for `gamekit:admin:login` policy.
- `src/GameKit.Auth/Data/AuthMigrationHostedService.cs` — pattern for applying a sibling package's migrations on app start.

### Core Surface Being Consumed
- `src/GameKit.Core/Entities/AdminAuditLog.cs` + `Data/Configurations/AdminAuditLogConfiguration.cs` — existing audit log entity; Admin.UI writes to it via `IAdminAuditWriter`.
- `src/GameKit.Core/Services/GdprDeleteService.cs` — superadmin GDPR delete panel calls this.
- `src/GameKit.Core/Data/GameKitDbContext.cs` — DI-preferred constructor pattern; Admin entities flow in via `IModelBuilderExtension`.
- `src/GameKit.Core/Data/IModelBuilderExtension.cs` — pattern for exposing Admin entities to the runtime query model.
- `src/GameKit.Core/Data/MigrationRunner.cs` — `MigrateWithLockAsync(ctx, lockKey, ct)` overload for Admin.

### Research Anchors
- MudBlazor on NuGet — verify `net10.0` TFM availability and latest version pin (as of 2026-04-18). Docs: https://mudblazor.com/
- ASP.NET Core Blazor Server cookie auth + antiforgery — MS Learn refs.
- ASP.NET Core CSP with nonce — reference middleware pattern (custom middleware + IHttpContextAccessor).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- **Core `admin_audit_log` table + entity + configuration** — already migrated; Phase 3 just writes rows.
- **`IPasswordHasher` / `BCryptPasswordHasher`** (Phase 2) — admin credentials hash with the same interface. No new hasher needed.
- **`IGameKitRateLimitPolicies`** (Phase 1) — admin login rate-limit policy registers through the existing abstraction.
- **`IAuthAuditWriter` pattern** (Phase 2) — mirror shape for `IAdminAuditWriter`: Scoped lifetime, writes to `admin_audit_log` via EF, includes actor-player-id + target-player-id + action namespace + before/after JSON.
- **`MigrationRunner.MigrateWithLockAsync(ctx, lockKey, ct)`** (Phase 2) — overload exists, Admin reuses it with its own `AdminMigrationConstants.AdvisoryLockKey`.
- **`AuthMigrationHostedService`** (Phase 2) — `IHostedService` pattern for applying package migrations after Core but before Kestrel accepts traffic. Direct template for `AdminMigrationHostedService`.
- **GameKitDbContext DI path** — DI-constructed context resolves `IEnumerable<IModelBuilderExtension>` from the application service provider, so registering `AdminModelBuilderExtension` in `AddGameKitAdmin` is enough to make Admin entities part of the runtime model. The FOLLOW-UP-02-03-01 shim (`AuthRuntimeQueryCustomizer`) is no longer needed.
- **`dotnet gamekit` CLI (`src/GameKit.Cli`)** — the `admin create` stub exists from Phase 1; Phase 3 fills in the handler.

### Established Patterns
- **Per-package migrations** — history table `__ef_migrations_admin`, distinct advisory-lock key, migrations assembly = `GameKit.Admin.UI`. Live-verify the hashtext value via Testcontainers per the 02-02 precedent (the pre-computed value can be wrong).
- **Fluent builder** — `services.AddGameKit().AddAuth(...).AddGameKitAdmin(...)`. Returns `IGameKitBuilder` so chaining composes.
- **Strict TFM pins in `Directory.Packages.props`** — every new dep (MudBlazor, any CSP helper library) gets a pin + a note confirming `net10.0` compatibility.
- **CS1591-as-error** — every public type/member needs an XML doc comment.
- **Integration tests via Testcontainers** — use the Phase 2 `AuthIntegrationFixture` shape. Spin up Postgres, apply Core + Auth + Admin migrations, drive via `WebApplicationFactory`.

### Integration Points
- `MapGameKitAdmin("/admin")` extends `IEndpointRouteBuilder` and mounts the Blazor Server `<Router>` rooted at the configured path. Must NOT interfere with the player-facing routes mounted via `MapGameKit()` + `MapAuth()`.
- `UseGameKitAdmin()` inserts the admin auth scheme into the pipeline. Middleware order per Phase 2 D-01: `UseRouting → UseRateLimiter → UseGameKitAuth → UseGameKitAdmin → UseGameKit → map*`. Admin auth runs after player auth so a request never has both cookies evaluated as player identity.
- The admin login page + panels must render with the strict CSP policy — MudBlazor's inline-style usage is allowed by `style-src 'self' 'unsafe-inline'`, but any MudBlazor inline `<script>` must be adjusted (MudBlazor ships no inline scripts by default; verify this during research).

</code_context>

<specifics>
## Specific Ideas

- Admin login UX is classic form — no flourishes. Username + password + Sign in button + "Contact a superadmin" stub for recovery.
- First-admin bootstrap message must be friendly — clearly state what to do, not just a stack trace.
- The MudBlazor dependency acceptance is a conscious tradeoff — the user weighed adding ~1.8 MB transitive deps vs hand-rolling components and chose the productivity path. Document this clearly in the 03-01 SUMMARY so future customers aren't surprised.
- The banned-reason-hash in the Phase 2 login response (D-03) should be human-readable-on-admin-side but opaque to the player — admins see the actual reason in the audit log; the player receives only "This account is banned" without the operator's note.

</specifics>

<deferred>
## Deferred Ideas

- **RBAC beyond admin/superadmin** — if a customer needs fine-grained permissions, add an `admin_roles` + `admin_permissions` surface in a later phase. Not blocking v1.
- **SSO for admin login (Entra / Okta / Keycloak)** — the cookie auth scheme is the only supported mechanism in v1. Pluggable `IAdminAuthScheme` abstraction was considered and deferred to keep surface small.
- **Admin UI localization (i18n)** — English-only in v1. Later phase can add resource files if a customer asks.
- **Audit log retention / archival policy** — audit log grows unbounded in v1. Later: consider cold-storage archival or operator-configurable retention with cascading delete safeguards.
- **Mobile-responsive admin UI** — admins are assumed to be at desktops. Mobile reflow can be a v2 polish.
- **Dark mode + theme hooks (`--gk-*` custom properties)** — operators live with default GameKit admin look in v1.
- **Admin password reset flow** — currently handled out-of-band (another superadmin uses the CLI to reset). Self-service flow is a v2 feature.
- **CSP violation reporting endpoint** — library does not phone home. If an operator wants CSP violation telemetry, they wire their own `report-to` endpoint.
- **WebAuthn / passkey admin login** — modern option; considered but deferred. Cookie+password is the v1 baseline.

</deferred>

---

*Phase: 03-admin-ui*
*Context gathered: 2026-04-18*
