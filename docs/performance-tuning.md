# GameKit Performance Tuning Guide

> **PERF-05** — Operator guidance for the three highest-impact tuning levers in a
> self-hosted GameKit deployment: password-hashing cost factors, Npgsql connection-pool
> sizing, and the top-5 hot queries.
>
> **Source of truth for measured latency values:** All mean values cited in this document
> are transcribed from `benchmarks/BASELINES.md`, which in
> turn reflects the committed BenchmarkDotNet baseline captured on the dev machine
> (11th Gen Intel i7-11700K, Ubuntu 26.04, .NET 10.0.9, BDN 0.15.8). Table figures below
> are rules-of-thumb; BASELINES.md carries the machine-measured truth. Re-capture baselines
> when upgrading the server hardware class or .NET runtime.

---

## Table of Contents

1. [BCrypt Cost-Factor vs Latency](#1-bcrypt-cost-factor-vs-latency)
2. [Argon2id Cost-Factor vs Latency](#2-argon2id-cost-factor-vs-latency)
3. [Npgsql Connection-Pool Sizing](#3-npgsql-connection-pool-sizing)
4. [Top-5 Hot Queries](#4-top-5-hot-queries)
5. [Tool Licensing Posture](#5-tool-licensing-posture)

---

## 1. BCrypt Cost-Factor vs Latency

`GameKit.Auth` ships `BCryptPasswordHasher` backed by `BCrypt.Net-Next` (4.0.3).
The cost ("work factor") is set via `GameKitAuthOptions.Password.BCryptWorkFactor`
(default: `12`).

BCrypt is inherently serial and single-threaded. Each step up in work factor doubles the
hash time, which directly multiplies login-endpoint latency for concurrent users.

### Cost-Factor Table

| Work Factor | Approx. Hash Time | Notes |
|-------------|-------------------|-------|
| 10 | ~25 ms | Development only — too fast for production. Enables `AllowInsecureParametersForTesting`. |
| 11 | ~50 ms | Borderline — OWASP 2025 recommends ≥ 100 ms. |
| **12** (default) | **~100 ms** | **Production default. Meets OWASP 2025 target.** |
| 13 | ~200 ms | High-security deployments with very low login concurrency (< 5/s per core). |
| 14 | ~400 ms | Not recommended unless login concurrency is single digits per second. |

> **Measured baseline (BASELINES.md):** `BCryptVerify` at work factor 12 = **202.5 ms**
> on the i7-11700K dev machine. The ~100 ms figure above is the OWASP design target; your
> hardware may be slower (ARM server) or faster (dedicated bare-metal x86). Run
> `tests/GameKit.LoadTests` on your target hardware and update BASELINES.md before
> choosing a production work factor.

### Concurrency Impact Formula

At work factor 12 (~100-200 ms per verify), a single CPU core can process roughly
5-10 login verifications per second. For N concurrent logins per second you need
approximately `ceil(N / 5)` CPU cores dedicated to login traffic at wf=12 (more
cores at higher work factors). Plan your rate-limit policy (`GameKitRateLimitOptions.AuthLogin`)
accordingly.

### Configuration Example

```csharp
builder.Services
    .AddGameKit(o => o.ConnectionString = "...")
    .AddAuth(o =>
    {
        // Work factor 12 is the default; set explicitly for clarity.
        o.Password.BCryptWorkFactor = 12;
    });
```

---

## 2. Argon2id Cost-Factor vs Latency

`GameKit.Auth.Argon2` (optional sibling package) provides `Argon2idPasswordHasher`
backed by `Isopoh.Cryptography.Argon2` (2.0.0 — fully managed, no native bindings).
Parameters are set via `GameKitArgon2Options` (defaults: m=65536 KiB, t=3, p=1).

Argon2id has three independent axes:
- **m** — memory cost in KiB (scales linearly with RAM use)
- **t** — iteration count (scales linearly with CPU time at fixed m)
- **p** — parallelism / lanes (exploits multiple CPU threads)

### Cost-Factor Table

| m (KiB) | t (iterations) | p (lanes) | Approx. Hash Time | Security Level |
|---------|-----------------|-----------|-------------------|----------------|
| 19 456 | 2 | 1 | ~40 ms | OWASP 2025 minimum |
| **65 536** | **3** | **1** | **~100 ms** | **Default (recommended — meets OWASP 2025)** |
| 65 536 | 3 | 4 | ~30 ms | Multi-core server; uses 4 threads per hash |
| 131 072 | 3 | 1 | ~200 ms | Enhanced security; requires > 128 MiB RAM per concurrent hash |
| 131 072 | 4 | 2 | ~400 ms | Maximum hardening; suitable only for extremely low login rates |

> **Measured baseline (BASELINES.md):** `Argon2idVerify` at m=65 536 / t=3 / p=1 = **237.7 ms**
> on the i7-11700K dev machine. The ~100 ms figure is the OWASP design target; actual
> timing is hardware-dependent. Measure on your target server before shipping.

### Parallelism Trade-Off

Increasing `p` (parallelism) uses multiple CPU threads per hash. On a server handling
concurrent logins this can conflict with ASP.NET Core's thread pool. Prefer increasing
`m` (memory) over `p` (threads) when strengthening security without adding CPU
thread contention.

### Configuration Example

```csharp
// Install GameKit.Auth.Argon2 NuGet package in addition to GameKit.Auth.
builder.Services
    .AddGameKit(o => o.ConnectionString = "...")
    .AddAuth(o => { /* BCrypt options ignored when Argon2 hasher is registered */ })
    .AddArgon2PasswordHasher(o =>
    {
        o.MemoryCostKiB = 65536;   // 64 MiB
        o.TimeCost = 3;
        o.Parallelism = 1;
    });
```

---

## 3. Npgsql Connection-Pool Sizing

Npgsql maintains a connection pool per unique connection string. Pool exhaustion
(waiting for a free connection) adds latency directly to every database-backed request.

### Key Numbers

| Setting | Default | GameKit Recommendation (Matchmaking Path) |
|---------|---------|-------------------------------------------|
| `MaxPoolSize` | 100 | **25** (see rationale below) |
| `MinPoolSize` | 0 | 2-5 for warm pools in production |
| `ConnectionLifetime` | 0 (unlimited) | 300 s (rotates stale connections) |

### Why 25 for the Matchmaking Path

The `Maximum Pool Size=25` recommendation originates from the Phase 5 load-test
fixture (`tests/GameKit.Matchmaking.LoadTests/LoadTestFixture.cs`, Pitfall §8 mitigation):

- The matchmaking ticker is **Redis-only** (zero Postgres connections during tick scans).
- The drain service holds **at most one connection per batch sweep**.
- The reconciler holds **at most one connection per reconcile pass**.
- The retention sweeper holds **at most one connection per nightly pass**.
- The test driver's ad-hoc seed/poll connections use ~4-5 connections concurrently.

Sum: ~7 connections in steady state, leaving 18 connections of headroom in a 25-cap pool.
A pool of 100 wastes memory on the Postgres side (each idle connection consumes ~8 MB on
the server) and inflates `pg_stat_activity` noise.

For **non-matchmaking packages** (Auth, Lobby, Rankings) that serve high-concurrency HTTP
traffic, the appropriate pool size depends on your request concurrency:

```
MaxPoolSize = ceil(peak_concurrent_requests × avg_connection_hold_ms / avg_request_ms) + safety_margin
```

**Example:** 200 peak concurrent requests, 5 ms average Postgres connection hold time,
50 ms average total request time, 10 connections safety margin:

```
MaxPoolSize = ceil(200 × 5 / 50) + 10 = 20 + 10 = 30
```

Increase `safety_margin` if you observe pool-wait events (see monitoring note below).

### Setting the Pool Size

```json
{
  "ConnectionStrings": {
    "GameKit": "Host=db;Database=gamekit;Username=gamekit_app;Password=...;Maximum Pool Size=25"
  }
}
```

Or programmatically via `NpgsqlConnectionStringBuilder`:

```csharp
var csb = new NpgsqlConnectionStringBuilder(rawConnectionString)
{
    MaxPoolSize = 25,
    MinPoolSize = 2,
    ConnectionLifetime = 300,
};
```

### Monitoring Pool Exhaustion

Npgsql emits pool statistics via its built-in `EventSource` named `Npgsql`.
If you have OpenTelemetry configured, the `npgsql.pool.available_idle_connections` metric
(available via `OpenTelemetry.Instrumentation.Npgsql` or the Npgsql EventSource) drops
to zero when the pool is exhausted and requests begin queuing.

**Alert threshold:** if `npgsql.pool.available_idle_connections` hits 0 for more than
5 consecutive seconds under normal load, increase `MaxPoolSize` by 25% and re-measure.

---

## 4. Top-5 Hot Queries

The matchmaking subsystem drives the majority of Postgres write traffic. The following
five queries account for most of the database load during a burst; each should be covered
by the stated index to avoid sequential scans.

### 1. Matchmaking Ticker Scan

```sql
SELECT * FROM gamekit.matchmaking_tickets WHERE "Status" = 0
```

**When:** Every ~500 ms (the ticker fires on a background `IHostedService`).
**Risk:** Full table scan if the index is missing. Under a 500-ticket burst this can
take 10-50 ms per scan and starve other queries.
**Recommended index:**

```sql
CREATE INDEX CONCURRENTLY idx_matchmaking_tickets_status_created
  ON gamekit.matchmaking_tickets ("Status", "CreatedAt")
  WHERE "Status" = 0;
```

A partial index (`WHERE "Status" = 0`) is especially effective because terminal tickets
(matched, cancelled, expired) are excluded from the scan entirely.

### 2. Rank Lookup Per Matchmaking Candidate

```sql
SELECT * FROM gamekit.player_ratings
  WHERE "PlayerId" = $1 AND "LadderId" = $2
```

**When:** For every candidate ticket evaluated during a ticker scan pass.
**Risk:** N+1 problem if the ORM loads candidates without eager-loading their ratings.
**Recommended index:**

```sql
CREATE UNIQUE INDEX CONCURRENTLY idx_player_ratings_player_ladder
  ON gamekit.player_ratings ("PlayerId", "LadderId");
```

This also enforces the one-rating-per-(player, ladder) constraint at the database level.

### 3. Idempotent Ticket Enqueue

```sql
INSERT INTO gamekit.matchmaking_tickets (...)
  ON CONFLICT ("PlayerId", "LadderId") WHERE "Status" = 0 DO NOTHING
```

**When:** `POST /api/mm/queue` — every enqueue attempt.
**Risk:** Without the conflict target index, Postgres falls back to a full scan to detect
conflicts, making 500-VU bursts O(N × M) in the worst case.
**Recommended index:**

```sql
CREATE UNIQUE INDEX CONCURRENTLY idx_matchmaking_tickets_active_player_ladder
  ON gamekit.matchmaking_tickets ("PlayerId", "LadderId")
  WHERE "Status" = 0;
```

### 4. Player Existence Check

```sql
SELECT 1 FROM gamekit.players WHERE "Id" = $1
```

**When:** Auth (login, register) and matchmaking enqueue validation.
**Risk:** Players table primary-key lookup — the PK index exists by default. No additional
index needed. **Ensure** that downstream queries also pass `Id` directly (avoid implicit
casting from `varchar` to `uuid` or vice-versa — Npgsql sends UUIDs as binary by default
with the `uuid` type; verify with `EnableSensitiveDataLogging` + query log).

### 5. Bulk Ticket Status Update at Match Formation

```sql
UPDATE gamekit.matchmaking_tickets
  SET "Status" = $1
  WHERE "Id" = ANY($2)
```

**When:** Match-formation commit — updates 2-N tickets from `Queued` to `Matched` or
`Proposal` in a single statement.
**Risk:** Row-level locking under concurrent bursts; can escalate to table-level lock if
the planner chooses a sequential scan instead of the PK index.
**Recommended index:** The PK index on `Id` is sufficient. Ensure EF Core generates the
`= ANY($2)` form (not N separate `UPDATE ... WHERE "Id" = $N` calls). Verify with EF Core
query logs (`EnableSensitiveDataLogging` + `LogTo`).

### General Index Hygiene

- Run `ANALYZE gamekit.matchmaking_tickets` after bulk seed loads in staging.
- Use `EXPLAIN (ANALYZE, BUFFERS)` on the ticker scan to verify index usage.
- Monitor `pg_stat_user_indexes` for low-use indexes (high `idx_scan = 0` after
  sustained load indicates the query plan is not using the index).
- EF Core's change tracker can generate N+1 queries if `Include()` chains are omitted
  on navigation properties. Enable `EnableSensitiveDataLogging()` + EF Core query logs
  in your development environment to catch N+1 patterns early.

---

## 5. Tool Licensing Posture

GameKit is Apache-2.0. All performance tooling used during development and CI
must respect this licensing posture.

### BenchmarkDotNet (PERF-01/02/06)

**License:** MIT.

BenchmarkDotNet is a fine NuGet dependency. It is referenced by
`tests/GameKit.LoadTests` (a non-packaged console app, `IsPackable=false`). It is NOT
referenced by any `src/GameKit.*` package and therefore does NOT appear in any NuGet
package's transitive dependency tree. The MIT license is compatible with Apache-2.0
in this non-distribution context.

### k6 (PERF-03/04)

**License:** AGPLv3.

k6 is used exclusively as an **external Docker process**:

```bash
docker run --rm -i grafana/k6:latest run - < tests/k6/<scenario>.js
```

It is **never** referenced as a NuGet package, never linked into any build artifact,
and never shipped inside any `GameKit.*` NuGet package. The k6 `.js` scenario scripts
in `tests/k6/` are GameKit repository files licensed under Apache-2.0.

The AGPLv3 copyleft applies to the k6 binary distribution (the Docker image). Test
scripts that invoke the binary as a subprocess are not considered "derivative works"
under the standard interpretation of AGPLv3 § 5(d); however, consult your legal
counsel before publishing the `.js` scripts publicly if you are uncertain. The
key constraint GameKit enforces is: **k6 is never shipped in a package**.

### Summary

| Tool | License | Can be NuGet dep? | Can be in `src/`? | Can be in `tests/`? |
|------|---------|-------------------|-------------------|---------------------|
| BenchmarkDotNet | MIT | Yes (non-packaged only) | No | Yes (`IsPackable=false`) |
| k6 | AGPLv3 | **Never** | **Never** | **External CLI only** |
| xUnit | Apache-2.0 | Yes (non-packaged) | No | Yes |
| Testcontainers | MIT | Yes (non-packaged) | No | Yes |

---

*Last updated: 2026-06-23 — baselines from `benchmarks/BASELINES.md` (BDN 0.15.8, i7-11700K, .NET 10.0.9).*
