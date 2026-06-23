# Phase 18: Security Audit - Context

**Gathered:** 2026-06-23
**Status:** Ready for planning
**Mode:** Auto-generated (discuss skipped via workflow.skip_discuss)

<domain>
## Phase Boundary

Every auth/admin/GDPR/egress/rate-limit security invariant is verified by an automated test and a CI gate; known CVEs are impossible to merge undetected; the full threat model is traceable from requirement to implementation to test.

**Requirements:** SEC-01..SEC-08
**Depends on:** Phase 16, Phase 17 (audit runs against completed code)
**UI hint:** no — test + CI + docs phase. Plan with `--skip-ui`.

</domain>

<decisions>
## Implementation Decisions

### Claude's Discretion
All implementation choices at Claude's discretion (discuss skipped).

### Requirements (authoritative text)
- **SEC-01** JWT threat tests: reject `alg:none`/algorithm-downgrade, wrong audience/issuer, expired tokens, exchange of a revoked refresh token.
- **SEC-02** Admin endpoint auth audit: route-enumeration test asserts every `/admin/*` route requires the `GameKitAdmin` cookie scheme and NO admin route is reachable by a player JWT (401/403).
- **SEC-03** Rate-limit audit: enumeration test asserts every public auth write endpoint (login, register, refresh, …) has an enforced rate-limit policy.
- **SEC-04** GDPR delete completeness: `DeletePlayerAsync` reaches ALL FK tables incl. v2.0 additions (lobby_members, party_members, matchmaking_tickets, regional-pool refs, account-merge tombstones); test seeds a player across every table and asserts zero residual rows post-delete.
- **SEC-05** Egress audit: static check + integration test assert no package makes outbound HTTP beyond configured OAuth provider hosts (preserves air-gap).
- **SEC-06** Security-invariant regression: refresh tokens never stored raw (SHA-256 invariant) + state-changing admin API calls without a valid antiforgery token return 400 (CSRF gate).
- **SEC-07** Dependency/CVE CI gate: `NuGetAuditMode=all` (built into .NET 10 SDK) fails the build on high/critical CVEs in GameKit's OWN dependency graph (scoped to GameKit, not consumer).
- **SEC-08** `docs/security-checklist.md` mapping threat model → implementation → test for auth/admin/rate-limit/egress/GDPR.

</decisions>

<code_context>
## Existing Code Insights

- **GDPR (SEC-04):** `src/GameKit.Core/Services/GdprDeleteService.cs` + `IGdprDeleteService.cs` own `DeletePlayerAsync`. The completeness test must seed a player across EVERY FK table — including the v2.0 additions: `lobby_members`, `party_members`, `matchmaking_tickets`, regional-pool refs, account-merge tombstones — and assert zero residual rows after delete. If `DeletePlayerAsync` is found to MISS any table, fixing it (adding the missing cascade/delete) is in-scope.
- **JWT (SEC-01):** validation lives in `GameKit.Auth` (TokenValidationParameters / JwtBearer config). Tests forge `alg:none`, wrong aud/iss, expired, and revoked-refresh-exchange.
- **Admin auth (SEC-02/06):** `GameKit.Admin.UI` — `GameKitAdminOptions` (MountPath), the `GameKitAdmin` cookie scheme, `AdminPolicies`. Route-enumeration test walks the EndpointDataSource for `/admin/*` and asserts each requires the admin cookie (player JWT → 401/403). CSRF: antiforgery on state-changing admin calls.
- **Rate limit (SEC-03):** Presence already wires a RateLimiter (`PresenceEndpoints.cs`). Auth write endpoints (login/register/refresh) must each have an enforced policy; the enumeration test asserts coverage. If any endpoint lacks a policy, adding it is in-scope.
- **Refresh-token hashing (SEC-06):** CLAUDE.md invariant — never store raw refresh tokens, always SHA-256. Test asserts the stored value is a hash, never the raw issued token.

### CRUX — SEC-07 vs the pre-existing MessagePack advisory
The repo has a KNOWN pre-existing **MessagePack NU1903** (HIGH severity) advisory — we have been building affected packages (Lobby/Matchmaking/Cli/Admin) with `-p:NuGetAudit=false` to suppress it. **SEC-07 requires turning the CVE gate ON** (`NuGetAuditMode=all`, fail on high/critical). Enabling it will FAIL the build on MessagePack NU1903. This phase MUST resolve the tension by EITHER:
  (a) **Upgrade MessagePack** to a non-vulnerable version (preferred — actually removes the CVE), OR
  (b) Formally suppress ONLY that advisory via `<NuGetAuditSuppress Include="NU1903"/>` (or the specific advisory URL) WITH a documented rationale in the security checklist (acceptable only if no fixed version is compatible with net10.0).
Investigate which MessagePack package pulls it in (likely a transitive dep of a SignalR/serialization path), whether a patched version exists for net10.0, and choose (a) if possible. After this phase, the goal is that the full solution builds CLEAN with the CVE gate ON — meaning the `-p:NuGetAudit=false` workaround should no longer be needed (or only a narrowly-scoped, documented suppression remains).

</code_context>

<specifics>
## Specific Ideas

- Tests use xUnit + Testcontainers (real Postgres for GDPR completeness; WebApplicationFactory/TestServer for route-enum + JWT + CSRF). Docker available.
- SEC-05 egress: a static analyzer/test could scan for `HttpClient`/`new Uri(...)` outbound calls and assert only OAuth provider hosts (Steam/Discord/Apple) are reachable; or a DelegatingHandler that fails on non-allowlisted hosts in tests. The grep CI check asserts no SaaS OTLP endpoint string in samples/ or src/.
- SEC-07 CVE gate is "the first task" per ROADMAP criterion #1 — wire `NuGetAuditMode=all` in Directory.Build.props/CI, then resolve MessagePack so it passes.
- Once SEC-07 resolves MessagePack, prior phases' `-p:NuGetAudit=false` guidance becomes obsolete — note this for the verifier (full-suite builds should then be clean without the flag, modulo any documented narrow suppression).

</specifics>

<deferred>
## Deferred Ideas

None — discuss phase skipped.

</deferred>
