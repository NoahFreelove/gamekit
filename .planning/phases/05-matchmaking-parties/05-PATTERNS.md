# Phase 05 — Matchmaking + Parties: File-to-Pattern Map

**Mapped:** 2026-05-16
**Files analyzed:** ~50 new/modified files
**Analogs found:** 42 / 50 (8 files have no existing analog — marked below)

> For each file to be created, the closest existing analog in the codebase and the code excerpt that should anchor the planner's task. Files with no analog are explicitly marked with the RESEARCH.md section the planner must derive from.

---

## src/GameKit.Matchmaking — Source Files

### Entities

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/Entities/Party.cs` | `src/GameKit.Rankings/Entities/Ladder.cs` | Durable Postgres entity with state enum, ID, timestamps, JSONB-free columns | role-match |
| `src/GameKit.Matchmaking/Entities/PartyState.cs` | `src/GameKit.Rankings/Entities/SeasonResetPolicy.cs` | C# enum stored as integer (Phase 5 default per CONTEXT code insights) | exact |
| `src/GameKit.Matchmaking/Entities/PartyMember.cs` | `src/GameKit.Core/Entities/SessionParticipant.cs` | FK-to-party + FK-to-player join row, nullable after GDPR | role-match |
| `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` | `src/GameKit.Rankings/Entities/PendingRatingUpdate.cs` | Analytics-only async-write table with terminal states; written via Channel, not on hot path | exact |
| `src/GameKit.Matchmaking/Entities/TicketStatus.cs` | `src/GameKit.Rankings/Entities/SeasonResetPolicy.cs` | Integer enum (8 values: Queued/Proposed/Accepted/Declined/TimedOut/Matched/Cancelled/Expired) | exact |
| `src/GameKit.Matchmaking/Entities/TicketEvent.cs` | `src/GameKit.Rankings/Entities/PendingRatingUpdate.cs` | Per-ticket event row drained to Postgres; `event_type` integer enum; optional JSONB payload | role-match |
| `src/GameKit.Matchmaking/Entities/TicketEventType.cs` | `src/GameKit.Rankings/Entities/SeasonResetPolicy.cs` | Integer enum matching D-18 taxonomy (8 event types) | exact |
| `src/GameKit.Matchmaking/Entities/DeclineHistory.cs` | `src/GameKit.Rankings/Entities/SessionCompleteIdempotency.cs` | Analytics/cooldown-tracking row; rolling-window queries by (player_id, declined_at DESC) | role-match |

**Anchor — entity pattern** (`src/GameKit.Rankings/Entities/Ladder.cs:1-60`):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
namespace GameKit.Rankings.Entities;
/// <summary>...</summary>
public sealed class Ladder
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastDrainedAt { get; set; }
}
```

Copy this pattern for `Party`: `Id`, required string `PartyCode`, integer `State` (enum), `Guid OwnerId` FK, `DateTimeOffset CreatedAt`, `DateTimeOffset? ExpiresAt`. Integer enum storage (not `HasConversion<string>()`).

---

### Data / EF Configurations

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/Data/MatchmakingMigrationConstants.cs` | `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs` | Per-package advisory-lock key constant + history table name — copy verbatim, swap strings | exact |
| `src/GameKit.Matchmaking/Data/MatchmakingModelBuilderExtension.cs` | `src/GameKit.Rankings/Data/RankingsModelBuilderExtension.cs` | `IModelBuilderExtension.ApplyTo` applying all 5 entity configurations | exact |
| `src/GameKit.Matchmaking/Data/MatchmakingMigrationHostedService.cs` | `src/GameKit.Rankings/Data/RankingsMigrationHostedService.cs` | `IHostedService` applying `__ef_migrations_matchmaking` under advisory lock at startup | exact |
| `src/GameKit.Matchmaking/Data/MatchmakingDesignTimeDbContextFactory.cs` | `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs` | Design-time EF factory + `MatchmakingMigrationModelCustomizer` — reads `GAMEKIT_MIGRATIONS_CONNECTION` | exact |
| `src/GameKit.Matchmaking/Data/Configurations/PartyConfiguration.cs` | `src/GameKit.Rankings/Data/Configurations/LadderConfiguration.cs` | EF `IEntityTypeConfiguration<Party>`: citext for `party_code`, unique index | exact |
| `src/GameKit.Matchmaking/Data/Configurations/PartyMemberConfiguration.cs` | `src/GameKit.Core/Data/Configurations/SessionParticipantConfiguration.cs` | FK-to-party + FK-to-player, unique application-enforced constraint | role-match |
| `src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs` | `src/GameKit.Rankings/Data/Configurations/PendingRatingUpdateConfiguration.cs` | Nullable FKs (party_id, session_id), integer status enum, timestamp columns | exact |
| `src/GameKit.Matchmaking/Data/Configurations/TicketEventConfiguration.cs` | `src/GameKit.Rankings/Data/Configurations/PendingRatingUpdateConfiguration.cs` | FK to ticket, integer event type, JSONB payload | role-match |
| `src/GameKit.Matchmaking/Data/Configurations/DeclineHistoryConfiguration.cs` | `src/GameKit.Rankings/Data/Configurations/SessionCompleteIdempotencyConfiguration.cs` | FK to player, timestamp index for rolling-window query | role-match |
| `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` | `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs` | Per-package migration: 5 new tables, citext reuse, integer enum columns, gamekit schema | exact |

**Anchor — MigrationConstants pattern** (`src/GameKit.Rankings/Data/RankingsMigrationConstants.cs:1-44`):
```csharp
public static class RankingsMigrationConstants
{
    public const string MigrationsHistoryTable = "__ef_migrations_rankings";
    // SELECT hashtext('gamekit.rankings.migrations')::bigint — verified via Testcontainers
    public const long AdvisoryLockKey = -156812172L;
}
```
Copy as `MatchmakingMigrationConstants`, change strings to `"__ef_migrations_matchmaking"` / `"gamekit.matchmaking.migrations"`. AdvisoryLockKey is a placeholder `0L` until Wave 0 test fills in the live-verified value.

**Anchor — MigrationModelCustomizer pattern** (`src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs:91-130`):
```csharp
public sealed class RankingsMigrationModelCustomizer : RelationalModelCustomizer
{
    public RankingsMigrationModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        // Apply matchmaking configurations directly — bypass DI
        modelBuilder.ApplyConfiguration(new PartyConfiguration());
        // ... (5 configurations total)
        // Exclude Core entities from matchmaking migration diff
        foreach (var type in new[] { typeof(Player), typeof(GameSession), ... })
        {
            modelBuilder.Entity(type).ToTable(tableName, schema, t => t.ExcludeFromMigrations());
        }
        // Also exclude Rankings entities (Matchmaking has ProjectReference to Rankings)
    }
}
```

---

### Redis Key Constants

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` | `src/GameKit.Rankings/GameKitRankingsOptions.cs` (LockKey field) | Centralises all Redis key patterns; no direct analog exists but LockKey string is the seed pattern | partial |

No direct file analog for a Redis-key-constants class. The lock key pattern comes from `GameKitRankingsOptions.Ticker.LockKey = "gamekit:rankings:ticker:lease"`. Build a static class:
```csharp
public static class MatchmakingRedisKeys
{
    public static string Queue(Guid ladderId, string pool) => $"mm:queue:{ladderId}:{pool}";
    public static string Ticket(Guid ticketId) => $"mm:ticket:{ticketId}";
    public static string Proposal(Guid proposalId) => $"mm:proposal:{proposalId}";
    public static string StatusChannel(Guid ticketId) => $"mm:status:{ticketId}";
    public const string MatcherLock = "gamekit:matchmaking:matcher:lock";
    public const string ControlPaused = "mm:control:paused";
    public const string ControlDrain = "mm:control:drain";
}
```

---

### Options + Builder

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/GameKitMatchmakingOptions.cs` | `src/GameKit.Rankings/GameKitRankingsOptions.cs` | Root options class with nested sub-options classes; IOptions<T> singleton pattern | exact |
| `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` | `src/GameKit.Rankings/Builder/LadderConfig.cs` | Per-ladder build-time config with matchmaking fields: BracketStart/BracketEnd/BracketRampSeconds/PartyRatingAggregator/MaxPartyRatingSpread | exact |
| `src/GameKit.Matchmaking/Builder/MatchmakingBuilderExtensions.cs` | `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` | `AddMatchmaking()` fluent extension on `IGameKitBuilder`; `AddLadder()` extension updates existing `LadderConfig` with matchmaking settings | exact |
| `src/GameKit.Matchmaking/Builder/IGameKitMatchmakingBuilder.cs` | `src/GameKit.Rankings/Builder/IGameKitRankingsBuilder.cs` | Interface exposing Services + RegisteredLadders for DI and test introspection | exact |
| `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` | `src/GameKit.Rankings/Builder/GameKitRankingsBuilder.cs` | Internal impl accumulating MatchmakingLadderConfig list with case-insensitive duplicate guard | exact |
| `src/GameKit.Matchmaking/Builder/MatchmakingApplicationBuilderExtensions.cs` | `src/GameKit.Rankings/Builder/RankingsApplicationBuilderExtensions.cs` | `MapMatchmaking()` extension on `IEndpointRouteBuilder` mapping all matchmaking HTTP endpoints | exact |

**Anchor — RankingsBuilderExtensions.cs:39-86** (`AddRankings` method):
```csharp
public static IGameKitRankingsBuilder AddRankings(
    this IGameKitBuilder builder,
    Action<GameKitRankingsOptions>? configure = null)
{
    if (configure is not null)
        builder.Services.Configure(configure);

    builder.Services.TryAddEnumerable(
        ServiceDescriptor.Singleton<IModelBuilderExtension, RankingsModelBuilderExtension>());

    builder.Services.AddHostedService<RankingsMigrationHostedService>();

    builder.Services.AddSingleton<StartupLadderUpserter>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<StartupLadderUpserter>());

    // ... service registrations ...

    var rankingsBuilder = new GameKitRankingsBuilder(builder.Services);
    builder.Services.AddSingleton<IGameKitRankingsBuilder>(rankingsBuilder);
    return rankingsBuilder;
}
```
Copy as `AddMatchmaking`, substituting Matchmaking types. The `AddLadder` extension on `IGameKitMatchmakingBuilder` must extend the existing `LadderConfig` (from Rankings) with matchmaking fields stored in the JSONB Config column — or introduce a separate `MatchmakingLadderConfig` stored in `GameKitMatchmakingOptions.LadderConfigs`.

**Anchor — GameKitRankingsOptions.cs:36-58** (ticker sub-options pattern):
```csharp
public sealed class GameKitRankingsTickerOptions
{
    public int TickIntervalSeconds { get; set; } = 60;
    public int LockTtlSeconds { get; set; } = 90;
    public string LockKey { get; set; } = "gamekit:rankings:ticker:lease";
}
```
Copy as `GameKitMatchmakingTickerOptions` with `TickIntervalMs = 500`, `LockTtlSeconds = 90`, `LockKey = "gamekit:matchmaking:matcher:lock"`. Add sibling options classes: `GameKitMatchmakingCooldownOptions`, `GameKitMatchmakingAnalyticsOptions`, `GameKitMatchmakingReconcilerOptions`.

---

### Services — Background Services

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | `src/GameKit.Rankings/Services/RankingsTickerService.cs` | `BackgroundService` + `PeriodicTimer` + `IRankingsTicker`-style interface + lock-acquire-renew-release loop | exact |
| `src/GameKit.Matchmaking/Services/IMatchmakerTicker.cs` | `src/GameKit.Rankings/Services/IRankingsTicker.cs` | Interface exposing `RunOnceAsync(ct) → MatcherTickResult` for test injection | exact |
| `src/GameKit.Matchmaking/Services/MatchmakerLeaseHelper.cs` | `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` | `LockTake / LockExtend / LockRelease` via Polly v8 retry; new lock key `gamekit:matchmaking:matcher:lock` | exact |
| `src/GameKit.Matchmaking/Services/MatchmakingRetentionCleanupService.cs` | `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs` | Nightly `BackgroundService` + `PeriodicTimer`; startup-immediate pass; `ExecuteDeleteAsync WHERE terminal_at < cutoff` | exact |
| `src/GameKit.Matchmaking/Services/MatchmakingReconcilerService.cs` | `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs` | 30s periodic sweep BackgroundService; leader-gated; Postgres-only sweep (no Redis write) | role-match |
| `src/GameKit.Matchmaking/Services/MatchmakingAnalyticsDrainService.cs` | `src/GameKit.Rankings/Services/RankingsTickerService.cs` (Polly v8 portion) | `BackgroundService` draining `Channel<TicketEvent>`; Polly v8 retry on `NpgsqlException`; every-replica (not leader-gated) | role-match |

**Anchor — MatchmakerTickerService: lock-acquire pattern** (`src/GameKit.Rankings/Services/RankingsTickerService.cs:95-201`):
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_opts.Ticker.TickIntervalMs));
    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try { var result = await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "...unhandled exception during tick. Continuing."); }
        }
    }
    catch (OperationCanceledException) { /* Normal shutdown. */ }
}

public async Task<MatcherTickResult> RunOnceAsync(CancellationToken ct)
{
    var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
    if (!acquired) return MatcherTickResult.LockNotAcquired;
    try
    {
        // ... for each ladder pool:
        // Pitfall §2: renew lease before processing each pool
        var renewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
        if (!renewed) { _logger.LogWarning("Lock lost mid-tick"); break; }
        // ... Lua atomic-claim ...
    }
    finally { await _lease.ReleaseLeaseAsync(ct).ConfigureAwait(false); }
}
```

**Anchor — IdempotencyCleanupService: nightly pattern** (`src/GameKit.Rankings/Services/IdempotencyCleanupService.cs:65-101`):
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    // Startup-immediate pass
    await RunCleanupOnceAsync(stoppingToken).ConfigureAwait(false);

    using var timer = new PeriodicTimer(CleanupInterval); // default 24h; retention = 30d
    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try { await RunCleanupOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "...Will retry next interval."); }
        }
    }
    catch (OperationCanceledException) { }
}

public async Task RunCleanupOnceAsync(CancellationToken ct)
{
    using var scope = _scopeFactory.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<IClock>();
    var cutoff = clock.UtcNow - _opts.TicketRetention; // default 30 days
    var deleted = await ctx.Set<MatchmakingTicket>()
        .Where(t => t.TerminalAt != null && t.TerminalAt < cutoff)
        .ExecuteDeleteAsync(ct).ConfigureAwait(false);
}
```

**Anchor — LeaseHelper: Polly v8 pattern** (`src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs:39-172`):
```csharp
public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";

// Polly v8 pipeline — 3 retries, exponential jitter, Redis connection/timeout only
_polly = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<RedisConnectionException>()
            .Handle<RedisTimeoutException>(),
    })
    .Build();

public async Task<bool> TryAcquireLeaseAsync(CancellationToken ct)
{
    return await _polly.ExecuteAsync(async token =>
    {
        var db = _redis.GetDatabase();
        return await db.LockTakeAsync(
            _opts.Ticker.LockKey, InstanceId,
            TimeSpan.FromSeconds(_opts.Ticker.LockTtlSeconds)).ConfigureAwait(false);
    }, ct).ConfigureAwait(false);
}

// CRITICAL — Pitfall §6: caller MUST check false return and bail
public async Task<bool> RenewLeaseAsync(CancellationToken ct) { ... }
public async Task ReleaseLeaseAsync(CancellationToken ct) { ... }
```

**Anchor — AnalyticsDrainService: Polly v8 drain pattern** (from RESEARCH.md §Decision 7):
```csharp
// Drain service Polly pipeline
new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 4,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        Delay = TimeSpan.FromMilliseconds(500),
        ShouldHandle = new PredicateBuilder()
            .Handle<NpgsqlException>()
            .Handle<DbUpdateException>()
    })
    .AddTimeout(TimeSpan.FromSeconds(30))
    .Build()
// OTel counter: matchmaking.analytics.dropped_events
// Tags: { "reason": "channel_full" | "polly_exhausted" }
```

---

### Services — Application Services

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/Services/IMatchmakingService.cs` | `src/GameKit.Rankings/Services/IEndSeasonService.cs` | Application service interface: enqueue, cancel, status, accept/decline | role-match |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` | `src/GameKit.Rankings/Services/EndSeasonService.cs` | Scoped service doing cooldown check, ZADD, Channel.TryWrite | role-match |
| `src/GameKit.Matchmaking/Services/IPartyService.cs` | `src/GameKit.Rankings/Services/ILeaderboardService.cs` | Application service interface: create, join (code), dissolve, get | role-match |
| `src/GameKit.Matchmaking/Services/PartyService.cs` | `src/GameKit.Rankings/Services/LeaderboardService.cs` | Scoped service; Postgres-only CRUD; SERIALIZABLE tx for single-active-party enforcement | role-match |
| `src/GameKit.Matchmaking/Services/IMatchmakingObservability.cs` | `src/GameKit.Rankings/Services/ILeaderboardService.cs` | Port interface: `GetQueueStatsAsync(ct) → MatchmakingQueueStats` | role-match |
| `src/GameKit.Matchmaking/Services/RedisMatchmakingObservability.cs` | `src/GameKit.Rankings/Services/LeaderboardService.cs` | Adapter: ZCARD per sorted set + GET lock key for leader identity | role-match |
| `src/GameKit.Matchmaking/Services/MatchmakingQueueStats.cs` | `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` | Response record: `IReadOnlyList<PoolDepth> Pools, int ActiveLeaseCount, string? LeaderInstanceId, DateTimeOffset AsOf` | role-match |

**Anchor — IMatchmakingObservability shape** (from RESEARCH.md §Decision 11):
```csharp
public interface IMatchmakingObservability
{
    Task<MatchmakingQueueStats> GetQueueStatsAsync(CancellationToken ct);
}

public record MatchmakingQueueStats(
    IReadOnlyList<PoolDepth> Pools,
    int ActiveLeaseCount,
    string? LeaderInstanceId,
    DateTimeOffset AsOf);

public record PoolDepth(Guid LadderId, string PoolName, long Depth);
```

---

### Strategy (IMatchmakingStrategy contract)

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/Strategy/IMatchmakingStrategy.cs` | `src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs` | Pluggable strategy interface; XML-doc contract; registered as singleton; implementers must be thread-safe | exact |
| `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` | `src/GameKit.Rankings/Algorithms/Glicko2Algorithm.cs` (150-line impl) | Default strategy impl; bracket-flex formula; `GlickoWeighted/Mean/Max` aggregator switch | role-match |
| `src/GameKit.Matchmaking/Strategy/PartyRatingAggregator.cs` | `src/GameKit.Rankings/Entities/SeasonResetPolicy.cs` | Integer enum: `Mean = 0, Max = 1, GlickoWeighted = 2` | exact |
| `src/GameKit.Matchmaking/Strategy/QueuedParty.cs` | `src/GameKit.Rankings/Algorithms/RankingState.cs` | Value record passed to strategy: ticket data, aggregate rating, `queuedAt`, ladderId, poolName | role-match |
| `src/GameKit.Matchmaking/Strategy/MatchResult.cs` | `src/GameKit.Rankings/Algorithms/RankingBatch.cs` | Output record from strategy: matched party IDs, proposed team assignments | role-match |

**Anchor — IRankingAlgorithm interface pattern** (`src/GameKit.Rankings/Algorithms/IRankingAlgorithm.cs:31-76`):
```csharp
public interface IRankingAlgorithm
{
    string Name { get; }
    RankingState Apply(RankingState state, RankingBatch batch);
}
```
Mirror for matchmaking:
```csharp
public interface IMatchmakingStrategy
{
    /// <summary>Stable name. Default: "elo-range".</summary>
    string Name { get; }
    /// <summary>
    /// Try to form a match for <paramref name="candidate"/> from <paramref name="pool"/>.
    /// Returns null if no match in the current tick. Must be deterministic and thread-safe.
    /// </summary>
    MatchResult? Match(QueuedParty candidate, IReadOnlyList<QueuedParty> pool, DateTimeOffset now);
}
```

**Anchor — EloRangeMatchmakingStrategy bracket formula** (from RESEARCH.md §Decision 4):
```csharp
// bracket(t) = min(BracketStart + (BracketEnd - BracketStart) * t / BracketRampSeconds, BracketEnd)
// t = (now - ticket.queuedAt).TotalSeconds  — from IClock snapshot at tick start
// Symmetric overlap: |rA - rB| <= bA AND |rA - rB| <= bB (conjunctive constraint)
private static double Bracket(MatchmakingLadderConfig cfg, double secondsInQueue)
    => Math.Min(cfg.BracketStart + (cfg.BracketEnd - cfg.BracketStart)
        * secondsInQueue / cfg.BracketRampSeconds, cfg.BracketEnd);
```

---

### HTTP — Endpoints, Validators, Rate Limiting

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` | `src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs` | Minimal API route group; JWT `RequireAuthorization()`; service injection; `ClaimTypes.NameIdentifier` for PlayerId | exact |
| `src/GameKit.Matchmaking/Http/PartyEndpoints.cs` | `src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs` | Minimal API route group for party CRUD; same JWT auth pattern | exact |
| `src/GameKit.Matchmaking/Http/MatchmakingAdminEndpoints.cs` | `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs` | Admin API: `pause-queue`, `drain-queue` verbs; cookie auth; audit write pattern | exact |
| `src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs` | `src/GameKit.Rankings/Http/Contracts/EndSeasonRequest.cs` | Request DTO: `Guid LadderId`, optional `string PoolName`, optional `Guid PartyId` | role-match |
| `src/GameKit.Matchmaking/Http/Contracts/CreatePartyRequest.cs` | `src/GameKit.Rankings/Http/Contracts/EndSeasonRequest.cs` | Simple request DTO validated by FluentValidation | role-match |
| `src/GameKit.Matchmaking/Http/Contracts/JoinPartyRequest.cs` | `src/GameKit.Rankings/Http/Contracts/EndSeasonRequest.cs` | `{ string Code }` — case-insensitive join code | role-match |
| `src/GameKit.Matchmaking/Http/Contracts/TicketStatusResponse.cs` | `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` | `{ status, proposalId?, deadline?, sessionId? }` | role-match |
| `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs` | `src/GameKit.Rankings/Http/Validators/SessionCompleteRequestValidator.cs` | FluentValidation `AbstractValidator<EnqueueRequest>` | exact |
| `src/GameKit.Matchmaking/Http/Validators/CreatePartyRequestValidator.cs` | `src/GameKit.Rankings/Http/Validators/EndSeasonRequestValidator.cs` | FluentValidation `AbstractValidator<CreatePartyRequest>` | exact |
| `src/GameKit.Matchmaking/Http/RateLimiting/MatchmakingRateLimitRegistrations.cs` | `src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs` | `AddMatchmakingRateLimits()` extension; `SlidingWindowLimiter` partitioned by `ClaimTypes.NameIdentifier` | exact |

**Anchor — RankingsPlayerEndpoints.cs endpoint pattern** (`src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs:33-73`):
```csharp
public static IEndpointRouteBuilder MapRankingsPlayer(this IEndpointRouteBuilder routes)
{
    routes.MapGet("/api/players/{id:guid}/export", PlayerGdprExportAsync)
        .RequireAuthorization(); // JWT-Bearer from Phase 2
    return routes;
}

private static async Task<IResult> PlayerGdprExportAsync(
    Guid id, HttpContext http, IGdprExportService svc, CancellationToken ct)
{
    var subClaim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? http.User.FindFirst("sub")?.Value;
    if (subClaim is null || !Guid.TryParse(subClaim, out var subId) || subId != id)
        return Results.Forbid();
    // ... service call + Results.Ok / Results.NotFound
}
```
Matchmaking endpoints follow this pattern. PlayerId claim extraction from `ClaimTypes.NameIdentifier` is used for rate-limit partition key and cooldown queries.

**Anchor — RankingsRateLimitRegistrations.cs rate-limit pattern** (`src/GameKit.Rankings/Http/RateLimiting/RankingsRateLimitRegistrations.cs:86-107`):
```csharp
private static void ConfigurePolicy(RateLimiterOptions opt, IGameKitRateLimitPolicies names)
{
    opt.AddPolicy(names.MmEnqueue, httpContext =>
    {
        var playerId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var partitionKey = string.IsNullOrEmpty(playerId)
            ? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : $"player:{playerId}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 6,
                QueueLimit = 0,
            });
    });
}
```
Note: `IGameKitRateLimitPolicies` already has `MmEnqueue` property (`src/GameKit.Core/RateLimiting/IGameKitRateLimitPolicies.cs:28`). Implementation constant already present in `GameKitRateLimitPolicies.cs` — Matchmaking just registers the policy via `AddMatchmakingRateLimits()`.

**Anchor — RankingsAdminEndpoints.cs audit pattern** (`src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:196-213`):
```csharp
// D-22 invariant: Matchmaking cannot reference Admin.UI — duplicate the literal string
private const string AuditActionPauseQueue = "admin.matchmaking.pause_queue";
private const string AuditActionDrainQueue = "admin.matchmaking.drain_queue";

// Audit row write (copy this pattern in MatchmakingAdminEndpoints.cs)
var auditRow = new AdminAuditLog
{
    Id = idGen.NewId(),
    Action = AuditActionPauseQueue,
    TargetType = "matchmaking",
    TargetId = ladderId,
    ActorId = actorId,
    Before = null, After = afterJson, Reason = null,
    CreatedAt = clock.UtcNow,
};
ctx.Set<AdminAuditLog>().Add(auditRow);
await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
```

---

### Admin Integration (new audit constants + command palette verbs)

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| Additions to `src/GameKit.Admin.UI/Services/AdminAuditActions.cs` | `src/GameKit.Admin.UI/Services/AdminAuditActions.cs:39-43` | Add `MatchmakingPauseQueue = "admin.matchmaking.pause_queue"`, `MatchmakingDrainQueue = "admin.matchmaking.drain_queue"` | exact |
| Additions to `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs` | `src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs:33-50` | Add Registry entries for the two new audit actions | exact |
| Additions to `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs` | `src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs:43-48` | Add `pause-queue` and `drain-queue` action rows (category "actions", RequiresSuperadmin: true, RequiresTarget: true for ladder) | exact |
| Fill-in `src/GameKit.Admin.UI/Components/Pages/QueueDepth.razor` | `src/GameKit.Admin.UI/Components/Pages/QueueDepth.razor` (existing placeholder) | Replace placeholder div with MudDataGrid displaying `MatchmakingQueueStats.Pools`; reflective lookup already present | role-match |

**Anchor — AdminCommandRegistry action row pattern** (`src/GameKit.Admin.UI/Services/AdminCommandRegistry.cs:33-47`):
```csharp
new("pause-queue", "Pause matchmaking queue", "actions", RequiresSuperadmin: true, RequiresTarget: true),
new("drain-queue", "Drain matchmaking queue", "actions", RequiresSuperadmin: true, RequiresTarget: true),
```

**Anchor — AuditSentenceTemplates entry pattern** (`src/GameKit.Admin.UI/Services/AuditSentenceTemplates.cs:36-38`):
```csharp
[AdminAuditActions.MatchmakingPauseQueue] = ctx =>
    new SentenceModel(ctx.ActorName, "paused matchmaking queue for", ctx.TargetName ?? "(unknown ladder)", null, ctx.Reason),
[AdminAuditActions.MatchmakingDrainQueue] = ctx =>
    new SentenceModel(ctx.ActorName, "drained matchmaking queue for", ctx.TargetName ?? "(unknown ladder)", null, ctx.Reason),
```

---

## tests/GameKit.Matchmaking.Tests — Unit Tests

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `tests/GameKit.Matchmaking.Tests/GameKit.Matchmaking.Tests.csproj` | `tests/GameKit.Rankings.Tests/` (csproj) | xUnit 2.9.2 + Moq; no Testcontainers | exact |
| `tests/GameKit.Matchmaking.Tests/Strategy/BracketFlexMathTests.cs` | `tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs` | Pure math unit test; no DI; assert formula at t=0, t=20, t=40, t=60 | exact |
| `tests/GameKit.Matchmaking.Tests/Strategy/GlickoWeightedAggregatorTests.cs` | `tests/GameKit.Rankings.Tests/Glicko2/Glicko2AlgorithmContractTests.cs` | Pure math unit test validating the 1/RD^2 weighted-mean formula | exact |
| `tests/GameKit.Matchmaking.Tests/Services/CooldownEscalationTests.cs` | `tests/GameKit.Rankings.Tests/Json/CanonicalJsonHasherTests.cs` | Pure logic unit test; mock IClock; assert 3/15/30 min thresholds | role-match |
| `tests/GameKit.Matchmaking.Tests/Services/PartyCodeGenerationTests.cs` | `tests/GameKit.Rankings.Tests/Json/CanonicalJsonHasherTests.cs` | Pure unit test: code length 6–8, Crockford alphabet (no I/L/O/0/1), case-insensitive round-trip | role-match |
| `tests/GameKit.Matchmaking.Tests/Services/MatchmakerLeaseHelperMockTests.cs` | `tests/GameKit.Rankings.Tests/Glicko2/Glicko2AlgorithmContractTests.cs` | Mock `IConnectionMultiplexer`; assert lock-acquire / renew / release call sequence | role-match |
| `tests/GameKit.Matchmaking.Tests/Services/TicketEventChannelDropTests.cs` | `tests/GameKit.Rankings.Tests/Json/CanonicalJsonHasherTests.cs` | Bounded channel drop behavior: fill to capacity, assert TryWrite returns false, OTel counter increments | role-match |

**Anchor — unit test structure** (`tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs:1-25`):
```csharp
// No DI, no fixtures — pure math assertions
public sealed class Glicko2WorkedExampleTests
{
    [Fact]
    public void Worked_Example_Rating_Matches_Glickman_2012_Paper()
    {
        var calc = new RatingCalculator(tau: 0.5, initVolatility: 0.06);
        // ... setup, assert
    }
}
```

---

## tests/GameKit.Matchmaking.Integration.Tests — Integration Tests

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `tests/GameKit.Matchmaking.Integration.Tests/GameKit.Matchmaking.Integration.Tests.csproj` | `tests/GameKit.Rankings.Integration.Tests/` (csproj) | xUnit + Testcontainers.PostgreSql + Testcontainers.Redis; xUnit1041 collection defs | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/CollectionDefinitions.cs` | `tests/GameKit.Rankings.Integration.Tests/CollectionDefinitions.cs` | `[CollectionDefinition("Matchmaking")] : ICollectionFixture<PostgresFixture>, ICollectionFixture<RedisFixture>` | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestModelCustomizer.cs` | `tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs:323-335` (TickerTestModelCustomizer) | `RelationalModelCustomizer` subclass applying `MatchmakingModelBuilderExtension` directly; bypasses EF global model cache (Pitfall §3) | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingAdvisoryLockKeyTests.cs` | `tests/GameKit.Rankings.Integration.Tests/RankingsAdvisoryLockKeyTests.cs` | Live-verify `hashtext('gamekit.matchmaking.migrations')::bigint`; assert distinct from Core/Auth/Admin/Rankings | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingMigrationDeterminismTests.cs` | `tests/GameKit.Rankings.Integration.Tests/RankingsMigrationDeterminismTests.cs` | Apply migration twice against fresh DB; assert idempotent (no duplicate-apply errors) | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingLeaderElectionTests.cs` | `tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs` | Two `ServiceProvider` instances pointing at same Redis; `Task.WhenAll`; assert `Single(LockNotAcquired)` + `Single(Matched|NoMatch)` | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingHappyPathTests.cs` | `tests/GameKit.Rankings.Integration.Tests/Glicko2ConvergenceTests.cs` | SC#1: enqueue party, advance StepClock 40s, assert bracket widened 100→500, assert ticket_events written | role-match |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingRateLimitTests.cs` | `tests/GameKit.Rankings.Integration.Tests/SessionCompleteIdempotencyTests.cs` | SC#5: WebApplicationFactory + rapid-fire POST /api/mm/queue; assert 429 on 6th request in 1 min | role-match |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingObservabilityTests.cs` | `tests/GameKit.Rankings.Integration.Tests/LeaderboardServiceTests.cs` | SC#6: enqueue N tickets, call `IMatchmakingObservability.GetQueueStatsAsync`, assert `PoolDepth.Depth == N` | role-match |
| `tests/GameKit.Matchmaking.Integration.Tests/ReconcilerSweepTests.cs` | `tests/GameKit.Rankings.Integration.Tests/IdempotencyCleanupServiceTests.cs` | Insert stale non-terminal ticket rows, run `MatchmakingReconcilerService.RunSweepOnceAsync`, assert status = Expired | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/PartyEndpointTests.cs` | `tests/GameKit.Rankings.Integration.Tests/LadderUpsertOnStartupTests.cs` | WebApplicationFactory; create party, join via code (case-insensitive per Pitfall §9), dissolve; Postgres assertions | role-match |
| `tests/GameKit.Matchmaking.Integration.Tests/CooldownEnforcementTests.cs` | `tests/GameKit.Rankings.Integration.Tests/ServiceTokenAuthenticationHandlerTests.cs` | Enqueue, decline proposal, re-enqueue; assert 403 `ProhibitedDuringCooldown` with `retryAfterSeconds` | role-match |

**Anchor — CollectionDefinitions.cs pattern** (`tests/GameKit.Rankings.Integration.Tests/CollectionDefinitions.cs:1-21`):
```csharp
[CollectionDefinition("Matchmaking")]
public sealed class MatchmakingCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

[CollectionDefinition("Postgres")]
public sealed class PostgresOnlyCollection : ICollectionFixture<PostgresFixture> { }
```

**Anchor — MatchmakingTestModelCustomizer pattern** (`tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs:323-335`):
```csharp
internal sealed class MatchmakingTestModelCustomizer : RelationalModelCustomizer
{
    public MatchmakingTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
        // Also apply Rankings extension (Matchmaking reads player_ranks)
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
```

**Anchor — LeaderElectionTests: two-replica pattern** (`tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs:71-115`):
```csharp
[Fact]
public async Task Two_Tickers_Only_One_Drains_Per_Tick()
{
    await using var sp1 = BuildMatchmakerServiceProvider(cs, redisCs, suffix: "1");
    await using var sp2 = BuildMatchmakerServiceProvider(cs, redisCs, suffix: "2");
    var ticker1 = sp1.GetRequiredService<IMatchmakerTicker>();
    var ticker2 = sp2.GetRequiredService<IMatchmakerTicker>();

    // Flush stale lock key
    await db.KeyDeleteAsync("gamekit:matchmaking:matcher:lock");

    var results = await Task.WhenAll(ticker1.RunOnceAsync(cts.Token), ticker2.RunOnceAsync(cts.Token));

    Assert.Single(results, r => r == MatcherTickResult.Matched || r == MatcherTickResult.NoMatch);
    Assert.Single(results, r => r == MatcherTickResult.LockNotAcquired);
}
```

---

## tests/GameKit.Matchmaking.Integration.Tests — Chaos Test (SC#2)

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingChaosTests.cs` | [NO ANALOG] | No chaos test harness exists in prior phases | none |

See "Files with NO analog" section below.

---

## tests/GameKit.Matchmaking.LoadTests — Load Harness (SC#3)

| New file | Closest analog | Why this analog | Match quality |
|----------|---------------|-----------------|---------------|
| `tests/GameKit.Matchmaking.LoadTests/GameKit.Matchmaking.LoadTests.csproj` | `tests/GameKit.Rankings.Integration.Tests/` (csproj structure) | xUnit with extended timeout `[Fact(Timeout = 15 * 60 * 1000)]`; Testcontainers | role-match |
| `tests/GameKit.Matchmaking.LoadTests/MatchmakingLoadTests.cs` | [NO ANALOG] | No load test harness exists in any prior phase | none |

See "Files with NO analog" section below.

---

## Sample App Changes (TicTacToeDuel)

| Modified file | Closest analog | Why this analog | Match quality |
|---------------|---------------|-----------------|---------------|
| `samples/TicTacToeDuel/Program.cs` (add `AddMatchmaking` + `AddLadder` matchmaking config) | `samples/TicTacToeDuel/Program.cs:59-72` (existing `AddRankings().AddLadder(...)` block) | Chain `AddMatchmaking(opts => { opts.Ticker.TickIntervalMs = 500; })` from the rankings builder; extend `AddLadder("tictactoe", ...)` with matchmaking opts | exact |

**Anchor — Program.cs extension chain** (`samples/TicTacToeDuel/Program.cs:59-72`):
```csharp
gameKitBuilder.AddRankings(opts => { })
    .AddLadder("main", c => { c.DefaultRating = 1500; ... });
// Phase 5: chain AddMatchmaking on the IGameKitRankingsBuilder return
// (or on gameKitBuilder directly if AddMatchmaking extends IGameKitBuilder)
gameKitBuilder.AddMatchmaking(opts =>
{
    opts.Ticker.TickIntervalMs = 500;
})
.AddLadder("tictactoe", ladder =>
{
    ladder.BracketStart = 100;
    ladder.BracketEnd = 500;
    ladder.BracketRampSeconds = 40;
    ladder.PartyRatingAggregator = PartyRatingAggregator.Mean;
});
```

Also add to `app.MapMatchmaking()` call after `app.MapRankings()`.

---

## Shared Patterns

### Authentication / Authorization
**Source:** `src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs:37-39`
**Apply to:** `MatchmakingEndpoints.cs`, `PartyEndpoints.cs`
```csharp
routes.MapPost("/api/mm/queue", EnqueueAsync)
    .RequireAuthorization(); // JWT-Bearer scheme from Phase 2
// PlayerId claim: http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
```

### Admin D-22 Invariant (no reverse reference to Admin.UI)
**Source:** `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:42-55`
**Apply to:** `MatchmakingAdminEndpoints.cs`
```csharp
// Source of truth: GameKit.Admin.UI.Authorization.AdminPolicies.Superadmin
private const string SuperadminPolicy = "gamekit.admin.superadmin";
// Source of truth: GameKit.Admin.UI.Authorization.AdminPolicies.Admin
private const string AdminPolicy = "gamekit.admin.admin";
// Audit action constant — duplicated here because Matchmaking cannot reference Admin.UI (D-22)
private const string AuditActionPauseQueue = "admin.matchmaking.pause_queue";
```

### IClock Injection
**Source:** `src/GameKit.Core/Services/IClock.cs`, `src/GameKit.Rankings/Services/IdempotencyCleanupService.cs:121-123`
**Apply to:** All services that record timestamps or compute cooldown thresholds
```csharp
var clock = scope.ServiceProvider.GetRequiredService<IClock>();
var now = clock.UtcNow; // Always DateTimeOffset, never DateTime.Now (Pitfall §4)
```

### Per-Package Migration Boundary
**Source:** `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs:113-128`
**Apply to:** `MatchmakingDesignTimeDbContextFactory.cs`, `MatchmakingMigrationModelCustomizer.cs`
```csharp
// In MatchmakingMigrationModelCustomizer.Customize:
// Exclude ALL prior-package entities: Core + Auth + Admin + Rankings
var excludedTypes = new[]
{
    typeof(Player), typeof(GameSession), typeof(SessionParticipant), typeof(AdminAuditLog),
    // Rankings entities (because Matchmaking has a ProjectReference to Rankings):
    typeof(Ladder), typeof(PlayerRank), typeof(PendingRatingUpdate), typeof(SessionCompleteIdempotency),
    typeof(LadderSeason), typeof(SeasonRankArchive), typeof(ServiceToken),
};
foreach (var type in excludedTypes)
    modelBuilder.Entity(type).ToTable(tableName, schema, t => t.ExcludeFromMigrations());
```

### Error Handling in Minimal API Handlers
**Source:** `src/GameKit.Rankings/Http/RankingsAdminEndpoints.cs:137-143`, `src/GameKit.Rankings/Http/RankingsPlayerEndpoints.cs:56-73`
**Apply to:** All HTTP endpoint handlers
```csharp
try { var result = await svc.DoThingAsync(...); return Results.Ok(result); }
catch (KeyNotFoundException ex) { return Results.NotFound(new { error = "not_found", detail = ex.Message }); }
catch (ArgumentException ex) { return Results.BadRequest(new { error = "invalid_request", detail = ex.Message }); }
// New for Matchmaking:
// 403 ProhibitedDuringCooldown: return Results.Forbid() — or custom 403 with body
// 400 PartyRatingSpreadExceeded
```

### Integer Enum Storage (Phase 5 mandatory)
**Source:** `src/GameKit.Rankings/Data/Configurations/LadderConfiguration.cs` (note absence of `HasConversion<string>()`)
**Apply to:** `PartyConfiguration.cs`, `MatchmakingTicketConfiguration.cs`, `TicketEventConfiguration.cs`
```csharp
// DO NOT add HasConversion<string>() for Phase 5 enums — Phase 4 was bitten by this.
// EF defaults to integer storage. Seeds in migration SQL use integer values directly.
b.Property(p => p.State); // stored as integer, no conversion
```

### Polly v8 Redis Retry Pattern
**Source:** `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs:66-86`
**Apply to:** `MatchmakerLeaseHelper.cs`
```csharp
_polly = new ResiliencePipelineBuilder()
    .AddRetry(new RetryStrategyOptions
    {
        MaxRetryAttempts = 3,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = true,
        ShouldHandle = new PredicateBuilder()
            .Handle<RedisConnectionException>()
            .Handle<RedisTimeoutException>(),
        OnRetry = args => { _logger.LogWarning(...); return ValueTask.CompletedTask; },
    })
    .Build();
```

---

## Files with NO Analog (Novel — Derive from RESEARCH.md)

| File | RESEARCH.md section | Reason no analog |
|------|---------------------|-------------------|
| `src/GameKit.Matchmaking/Redis/AtomicClaimScript.cs` (Lua atomic-claim script loader) | §Decision 3 "Atomic Match Formation" + Pitfall §2 | No Lua/EVAL pattern exists in any prior phase. Implement as `IDatabase.ScriptEvaluateAsync` with lease fencing check inside the Lua script. |
| `src/GameKit.Matchmaking/Services/ProposalService.cs` + `IProposalService.cs` | §Decision 2 "Proposal Storage" + §Canonical Path 2 | No accept-flow proposal pattern exists. Redis hash with TTL; "all-accepted" check; re-queue on decline (D-09); PUBLISH to long-poll channels. |
| `src/GameKit.Matchmaking/Http/LongPollStatusEndpoint.cs` (or inline in MatchmakingEndpoints.cs) | §Decision 9 "Long-Poll Status Endpoint" + Pitfall §5 | No long-poll + Redis pub/sub subscriber pattern exists. Use `ISubscriber.SubscribeAsync` + `CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted, ...)` + 30s timeout + `finally { await subscriber.UnsubscribeAsync(channel); }`. |
| `src/GameKit.Matchmaking/Services/PartyCodeGenerator.cs` | §Decision 1 "Pool Partitioning" domain note + CONTEXT D-02 | No short-code generation exists. Crockford base32 alphabet (no I/L/O/0/1); 6 chars; case-insensitive; `CITEXT` column type in Postgres (reuse Phase 2 `CREATE EXTENSION IF NOT EXISTS citext`). |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingChaosTests.cs` | §Decision 14 "Chaos Integration Test" | No chaos-interceptor pattern exists. Requires `IChaosInterceptor` test-only interface injected via `WebApplicationFactory<MatchmakingTestApp>`; abort at Lua claim step; run `MatchmakingReconcilerService.RunSweepOnceAsync()`; assert no duplicate `game_sessions`, no ghost Redis keys. |
| `tests/GameKit.Matchmaking.LoadTests/MatchmakingLoadTests.cs` | §Decision 13 "1k-Concurrent-Ticket Load Test" | No load test harness exists in any prior phase. `Parallel.ForEachAsync(DegreeOfParallelism=1000)` against `WebApplicationFactory`; 10-min run; Stopwatch per ticker iteration; assert `MaxIterationMs <= budget`; assert no pool exhaustion. |
| `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingTestApp.cs` (or inline in test host) | No equivalent `TestApp` bootstrapper exists in Rankings tests | Rankings tests build `ServiceProvider` directly (no `WebApplicationFactory`). Matchmaking endpoint tests need a full ASP.NET Core pipeline for rate-limiting, auth, antiforgery. Mirror `tests/GameKit.Admin.Integration.Tests/AdminTestHost.cs`. |
| `src/GameKit.Matchmaking/Services/StartupQueuePoolDiscovery.cs` (optional — scan Redis for registered pools) | No prior phase equivalent | No pool-discovery-at-startup pattern exists. `IMatchmakingObservability` does SCAN `mm:queue:*` on-demand; whether to cache at startup is Claude's Discretion. If skipped, ZCARD per-request on the admin panel is acceptable. |

---

## Metadata

**Analog search scope:** `src/GameKit.Rankings/`, `src/GameKit.Core/`, `src/GameKit.Admin.UI/`, `tests/GameKit.Rankings.Integration.Tests/`, `tests/GameKit.TestFixtures/`, `samples/TicTacToeDuel/`
**Files scanned:** 90+ source files read; key excerpts extracted from ~25 files
**Pattern extraction date:** 2026-05-16
