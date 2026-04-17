---
phase: 02
phase_name: Authentication
logged: 2026-04-17
purpose: audit-trail
---

# Phase 2: Authentication — Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in `02-CONTEXT.md`; this log preserves the alternatives considered.

**Date:** 2026-04-17
**Phase:** 02-authentication
**Areas discussed:** JWT shape + lifetimes, Client fingerprint, Egress allow-list, Guest upgrade + identity conflicts

---

## JWT shape + lifetimes

### Q1: Access token lifetime

| Option | Description | Selected |
|--------|-------------|----------|
| 15 minutes (recommended) | Industry-standard sweet spot | |
| 5 minutes | Aggressive; strong revocation; more refresh traffic | |
| 60 minutes | Mobile-friendly; leaked token usable up to 1h | |
| Customizable (user free-text) | User asked for configurability | ✓ |

**User's choice:** "Can it be customizable?" → Made configurable via `JwtOptions.AccessTokenLifetime`, default 15 min.

### Q2: Refresh token lifetime

| Option | Description | Selected |
|--------|-------------|----------|
| 30 days (recommended) | Consumer-app standard | ✓ (as default) |
| 7 days | Forces weekly re-auth | |
| 90 days | Console/launcher-style long-lived | |
| Customizable (user free-text) | User asked for configurability | ✓ |

**User's choice:** "Customizable — default 30". Configurable via `JwtOptions.RefreshTokenLifetime`, default 30 days.

### Q3: Optional claims (multi-select)

| Option | Description | Selected |
|--------|-------------|----------|
| is_guest (recommended) | Bool; no-DB-lookup guest check | ✓ |
| provider (recommended) | steam \| discord \| guest \| password | ✓ |
| sid (session id, recommended) | Ties access token to refresh family | ✓ |
| roles / admin flag | Mixes Phase 3 admin concerns | |

**User's choice:** is_guest + provider + sid. Admin claim explicitly excluded.

### Q4: Revocation strategy

| Option | Description | Selected |
|--------|-------------|----------|
| Stateless only (recommended) | Access self-expires; refresh revocation only | ✓ |
| Stateless + jti denylist in Redis | Per-request Redis read | |
| Always check sid-alive in Redis | Defeats stateless JWT | |

**User's choice:** Stateless only.

---

## Client fingerprint

### Q1: Fingerprint source

| Option | Description | Selected |
|--------|-------------|----------|
| SHA-256(IP + User-Agent) | Server-derived; false positives on Wi-Fi→cell | |
| Client-supplied device id header (recommended) | X-GameKit-Device uuid; stable, privacy-respecting | ✓ |
| SHA-256(User-Agent) only | Low entropy; weakens reuse detection | |
| SHA-256(IP /24 + User-Agent) | Middle ground; still breaks on cell transitions | |

**User's choice:** Client-supplied `X-GameKit-Device` header.

### Q2: Fingerprint mismatch behavior

| Option | Description | Selected |
|--------|-------------|----------|
| Revoke entire refresh family (recommended) | Strict AUTH-11/12; forces re-auth on both devices | ✓ |
| Revoke only colliding child | Weaker signal | |
| Challenge both (requires email/OTP) | No outbound services per CLAUDE.md | |

**User's choice:** Revoke entire family.

---

## Egress allow-list

### Q1: Gating mechanism

| Option | Description | Selected |
|--------|-------------|----------|
| Named HttpClientFactory + allow-list config (recommended) | gamekit.auth.provider.steam/discord named clients + host allow-list | ✓ |
| Marker attribute + reflection scan | Clever; fragile | |
| Per-provider SocketsHttpHandler + ConnectCallback | Strongest runtime; brittle to provider infra changes | |
| Drop the guard for Auth | Weakest guarantee | |

**User's choice:** Named HttpClient + allow-list config.

### Q2: CI test scope

| Option | Description | Selected |
|--------|-------------|----------|
| Core-only in netns + Auth integration tests mocked (recommended) | Keep Phase 1 netns scope; WireMock provider mocks | ✓ |
| Full netns + allow-listed hosts in /etc/hosts | More faithful; more moving parts | |
| No netns in Phase 2 | Not recommended given spec's egress emphasis | |

**User's choice:** Core-only netns, Auth uses mocked providers.

---

## Guest upgrade + identity conflicts

### Q1: Guest links to an identity already attached to another player

| Option | Description | Selected |
|--------|-------------|----------|
| Return 409 link-or-switch challenge (recommended) | Surfaces decision to client; preserves AUTH-14 | |
| Hard reject with 409 — "identity already linked" | Simpler; client must log out G and /auth/login P₂ | ✓ |
| Automatic switch to P, abandon G | Violates "explicit user choice" | |

**User's choice:** Hard reject 409. (Simpler than recommended; accepted with documented deferral of link-or-switch UX to Phase 3 or v2.)

### Q2: Password-register with a guest token

| Option | Description | Selected |
|--------|-------------|----------|
| Upgrade G in place (recommended) | Single SERIALIZABLE tx; mobile-game convention | ✓ |
| Dedicated /auth/upgrade/password endpoint | Doubles password endpoints | |
| Prompt client with 409 link-or-switch | Over-strict for self-owned credentials | |

**User's choice:** Upgrade G in place.

### Q3: is_guest state location

| Option | Description | Selected |
|--------|-------------|----------|
| Computed property: no identities AND no credentials (recommended) | No drift; no extra column | ✓ |
| Stored is_guest column on players | Faster JWT issuance; discipline required | |
| Derived via player_flags table | Over-engineered | |

**User's choice:** Computed property.

---

## Wrap-up

### Q: More areas or write context?

| Option | Description | Selected |
|--------|-------------|----------|
| Ready for context (recommended) | Proceed to CONTEXT.md | ✓ |
| Explore more gray areas | Endpoint surface, Discord scopes, username policy, rate-limit values, challenge/callback handshake | |

**User's choice:** Ready for context. All remaining items captured as Claude's Discretion in CONTEXT.md.

---

## Claude's Discretion (deferred to planner/researcher)

- Endpoint surface beyond the minimum (`/auth/me`, `/auth/identities`, `/auth/logout/all`)
- Discord scopes (locked to `identify` per AUTH-07 + STACK.md)
- Username policy (3–32 chars, `[a-zA-Z0-9_-]`, case-insensitive; operator override via `GameKitAuthOptions.Validation`)
- Per-endpoint rate-limit values (use Phase 1 `GameKitRateLimitPolicies`)
- SPA vs server-rendered challenge/callback handshake (standard 302 + JWT-in-body)
- WireMock.Net vs TestServer for provider mocks
- Migration timestamp + history table name (`__ef_migrations_auth`, follow Phase 1 convention)
- BCrypt work factor (12 default)

## Deferred Ideas (see CONTEXT.md § deferred)

- Argon2 sibling package (AUTH-V2-01)
- Account merge (AUTH-V2-03)
- Additional OAuth providers (AUTH-V2-02)
- Email-out-of-band flows
- Passkey / WebAuthn
- Universal sub-minute revocation (Redis jti denylist)
- `/auth/identities` listing endpoint
