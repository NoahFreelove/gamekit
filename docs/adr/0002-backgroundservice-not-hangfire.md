<!-- SPDX-License-Identifier: Apache-2.0 -->
<!-- Copyright (c) 2026 GameKit contributors -->

# ADR-0002: BackgroundService not Hangfire or Quartz

**Status:** Accepted

## Context

GameKit requires two recurring background jobs:

1. **Matchmaking ticker** — scans the Redis sorted sets for matchable tickets
   approximately every 500 ms.
2. **Queue reconciliation ticker** — reconciles the Redis live queue against
   the Postgres ticket-durability table approximately every 30 s.

Popular .NET job schedulers (Hangfire, Quartz.NET, TickerQ, NCronJob) offer
features like durable job queues, cron scheduling, clustering support, and
management dashboards. However, they come with a fundamental problem for a
library: **they require storage in the consuming application's database**.

Hangfire writes `hangfire.*` tables into the customer's Postgres schema. The
consumer must run `Hangfire.PostgreSql` and the Hangfire schema migration as
part of their own database setup. This violates GameKit's "install only what you
need" constraint — a consumer who uses GameKit.Matchmaking should not need to
understand or maintain a Hangfire schema.

Quartz.NET has similar issues: the JDBC job store (for clustering) requires its
own schema, and the in-memory job store loses all scheduled jobs on restart.

**Our jobs are trivially periodic.** They do not require cron expressions, fan-
out, durable job history, or cross-instance coordination beyond a Redis
distributed lock (which we already have for the leader-election pattern).

## Decision

GameKit uses `Microsoft.Extensions.Hosting.BackgroundService` (BCL) for all
background jobs. Leader election is implemented with a Redis distributed lock
(`SET NX PX`) per the `ILeaderLease` abstraction (ADR for `ILeaderLease` is
embedded in the SCALE-01 requirements). Exponential backoff on failure is
provided by Polly 8 pipelines.

No Hangfire, Quartz.NET, TickerQ, NCronJob, or other third-party scheduler is
added as a dependency.

## Consequences

- **Positive:** Zero additional tables in the consumer's database. Zero
  Hangfire/Quartz dependency licensing or upgrade risk. The background services
  are native BCL code — standard startup/shutdown, standard health probes, no
  external tooling to debug.
- **Positive:** The Polly + Redis-lock pattern for leader election is already in
  use for the matchmaking ticker and rankings decay ticker. Consistency across
  all background jobs reduces cognitive overhead.
- **Negative:** No built-in job history or retry UI. If consumers need to inspect
  whether a job ran or failed they must rely on structured logs / the OTel
  instrumentation (ActivitySource events from the ticker).
- **Negative:** Clustering is manual (Redis `SET NX PX` leader election) rather
  than Hangfire's or Quartz's managed clustering. We own the leader-election
  correctness — see the SCALE-05 graceful-drain integration tests.
- **Future:** If consumers request durable job history or a richer scheduling API,
  reconsider as an opt-in companion package rather than a core dependency.
