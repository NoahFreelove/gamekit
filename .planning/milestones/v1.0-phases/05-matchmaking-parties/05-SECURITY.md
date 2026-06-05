---
phase: 05
slug: matchmaking-parties
status: complete
audited_at: 2026-05-24
auditor: gsd-security-auditor
threats_total: 42
threats_closed: 35
threats_open: 0
accepted_risks: 7
asvs_level: 2
register_authored_at_plan_time: true
created: 2026-05-24
---

# Phase 5 — Matchmaking + Parties — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.
> Verify-mitigations mode. Source: 10 PLAN files (`05-01..05-10`) + SUMMARY narratives + UAT D5/D6 enforcement work.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| Browser → `/api/parties/*` (JWT bearer) | Player can only operate on their own parties — verify via JWT `sub` / NameIdentifier claim. | Party CRUD requests / responses. |
| Browser → `/api/mm/*` (JWT bearer + rate limit) | Per-player rate limit; ticket ownership verified via party membership / solo holder. | Ticket lifecycle requests / responses. |
| Browser → `/admin/api/matchmaking/*` (cookie auth + Superadmin policy + antiforgery) | Admin pause/drain actions; cookie scheme `GameKitAdmin` pinned via `AdminPolicies.Superadmin`. Antiforgery filter chained on both POSTs (Round-2 fix). | Admin control commands. |
| Blazor Dialog → `IMatchmakingControlService` (DI, no HTTP) | UAT-2 D1 refactor: dialog invokes service directly via DI inside Blazor Server circuit; no HTTP boundary so no CSRF token applicable. Server-side superadmin re-check enforced inside `EnsureSuperadminAsync()` via `IAuthorizationService` + `IHttpContextAccessor` (Round-2 fix). | In-process call carrying ladderId + reason + actorId. |
| Matchmaker leader → Redis (Lua eval) | Stale-leader fencing-token check inside `AtomicClaimScript` is the first Lua step (Pitfall §2). | Atomic claim of tickets into proposal. |
| Long-poll handler ← Redis pub/sub | Subscription leak on client abandon (Pitfall §5) — linked CTS + always-Unsubscribe `finally`. | Status notifications for `mm:status:{ticketId}`. |
| Reconciler → Postgres (write path) | Reconciler MUST NOT mutate Redis — boundary between durable Postgres and ephemeral Redis is sacred (Pitfall §1). | Mark-expired writes to `matchmaking_tickets` + `game_sessions`. |
| Analytics drain → Postgres (write path) | Drop-on-failure is intentional (D-16). | Batched `ticket_events` inserts. |
| EF migration runner → Postgres | Advisory-lock-gated, runs as `gamekit_owner`. | Schema DDL. |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation | Status | Evidence |
|-----------|----------|-----------|-------------|------------|--------|----------|
| T-05-01-SC | Tampering | Test-project NuGet additions | mitigate | Zero new pins added in Wave 0; CPM allow-list enforced. | closed | `Directory.Packages.props` (no net-new pins for tests/GameKit.Matchmaking.*); `05-01-SUMMARY.md` self-check. |
| T-05-01-01 | Information Disclosure | Testcontainer Postgres password | accept | Random per-run; never committed. | accepted | `05-01-PLAN.md:211` (disposition: accept). |
| T-05-02-01 | Tampering | Migration writes outside matchmaking tables | mitigate | `MatchmakingMigrationModelCustomizer.ExcludeFromMigrations` enumerates 16 prior-package entities; CI test verifies. | closed | `src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs`; `MatchmakingMigrationBoundaryTests` in integration suite. |
| T-05-02-02 | Denial of Service | Concurrent migrations deadlock on multi-replica startup | mitigate | Distinct advisory-lock key `388956820L`; acquired before `Migrate()`. | closed | `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs:46`; `MatchmakingAdvisoryLockKeyTests`. |
| T-05-02-03 | Tampering | Party-code case-mismatch bypass | mitigate | `party_code` declared `citext NOT NULL UNIQUE`; lookup uses `WHERE PartyCode == code` with no `ToUpperInvariant`. | closed | `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` (citext); `src/GameKit.Matchmaking/Services/PartyService.cs:181-186`. |
| T-05-03-01 | Denial of Service | Misconfigured BracketRampSeconds=0 / AcceptTimeoutSeconds=0 | mitigate | `MatchmakingOptionsValidator` rejects via `IValidateOptions`; throws `OptionsValidationException` at host startup. | closed | `src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs:44-106`. Per-ladder invariants enforced eagerly in `GameKitMatchmakingBuilder.AddLadder`. |
| T-05-03-02 | Information Disclosure | Future ladder names colliding with admin-internal names | accept | Case-insensitive dedup catches double-registration; intentional collision with Rankings ladder names is documented JOIN behavior. | accepted | `05-03-PLAN.md:223` (disposition: accept). |
| T-05-04-01 | Tampering | Stale leader writes a proposal after lock expired | mitigate | Lua script's FIRST step is `if GET KEYS[1] ~= ARGV[1] then return 'LEASE_LOST' end`. | closed | `src/GameKit.Matchmaking/Redis/AtomicClaimScript.cs:50`. |
| T-05-04-02 | Tampering | Double-claim race (two replicas process same ticket pair) | mitigate | Lua fencing check + Redis serialization of Lua execution; `MatchmakingLeaderElectionTests` SC#4 verifies. | closed | `src/GameKit.Matchmaking/Redis/AtomicClaimScript.cs:50-55`; `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderElectionTests.cs:149`. |
| T-05-04-03 | Information Disclosure | Party-code low entropy (30^6 ≈ 7.3·10⁸) brute force enumerates active parties | mitigate | Per-IP rate limit `gamekit:mm:party_join` 5/min/IP added in Plan 05-08; codes are 6-char Crockford. | closed | `src/GameKit.Matchmaking/Http/RateLimiting/MatchmakingRateLimitRegistrations.cs:119-132`; `src/GameKit.Matchmaking/Http/PartyEndpoints.cs:51`. |
| T-05-04-04 | Tampering | Predictable party code via System.Random | mitigate | `PartyCodeGenerator` uses `RandomNumberGenerator.GetInt32` per char (CSPRNG, rejection-sampled). | closed | `src/GameKit.Matchmaking/Services/PartyCodeGenerator.cs:51`. |
| T-05-04-05 | Denial of Service | Concurrent `CreateAsync` from one player creates duplicate parties | mitigate | `IsolationLevel.Serializable` transaction + active-membership guard + Polly retry on 40001. | closed | `src/GameKit.Matchmaking/Services/PartyService.cs:105-107,282-298`. |
| T-05-04-06 | Tampering | Player dissolves a party they do not own | mitigate | `DissolveCoreAsync` verifies `party.OwnerPlayerId == actorPlayerId`; throws `PartyAuthorizationException` on mismatch. | closed | `src/GameKit.Matchmaking/Services/PartyService.cs:262-265`. |
| T-05-05-01 | Tampering | Two replicas double-match same ticket pair during lease handoff | mitigate | Lua fencing check inside `AtomicClaimScript` + `RenewLeaseAsync` bail before each pool; SC#4 leader-election test. | closed | `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs:218-225`; `AtomicClaimScript.cs:50`; `MatchmakingLeaderElectionTests.Forced_Failover_NonLeader_Acquires_After_LeaseTtl`. |
| T-05-05-02 | Denial of Service | Ticker loop exceeds budget under 1k tickets; lock expires mid-tick | mitigate | `LockTtlSeconds` (90s) >> typical tick budget; `RenewLeaseAsync` between pools; SC#3 load test verifies. | closed | `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs:218-247`; `05-10-SUMMARY.md` SC#3 numbers (MaxIterationMs=29, p99=13.83ms). |
| T-05-05-03 | Information Disclosure | Long-running ticker leaks lease on crash between RunOnceAsync and ReleaseLeaseAsync | accept | Natural TTL expiry; `Forced_Failover` test proves recovery; cost = up to LockTtlSeconds of no matching. | accepted | `05-05-PLAN.md:231`. |
| T-05-05-04 | Tampering | Reaped proposal's accepting parties re-ZADDed with INCORRECT queuedAt (D-09 violation) | mitigate | `ProposalSweeper` reads `queuedAt` from each ticket's Redis hash and re-ZADDs with that score. | closed | `src/GameKit.Matchmaking/Services/ProposalSweeper.cs:226-256` (queuedAtMs from ticket hash → SortedSetAddAsync). |
| T-05-06-01 | Spoofing | Player accepts a proposal they are not in | mitigate | `ProposalService.AcceptAsync` verifies `proposal.Tickets.Any(t => t.TicketId == ticketId)` before Lua eval. | closed | `src/GameKit.Matchmaking/Services/ProposalService.cs:141, 209`. Endpoint at `MatchmakingEndpoints.AcceptAsync` returns 403 on `NotInProposal`. |
| T-05-06-02 | Tampering | Late accept races proposal sweeper | mitigate | `CompleteLuaSource` runs SADD+SCARD atomically; if proposal already gone, HGETALL returns empty → ProposalNotFound. | closed | `src/GameKit.Matchmaking/Redis/ProposalScripts.cs:46-62`; ProposalService.cs:131-138. |
| T-05-06-03 | Tampering | Decline writes DeclineHistory but Redis re-ZADD fails | mitigate | Order in `DeclineAsync`: `RecordDeclineAsync` FIRST (durable), THEN Lua re-ZADD; reconciler catches stuck tickets within `StaleTicketThresholdMinutes`. | closed | `src/GameKit.Matchmaking/Services/ProposalService.cs:212-232`. |
| T-05-06-04 | Tampering | Player accepts after game_session already created | mitigate | Lua: `if state == 'complete' then return 'COMPLETED' end`; AcceptResult.AlreadyAccepted returned. | closed | `src/GameKit.Matchmaking/Redis/ProposalScripts.cs:47-49`; ProposalService.cs:152-154. |
| T-05-06-05 | Information Disclosure | Time-based cooldown leak via `DateTime.Now` | mitigate | `DeclineCooldownService.GetCurrentCooldownAsync` operates on caller-supplied `now`; producers use `_clock.UtcNow` (`IClock`). | closed | `src/GameKit.Matchmaking/Services/DeclineCooldownService.cs:58-83`; `src/GameKit.Matchmaking/Services/MatchmakingService.cs:125`. |
| T-05-07-01 | Tampering | Reconciler accidentally ZADDs a stale ticket back into Redis (Pitfall §1) | mitigate | Reconciler uses `ZSCORE` only (read-only) — zero ZADD/HSET/SADD/PUBLISH; integration test asserts. | closed | `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs:222`; `ReconcilerSweepTests.Reconciler_DoesNotCallRedisWrites`. |
| T-05-07-02 | Denial of Service | Analytics drain holds Postgres connection across Polly retry sleep | mitigate | `AddTimeout(30s)` on Polly pipeline; per-batch scope opens + closes connection. | closed | `src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs:111` (`AddTimeout`); per-call `using var scope = _scopeFactory.CreateScope()`. |
| T-05-07-03 | Information Disclosure | Dropped events silently lost without operator awareness | mitigate | OTel counter `matchmaking.analytics.dropped_events` with `reason=polly_exhausted` tag; XML doc on `AddMatchmaking` warns operators to `AddMeter`. | closed | `src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs:183-185`; `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs:58`. |
| T-05-07-04 | Tampering | Two replicas run retention DELETE simultaneously | mitigate | Leader-gated via `MatchmakerLeaseHelper`; reconciler returns `SkippedBecauseNotLeader` when not leader. | closed | `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs:153-160`. |
| T-05-07-05 | Information Disclosure | Channel full → events dropped silently | mitigate (partial) | Bounded channel cap 10000; producer-side logs warning when `TryWrite` returns false. **However**, the `matchmaking.analytics.dropped_events` counter is never incremented with `reason=channel_full` — the meter's XML doc claims this tag exists but no producer emits it. Note: `FullMode = BoundedChannelFullMode.DropNewest` means `TryWrite` returns true and silently drops the previous newest item; the producer-side `if (!_events.TryWrite(evt))` branches are effectively dead. Visibility comes from logs only, not OTel. | closed (with caveat) | `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Background.cs:62-64` (DropNewest); `src/GameKit.Matchmaking/Services/MatchmakingService.cs:298-303` (log only); `src/GameKit.Matchmaking/Telemetry/MatchmakingMeter.cs:52-53` (documents `reason=channel_full` but no emitter). Tracked as Risk-05-A in Accepted Risks Log. |
| T-05-08-01 | Tampering | Cross-player ticket cancellation | mitigate | `MatchmakingService.CancelAsync` reads ticket-hash partyId / playerId, verifies caller is party member or solo holder; returns 403 on mismatch. | closed | `src/GameKit.Matchmaking/Services/MatchmakingService.cs:342-360`; LongPoll ownership check at `src/GameKit.Matchmaking/Http/LongPollStatusHandler.cs:180-218`. |
| T-05-08-02 | Spoofing | Forged ProposalId in accept/decline endpoints | mitigate | `ProposalService.AcceptAsync`/`DeclineAsync` verify `ticketId ∈ proposal.Tickets`; player id extracted from JWT `sub` claim, not request body. | closed | `src/GameKit.Matchmaking/Services/ProposalService.cs:141,209`; `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs:218-224`. |
| T-05-08-03 | Denial of Service | Enqueue rate-limit bypass via burst | mitigate | `gamekit:mm:enqueue` uses `RateLimitPartition.GetSlidingWindowLimiter` (NOT FixedWindow); 5 req / 1 min / player. | closed | `src/GameKit.Matchmaking/Http/RateLimiting/MatchmakingRateLimitRegistrations.cs:103-113`; `MatchmakingRateLimitTests` SC#5. |
| T-05-08-04 | Information Disclosure | Party-code enumeration via /api/parties/join | mitigate | Per-IP `gamekit:mm:party_join` sliding window 5/min/IP; partitioned strictly by RemoteIp. | closed | `src/GameKit.Matchmaking/Http/RateLimiting/MatchmakingRateLimitRegistrations.cs:119-132`; `src/GameKit.Matchmaking/Http/PartyEndpoints.cs:51`. |
| T-05-08-05 | Elevation of Privilege | Admin pause-queue without authentication | mitigate | `MatchmakingAdminEndpoints` chains `.RequireAuthorization(SuperadminPolicy).AddEndpointFilter<AntiforgeryValidationFilter>()` on BOTH `pause-queue` and `drain-queue` POSTs. Matches Rankings precedent. Antiforgery service + middleware are pre-wired by `AddGameKitAdmin` / `UseGameKitAdmin`. | closed | `src/GameKit.Matchmaking/Http/MatchmakingAdminEndpoints.cs:56-62` (filter chain); `src/GameKit.Admin.UI/Http/EndpointFilters/AntiforgeryValidationFilter.cs:36-44`; cross-ref `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:84,108`. |
| NEW-05-A | Elevation of Privilege | `IMatchmakingControlService` invocable from DI without re-checking caller policy | mitigate | `RedisMatchmakingControlService` now injects `IAuthorizationService` + `IHttpContextAccessor` and calls `EnsureSuperadminAsync()` as the FIRST line of both `PauseAsync` and `DrainAsync` (before any Redis write or audit row). Helper throws `UnauthorizedAccessException` if `HttpContext` is null OR if the policy check on `http.User` does not succeed against `AdminPolicies.Superadmin` (`"gamekit.admin.superadmin"`). Service is registered Scoped so per-request `IHttpContextAccessor` flows correctly. | closed | `src/GameKit.Matchmaking/Services/RedisMatchmakingControlService.cs:39-72,77,97` (constructor + helper + call sites); `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Http.cs:37` (scoped registration); `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs:13` (constant); `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:139,189` (AddAuthorization + AddHttpContextAccessor in admin host). |
| T-05-08-06 | Information Disclosure | Long-poll subscription leak on client abandon | mitigate | `LongPollStatusHandler` uses `CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted, ct)` + always-`UnsubscribeAsync` in `finally`. Phase-gate test asserts subscriber count returns to baseline ≤500ms after abort. | closed | `src/GameKit.Matchmaking/Http/LongPollStatusHandler.cs:103-171`; `LongPollStatusTests.LongPoll_AbortMidPoll_UnsubscribesWithin500ms`. |
| T-05-08-07 | Tampering | `QueueDepth.razor` renders stale Postgres data instead of live Redis | mitigate | `RedisMatchmakingObservability` reads ZCARD directly from Redis; SC#6 test verifies depth survives Postgres row deletion. | closed | `src/GameKit.Matchmaking/Services/RedisMatchmakingObservability.cs`; `MatchmakingObservabilityTests.NotSourcedFromReconciliationMirrors`. |
| T-05-09-01 | Tampering | Operator wires non-Null `IChaosInterceptor` into production | mitigate | `TryAddSingleton<IChaosInterceptor, NullChaosInterceptor>` default; verbose class name; explicit XML-doc warning. | closed | `src/GameKit.Matchmaking/Services/NullChaosInterceptor.cs:21`; `src/GameKit.Matchmaking/Services/IChaosInterceptor.cs:17-25` (warning). |
| T-05-09-02 | Spoofing | Sample app guest JWT impersonation via DevTools | accept | Sample-app demo only; guest-login flow already accepted in Phase 2 UAT; no new risk introduced by Phase 5. | accepted | `05-09-PLAN.md:241`; `05-09-SUMMARY.md:275`. |
| T-05-09-03 | Information Disclosure | `matchmaking.html` exposes `/api/mm/*` surface to browser inspection | accept | All routes are JWT-authorized + documented in OpenAPI (Phase 6); no new attack surface vs auth. | accepted | `05-09-PLAN.md:242`; `05-09-SUMMARY.md:278-279`. |
| T-05-10-01 | Denial of Service | Load test exhausts CI runner resources on default `dotnet test` | mitigate | `[Trait("Category", "LoadTest")]`; CI default excludes it. | closed | `tests/GameKit.Matchmaking.LoadTests/MatchmakingLoadTests.cs:74` (`[Trait("Category", "LoadTest")]`). |
| T-05-10-02 | Information Disclosure | Load test creates 1000 player rows in production Postgres if mis-pointed | mitigate | `LoadTestFixture` uses Testcontainers only — random local ports + ephemeral containers; cannot connect to production. | closed | `tests/GameKit.Matchmaking.LoadTests/LoadTestFixture.cs` (PostgresFixture + RedisFixture wrappers — no `Environment.GetEnvironmentVariable("CONNECTION_STRING")` path). |
| T-05-10-03 | Tampering | Green load-test result masks regression because budget is too lax | accept | 50ms default budget is intentionally tight per RESEARCH §Decision 13; operators justify any relaxation in phase SUMMARY. | accepted | `05-10-PLAN.md:216`; `05-10-SUMMARY.md:384-385`. |

*Status legend: closed · open · accepted (entry exists in Accepted Risks Log).*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party).*

---

## Open Findings (BLOCKER & WARNING)

> Round-2 audit (2026-05-24): Both BLOCKERs CLOSED. See `## Security Audit 2026-05-24 — Round 2` below for verification evidence. Historical Round-1 findings retained verbatim for audit trail.

### CLOSED (Round 2, 2026-05-24) — T-05-08-05-antiforgery

- **Status:** CLOSED.
- **Round-1 finding (CSRF / Tampering sub-issue of T-05-08-05):** `MatchmakingAdminEndpoints.cs` POST chains lacked `.AddEndpointFilter<AntiforgeryValidationFilter>()`, diverging from the Rankings precedent and the Plan 05-08 trust-boundary table.
- **Round-2 verification:** Live tree confirms BOTH POSTs now chain the filter:
  - `src/GameKit.Matchmaking/Http/MatchmakingAdminEndpoints.cs:56-58` — `group.MapPost("/pause-queue", PauseQueueAsync).RequireAuthorization(SuperadminPolicy).AddEndpointFilter<AntiforgeryValidationFilter>();`
  - `src/GameKit.Matchmaking/Http/MatchmakingAdminEndpoints.cs:60-62` — `group.MapPost("/drain-queue", DrainQueueAsync).RequireAuthorization(SuperadminPolicy).AddEndpointFilter<AntiforgeryValidationFilter>();`
  - `src/GameKit.Matchmaking/Http/MatchmakingAdminEndpoints.cs:9` — `using GameKit.Admin.UI.Http.EndpointFilters;` present.
  - `GameKit.Matchmaking.csproj:26` still has `ProjectReference Include="..\GameKit.Admin.UI\GameKit.Admin.UI.csproj"` so the filter type resolves at compile time.
  - Antiforgery service registered at `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:171` (`AddAntiforgery`); middleware wired at `src/GameKit.Admin.UI/Builder/AdminApplicationBuilderExtensions.cs:32` (`app.UseAntiforgery()`). The filter therefore runs against a populated antiforgery context at request time.
- **Pattern parity confirmed:** matches `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:83-85, 107-108`.

### CLOSED (Round 2, 2026-05-24) — NEW-05-A — server-side superadmin recheck in `IMatchmakingControlService`

- **Status:** CLOSED.
- **Round-1 finding (Elevation of Privilege, defense-in-depth gap):** `RedisMatchmakingControlService` accepted `actorId Guid` as a trusted parameter with no server-side policy recheck; the Blazor dialog path could resolve the service from DI and call `PauseAsync` / `DrainAsync` without ever crossing the HTTP `RequireAuthorization` gate.
- **Round-2 verification:** Live tree confirms the recheck is now performed by the service itself, BEFORE any side effect:
  - Constructor at `src/GameKit.Matchmaking/Services/RedisMatchmakingControlService.cs:47-61` injects `IConnectionMultiplexer`, `IAdminAuditWriter`, `IAuthorizationService`, and `IHttpContextAccessor` (all four `ArgumentNullException.ThrowIfNull`-guarded).
  - Helper at lines 63-72: `EnsureSuperadminAsync()` resolves `_httpContextAccessor.HttpContext` (throwing `UnauthorizedAccessException` on null — covers the "called outside a request/circuit" misuse) and then runs `_authz.AuthorizeAsync(http.User, AdminPolicies.Superadmin)`; throws `UnauthorizedAccessException` on `result.Succeeded == false`.
  - `PauseAsync` at line 77 — `await EnsureSuperadminAsync().ConfigureAwait(false);` is the FIRST statement, BEFORE `_redis.GetDatabase()` and BEFORE `_audit.WriteAsync` (cannot leave residual Redis state or partial audit row on failure).
  - `DrainAsync` at line 97 — same pattern, FIRST statement.
  - Policy constant: `using GameKit.Admin.UI.Authorization;` at line 7; `AdminPolicies.Superadmin` resolves to the string `"gamekit.admin.superadmin"` per `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs:13` (matches the policy name used by the HTTP-layer `RequireAuthorization`).
  - DI prerequisites present: `AddAuthorization` at `src/GameKit.Admin.UI/Builder/AdminBuilderExtensions.cs:139` (registers `IAuthorizationService` as scoped via framework defaults), `AddHttpContextAccessor` at line 189.
  - Service lifetime: `services.TryAddScoped<IMatchmakingControlService, RedisMatchmakingControlService>();` at `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.Http.cs:37`. Scoped is the correct choice — matches the `IAdminAuditWriter` (scoped, per `AdminBuilderExtensions.cs:152`) lifetime and aligns with the per-request `IHttpContextAccessor` flow. A Singleton lifetime would cache the first request's HttpContext and silently break the recheck; Scoped avoids that.
- **Coverage of the bypass path:** the Blazor dialog circuit hits the same service instance; `IHttpContextAccessor.HttpContext` is non-null inside a Blazor Server interactive circuit (carries the cookie-authenticated `ClaimsPrincipal`), so `EnsureSuperadminAsync` reaches the policy check rather than the null-check branch. A regular admin (not superadmin) invoking `PauseAsync` / `DrainAsync` either via HTTP loopback OR via DI from any `[Authorize(Policy = AdminPolicies.Admin)]` page is now rejected by the service before any state change.

### WARNING — unregistered_flag — `IMatchmakingControlService.PauseAsync` body persistence (Tampering)

- The Redis pause flag (`mm:control:paused:{ladderId}`) is written via `StringSetAsync` with **no TTL**. There is no auto-expiry, no unpause verb (call-out already in UAT-2 §follow_up.1), so a transient operator session that pauses a ladder and crashes leaves the queue paused indefinitely without admin oversight. Not in any threat-model row. **Recommend Plan 05-11 / v1.1 backlog item** (matches UAT follow-up): add unpause/undrain verbs OR a default TTL (e.g. 24h with renewal on touch). Not a phase blocker, but track explicitly so it does not vanish into ambient debt.

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| Risk-05-1 | T-05-01-01 | Testcontainer Postgres password is random per run, never committed, lives only in fixture state. No production exposure path. | Plan author (05-01) | 2026-05-17 |
| Risk-05-2 | T-05-03-02 | Ladder-name collision with Rankings ladder names is the intended JOIN behavior; case-insensitive dedup catches accidental double-registration. | Plan author (05-03) | 2026-05-17 |
| Risk-05-3 | T-05-05-03 | Up to `LockTtlSeconds` (default 90s) of no matching following an ungraceful matcher crash. Natural TTL expiry restores leadership; `Forced_Failover` test pins the recovery contract. Acceptable for a self-hosted backend. | Plan author (05-05) | 2026-05-17 |
| Risk-05-4 | T-05-09-02 | Sample app's anonymous-guest JWT exposes the matchmaking flow to "impersonation" via DevTools — but the guest-login flow was Phase 2's UAT and accepts anonymous players. No new risk introduced in Phase 5. | Plan author (05-09) | 2026-05-19 |
| Risk-05-5 | T-05-09-03 | `matchmaking.html` reveals the `/api/mm/*` surface to browser inspection — but every route is JWT-authorized and will be documented in OpenAPI (Phase 6). No surface beyond existing auth. | Plan author (05-09) | 2026-05-19 |
| Risk-05-6 | T-05-10-03 | The 50 ms per-iteration load-test budget is intentionally tight per RESEARCH §Decision 13. Operators relaxing the budget must justify it in the phase SUMMARY. The framework prefers a flaky-tight gate over a permissive one. | Plan author (05-10) | 2026-05-20 |
| Risk-05-A | T-05-07-05 (caveat) | The `matchmaking.analytics.dropped_events` counter is documented to emit a `reason=channel_full` tag but no producer actually increments it (only `reason=polly_exhausted` from the drain). Operators get visibility via log warnings ("dropped Queued event for ticket ...") rather than OTel. Logged channel-full events are still discoverable but require log aggregation. Defer wiring the producer-side counter to v1.1 once production drop rates motivate the work. Also note: `BoundedChannelFullMode = DropNewest` semantics mean `TryWrite` returns true on full and silently drops the previous newest item — the producer `if (!TryWrite)` branches are effectively dead code under DropNewest. Channel-full visibility today = ONLY logs; not OTel. | Auditor noted gap 2026-05-24 | 2026-05-24 |

*Accepted risks do not resurface in future audit runs unless the implementation changes.*

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Accepted | Run By |
|------------|---------------|--------|------|----------|--------|
| 2026-05-24 (Round 1) | 40 | 33 | 2 | 7 | gsd-security-auditor (Opus 4.7 1M) |
| 2026-05-24 (Round 2) | 42 | 35 | 0 | 7 | gsd-security-auditor (Opus 4.7 1M) |

### Round-1 audit summary (historical)

Verified 30 mitigated + 7 accepted threats against the live tree at HEAD (post commits 95da329 + 29b7bfe + af2bb19). Two BLOCKER findings surfaced:

1. **T-05-08-05-antiforgery** — `MatchmakingAdminEndpoints` is missing `AddEndpointFilter<AntiforgeryValidationFilter>()` on `pause-queue` and `drain-queue` POSTs. Divergence from the documented Rankings precedent. Phase trust-boundary table required antiforgery.
2. **NEW-05-A** — `IMatchmakingControlService` performs no server-side superadmin role check. Both the HTTP endpoint and the Blazor dialog rely on caller authorization; the dialog path can be reached from any `[Authorize(Admin)]` page in the admin SPA via the service registration. Defense-in-depth gap.

One WARNING: pause/drain Redis flags persist without TTL and have no unpause verb (already in UAT-2 follow-ups; surfaced again here for the security audit trail).

---

## Security Audit 2026-05-24 — Round 2

**Scope:** verify-mitigations re-audit against the uncommitted working tree after the operator applied fixes for both Round-1 BLOCKERs. Implementation files were read-only (no edits to `.cs` or `.razor`); only this `05-SECURITY.md` artifact was updated.

**Threat verification table (changes vs Round 1 only):**

| Threat ID | Disposition | Round-1 Status | Round-2 Status | Evidence |
|-----------|-------------|----------------|----------------|----------|
| T-05-08-05 | mitigate | open | closed | `src/GameKit.Matchmaking/Http/MatchmakingAdminEndpoints.cs:56-62` — antiforgery filter chained on both POSTs; using directive at line 9; antiforgery service+middleware live at `AdminBuilderExtensions.cs:171` + `AdminApplicationBuilderExtensions.cs:32`. |
| NEW-05-A | mitigate | open (Open Findings only) | closed (promoted to register row) | `src/GameKit.Matchmaking/Services/RedisMatchmakingControlService.cs:47-72,77,97` — `IAuthorizationService` + `IHttpContextAccessor` injected; `EnsureSuperadminAsync` called as the FIRST line of both PauseAsync and DrainAsync; `AdminPolicies.Superadmin` resolves to `"gamekit.admin.superadmin"`. Scoped registration confirmed at `MatchmakingBuilderExtensions.Http.cs:37`. |

**Adjacent surface sweep (lightly re-checked at auditor's discretion):**

- `IMatchmakingControlService` registered as **Scoped** (`MatchmakingBuilderExtensions.Http.cs:37`). Aligns with the per-request `IHttpContextAccessor` flow and the scoped `IAdminAuditWriter` (`AdminBuilderExtensions.cs:152`). A Singleton lifetime would have silently cached the first request's HttpContext and broken the new policy recheck after the first call — explicitly verified safe. ✓
- The fix uses `IAuthorizationService.AuthorizeAsync(http.User, policyName)` rather than `User.IsInRole(...)`. This is the stronger API: it evaluates the full policy (which is wired to the `GameKitAdmin` cookie scheme via `AddAuthorization` at `AdminBuilderExtensions.cs:139`), so a forged role claim presented under a non-admin auth scheme would not satisfy it. ✓
- The new `EnsureSuperadminAsync` throws on `HttpContext == null`. This is the correct fail-closed posture for the "called outside any request / circuit" misuse (e.g. a hosted service trying to invoke the control surface) — the service is unambiguously HTTP/circuit-bound. ✓
- Reason-string handling unchanged (`safeReason = string.IsNullOrWhiteSpace(reason) ? "(no reason)" : reason;`) — still trusts the caller-supplied reason. No new threat introduced; pre-existing UAT WARNING about pause-flag TTL / unpause verb still outstanding (tracked in UAT-2 follow-ups, NOT a blocker).
- No new `MapPost` / `MapPut` / `MapDelete` chains appeared in `MatchmakingAdminEndpoints.cs` that would need a matching antiforgery filter — only pause/drain (the two expected verbs).

**Round-2 result:** **SECURED.** All declared mitigations present in the runtime path. `threats_open: 0`; frontmatter flipped to `status: complete`.

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log (7 entries)
- [x] `threats_open: 0` — both Round-1 BLOCKERs closed
- [x] `status: complete` set in frontmatter

**Approval:** approved 2026-05-24 (Round 2). Phase 5 cleared for close-out from the security gate.
