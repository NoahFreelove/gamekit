# GameKit.Admin.UI — Concepts

## What It Does

`GameKit.Admin.UI` provides a Blazor Server admin console and a supporting HTTP API for
operating a GameKit backend in production. It gives operators a player management panel
(search, ban/unban, rank-adjust, GDPR delete), matchmaking controls (pause/drain ladders),
a health dashboard (Postgres + Redis probes), and an admin-user CRUD (create/delete admins).
All admin operations are protected by cookie authentication (`"GameKitAdmin"` scheme) and
write audit rows to `admin_audit_log`.

## Coupling with GameKit.Auth

`GameKit.Admin.UI` has a **documented dependency on `GameKit.Auth`** — it cannot be installed
without Auth. The Admin UI uses `IPasswordHasher` from Auth for admin credential verification
and integrates with the Auth audit-writer pattern. This is a deliberate v1 design decision;
revisit only if a consumer credibly needs admin ops on a Core-only install.

## Health Delegation (HLTH-06)

Starting with Phase 14, `IHealthProbeService` in the Admin UI **delegates to Core's health
checks** (`IHealthCheckService`) for Postgres and Redis probes rather than running its own
queries. The health panel and the `/health/ready` endpoint are consistent — they reflect the
same probe results. Operators who see a red health panel should also see `/health/ready`
returning unhealthy.

## Key Public Interfaces

### `IAdminAuthService`

Admin-side password verification with timing-parity protection (constant-time dummy-hash on
user-not-found to prevent username enumeration via timing). Returns the admin's id and role
on success, `null` on any failure.

### `IPlayerBanService`

Ban and unban operations, each running inside a SERIALIZABLE transaction that commits the
mutation and the audit row together. Ban reasons are 3–512 characters (enforced by
FluentValidation upstream).

### `IAdminUserService`

Superadmin-only CRUD for admin accounts. Blocks deletion of the last remaining superadmin
(`LastSuperadminException`). Username collisions surface as
`AdminUsernameAlreadyTakenException`.

### `IPlayerSearchService`

Unified player search — auto-classifies the query string into UUID lookup, `provider:id`
identity lookup, or case-insensitive display-name prefix search with keyset pagination.
Replace this interface to route search to a custom index (e.g. Elasticsearch):

```csharp
services.AddScoped<IPlayerSearchService, MyElasticsearchPlayerSearch>();
// Register before AddGameKitAdmin() — uses TryAddScoped
```

### `IHealthProbeService`

Three-probe health check: Postgres connectivity (`SELECT 1`), Redis connectivity
(`PING`), and recent error count (via `IRedisErrorRateCounter`). Delegates to Core's
`IHealthCheckService` for unified probe results (HLTH-06). The admin panel reads from
this interface; the `/health/ready` endpoint is the canonical machine-readable equivalent.

### `IAdminAuditWriter`

Writes rows to `admin_audit_log` with before/after JSONB snapshots and a free-text reason.
Scoped lifetime — writes ride the caller's transaction, so a surrounding rollback also rolls
back the audit row. Replace to route audit rows to a custom store:

```csharp
services.AddScoped<IAdminAuditWriter, MyAuditWriter>();
```

### `IRedisErrorRateCounter`

Cross-replica error-rate counter — increments a Redis sliding-window counter so the health
panel shows aggregate error rates across all replicas, not just the connected instance. Fire-
and-forget write contract (must not throw). Returns `-1` when Redis is unavailable so the
health probe falls back to the in-process ring buffer.

## Wire-Up

```csharp
gk.AddGameKitAdmin(admin =>
{
    admin.MountPath          = "/admin";
    admin.DefaultSuperadmin  = new AdminBootstrapOptions
    {
        Username = config["Admin:Username"]!,
        Password = config["Admin:Password"]!,
    };
});

// In the pipeline:
app.MapStaticAssets();                 // Required for Blazor static assets
app.MapGameKitAdmin("/admin");         // /admin/* Blazor console + /admin/api/* HTTP surface
```

## Library-vs-Consumer Responsibility Line

| GameKit.Admin.UI owns | Consumer owns |
|-----------------------|---------------|
| Blazor admin console + admin HTTP API | Secrets for the bootstrap superadmin account |
| Player ban/unban + audit trail | Ban policy (when to ban, reason enforcement is UI-side) |
| Admin-user CRUD (superadmin-only) | Admin user provisioning process |
| Health panel (delegates to Core probes) | None — probe results from `AddGameKitHealthChecks()` |
| Audit row schema (`admin_audit_log`) | Audit record retention + reporting (`IAdminAuditWriter`) |
| Player search (default: display-name prefix) | Custom search index (`IPlayerSearchService`) |

## See Also

- [auth.md](auth.md) — Auth coupling (required) and `IPasswordHasher`.
- [core.md](core.md) — `AddGameKitHealthChecks()` and `MapGameKitHealth()`.
- [API reference](../api/GameKit.Admin.UI.yml) — full member-level docs.
- [docs/security-checklist.md](../security-checklist.md) — admin hardening checklist.
- [docs/ops/postgres-roles.md](../ops/postgres-roles.md) — database role separation.
