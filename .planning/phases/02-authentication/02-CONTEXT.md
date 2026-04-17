---
phase: 02
phase_name: Authentication
gathered: 2026-04-17
status: Ready for planning
---

# Phase 2: Authentication - Context

**Gathered:** 2026-04-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Ships `GameKit.Auth` as a composable NuGet package. Adds `player_identities`, `player_credentials`, and `refresh_tokens` tables via the per-package migration pattern. Delivers four authentication providers (Steam, Discord, Guest, Username/Password), JWT issuance + rotating refresh-token flow with reuse-attack detection and a fingerprint-gated grace window, SERIALIZABLE guest→real-account upgrade, and rate-limited `/auth/*` endpoints.

Scope covers AUTH-01 through AUTH-16. Out of scope (deferred to v2): Argon2 sibling package, additional OAuth providers (Google/Apple/Epic), account merge of two distinct `player_id`s.

</domain>

<decisions>
## Implementation Decisions

### JWT Shape + Lifetimes

- **D-01:** Access-token lifetime is **configurable** via `JwtOptions.AccessTokenLifetime`. Default = **15 minutes**. Operators may shorten (stronger revocation story) or lengthen (mobile battery / sticky-session friendliness) per deployment.
- **D-02:** Refresh-token lifetime is **configurable** via `JwtOptions.RefreshTokenLifetime`. Default = **30 days**. Rotation + reuse detection is the real security property, not the TTL.
- **D-03:** Standard claims always emitted: `sub` (= `player_id`), `jti`, `iat`, `exp`, `iss`, `aud`. Optional claims always included in Phase 2: `is_guest` (bool), `provider` (`steam` | `discord` | `guest` | `password`), `sid` (session / refresh-family id). Admin-role claims are explicitly NOT in player tokens — Phase 3 admin auth is a separate scheme per ROADMAP.md.
- **D-04:** Revocation strategy is **stateless**. Access tokens self-expire; ban/logout invalidates the refresh-token family (next `/auth/refresh` returns 401). No Redis jti denylist in Phase 2. Reconsidered only if a customer demonstrates a need for sub-minute universal revocation.

### Client Fingerprint

- **D-05:** Client fingerprint is **client-supplied** via the `X-GameKit-Device: <uuid>` request header. Stored on `refresh_tokens.device_fingerprint` on the first `/auth/login` or `/auth/register` call. Stable across IP/Wi-Fi/cellular changes and privacy-respecting (no IP+UA hashing on the server). Sample HTML client demonstrates how to persist and send it. Missing header = fingerprint is `NULL`; the grace window does not apply (the next /refresh is treated strictly as a reuse-attack signal).
- **D-06:** When two concurrent `/auth/refresh` calls arrive for the same token within the 30–60 s grace window and the fingerprints **do not match**, the entire refresh-token family is revoked (AUTH-11 / AUTH-12 strict interpretation). Both devices are forced to re-authenticate. One audit-log row per revocation event with `reason = "refresh_fingerprint_mismatch"`.

### Egress Allow-List for Provider HTTP (Resolves Phase 1 D-21)

- **D-07:** Provider HTTP uses **named `HttpClientFactory` instances** — `"gamekit.auth.provider.steam"`, `"gamekit.auth.provider.discord"`. Each is registered via `IHttpClientBuilder` with `Microsoft.Extensions.Http.Resilience` (retry / circuit-breaker / timeout per STACK.md). No naked `new HttpClient()` anywhere in Auth.
- **D-08:** Operators configure an allow-list via `GameKitAuthOptions.AllowedProviderHosts` — default populated with `steamcommunity.com`, `api.steampowered.com`, `discord.com`, `discordapp.com`. A `DelegatingHandler` on the named clients asserts the outbound request URI host is on the allow-list; a violation throws `EgressViolationException` (caught by middleware, logged, never silently swallowed).
- **D-09:** Phase 1's netns-sandboxed CI test remains **Core-only** (same `[Fact(Skip = "Linux-only")]` guard on dev machines, `unshare --net` wrapper on CI — D-19). Auth integration tests run **outside** the netns against mocked provider endpoints using **WireMock.Net** (or equivalent; planner may substitute `TestServer` if simpler). No real Steam or Discord calls in CI.
- **D-10:** A dedicated `EgressAllowListTests` fixture asserts: (a) calling any named Auth HttpClient with an off-list URI throws; (b) the default allow-list resolves the canonical provider endpoints; (c) Auth registers zero HttpClient instances that are NOT on the allow-list path.

### Guest Upgrade + Identity Conflicts

- **D-11:** Guest → OAuth link: when guest `G` completes OAuth for provider `P` with `external_id = X`, and `(P, X)` is **already linked to a different player P₂**, the server returns **HTTP 409** with error code `identity_already_linked` and body `{ error, provider, external_id_hash }` (hashed to avoid leaking the raw external id to G). No `link-or-switch` UX in Phase 2 — simpler contract, aligns with AUTH-14 "explicit user choice" by forcing the client to log out G and /auth/login manually as P₂ to continue. Player-merge across `player_id`s is AUTH-V2-03 (deferred).
- **D-12:** Guest → Password upgrade: a `/auth/register` call that carries a valid guest `Authorization: Bearer` header **upgrades the guest in place**. Single SERIALIZABLE transaction: insert a row into `player_credentials` with `player_id = G.player_id`, keep all of G's session history, re-issue the JWT without the `is_guest` claim. A `/auth/register` with no auth header creates a fresh player as today. This is the only endpoint that behaves differently depending on whether the caller is already authenticated as a guest.
- **D-13:** `is_guest` is a **computed property**, NOT a stored column. `Player.IsGuest = !Identities.Any() && Credentials is null`. The JWT issuance service evaluates it in the same transaction that issues the token. Consequence: cleared automatically the moment the first identity or credential lands. No drift risk, no extra migration column.
- **D-14:** Concurrent guest-upgrade race (AUTH-13 success criterion): two simultaneous `/auth/link/steam` calls for the same guest `G` with the same Steam `X` are serialized by the unique constraint `(provider, external_id)` on `player_identities`. Inside the SERIALIZABLE transaction, one succeeds; the other fails on the constraint and returns 409 `identity_already_linked` (same code as D-11 — the client can't tell the difference, which is fine). No duplicate rows. Integration test per ROADMAP success criterion #4.

### Claude's Discretion

Claude decides (planner / researcher will resolve without further user input):

- **Endpoint surface** — minimum set per spec: `/auth/login`, `/auth/register`, `/auth/refresh`, `/auth/logout`, `/auth/challenge/{provider}`, `/auth/callback/{provider}`, `/auth/link/{provider}`. Optional helpers like `/auth/me`, `/auth/identities`, `/auth/logout/all` — planner decides based on the Sample app's real needs. Default: ship `/auth/me` + `/auth/logout/all`, defer `/auth/identities` unless the Sample needs it.
- **Discord OAuth scopes** — locked to `identify` only (AUTH-07); no email scope (avoids GDPR scope creep + matches STACK.md note).
- **Username policy** — RFC-lite: 3–32 chars, `[a-zA-Z0-9_-]`, case-insensitive uniqueness, no reserved-word list in Phase 2. Operators can tighten via `GameKitAuthOptions.Validation`.
- **Rate-limit values per endpoint** — use the named policies introduced in Phase 1 (`GameKitRateLimitPolicies`). Suggested: `/auth/login` 10/min/IP, `/auth/refresh` 60/min/IP (accounts for mobile resumes), `/auth/register` 5/min/IP.
- **SPA vs server-rendered challenge/callback handshake** — planner picks the standard ASP.NET Core approach: 302 redirect on `/auth/challenge/{provider}`, provider redirects to `/auth/callback/{provider}`, server issues JWT pair in response body (NOT cookie) so the contract works for both SPA and native clients. Sample app uses fetch + localStorage.
- **WireMock.Net vs TestServer** for provider mocks — planner's call; both satisfy D-09.
- **Migration timestamp + history table name** — must follow Phase 1's pattern: per-package history table `__ef_migrations_auth` in `gamekit` schema, migration assembly = `GameKit.Auth`.
- **Password hashing parameters** — BCrypt work factor 12 (default); `IPasswordHasher` interface per AUTH-16 so Argon2 can swap in later without breaking change.

### Folded Todos

None — no pending todos surfaced for this phase.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Project-level (read first)
- `CLAUDE.md` — Full stack pin table (JWT, BCrypt, aspnet-contrib OAuth, Scrutor, FluentValidation 12, `Microsoft.Extensions.Http.Resilience`, Polly 8, MinVer 7), including explicit "NOT added" rejections (no IdentityServer, no OpenIddict, no ASP.NET Core Identity).
- `.planning/ROADMAP.md` § Phase 2 — 16 AUTH requirements + 6 success criteria.
- `.planning/REQUIREMENTS.md` — AUTH-01 through AUTH-16.
- `.planning/STATE.md` — Phase 1 decisions locked list (includes D-21 egress-for-Auth deferred into Phase 2).

### Phase 1 (prior decisions)
- `.planning/phases/01-foundation-core-migrations-ops-defaults-gpl/01-CONTEXT.md` § D-07 (UseGameKit auto-migrate), D-09 (advisory lock), D-10/D-11/D-13 (GDPR hard-delete + ON DELETE SET NULL, `"Deleted Player"` fallback), D-18/D-19/D-20 (egress guard layering), D-21 (Phase 2 egress carve-out — resolved by D-07/D-08 above).
- `src/GameKit.Core/Data/GameKitDbContext.cs` — single shared context; Auth extends via `IModelBuilderExtension`.
- `src/GameKit.Core/Data/IModelBuilderExtension.cs` — sibling extension contract (AUTH adds 3 tables).
- `src/GameKit.Core/Builder/IGameKitBuilder.cs` — extend with `.AddAuth(opts => ...)` fluent method.
- `src/GameKit.Core/RateLimiting/GameKitRateLimitPolicies.cs` — 5 named policy constants; Auth reuses `Login`, `Register`, `Refresh`.
- `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` — authorization middleware already registered; Auth plugs in as an authentication scheme.

### External (stack-pinned; planner reads as needed)
- AspNet.Security.OpenId.Steam 10.0.0 — [https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers](https://github.com/aspnet-contrib/AspNet.Security.OpenId.Providers)
- AspNet.Security.OAuth.Discord 10.0.0 — [https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Discord](https://github.com/aspnet-contrib/AspNet.Security.OAuth.Providers/tree/dev/src/AspNet.Security.OAuth.Discord)
- Steam OpenID 2.0 `check_authentication` spec — [https://partner.steamgames.com/doc/features/auth](https://partner.steamgames.com/doc/features/auth) (server-side roundtrip per AUTH-06 security requirement; forgery rejection per success criterion #2).
- Discord OAuth2 — [https://discord.com/developers/docs/topics/oauth2](https://discord.com/developers/docs/topics/oauth2) (`identify` scope only).
- BCrypt.Net-Next — [https://github.com/BcryptNet/bcrypt.net](https://github.com/BcryptNet/bcrypt.net).
- `Microsoft.AspNetCore.Authentication.JwtBearer` — .NET 10 shared framework.
- Microsoft.Extensions.Http.Resilience — [https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience](https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience).
- OWASP Cheat Sheet — Authentication (password policy + JWT claims guidance) [https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/Authentication_Cheat_Sheet.html).
- OWASP Cheat Sheet — JSON Web Token for Java — claim / rotation pitfalls [https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html](https://cheatsheetseries.owasp.org/cheatsheets/JSON_Web_Token_for_Java_Cheat_Sheet.html).

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `GameKit.Core` — `IClock`, `IIdGenerator` (UUIDv7), `ICurrentPlayer`, `IGdprDeleteService`, `IPlayerDisplayNameResolver`, `GameKitOptions`, rate-limit policy constants.
- `src/GameKit.Core/Builder/GameKitBuilder.cs` — fluent builder; Auth extends via `.AddAuth(opts => ...)`.
- `src/GameKit.Core/Data/IModelBuilderExtension.cs` — Auth implements `AuthModelBuilderExtension` to register `PlayerIdentity`, `PlayerCredential`, `RefreshToken`.
- `tests/GameKit.TestFixtures/PostgresFixture.cs` — three-role Testcontainers fixture; Auth integration tests reuse it.
- `src/GameKit.Core/Builder/GameKitApplicationBuilderExtensions.cs` — `UseGameKit` auto-migrate + authorization middleware; Auth migrations apply on the same path (multi-package migration pattern D-04).

### Established Patterns
- Per-package migration assembly + `__ef_migrations_<pkg>` history table (Phase 1 D-04). Auth follows with `__ef_migrations_auth`.
- SPDX GPL-3.0-or-later header on every `.cs` (REUSE.toml covers new files automatically).
- `IDesignTimeDbContextFactory<GameKitDbContext>` — Auth doesn't need its own; uses Core's.
- Entity Ids: UUIDv7 from `IIdGenerator`, `ValueGeneratedNever` (Phase 1 D-01-03).
- Minimal APIs + FluentValidation 12 explicit `IValidator<T>` injection in handlers (STACK.md #6).
- Scrutor-based assembly scanning for pluggable strategies (`IOAuthProvider` discovered this way).

### Integration Points
- `GameKit.Cli admin create` (Phase 1 stub) — Phase 3 replaces with real impl, but Phase 2's password provider needs to coexist with a future admin password path. Share `IPasswordHasher` service.
- `GameKit.Core` `SessionParticipant.PlayerId` is nullable (GDPR). If a deleted player had Auth rows, the cascade must also null them — already covered because `player_identities.player_id` FK will use `ON DELETE CASCADE` (Auth rows have no independent meaning without the player).
- Sample `samples/TicTacToeDuel` — Phase 2 replaces its temporary `/demo/players/register` endpoint with real `/auth/register`. Updates the HTML client to send `X-GameKit-Device` header and handle JWT storage (localStorage for the sample; production apps choose their own).
- `RequireAuthorization()` on `/api/players` (Phase 1 WR-05 fix) — becomes functional once Auth registers the JwtBearer scheme and it is set as the default scheme.

</code_context>

<specifics>
## Specific Ideas

- Keep the Phase 2 netns CI test at its current Phase 1 scope (Core only). Don't widen. Auth mocks provider HTTP.
- The allow-list default must be a literal list in code (with a public constant), not resolved from config with no default — we don't want the test harness to silently pass when an operator forgets to set it.
- `X-GameKit-Device` header is case-insensitive; document it in the sample README + a future ops guide (Phase 6).
- When family revocation fires, the audit row MUST record the `reason` field so operators can grep for `refresh_fingerprint_mismatch` separately from `admin_logout_all` or `manual_logout`.
- Hard reject on Steam-link collision (D-11) is intentionally a simpler UX than spec's "link-or-switch" phrasing. If this turns out to be painful in practice, revisit in Phase 3 or add as a v2 feature — don't retrofit silently.

</specifics>

<deferred>
## Deferred Ideas

- **Argon2 password hasher** — ship as `GameKit.Auth.Argon2` sibling package using Isopoh (AUTH-V2-01 per REQUIREMENTS.md v2).
- **Account merge** (combine two distinct `player_id`s into one) — AUTH-V2-03. Would enable a real "link-or-switch" resolution to D-11's hard reject.
- **Additional OAuth providers** (Google, Apple, Epic) — AUTH-V2-02, opt-in sibling packages following the Steam/Discord pattern.
- **Email-out-of-band flows** (password reset via email, suspicious-login email, email verification) — CLAUDE.md says no outbound services; would need an `IEmailSender` interface the operator implements. Post-v1.
- **Passkey / WebAuthn support** — very attractive but a whole feature unto itself; post-v1.
- **Universal sub-minute revocation** (jti denylist in Redis) — D-04 rejected it for Phase 2. Revisit if a real operator need surfaces.
- **`/auth/identities`** listing endpoint — deferred unless the sample or admin UI actually needs it.
- **Admin auth** — Phase 3 scope; separate scheme per ROADMAP.md.

### Reviewed Todos (not folded)

None — no todos were surfaced as potentially in-scope.

</deferred>

---

*Phase: 02-authentication*
*Context gathered: 2026-04-17*
