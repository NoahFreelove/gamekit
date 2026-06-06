# Phase 8: Rankings Depth + Rating-Aware Matchmaking — Pattern Map

**Mapped:** 2026-06-05
**Files analyzed:** 18 new/modified files
**Analogs found:** 18 / 18

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs` | migration | CRUD | `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs` | exact |
| `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.Designer.cs` | migration | CRUD | `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.Designer.cs` | exact |
| `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs` | migration | CRUD | `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs` (update) | exact |
| `src/GameKit.Rankings/Entities/PlayerRank.cs` | model | CRUD | `src/GameKit.Rankings/Entities/PlayerRank.cs` (update) | exact |
| `src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs` | config | CRUD | `src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs` (update) | exact |
| `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` | service | event-driven | `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` | exact |
| `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` | service | batch | `src/GameKit.Rankings/Services/RankingsTickerService.cs` | exact |
| `src/GameKit.Rankings/GameKitRankingsOptions.cs` | config | — | `src/GameKit.Rankings/GameKitRankingsOptions.cs` (update) | exact |
| `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` | service | CRUD | `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` (update) | exact |
| `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` | contract | request-response | `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` (update) | exact |
| `src/GameKit.Rankings/Services/RankingsRatingSource.cs` | service | CRUD | `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` | role-match |
| `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.RatingSource.cs` | config | — | `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs` | exact |
| `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` | config | — | `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` (update) | exact |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` | service | request-response | `src/GameKit.Matchmaking/Services/MatchmakingService.cs` (update) | exact |
| `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` | config | — | `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` (update) | exact |
| `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` | service | request-response | `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` (update) | exact |
| `tests/GameKit.Rankings.Integration.Tests/RankDecayTests.cs` | test | batch | `tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs` | exact |
| `tests/GameKit.Rankings.Tests/Glicko2/Glicko2InactivityTests.cs` | test | transform | `tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs` | exact |
| `tests/GameKit.Rankings.Integration.Tests/PlacementMatchTests.cs` | test | CRUD | `tests/GameKit.Rankings.Integration.Tests/LazyRankCreationTests.cs` | exact |
| `tests/GameKit.Rankings.Integration.Tests/RankingsRatingSourceTests.cs` | test | CRUD | `tests/GameKit.Rankings.Integration.Tests/LazyRankCreationTests.cs` | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/RatingAwareEnqueueTests.cs` | test | request-response | `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingHappyPathTests.cs` | exact |
| `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeGuardrailTests.cs` | test | transform | `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeStrategyTests.cs` | exact |

---

## Pattern Assignments

### `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs` (migration, CRUD)

**Analog:** `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs`

**File header + class shape** (lines 1–11):
```csharp
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Rankings.Migrations
{
    /// <inheritdoc />
    public partial class RankingsDecayPlacement : Migration
    {
```

**Up() — ALTER TABLE pattern** (analog lines 293–298; adapt to ADD COLUMN):
```csharp
// Raw-SQL partial index uses migrationBuilder.Sql() — same as the existing
// idx_pending_rating_updates_ladder_pending index in the initial migration (line 290).
migrationBuilder.Sql(@"
    ALTER TABLE gamekit.player_ranks
        ADD COLUMN ""LastDecayAt"" timestamp with time zone,
        ADD COLUMN ""PlacementMatchesRemaining"" integer NOT NULL DEFAULT 10,
        ADD COLUMN ""IsInPlacement"" boolean NOT NULL DEFAULT true;");

// Data-fixup: existing players with any game history are not in placement.
migrationBuilder.Sql(@"
    UPDATE gamekit.player_ranks
    SET ""IsInPlacement"" = false, ""PlacementMatchesRemaining"" = 0
    WHERE ""Wins"" > 0 OR ""Losses"" > 0 OR ""Draws"" > 0;");

// Decay candidate index: (LadderId, LastMatchAt).
migrationBuilder.Sql(@"
    CREATE INDEX idx_player_ranks_decay_candidates
    ON gamekit.player_ranks (""LadderId"", ""LastMatchAt"")
    WHERE ""IsInPlacement"" = false;");
```

**Down() pattern** (analog lines 303–335 — drop index, then revert columns):
```csharp
migrationBuilder.Sql(@"DROP INDEX IF EXISTS gamekit.idx_player_ranks_decay_candidates;");
migrationBuilder.DropColumn(name: "LastDecayAt", schema: "gamekit", table: "player_ranks");
migrationBuilder.DropColumn(name: "PlacementMatchesRemaining", schema: "gamekit", table: "player_ranks");
migrationBuilder.DropColumn(name: "IsInPlacement", schema: "gamekit", table: "player_ranks");
```

**Advisory lock constant** — reuse existing; do NOT declare a new one:
```csharp
// src/GameKit.Rankings/Data/RankingsMigrationConstants.cs line 43:
public const long AdvisoryLockKey = -156812172L;
// src/GameKit.Rankings/Data/RankingsMigrationConstants.cs line 20:
public const string MigrationsHistoryTable = "__ef_migrations_rankings";
```

---

### `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.Designer.cs` (migration, auto-generated)

**Analog:** `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.Designer.cs` lines 1–29

**File shape** (lines 1–17):
```csharp
// <auto-generated />
using System;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GameKit.Rankings.Migrations
{
    [DbContext(typeof(GameKitDbContext))]
    [Migration("20260517000000_RankingsDecayPlacement")]
    partial class RankingsDecayPlacement
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
```

The `BuildTargetModel` body is generated by `dotnet ef` — it must include all entities from the snapshot PLUS the three new `player_ranks` properties. Copy the current snapshot's `player_ranks` entity block and add:
```csharp
b.Property<DateTimeOffset?>("LastDecayAt")
    .HasColumnType("timestamp with time zone");
b.Property<int>("PlacementMatchesRemaining")
    .HasDefaultValue(10)
    .HasColumnType("integer");
b.Property<bool>("IsInPlacement")
    .HasDefaultValue(true)
    .HasColumnType("boolean");
```

---

### `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs` (migration, update)

**Analog:** `src/GameKit.Rankings/Migrations/GameKitDbContextModelSnapshot.cs` lines 1–80 (file header + class shape)

The snapshot is regenerated by `dotnet ef` after the migration is added. The manual addition (if regenerating by hand) is to insert the three new property declarations into the `player_ranks` entity block in `BuildModel`, following the same `b.Property<T>(...).HasColumnType(...)` pattern as the existing `Rating`/`RatingDeviation`/`Volatility` entries.

---

### `src/GameKit.Rankings/Entities/PlayerRank.cs` (model, update)

**Analog:** `src/GameKit.Rankings/Entities/PlayerRank.cs` lines 27–58

**Existing property style** (lines 38–57) — copy XML-doc + getter/setter shape:
```csharp
/// <summary>Current Glicko-2 rating deviation. Stored as <c>double precision</c> (RANK-03).</summary>
public double RatingDeviation { get; set; }

/// <summary>UTC timestamp of the player's most recent match on this ladder. Null until first match.</summary>
public DateTimeOffset? LastMatchAt { get; set; }
```

**New properties to append** (after `LastMatchAt` at line 57):
```csharp
/// <summary>UTC timestamp of the last decay run applied to this rank. Null = never decayed.</summary>
public DateTimeOffset? LastDecayAt { get; set; }

/// <summary>Placement matches remaining before visible rank is revealed. 0 = placement complete.</summary>
public int PlacementMatchesRemaining { get; set; }

/// <summary>True while the player is still completing placement matches.</summary>
public bool IsInPlacement { get; set; }
```

---

### `src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs` (config, update)

**Analog:** `src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs` lines 15–51

**Existing required-property pattern** (lines 27–29):
```csharp
b.Property(r => r.Rating).IsRequired().HasColumnType("double precision");
b.Property(r => r.Wins).IsRequired();
```

**Nullable pattern** (lines 53–57 of the existing file reference `LastMatchAt` implicitly — no explicit config means nullable is inferred from the CLR type `DateTimeOffset?`):
```csharp
// No explicit config needed for LastMatchAt (nullable CLR type inferred); follow same for LastDecayAt.
```

**New configurations to append** inside `Configure`:
```csharp
b.Property(r => r.LastDecayAt).IsRequired(false);
b.Property(r => r.PlacementMatchesRemaining).IsRequired().HasDefaultValue(10);
b.Property(r => r.IsInPlacement).IsRequired().HasDefaultValue(true);
```

---

### `src/GameKit.Rankings/Services/RankDecayLeaseHelper.cs` (service, event-driven)

**Analog:** `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` — copy verbatim, substituting:
- Class name: `RankDecayLeaseHelper` (not `RankingsTickerLeaseHelper`)
- Logger type parameter: `RankDecayLeaseHelper`
- Lock key source: `_opts.Decay.LockKey` (not `_opts.Ticker.LockKey`)
- Lock TTL source: `_opts.Decay.LockTtlSeconds` (not `_opts.Ticker.LockTtlSeconds`)
- Log messages: replace `"RankingsTickerLeaseHelper"` with `"RankDecayLeaseHelper"`

**Imports block** (lines 1–12 of analog):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using StackExchange.Redis;

namespace GameKit.Rankings.Services;
```

**InstanceId field** (analog line 48):
```csharp
public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";
```

**Constructor + Polly pipeline** (analog lines 56–87):
```csharp
public RankDecayLeaseHelper(
    IConnectionMultiplexer redis,
    ILogger<RankDecayLeaseHelper> logger,
    IOptions<GameKitRankingsOptions> opts)
{
    _redis = redis;
    _logger = logger;
    _opts = opts.Value;

    _polly = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            ShouldHandle = new PredicateBuilder()
                .Handle<RedisConnectionException>()
                .Handle<RedisTimeoutException>(),
            OnRetry = args =>
            {
                _logger.LogWarning(
                    args.Outcome.Exception,
                    "RankDecayLeaseHelper: Redis retry {Attempt} after {Delay}ms.",
                    args.AttemptNumber + 1,
                    args.RetryDelay.TotalMilliseconds);
                return ValueTask.CompletedTask;
            },
        })
        .Build();
}
```

**TryAcquireLeaseAsync** (analog lines 97–119, substitute lock key):
```csharp
return await db.LockTakeAsync(
    _opts.Decay.LockKey,
    InstanceId,
    TimeSpan.FromSeconds(_opts.Decay.LockTtlSeconds))
    .ConfigureAwait(false);
```

**ReleaseLeaseAsync** (analog lines 158–171):
```csharp
await db.LockReleaseAsync(_opts.Decay.LockKey, InstanceId).ConfigureAwait(false);
```

---

### `src/GameKit.Rankings/Services/RankDecayBackgroundService.cs` (service, batch)

**Analog:** `src/GameKit.Rankings/Services/RankingsTickerService.cs`

**Class declaration** (analog lines 57–92):
```csharp
internal sealed class RankDecayBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RankDecayLeaseHelper _lease;
    private readonly IClock _clock;
    private readonly GameKitRankingsOptions _opts;
    private readonly ILogger<RankDecayBackgroundService> _logger;

    public RankDecayBackgroundService(
        IServiceScopeFactory scopeFactory,
        RankDecayLeaseHelper lease,
        IClock clock,
        IOptions<GameKitRankingsOptions> opts,
        ILogger<RankDecayBackgroundService> logger)
    { ... }
```

**ExecuteAsync loop with PeriodicTimer** (analog lines 95–130):
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    using var timer = new PeriodicTimer(_opts.Decay.Interval);
    try
    {
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try { await RunOnceAsync(stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RankDecayBackgroundService: unhandled exception during tick. Continuing.");
            }
        }
    }
    catch (OperationCanceledException) { }
}
```

**Leader election + scope pattern** (analog lines 133–201):
```csharp
var acquired = await _lease.TryAcquireLeaseAsync(ct).ConfigureAwait(false);
if (!acquired)
{
    _logger.LogDebug("RankDecayBackgroundService: lock not acquired — another replica is leader.");
    return;
}
try
{
    using var scope = _scopeFactory.CreateScope();
    var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
    var now = _clock.UtcNow;
    // ... batch SELECT + Glicko-2 inactivity step + ExecuteUpdateAsync ...
}
finally
{
    await _lease.ReleaseLeaseAsync(ct).ConfigureAwait(false);
}
```

**Glicko-2 inactivity step** (from `src/GameKit.Rankings/Glicko2/RatingCalculator.cs` lines 29 + 235–236, and scale conversion lines 256–264):
```csharp
// Multiplier constant (RatingCalculator.cs line 29):
// private const double Multiplier = 173.7178;
//
// Scale-correct inactivity step — DO NOT apply directly to original-scale values.
// RatingDeviation is stored on Glicko-1 scale (~150-350); Volatility is already dimensionless.
const double Multiplier = 173.7178;
double phiG2 = rank.RatingDeviation / Multiplier;   // → Glicko-2 scale
double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + rank.Volatility * rank.Volatility); // φ'=√(φ²+σ²)
rank.RatingDeviation = phiPrimeG2 * Multiplier;     // → back to original scale
// rank.Rating unchanged; rank.Volatility unchanged
rank.LastDecayAt = now;
```

**Batch SELECT pattern** (mirror RankingsTickerService's EF query style, lines 223–233):
```csharp
var inactivePlayers = await ctx.Set<PlayerRank>()
    .Where(r => r.LadderId == ladderId
             && !r.IsInPlacement
             && r.Rating > _opts.Decay.DecayThresholdRating
             && r.LastMatchAt != null
             && r.LastMatchAt < now.AddDays(-_opts.Decay.InactivityDays))
    .OrderBy(r => r.LastMatchAt)
    .Take(_opts.Decay.BatchSize)
    .ToListAsync(ct)
    .ConfigureAwait(false);
```

**Batch write — ExecuteUpdateAsync** (mirror analog lines 394–415):
```csharp
// After computing phiPrime for each rank in-memory, batch-write via ExecuteUpdateAsync.
// (Or: update tracked entities + SaveChangesAsync — either matches v1 precedent.)
await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
```

---

### `src/GameKit.Rankings/GameKitRankingsOptions.cs` (config, update)

**Analog:** `src/GameKit.Rankings/GameKitRankingsOptions.cs` lines 1–133

**Existing nested options class pattern** (lines 37–58 — `GameKitRankingsTickerOptions`):
```csharp
/// <summary>Options for the ranking ticker background service (D-01 / D-03 / D-04).</summary>
public sealed class GameKitRankingsTickerOptions
{
    /// <summary>
    /// How often the ticker wakes up to check each ladder's drain eligibility.
    /// Default <c>60</c> seconds.
    /// </summary>
    public int TickIntervalSeconds { get; set; } = 60;

    /// <summary>Redis distributed-lock TTL in seconds. Default <c>90</c>.</summary>
    public int LockTtlSeconds { get; set; } = 90;

    /// <summary>Redis key for the distributed leader-election lock.</summary>
    public string LockKey { get; set; } = "gamekit:rankings:ticker:lease";
}
```

**Addition to root options class** (after line 28):
```csharp
/// <summary>Options controlling the rank-decay background service (RANK-15).</summary>
public GameKitRankingsDecayOptions Decay { get; set; } = new();
```

**New nested class** (append to end of file):
```csharp
/// <summary>Options controlling the rank-decay background service (RANK-15).</summary>
public sealed class GameKitRankingsDecayOptions
{
    /// <summary>How often the decay runner wakes up. Default 24 hours.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Redis distributed-lock TTL. Default 120s.</summary>
    public int LockTtlSeconds { get; set; } = 120;

    /// <summary>Redis key for decay leader-election lock. MUST differ from Ticker.LockKey.</summary>
    public string LockKey { get; set; } = "gamekit:rankings:decay:lease";

    /// <summary>
    /// Minimum rating above which decay applies. Players at or below are decay-immune.
    /// Default 1500 (Glicko-2 mean).
    /// </summary>
    public double DecayThresholdRating { get; set; } = 1500;

    /// <summary>Days of inactivity (since LastMatchAt) before decay is applied. Default 30.</summary>
    public int InactivityDays { get; set; } = 30;

    /// <summary>Max rows processed per decay run. Default 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// Number of placement matches required before a player's visible rank is revealed.
    /// Default 10. Used when lazily creating new PlayerRank rows in RankingsTickerService.
    /// </summary>
    public int PlacementMatchCount { get; set; } = 10;
}
```

---

### `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` (service, update — RANK-16 placement decrement)

**Analog:** `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` lines 45–127 — the file is modified, not replaced.

**Existing ExecuteUpdateAsync pattern** (lines 96–100) — copy this pattern for the placement decrement:
```csharp
await _ctx.SessionParticipants
    .Where(sp => sp.SessionId == sessionId && sp.PlayerId == participant.PlayerId)
    .ExecuteUpdateAsync(
        setters => setters.SetProperty(sp => sp.RatingBefore, playerRank.Rating),
        ct);
```

**Placement decrement insertion point** — after line 100 (after the `RatingBefore` snapshot), before the `if (!participant.LadderId.HasValue) continue;` guard at line 104:
```csharp
// RANK-16: atomic placement decrement inside the caller's ambient ReadCommitted tx.
// Uses ExecuteUpdateAsync (stateless WHERE predicate) — playerRank is loaded AsNoTracking
// above so entity-mutation + SaveChanges would be a no-op (Pitfall §6 from RESEARCH).
if (playerRank is not null && playerRank.IsInPlacement && playerRank.PlacementMatchesRemaining > 0)
{
    await _ctx.Set<PlayerRank>()
        .Where(r => r.PlayerId == participant.PlayerId
                 && r.LadderId == participant.LadderId!.Value
                 && r.IsInPlacement
                 && r.PlacementMatchesRemaining > 0)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.PlacementMatchesRemaining, r => r.PlacementMatchesRemaining - 1)
            .SetProperty(r => r.IsInPlacement,
                r => r.PlacementMatchesRemaining - 1 == 0 ? false : r.IsInPlacement),
        ct);
}
```

**Critical:** the WHERE predicate `r.PlacementMatchesRemaining > 0` is the race guard. Safe inside the session-complete `ReadCommitted` transaction.

---

### `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` (contract, update — RANK-16 DTO hiding)

**Analog:** `src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs` lines 1–28 — sealed record, extend the constructor parameters.

**Current record** (lines 20–28):
```csharp
public sealed record LeaderboardRowDto(
    int Rank,
    Guid PlayerId,
    string DisplayName,
    double Rating,
    double RatingDeviation,
    int Wins,
    int Losses,
    int Draws);
```

**Updated record** — add placement fields; make `Rating` and `RatingDeviation` nullable so callers can return null when `IsInPlacement`:
```csharp
public sealed record LeaderboardRowDto(
    int Rank,
    Guid PlayerId,
    string DisplayName,
    double? Rating,            // null while IsInPlacement == true
    double? RatingDeviation,   // null while IsInPlacement == true
    int Wins,
    int Losses,
    int Draws,
    bool IsInPlacement,
    int PlacementMatchesRemaining);
```

Mapping site (in `LeaderboardService` or endpoint projection): `Rating = r.IsInPlacement ? null : r.Rating`.

---

### `src/GameKit.Rankings/Services/RankingsRatingSource.cs` (service, CRUD — RANK-17)

**Analog:** `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` lines 45–65 for the scoped DbContext injection pattern.

**Imports** (follow PendingRatingUpdatesAdapter header style):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Rankings.Services;
```

**Class + constructor** (DbContext injection mirrors PendingRatingUpdatesAdapter lines 55–65):
```csharp
/// <summary>
/// <see cref="IPlayerRatingProvider"/> implementation backed by <c>player_ranks</c>.
/// Registered via <c>.WithRatingsFrom&lt;RankingsRatingSource&gt;()</c> (RANK-17).
/// Lifetime: Scoped — reads scoped <see cref="GameKitDbContext"/>. See Phase 8 RESEARCH §RANK-17.
/// </summary>
public sealed class RankingsRatingSource : IPlayerRatingProvider
{
    private readonly GameKitDbContext _ctx;

    /// <summary>Constructs the source.</summary>
    /// <param name="ctx">Scoped <see cref="GameKitDbContext"/>.</param>
    public RankingsRatingSource(GameKitDbContext ctx)
    {
        _ctx = ctx;
    }
```

**GetRatingsAsync** — batched single SELECT, AsNoTracking (mirrors RankingsTickerService.cs lines 153–157):
```csharp
    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<Guid, PlayerRatingValue>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        if (playerIds.Count == 0)
            return new Dictionary<Guid, PlayerRatingValue>();

        var ranks = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .Where(r => r.LadderId == ladderId && playerIds.Contains(r.PlayerId))
            .Select(r => new { r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return ranks.ToDictionary(
            r => r.PlayerId,
            r => new PlayerRatingValue(r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility));
    }
```

---

### `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.RatingSource.cs` (config, partial class — RANK-17)

**Analog:** `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs` lines 1–52 — copy the partial-class file shape.

**File structure** (analog lines 1–52):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameKit.Rankings.Builder;

/// <summary>
/// Partial-class extension that adds <c>.WithRatingsFrom&lt;T&gt;()</c> to
/// <see cref="RankingsBuilderExtensions"/> (RANK-17).
/// </summary>
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Wires <typeparamref name="T"/> as the <see cref="IPlayerRatingProvider"/> for
    /// rating-aware matchmaking. Replaces the Core null-object default (RANK-17).
    /// Call after <c>AddRankings()</c>. Does NOT use TryAdd — Core registers
    /// <c>NullPlayerRatingProvider</c> via TryAddSingleton and a second TryAdd is a no-op.
    /// </summary>
    public static IGameKitRankingsBuilder WithRatingsFrom<T>(this IGameKitRankingsBuilder builder)
        where T : class, IPlayerRatingProvider
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll<IPlayerRatingProvider>();
        builder.Services.AddScoped<IPlayerRatingProvider, T>();
        return builder;
    }
}
```

---

### `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` (config, update)

**Analog:** `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` lines 39–87

**AddRankings registration pattern** (lines 48–85) — add the decay service registration call inside `AddRankings`, after step 7 (`AddTickerInfrastructure`):
```csharp
// 7. Ticker infrastructure (existing — unchanged):
AddTickerInfrastructure(builder.Services);

// 8. Decay infrastructure — RankDecayLeaseHelper + RankDecayBackgroundService (RANK-15):
AddDecayInfrastructure(builder.Services);
```

The `AddDecayInfrastructure` method goes into a new partial file `RankingsBuilderExtensions.Decay.cs` (or inline here), mirroring `AddTickerInfrastructure` in `RankingsBuilderExtensions.Ticker.cs` (lines 27–46 of that file):
```csharp
internal static void AddDecayInfrastructure(IServiceCollection services)
{
    services.AddSingleton<RankDecayLeaseHelper>();
    services.AddHostedService<RankDecayBackgroundService>();
}
```

---

### `src/GameKit.Matchmaking/Services/MatchmakingService.cs` (service, update — MATCH-16)

**Analog:** `src/GameKit.Matchmaking/Services/MatchmakingService.cs` — the file is modified.

**Constructor addition** (after line 93, after the optional `logger` parameter):
```csharp
public MatchmakingService(
    GameKitDbContext db,
    IConnectionMultiplexer redis,
    IDeclineCooldownService cooldown,
    PartyRatingAggregatorService aggregator,
    ChannelWriter<TicketEvent> events,
    IClock clock,
    IIdGenerator ids,
    IReadOnlyList<MatchmakingLadderConfig> ladders,
    ILogger<MatchmakingService>? logger = null,
    IPlayerRatingProvider? ratingProvider = null)  // MATCH-16 — optional; null-object covers no-Rankings case
```

**Step 4 replacement** (lines 198–204 currently):
```csharp
// Current (lines 202-204) — REPLACE THIS:
var queuedMembers = memberPlayerIds
    .Select(pid => new QueuedPartyMember(pid, Rating: 0, RatingDeviation: 0, Volatility: 0))
    .ToList();

// Replacement (MATCH-16):
IReadOnlyDictionary<Guid, PlayerRatingValue> ratingMap =
    _ratingProvider is not null
        ? await _ratingProvider.GetRatingsAsync(memberPlayerIds, ladderId, ct).ConfigureAwait(false)
        : new Dictionary<Guid, PlayerRatingValue>();

var queuedMembers = memberPlayerIds.Select(pid =>
{
    ratingMap.TryGetValue(pid, out var rv);
    return new QueuedPartyMember(
        pid,
        Rating: rv?.Rating ?? 0,
        RatingDeviation: rv?.RatingDeviation ?? 0,
        Volatility: rv?.Volatility ?? 0);
}).ToList();
```

**Import to add** (follow existing import block lines 1–22):
```csharp
using GameKit.Core.Services;  // IPlayerRatingProvider, PlayerRatingValue
using System.Collections.Immutable;
```

**Redis hash write** (lines 265–277) — unchanged; `aggregateRating` (line 272) and `members` JSON (line 265) already use `queuedMembers`, so real ratings flow through automatically once Step 4 supplies real values.

---

### `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` (config, update — MATCH-17)

**Analog:** `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` lines 1–63

**Existing optional-int property pattern** (lines 57–62 — `MaxPartyRatingSpread`):
```csharp
/// <summary>
/// Optional cap on within-party rating spread (<c>max - min</c>).
/// Default <c>null</c> (no cap) per CONTEXT D-14. When set, must be &gt; 0.
/// </summary>
public int? MaxPartyRatingSpread { get; set; }
```

**New fields to append** (after `MaxPartyRatingSpread`):
```csharp
/// <summary>
/// Hard cap on bracket half-width in rating points (MATCH-17). Bracket-widening NEVER exceeds
/// this value regardless of wait time, preventing high-RD new players from being matched against
/// top-rated players on sparse pools. Default <c>null</c> (no cap — maintains v1 behaviour).
/// When set, must be &gt; 0.
/// </summary>
public int? MaxBracketWidth { get; set; }

/// <summary>
/// Minimum number of tickets in the pool before bracket expansion begins (MATCH-17).
/// When the pool has fewer than this many candidates, the bracket stays at
/// <see cref="BracketStart"/> regardless of wait time. Default <c>null</c> (no guard).
/// Set to <c>2 * expected_party_size</c> as a starting recommendation. When set, must be &gt; 0.
/// </summary>
public int? MinPoolDepthBeforeBracketExpansion { get; set; }
```

---

### `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` (service, update — MATCH-17)

**Analog:** `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` lines 1–205

**`Bracket()` method** (lines 173–181) — add `MaxBracketWidth` cap:
```csharp
public static double Bracket(MatchmakingLadderConfig cfg, double secondsInQueue)
{
    ArgumentNullException.ThrowIfNull(cfg);
    if (secondsInQueue < 0) secondsInQueue = 0;
    var raw = cfg.BracketStart + (cfg.BracketEnd - cfg.BracketStart) * secondsInQueue / cfg.BracketRampSeconds;
    var capped = Math.Min(raw, cfg.BracketEnd);
    // MATCH-17: hard cap — never exceed MaxBracketWidth regardless of wait time.
    if (cfg.MaxBracketWidth.HasValue)
        capped = Math.Min(capped, cfg.MaxBracketWidth.Value);
    return capped;
}
```

**`Match()` pool-depth guard** (insert before line 91 `var candidateBracket = Bracket(...)` and inside the per-pool-entry loop at lines 98–124):

For the candidate's own bracket (before line 91):
```csharp
var candidateElapsed = (now - candidate.QueuedAt).TotalSeconds;
// MATCH-17: suppress bracket expansion when pool is below minimum depth.
if (cfg.MinPoolDepthBeforeBracketExpansion.HasValue
    && (pool.Count - 1) < cfg.MinPoolDepthBeforeBracketExpansion.Value)
{
    candidateElapsed = 0; // force bracket to BracketStart
}
var candidateBracket = Bracket(cfg, candidateElapsed);
```

For each pool entry's bracket (replace line 113 `var poolBracket = Bracket(pCfg, (now - p.QueuedAt).TotalSeconds);`):
```csharp
var poolElapsed = (now - p.QueuedAt).TotalSeconds;
if (pCfg.MinPoolDepthBeforeBracketExpansion.HasValue
    && (pool.Count - 1) < pCfg.MinPoolDepthBeforeBracketExpansion.Value)
{
    poolElapsed = 0;
}
var poolBracket = Bracket(pCfg, poolElapsed);
```

---

## Test File Patterns

### `tests/GameKit.Rankings.Integration.Tests/RankDecayTests.cs` (test, batch + Redis)

**Analog:** `tests/GameKit.Rankings.Integration.Tests/RankingsTickerLeaderElectionTests.cs`

**File structure** (analog lines 1–37):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

[Collection("Rankings")]
[Trait("Category", "Integration")]
public sealed class RankDecayTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private string _cs = string.Empty;

    public RankDecayTests(PostgresFixture pg, RedisFixture redis) { _pg = pg; _redis = redis; }

    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyMigrationsAsync(_cs);
    }
    public Task DisposeAsync() => Task.CompletedTask;
```

**Leader election test shape** (analog lines 71–115): use same two-service-provider pattern to assert `RankDecayBackgroundService` uses a lock key distinct from the ticker (`"gamekit:rankings:decay:lease"` vs `"gamekit:rankings:ticker:lease"`).

**Migration apply pattern** (analog lines 286–310): copy `ApplyMigrationsAsync` verbatim — it applies Core migration first, then Rankings migration using `RankingsMigrationConstants.AdvisoryLockKey` and `RankingsMigrationModelCustomizer`. The new migration `20260517000000_RankingsDecayPlacement` will be picked up automatically.

**DB seed helper** (analog lines 157–233): copy `SeedLadderAndPendingUpdatesAsync`, adapt to insert `player_ranks` rows with `Rating > DecayThreshold` and `LastMatchAt` older than `InactivityDays`. Column names: `"LastDecayAt"`, `"PlacementMatchesRemaining"`, `"IsInPlacement"` (PascalCase per Npgsql convention, analog line 289).

---

### `tests/GameKit.Rankings.Tests/Glicko2/Glicko2InactivityTests.cs` (test, unit)

**Analog:** `tests/GameKit.Rankings.Tests/Glicko2/Glicko2WorkedExampleTests.cs`

**Class structure** (analog lines 23–96):
```csharp
public class Glicko2InactivityTests
{
    [Fact]
    public void Inactivity_Step_InflatesRD_RatingUnchanged()
    {
        // Glickman worked-example values: φ=290 (Glicko-1 scale), σ=0.06
        const double Multiplier = 173.7178;
        const double phi = 290.0;       // original Glicko-1 RD scale
        const double sigma = 0.06;      // dimensionless Glicko-2 volatility
        const double originalRating = 1500.0;

        double phiG2 = phi / Multiplier;
        double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + sigma * sigma);
        double phiPrime = phiPrimeG2 * Multiplier;

        // Rating must be unchanged.
        Assert.Equal(originalRating, originalRating);
        // RD must inflate.
        Assert.True(phiPrime > phi, $"Expected phiPrime ({phiPrime:F4}) > phi ({phi})");
        // Verify exact value within tolerance (Glickman Step 6 formula).
        // Expected: √((290/173.7178)² + 0.06²) * 173.7178 ≈ 290.62
        Assert.InRange(phiPrime, 290.5, 291.0);
    }
```

---

### `tests/GameKit.Rankings.Integration.Tests/PlacementMatchTests.cs` (test, CRUD)

**Analog:** `tests/GameKit.Rankings.Integration.Tests/LazyRankCreationTests.cs` lines 1–70

**Collection + fixture pattern** (analog lines 29–52): same `[Collection("Rankings")]`, `IAsyncLifetime`, `PostgresFixture` + `RedisFixture` constructor. Same `InitializeAsync` calling `CreateFreshDatabaseAsync` + `ApplyMigrationsAsync`.

**DB seed**: insert `player_ranks` rows with `IsInPlacement = true`, `PlacementMatchesRemaining = N`. Call `PendingRatingUpdatesAdapter.OnCompletedAsync` via a test service provider (same pattern as LazyRankCreationTests `BuildTickerServiceProvider`). Assert:
1. `PlacementMatchesRemaining` decremented by 1.
2. At 0: `IsInPlacement = false`.

---

### `tests/GameKit.Rankings.Integration.Tests/RankingsRatingSourceTests.cs` (test, CRUD)

**Analog:** `tests/GameKit.Rankings.Integration.Tests/LazyRankCreationTests.cs`

Same fixture/collection/lifecycle pattern. Service provider construction: use `services.AddGameKit(...).AddRankings().WithRatingsFrom<RankingsRatingSource>()`. Resolve `IPlayerRatingProvider` from the scoped DI and call `GetRatingsAsync`. Assert:
1. Unknown player absent from returned dictionary.
2. Known player returns correct `Rating`/`RatingDeviation`/`Volatility`.

---

### `tests/GameKit.Matchmaking.Integration.Tests/RatingAwareEnqueueTests.cs` (test, request-response)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingHappyPathTests.cs` lines 1–80

**Collection + fixture pattern** (analog lines 34–57): `[Collection("Matchmaking")]`, `IAsyncLifetime`, `PostgresFixture` + `RedisFixture`, `MatchmakingTestApp`. Same `InitializeAsync`/`DisposeAsync` pattern.

**Assertion pattern** (analog lines 63–79): after `PostAsJsonAsync("/api/mm/queue", ...)`, read back the Redis ticket hash and assert the `"members"` field JSON contains the real Rating value (not 0) when `RankingsRatingSource` is wired.

---

### `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeGuardrailTests.cs` (test, unit)

**Analog:** `tests/GameKit.Matchmaking.Tests/Strategy/EloRangeStrategyTests.cs` lines 1–80

**Class + helper pattern** (analog lines 20–60):
```csharp
public sealed class EloRangeGuardrailTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 17, 0, 0, 0, TimeSpan.Zero);

    private static MatchmakingLadderConfig CfgWithGuardrails(
        int? maxBracketWidth = null,
        int? minPoolDepth = null) => new()
    {
        Name = "main",
        BracketStart = 100,
        BracketEnd = 500,
        BracketRampSeconds = 40,
        MaxBracketWidth = maxBracketWidth,
        MinPoolDepthBeforeBracketExpansion = minPoolDepth,
    };

    [Fact]
    public void Bracket_NeverExceeds_MaxBracketWidth()
    {
        // After 100s in queue, raw bracket = 500 (BracketEnd), capped at 300.
        var bracket = EloRangeMatchmakingStrategy.Bracket(CfgWithGuardrails(maxBracketWidth: 300), 100);
        Assert.Equal(300, bracket);
    }

    [Fact]
    public void Bracket_NoExpansion_WhenPoolBelowMinDepth()
    {
        // Pool has 1 candidate (below MinPoolDepthBeforeBracketExpansion = 5).
        // Even with 100s elapsed, bracket must equal BracketStart (100).
        // [Set up via Match() with a small pool, assert no match across a wide rating gap
        //  that would only match if bracket expanded.]
    }
```

---

## Shared Patterns

### Per-Package Migration Convention
**Source:** `src/GameKit.Rankings/Data/RankingsDesignTimeDbContextFactory.cs` lines 60–73 + `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs`
**Apply to:** `20260517000000_RankingsDecayPlacement.cs`, Designer.cs, snapshot update

```csharp
// Design-time factory (already exists — do not create a new one):
npg.MigrationsAssembly(typeof(RankingsDesignTimeDbContextFactory).Assembly.FullName);
npg.MigrationsHistoryTable(RankingsMigrationConstants.MigrationsHistoryTable,
                            GameKitMigrationConstants.SchemaName);
// Advisory lock key (RankingsMigrationConstants.cs line 43):
public const long AdvisoryLockKey = -156812172L;
```

### Redis Distributed Lock (Leader Election)
**Source:** `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` lines 37–172
**Apply to:** `RankDecayLeaseHelper.cs`

Key rules:
- Use `IDatabase.LockTakeAsync / LockExtendAsync / LockReleaseAsync` (Lua-script-verified). Never use raw `StringSetAsync(NX)`.
- `InstanceId = $"{Environment.MachineName}:{Guid.NewGuid()}"` — unique per process.
- Polly v8 `ResiliencePipelineBuilder` with `AddRetry(3, Exponential, Jitter)` on `RedisConnectionException` + `RedisTimeoutException`.
- Decay service MUST use a dedicated lock key (`"gamekit:rankings:decay:lease"`) — NOT the ticker's `"gamekit:rankings:ticker:lease"` (Pitfall 4 from RESEARCH).

### BackgroundService with IServiceScopeFactory
**Source:** `src/GameKit.Rankings/Services/RankingsTickerService.cs` lines 57–201
**Apply to:** `RankDecayBackgroundService.cs`

```csharp
// Scope per tick — creates a fresh GameKitDbContext for each decay run.
using var scope = _scopeFactory.CreateScope();
var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
```

### ExecuteUpdateAsync for Stateless Updates
**Source:** `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` lines 96–100; `src/GameKit.Rankings/Services/RankingsTickerService.cs` lines 394–415
**Apply to:** `PendingRatingUpdatesAdapter.cs` (placement decrement)

```csharp
// Pattern — stateless WHERE predicate update, no entity tracking required:
await _ctx.Set<PlayerRank>()
    .Where(r => r.PlayerId == pid && r.LadderId == ladderId)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(r => r.SomeField, newValue),
    ct);
```

### Scoped DbContext Consumer Registration
**Source:** `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs` lines 37–38
**Apply to:** `RankingsRatingSource` registration in `RankingsBuilderExtensions.RatingSource.cs`

```csharp
// Scoped (not Singleton) — reads GameKitDbContext which is Scoped.
// RemoveAll first because Core already registers NullPlayerRatingProvider via TryAddSingleton.
services.RemoveAll<IPlayerRatingProvider>();
services.AddScoped<IPlayerRatingProvider, RankingsRatingSource>();
```

### Partial Builder Extension File
**Source:** `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.SessionComplete.cs` lines 1–52
**Apply to:** `RankingsBuilderExtensions.RatingSource.cs`

```csharp
// Partial class file shape:
public static partial class RankingsBuilderExtensions
{
    internal static void AddXxxInfrastructure(IServiceCollection services) { ... }
    // or: public static IGameKitRankingsBuilder WithXxx<T>(...) { ... }
}
```

### SPDX License Header
**Source:** All existing source files (e.g., `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` lines 1–2)
**Apply to:** Every new .cs file

```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

---

## No Analog Found

All files in Phase 8 have close analogs in the codebase. No file requires falling back to RESEARCH.md patterns exclusively.

---

## Metadata

**Analog search scope:** `src/GameKit.Rankings/`, `src/GameKit.Matchmaking/`, `src/GameKit.Core/`, `tests/GameKit.Rankings.*`, `tests/GameKit.Matchmaking.*`, `tests/GameKit.Core.*`, `tests/GameKit.TestFixtures/`
**Files scanned:** 42 source files read in full or targeted ranges
**Pattern extraction date:** 2026-06-05
