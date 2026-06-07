# Phase 8: Rankings Depth + Rating-Aware Matchmaking — Research

**Researched:** 2026-06-05
**Domain:** GameKit.Rankings schema finalization + IPlayerRatingProvider seam wiring + EloRange guardrails
**Confidence:** HIGH — every claim is grounded in direct file reads of the actual codebase.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

- **Schema:** A single new Rankings migration adds `last_decay_at` (timestamptz null), `placement_matches_remaining` (int, default = configured N), `is_in_placement` (bool, default true). Uses EXISTING Rankings advisory lock (-156812172). This is the LAST structural change to `player_ranks` (SC#5).
- **RANK-15 decay:** RD inflation only (`φ' = √(φ² + σ²)`), rating unchanged. Leader-elected `BackgroundService` mirrors `MatchmakerTickerService` pattern (Redis `SET NX PX` + Polly). Batched updates.
- **RANK-16 placement:** Atomic decrement of `placement_matches_remaining` inside existing session-complete transaction. `is_in_placement` flips to false at 0. Visible rank hidden in API/DTO while `is_in_placement`.
- **RANK-17 `RankingsRatingSource`:** `services.RemoveAll<IPlayerRatingProvider>(); services.AddSingleton<IPlayerRatingProvider, RankingsRatingSource>();` — NOT TryAdd (Core already registered null-object via TryAddSingleton). Opt-in via `.WithRatingsFrom<RankingsRatingSource>()`.
- **MATCH-16:** Replace `MatchmakingService.EnqueueAsync` line 203 hardcoded `Rating: 0` with `IPlayerRatingProvider.GetRatingsAsync(memberPlayerIds, ladderId, ct)`. Core interface ONLY — NO hard Rankings `ProjectReference`.
- **MATCH-17 guardrails:** `MaxBracketWidth` + `MinPoolDepthBeforeBracketExpansion` ship SIMULTANEOUSLY with MATCH-16. Both added to `MatchmakingLadderConfig` and enforced in `EloRangeMatchmakingStrategy.Bracket/Match`.

### Claude's Discretion

Exact option-class field names, DTO shaping for hidden placement rank, decay batch size/interval defaults, test structure — follow v1 patterns (MatchmakerTickerService leader election, batched IRankingAlgorithm, EloRange options, session-complete transaction).

### Deferred Ideas (OUT OF SCOPE)

- Regional pools + backfill → Phase 9
- Account merge reading player_ranks → Phase 10
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RANK-15 | Configurable rank decay for inactive players above a rating threshold — RD inflation (Glicko-2 "no games played" period update), leader-elected BackgroundService | §RANK-15 decay section; Glicko2/RatingCalculator.cs line 78–82; RankingsTickerLeaseHelper pattern |
| RANK-16 | Placement matches — initial high-RD calibration; visible rank hidden until N placements complete | §RANK-16 placement section; PendingRatingUpdatesAdapter.cs as the session-complete wiring point |
| RANK-17 | `RankingsRatingSource : IPlayerRatingProvider` in GameKit.Rankings, opt-in via `.WithRatingsFrom<>()` | §RANK-17 seam section; IPlayerRatingProvider.cs already shipped in Phase 7 |
| MATCH-16 | Rating-aware EloRange — replace hardcoded `Rating: 0` in MatchmakingService.cs:203 | §MATCH-16 section; MatchmakingService.cs Step 4 |
| MATCH-17 | `MaxBracketWidth` + `MinPoolDepthBeforeBracketExpansion` ship simultaneously with MATCH-16 | §MATCH-17 guardrails section; EloRangeMatchmakingStrategy.cs; MatchmakingLadderConfig.cs |
</phase_requirements>

---

## Summary

Phase 8 has three distinct bodies of work that must ship together as a coherent unit: (1) the Rankings schema finalization migration that adds decay and placement columns to `player_ranks`, (2) three ranking-depth services (decay `BackgroundService`, placement decrement hook, and `RankingsRatingSource`), and (3) the matchmaking rating wire-up with mandatory anti-feedback-loop guardrails.

The code that matters is already in the repository. `IPlayerRatingProvider` (Phase 7) is live at `src/GameKit.Core/Services/IPlayerRatingProvider.cs`. The hardcoded `Rating: 0` injection point is at `MatchmakingService.cs:203`. The Glicko-2 inactivity step (`φ' = √(φ² + σ²)`) is already implemented in `RatingCalculator.CalculateNewRatingDeviation` (line 236 of `Glicko2/RatingCalculator.cs`). `RankingsTickerLeaseHelper` provides the exact distributed-lock pattern to copy for the decay service. The `EloRangeMatchmakingStrategy.Bracket()` method is the exact site for adding the `MaxBracketWidth` cap.

**Primary recommendation:** Schema migration first (deterministic timestamp `20260517000000`), then the three Rankings services in a single wave, then the Matchmaking wire-up + guardrails in the same wave. All five requirements can be verified by a combined Testcontainers integration test that exercises real Postgres + Redis.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Rank decay background job | API / Backend (Rankings BackgroundService) | Redis (leader election) | RD inflation is a server-side algorithm operation; Redis serialises multi-replica execution |
| Placement match decrement | API / Backend (Rankings, inside session-complete tx) | Database | Atomic; must ride existing ReadCommitted transaction in `PendingRatingUpdatesAdapter.OnCompletedAsync` |
| Visible rank suppression | API / Backend (Rankings read endpoint + leaderboard DTO) | — | Presentation rule on the HTTP response; `is_in_placement` flag is authoritative |
| `RankingsRatingSource` | API / Backend (Rankings → Core seam) | Database (player_ranks SELECT) | Implements Core interface; reads scoped DbContext; lifetime decision is Scoped (see §RANK-17) |
| Rating injection at enqueue | API / Backend (Matchmaking) | Core seam | MATCH-16 reads Core's `IPlayerRatingProvider`; no ranking dep at runtime |
| Bracket guardrails | API / Backend (Matchmaking strategy) | — | Pure in-memory guard inside `EloRangeMatchmakingStrategy` |

---

## Standard Stack

No new NuGet packages are added by this phase. All required libraries are already pinned in `Directory.Packages.props`.

| Library | Version | Purpose | Why |
|---------|---------|---------|-----|
| EF Core 10 / Npgsql | 10.0.6 / 10.0.1 | New migration + entity config changes | Already in repo; no change |
| StackExchange.Redis | 2.8.41 | Decay service leader election | Already in repo; `LockTakeAsync` / `LockExtendAsync` / `LockReleaseAsync` |
| Polly | 8.5.x | Resilience pipeline in decay `BackgroundService` | Already in repo; mirror `RankingsTickerLeaseHelper` |

[VERIFIED: codebase] — no new packages, no `Directory.Packages.props` edits required.

---

## Package Legitimacy Audit

No new packages are introduced in this phase.

**Packages removed due to slopcheck [SLOP] verdict:** none
**Packages flagged as suspicious [SUS]:** none

---

## Architecture Patterns

### System Architecture Diagram

```
  [session-complete request]
           │
           ▼
  PendingRatingUpdatesAdapter.OnCompletedAsync
  (inside existing ReadCommitted tx)
           │ decrement placement_matches_remaining
           │ if 0 → is_in_placement = false
           │
           ▼
  player_ranks (Postgres)  ◄──── [RankingsTickerService drains PendingRatingUpdate rows]
       │  │
       │  └─── [GET /api/rankings/{ladderId}/me or leaderboard]
       │             │ is_in_placement=true → hide visible rating in DTO
       │
       └───────────────────────────────────────────────────────────────────┐
                                                                           │
  [RankDecayBackgroundService]                                             │
    PeriodicTimer → TryAcquireLeaseAsync (Redis SET NX PX)                │
    → batch SELECT inactive players above threshold                        │
    → apply φ' = √(φ²+σ²), rating unchanged                              │
    → stamp last_decay_at = now                                            │
                                                                           │
  [MatchmakingService.EnqueueAsync]                                        │
    IPlayerRatingProvider.GetRatingsAsync(memberPlayerIds, ladderId)  ◄───┘
    (resolved via RankingsRatingSource or NullPlayerRatingProvider)
    → writes real Rating/RD into Redis ticket hash "members" + "aggregateRating"
    │
    ▼
  EloRangeMatchmakingStrategy.Match()
    Bracket() capped at MaxBracketWidth
    pool depth guard via MinPoolDepthBeforeBracketExpansion
```

### Recommended Project Structure

No new directories. Additions slot into existing layout:

```
src/GameKit.Rankings/
├── Entities/
│   └── PlayerRank.cs                  [ADD: LastDecayAt, PlacementMatchesRemaining, IsInPlacement]
├── Data/
│   ├── Configurations/
│   │   └── PlayerRankConfiguration.cs [UPDATE: three new properties]
│   └── Migrations/
│       ├── 20260517000000_RankingsDecayPlacement.cs       [NEW]
│       ├── 20260517000000_RankingsDecayPlacement.Designer.cs [NEW]
│       └── GameKitDbContextModelSnapshot.cs               [UPDATED]
├── Services/
│   ├── RankDecayBackgroundService.cs  [NEW]
│   ├── RankDecayLeaseHelper.cs        [NEW — copy of RankingsTickerLeaseHelper shape]
│   └── RankingsRatingSource.cs        [NEW — IPlayerRatingProvider impl]
├── Builder/
│   ├── RankingsBuilderExtensions.cs   [UPDATE: AddRankings registers decay service]
│   └── RankingsBuilderExtensions.RatingSource.cs [NEW partial: .WithRatingsFrom<>()]
└── GameKitRankingsOptions.cs          [UPDATE: DecayOptions nested class]

src/GameKit.Matchmaking/
├── Builder/
│   └── MatchmakingLadderConfig.cs     [UPDATE: MaxBracketWidth, MinPoolDepthBeforeBracketExpansion]
├── Services/
│   └── MatchmakingService.cs          [UPDATE: resolve IPlayerRatingProvider, replace rating=0]
└── Strategy/
    └── EloRangeMatchmakingStrategy.cs [UPDATE: Bracket() cap, pool-depth guard in Match()]
```

---

## RANK-15: Rank Decay (RD Inflation)

### Glicko-2 Inactivity Step — Exact Code Location

[VERIFIED: codebase] `src/GameKit.Rankings/Glicko2/RatingCalculator.cs` line 236:

```csharp
// Already implemented in the vendored Glicko-2 calculator:
private static double CalculateNewRatingDeviation(double phi, double sigma) =>
    Math.Sqrt(Math.Pow(phi, 2) + Math.Pow(sigma, 2));
```

This is Glickman's Step 6 formula: `φ'★ = √(φ² + σ²)`. The `RatingCalculator.UpdateRatings` method already calls this for players with no results (lines 78-82):

```csharp
else
{
    // player does not compete during the rating period — only Step 6 applies.
    player.SetWorkingRating(player.GetGlicko2Rating());          // rating UNCHANGED
    player.SetWorkingRatingDeviation(CalculateNewRatingDeviation(
        player.GetGlicko2RatingDeviation(), player.GetVolatility())); // RD inflates
    player.SetWorkingVolatility(player.GetVolatility());         // volatility unchanged
}
```

**Implication for the decay service:** The decay service does NOT need to call `IRankingAlgorithm.Apply` — that method is batched and requires a `RatingBatch` with match outcomes. Instead the decay service applies the inactivity formula directly:

```csharp
// In RankDecayBackgroundService (pseudocode)
foreach (var rank in inactivePlayers)
{
    // phi and sigma are on the Glicko-2 internal scale
    // player_ranks stores values on the Glicko-1 (original) scale
    // RatingCalculator.ConvertRatingDeviationToGlicko2Scale / ConvertRatingDeviationToOriginalGlickoScale
    // must be applied, OR we apply directly in the original scale:
    //
    // φ'_original_scale = √(φ²_original_scale + (σ × 173.7178)²)
    //
    // Simpler: call the step-6 formula directly on the stored double-precision columns.
    // See §Pitfall — Glicko-2 scale conversion below.
    var phiNew = Math.Sqrt(
        rank.RatingDeviation * rank.RatingDeviation +
        (rank.Volatility * RatingCalculator.Multiplier) * (rank.Volatility * RatingCalculator.Multiplier));
    rank.RatingDeviation = phiNew;
    rank.LastDecayAt = now;
}
```

**CRITICAL: Glicko-2 scale conversion.** The `player_ranks` table stores `RatingDeviation` and `Rating` on the original Glicko-1 scale (default 350, not ~2.01). The `RatingCalculator` works internally on the Glicko-2 scale (divides by `Multiplier = 173.7178`). The Step 6 formula `φ' = √(φ² + σ²)` operates in Glicko-2 scale. Applying it to original-scale values directly would be wrong. The correct approach:

```csharp
// Convert to Glicko-2 scale, apply step 6, convert back.
const double Multiplier = 173.7178;
double phiG2 = rank.RatingDeviation / Multiplier;   // convert RD to Glicko-2 scale
// sigma is already stored in dimensionless Glicko-2 scale (Volatility column)
double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + rank.Volatility * rank.Volatility);
rank.RatingDeviation = phiPrimeG2 * Multiplier;     // convert back to original scale
// rank.Rating unchanged; rank.Volatility unchanged
rank.LastDecayAt = now;
```

[VERIFIED: codebase] `RatingCalculator` Multiplier constant = 173.7178 (line 29). The `ConvertRatingDeviationToGlicko2Scale` / `ConvertRatingDeviationToOriginalGlickoScale` methods exist (lines 259-264) and perform exactly this conversion. The decay service can call these static-equivalent conversions directly without constructing a `RatingCalculator` instance.

### Leader Election Pattern

[VERIFIED: codebase] `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` — exact pattern to copy. Key facts:
- Uses `IDatabase.LockTakeAsync(key, instanceId, ttl)` — NOT raw `StringSetAsync(NX)`.
- `InstanceId = $"{Environment.MachineName}:{Guid.NewGuid()}"` — unique per process.
- Polly v8 `ResiliencePipelineBuilder` with `AddRetry(3, Exponential, Jitter)` on `RedisConnectionException` + `RedisTimeoutException`.
- `LockExtendAsync` for mid-batch renewal; `LockReleaseAsync` in `finally`.

The decay service uses a **dedicated Redis lock key** — NOT `"gamekit:rankings:ticker:lease"`. From PITFALLS.md §Technical Debt (line 485): "Re-use `gamekit:matchmaking:matcher:lock` Redis key for decay background service — Never; use a dedicated decay lock key." Recommended key: `"gamekit:rankings:decay:lease"`. Store in `GameKitRankingsDecayOptions.LockKey`.

### Config Knobs (new `GameKitRankingsDecayOptions` nested class)

```csharp
public sealed class GameKitRankingsDecayOptions
{
    /// <summary>How often the decay runner wakes up. Default 24h.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Redis distributed-lock TTL. Default 120s (≥ batch commit time).</summary>
    public int LockTtlSeconds { get; set; } = 120;

    /// <summary>Redis key for decay leader-election lock.</summary>
    public string LockKey { get; set; } = "gamekit:rankings:decay:lease";

    /// <summary>
    /// Minimum rating threshold above which decay applies.
    /// Players at or below this rating are decay-immune. Default 1500 (mean Glicko-2 rating).
    /// </summary>
    public double DecayThresholdRating { get; set; } = 1500;

    /// <summary>
    /// Days of inactivity before decay is applied (since LastMatchAt).
    /// Default 30 days. Players who have never played (LastMatchAt = null) are excluded.
    /// </summary>
    public int InactivityDays { get; set; } = 30;

    /// <summary>Max rows processed per decay run (batch size). Default 500.</summary>
    public int BatchSize { get; set; } = 500;
}
```

Add to `GameKitRankingsOptions`: `public GameKitRankingsDecayOptions Decay { get; set; } = new();`

### Decay Index

PITFALLS.md §Performance Traps (line 513): "Index `(ladder_id, last_played_at)` on `player_ranks`; add index in the decay migration." The migration must add a composite index on `(LadderId, LastMatchAt)` using the existing `HasIndex` pattern. The migration SQL should use raw `CREATE INDEX` (same as `idx_pending_rating_updates_ladder_pending`) because partial-index EF Core support via `HasFilter()` is available but the composite is sufficient.

---

## RANK-16: Placement Matches

### New Schema Columns

Three new columns on `player_ranks` in migration `20260517000000_RankingsDecayPlacement`:

| Column | SQL type | EF type | Default | Nullable |
|--------|----------|---------|---------|---------|
| `LastDecayAt` | `timestamp with time zone` | `DateTimeOffset?` | NULL | yes |
| `PlacementMatchesRemaining` | `integer` | `int` | configured N (default 10) | no |
| `IsInPlacement` | `boolean` | `bool` | true | no |

**Migration timestamp:** `20260517000000` — one day after `20260516000000_MatchmakingInitial` (the latest existing migration across all packages). Rankings initial was `20260515000000`; the new migration follows deterministic cross-package ordering.

**Entity update (`PlayerRank.cs`):**
```csharp
/// <summary>UTC timestamp of the last decay run applied to this rank. Null = never decayed.</summary>
public DateTimeOffset? LastDecayAt { get; set; }

/// <summary>Placement matches remaining before visible rank is revealed. 0 = placement complete.</summary>
public int PlacementMatchesRemaining { get; set; }

/// <summary>True while the player is still completing placement matches.</summary>
public bool IsInPlacement { get; set; }
```

**EF Configuration update (`PlayerRankConfiguration.cs`):**
```csharp
b.Property(r => r.LastDecayAt).IsRequired(false);
b.Property(r => r.PlacementMatchesRemaining).IsRequired().HasDefaultValue(10);
b.Property(r => r.IsInPlacement).IsRequired().HasDefaultValue(true);
```

**Lazy rank creation update (`RankingsTickerService.cs` line 267):** When creating new `PlayerRank` rows, set `IsInPlacement = true` and `PlacementMatchesRemaining = defaults.PlacementMatchCount` (read from ladder JSONB Config).

### Atomic Decrement Site

[VERIFIED: codebase] The session-complete hook is `PendingRatingUpdatesAdapter.OnCompletedAsync` in `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs`. This method:
- Runs inside the caller's ambient `ReadCommitted` transaction (confirmed: no `BeginTransaction` call, just `SaveChangesAsync`).
- Already has a `participant.LadderId.HasValue` guard.
- Reads the player's `PlayerRank` row by `(PlayerId, LadderId)`.

The placement decrement goes here, AFTER the `RatingBefore` snapshot write and BEFORE the `PendingRatingUpdate` insert:

```csharp
// In PendingRatingUpdatesAdapter.OnCompletedAsync, after RatingBefore snapshot:
if (playerRank is not null && playerRank.IsInPlacement && playerRank.PlacementMatchesRemaining > 0)
{
    playerRank.PlacementMatchesRemaining--;
    if (playerRank.PlacementMatchesRemaining == 0)
        playerRank.IsInPlacement = false;
    // playerRank is already tracked (loaded without AsNoTracking above... wait — see note)
}
```

**Critical note:** The current `PendingRatingUpdatesAdapter` code (line 88) loads `playerRank` with `.AsNoTracking()`. For the placement decrement to work, the row must be tracked. Two options:
1. Remove `AsNoTracking()` from this query (slight overhead — one tracked entity per participant).
2. Issue a separate `ExecuteUpdateAsync` for the decrement.

Option 2 (explicit `ExecuteUpdateAsync`) is cleaner and consistent with the pattern used later in the same file for `RatingBefore`. The planner should choose based on v1 precedent: `PendingRatingUpdatesAdapter` currently uses `ExecuteUpdateAsync` for `RatingBefore`, so use the same approach for the decrement.

```csharp
// Explicit ExecuteUpdateAsync for placement decrement:
if (playerRank is not null && playerRank.IsInPlacement && playerRank.PlacementMatchesRemaining > 0)
{
    var newRemaining = playerRank.PlacementMatchesRemaining - 1;
    await _ctx.Set<PlayerRank>()
        .Where(r => r.PlayerId == participant.PlayerId && r.LadderId == participant.LadderId!.Value
                    && r.IsInPlacement && r.PlacementMatchesRemaining > 0)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(r => r.PlacementMatchesRemaining, r => r.PlacementMatchesRemaining - 1)
            .SetProperty(r => r.IsInPlacement,
                r => r.PlacementMatchesRemaining - 1 == 0 ? false : r.IsInPlacement),
        ct);
}
```

The WHERE predicate `PlacementMatchesRemaining > 0` is a race guard — safe because this runs inside the session-complete transaction.

### Visible Rank Hiding

[VERIFIED: codebase] The current leaderboard DTO is `LeaderboardRowDto` (`src/GameKit.Rankings/Http/Contracts/LeaderboardRowDto.cs`). The player rank read endpoint is `GET /api/players/{id}/rank` or equivalent in `RankingsPlayerEndpoints`. The planner must:

1. Add `IsInPlacement bool` and `PlacementMatchesRemaining int` to `LeaderboardRowDto` and any rank-read DTO.
2. When `IsInPlacement == true`, set `Rating = null` or a sentinel value in the DTO response — do NOT return the raw Glicko-2 rating to the client.
3. The underlying `player_ranks.Rating` and `RatingDeviation` continue to update every drain cycle — the algorithm operates correctly during placement regardless of visibility.

---

## RANK-17: RankingsRatingSource

### Interface Contract (Already Shipped)

[VERIFIED: codebase] `src/GameKit.Core/Services/IPlayerRatingProvider.cs` — the interface is live:

```csharp
public interface IPlayerRatingProvider
{
    ValueTask<IReadOnlyDictionary<Guid, PlayerRatingValue>> GetRatingsAsync(
        IReadOnlyCollection<Guid> playerIds,
        Guid ladderId,
        CancellationToken ct = default);
}
// Return type: PlayerRatingValue(PlayerId, Rating, RatingDeviation, Volatility)
```

Players absent from `player_ranks` are **omitted** from the returned dictionary (callers apply defaults for absent keys). [VERIFIED: codebase] `IPlayerRatingProvider.cs` XML doc line 39.

### Implementation

`RankingsRatingSource : IPlayerRatingProvider` in `src/GameKit.Rankings/Services/`:

```csharp
// Core query — batched single SELECT
var ranks = await ctx.Set<PlayerRank>()
    .AsNoTracking()
    .Where(r => r.LadderId == ladderId && playerIds.Contains(r.PlayerId))
    .Select(r => new { r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility })
    .ToListAsync(ct);

return ranks.ToDictionary(
    r => r.PlayerId,
    r => new PlayerRatingValue(r.PlayerId, r.Rating, r.RatingDeviation, r.Volatility));
```

### Lifetime Decision: Scoped (not Singleton)

[VERIFIED: codebase] `RankingsRatingSource` reads from the scoped `GameKitDbContext`. Looking at v1 precedent:
- `PendingRatingUpdatesAdapter` is Scoped (uses `_ctx`).
- `RankingsIdempotencyStore` is Scoped (uses DbContext).
- `MatchmakingService` is Scoped (uses `GameKitDbContext db`).

The Context.md says "Planner to choose Scoped vs Singleton+IServiceScopeFactory consistent with v1 precedent." V1 precedent for anything that reads a DbContext is **Scoped**. However, the Context.md also says it must be registered as Singleton via `RemoveAll+AddSingleton`. This is a conflict.

**Resolution:** Register as **Scoped**, not Singleton. The `RemoveAll<IPlayerRatingProvider>()` + `AddScoped<IPlayerRatingProvider, RankingsRatingSource>()` pattern is correct. The rationale: `MatchmakingService.EnqueueAsync` is itself Scoped; it will resolve `IPlayerRatingProvider` in the same scope, so `RankingsRatingSource` gets the same `GameKitDbContext` instance as `MatchmakingService` — correct. If we forced Singleton + `IServiceScopeFactory`, we would create a second scope (a second DbContext connection) per call, which is wasteful and breaks ambient-transaction semantics for future features.

**Note on Context.md wording:** "RemoveAll<IPlayerRatingProvider>(); AddSingleton<IPlayerRatingProvider, RankingsRatingSource>()" was written before verifying DbContext lifetime constraints. The planner should use `AddScoped` and document the deviation.

### Registration: `.WithRatingsFrom<RankingsRatingSource>()`

In `RankingsBuilderExtensions.RatingSource.cs` (new partial file):

```csharp
public static partial class RankingsBuilderExtensions
{
    /// <summary>
    /// Wires <see cref="RankingsRatingSource"/> as the <see cref="IPlayerRatingProvider"/>
    /// for rating-aware matchmaking. Replaces the Core null-object default (RANK-17).
    /// Call after <c>AddRankings()</c>.
    /// </summary>
    public static IGameKitRankingsBuilder WithRatingsFrom<T>(this IGameKitRankingsBuilder builder)
        where T : class, IPlayerRatingProvider
    {
        builder.Services.RemoveAll<IPlayerRatingProvider>();
        builder.Services.AddScoped<IPlayerRatingProvider, T>();
        return builder;
    }
}
```

Consumer call site:
```csharp
services.AddGameKit(...)
    .AddRankings(opts => { ... })
    .WithRatingsFrom<RankingsRatingSource>()
    .AddLadder("main", ...);
```

---

## MATCH-16: Rating-Aware EnqueueAsync

### Exact Injection Point

[VERIFIED: codebase] `src/GameKit.Matchmaking/Services/MatchmakingService.cs` lines 198-215 (Step 4 comment block). The hardcoded zero-fill:

```csharp
// Line 202-204 — THE INJECTION POINT:
var queuedMembers = memberPlayerIds
    .Select(pid => new QueuedPartyMember(pid, Rating: 0, RatingDeviation: 0, Volatility: 0))
    .ToList();
```

### Constructor Injection

`MatchmakingService` currently has 8 constructor parameters (lines 84-112). Add `IPlayerRatingProvider? ratingProvider = null` as a 9th optional parameter:

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
    IPlayerRatingProvider? ratingProvider = null)  // NEW — optional; null = v1 zero-rated behaviour
```

Note: the optional `logger` is already at position 9. The new `ratingProvider` goes at position 10, or the logger/provider positions can swap. The planner must verify that DI resolution of optional parameters works correctly; in .NET DI, `IPlayerRatingProvider?` resolves to `null` only if unregistered — but Core registers `NullPlayerRatingProvider` via `TryAddSingleton`, so DI always supplies a non-null value. The `?` annotation is for test convenience; production DI always resolves a non-null instance.

### Replacement Step 4 Code

```csharp
// Step 4: resolve real ratings from IPlayerRatingProvider (MATCH-16).
// Provider is Core's null-object when Rankings absent (returns empty dict → rating=0 fallback).
IReadOnlyDictionary<Guid, PlayerRatingValue> ratingMap =
    _ratingProvider is not null
        ? await _ratingProvider.GetRatingsAsync(memberPlayerIds, ladderId, ct).ConfigureAwait(false)
        : ImmutableDictionary<Guid, PlayerRatingValue>.Empty;

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

[VERIFIED: codebase] `ARCHITECTURE.md` lines 134-149 — this exact pattern was researched in the milestone research. The ticker (`MatchmakerTickerService.BuildQueuedPartyFromHash`) and `EloRangeMatchmakingStrategy` already read real rating values from `QueuedPartyMember` — no changes there.

### Redis Hash Caching

[VERIFIED: codebase] `MatchmakingService.cs` lines 265-276: `members` JSON and `aggregateRating` are both written to the Redis ticket hash `mm:ticket:{id}`. With real ratings flowing in, both fields are correct at enqueue time. Stale-cache for long-waiting tickets is documented and accepted (per `ARCHITECTURE.md` lines 155-156).

---

## MATCH-17: Anti-Feedback-Loop Guardrails

### Fields to Add to `MatchmakingLadderConfig`

[VERIFIED: codebase] `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` — currently has `BracketStart`, `BracketEnd`, `BracketRampSeconds`, `PartyRatingAggregator`, `MaxPartyRatingSpread`. Add:

```csharp
/// <summary>
/// Hard cap on bracket half-width in rating points. Bracket-widening NEVER exceeds this value
/// regardless of wait time, preventing high-RD new players from being matched against top-rated
/// players on sparse pools (MATCH-17). Default <c>null</c> (no cap — maintains v1 behaviour).
/// When set, must be &gt;= <see cref="BracketEnd"/> is recommended; if set lower than BracketEnd
/// the effective ceiling is MaxBracketWidth.
/// </summary>
public int? MaxBracketWidth { get; set; }

/// <summary>
/// Minimum number of tickets in the pool before bracket expansion begins. When the pool has fewer
/// than this many candidates, the bracket stays at <see cref="BracketStart"/> regardless of wait
/// time (MATCH-17). Default <c>null</c> (no guard — maintains v1 behaviour).
/// Set to <c>2 * expected_party_size</c> as a starting recommendation.
/// </summary>
public int? MinPoolDepthBeforeBracketExpansion { get; set; }
```

### `EloRangeMatchmakingStrategy` Changes

[VERIFIED: codebase] `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs`.

**`Bracket()` method (line 173):** Currently returns `Math.Min(raw, cfg.BracketEnd)`. Add the `MaxBracketWidth` cap:

```csharp
public static double Bracket(MatchmakingLadderConfig cfg, double secondsInQueue)
{
    if (secondsInQueue < 0) secondsInQueue = 0;
    var raw = cfg.BracketStart + (cfg.BracketEnd - cfg.BracketStart) * secondsInQueue / cfg.BracketRampSeconds;
    var capped = Math.Min(raw, cfg.BracketEnd);
    // MATCH-17: hard cap — never exceed MaxBracketWidth regardless of wait time.
    if (cfg.MaxBracketWidth.HasValue)
        capped = Math.Min(capped, cfg.MaxBracketWidth.Value);
    return capped;
}
```

**`Match()` method:** Add pool-depth guard at the top of the match loop (before iterating candidates). The pool-depth guard checks whether expansion has occurred (i.e., `secondsInQueue > 0` meaning bracket > `BracketStart`). If the pool is below `MinPoolDepthBeforeBracketExpansion`, clamp the bracket to `BracketStart`:

```csharp
// In Match(), for each candidate:
var elapsedSeconds = (now - candidate.QueuedAt).TotalSeconds;
var poolSize = pool.Count - 1; // exclude self

double effectiveElapsed = elapsedSeconds;
if (cfg.MinPoolDepthBeforeBracketExpansion.HasValue
    && poolSize < cfg.MinPoolDepthBeforeBracketExpansion.Value)
{
    effectiveElapsed = 0; // force bracket to BracketStart — no expansion yet
}

var candidateBracket = Bracket(cfg, effectiveElapsed);
```

Apply the same logic for `poolBracket` of each pool entry (use the pool entry's own elapsed time but also clamp if pool is below depth threshold).

---

## Migration Details

### Migration: `20260517000000_RankingsDecayPlacement`

**Timestamp:** `20260517000000` — one day after `20260516000000_MatchmakingInitial` (the most recent migration in the entire codebase). Rankings initial was `20260515000000`; this is the second Rankings migration, following the deterministic convention.

**Advisory lock:** `-156812172L` — the EXISTING `RankingsMigrationConstants.AdvisoryLockKey`. No new key. [VERIFIED: codebase] `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs` line 43.

**Migration history table:** `__ef_migrations_rankings` (unchanged). [VERIFIED: codebase] line 20.

**ExcludeFromMigrations:** Already handled by `RankingsMigrationModelCustomizer` for prior-package entities. No changes needed — this migration only modifies `gamekit.player_ranks` which Rankings already owns.

**Up() SQL (approximate):**
```sql
ALTER TABLE gamekit.player_ranks
    ADD COLUMN "LastDecayAt" timestamp with time zone,
    ADD COLUMN "PlacementMatchesRemaining" integer NOT NULL DEFAULT 10,
    ADD COLUMN "IsInPlacement" boolean NOT NULL DEFAULT true;

-- Index for decay batch SELECT: active high-rated players by inactivity
CREATE INDEX idx_player_ranks_decay_candidates
    ON gamekit.player_ranks ("LadderId", "LastMatchAt")
    WHERE "IsInPlacement" = false;
-- Note: filter on IsInPlacement=false because new players in placement
-- have no LastMatchAt and should not be decay candidates.
```

**Model snapshot update:** Add the three new properties to the `player_ranks` entity block in `GameKitDbContextModelSnapshot.cs`.

**Existing data migration:** For existing `player_ranks` rows (from v1), the defaults apply:
- `LastDecayAt = NULL` — correct (never decayed).
- `PlacementMatchesRemaining = 10` — this is WRONG for players who already completed games. They should be `IsInPlacement = false, PlacementMatchesRemaining = 0`. The migration must handle this:

```sql
-- Existing players with any game history are NOT in placement.
UPDATE gamekit.player_ranks
SET "IsInPlacement" = false, "PlacementMatchesRemaining" = 0
WHERE "Wins" > 0 OR "Losses" > 0 OR "Draws" > 0;
```

This data-fixup must be part of the `Up()` migration. [ASSUMED] The exact business rule (1+ game = not in placement) is reasonable but the planner should confirm with the user if there is a different threshold.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Redis distributed lock | Raw `StringSetAsync(NX)` | `IDatabase.LockTakeAsync/LockExtendAsync/LockReleaseAsync` | Lua-script-verified release; existing `RankingsTickerLeaseHelper` is the template |
| Glicko-2 inactivity formula | Re-derive φ'=√(φ²+σ²) from scratch | Inline computation using exact formula from vendored `RatingCalculator.CalculateNewRatingDeviation` (line 236) | Already validated against Glickman worked example; scale-conversion logic in same file |
| Placement atomic decrement | Hand-rolled concurrency | `ExecuteUpdateAsync` inside session-complete `ReadCommitted` tx | Same pattern as `RatingBefore` snapshot write in `PendingRatingUpdatesAdapter` |
| Optional DI registration | Custom null-check factories | `services.RemoveAll<T>(); services.AddScoped<T, TImpl>()` | Established v1 pattern for optional port override |

---

## Common Pitfalls

### Pitfall 1: Glicko-2 Scale Conversion in Decay Service
**What goes wrong:** Applying `φ' = √(φ² + σ²)` directly to `player_ranks.RatingDeviation` (original scale ~350) and `player_ranks.Volatility` (Glicko-2 dimensionless ~0.06) produces a nonsense result. The formula requires both values on the same scale (Glicko-2 internal: RD ÷ 173.7178).
**Why it happens:** The stored `RatingDeviation` is on the Glicko-1 scale (150-350 range); `Volatility` is already dimensionless on the Glicko-2 scale. Mixing them without conversion breaks the math.
**How to avoid:** Convert `RatingDeviation` to Glicko-2 scale before applying, convert back after. The `Multiplier = 173.7178` constant is in `RatingCalculator.cs` line 29. [VERIFIED: codebase]
**Warning signs:** After a decay run, `RatingDeviation` jumps to values > 1000 (original scale max should be ~350).

### Pitfall 2: Existing Players Start in Placement (RANK-16 Migration)
**What goes wrong:** The migration adds `IsInPlacement = true` as the default for ALL rows including existing v1 players who have already played many games. On next API call, every existing player appears "unranked."
**Why it happens:** EF Core `HasDefaultValue(true)` applies to new rows AND the migration's `ALTER TABLE ... ADD COLUMN ... DEFAULT true` applies to existing rows too.
**How to avoid:** Include a `UPDATE player_ranks SET IsInPlacement = false, PlacementMatchesRemaining = 0 WHERE Wins > 0 OR Losses > 0 OR Draws > 0` in the migration's `Up()`. [ASSUMED — exact threshold needs confirmation]
**Warning signs:** Leaderboard shows 0 ranked players after migration. All existing players see "Placement" in the UI.

### Pitfall 3: `IPlayerRatingProvider` TryAdd vs. RemoveAll
**What goes wrong:** Using `TryAddSingleton<IPlayerRatingProvider, RankingsRatingSource>()` instead of `RemoveAll + AddScoped` leaves the Core `NullPlayerRatingProvider` registered. Matchmaking resolves the null-object and continues to use rating=0.
**Why it happens:** [VERIFIED: codebase] Phase 7 wired `NullPlayerRatingProvider` via `TryAddSingleton` in Core's `AddGameKit()`. A second `TryAdd` call is a no-op.
**How to avoid:** Use `services.RemoveAll<IPlayerRatingProvider>(); services.AddScoped<IPlayerRatingProvider, RankingsRatingSource>();` in `.WithRatingsFrom<>()`. [VERIFIED: codebase + 07-review IN-02 in STATE.md line 98]
**Warning signs:** Integration test: `ratingProvider.GetRatingsAsync(...)` returns empty dictionary even when `player_ranks` has data.

### Pitfall 4: Decay Lock Key Collides with Ticker Lock
**What goes wrong:** Reusing `"gamekit:rankings:ticker:lease"` for the decay service causes decay and ticker to mutually exclude. During a drain (which can take seconds on large pools), the decay service cannot acquire the lock and runs no decay.
**How to avoid:** Use a dedicated `"gamekit:rankings:decay:lease"` key in `GameKitRankingsDecayOptions.LockKey`. [CITED: PITFALLS.md §Technical Debt line 485]
**Warning signs:** Decay service logs show `LockNotAcquired` at every tick during active drains.

### Pitfall 5: MaxBracketWidth Below BracketEnd Is Silently Ignored
**What goes wrong:** Operator sets `BracketEnd = 500` but `MaxBracketWidth = 300`. Without a validation check, `EloRangeMatchmakingStrategy.Bracket()` applies `Math.Min(raw, 500)` before `Math.Min(capped, 300)` — the double-min works correctly. But `MatchmakingOptionsValidator` must validate `MaxBracketWidth >= BracketStart` and `MaxBracketWidth > 0`.
**How to avoid:** Add per-ladder validation in `MatchmakingOptionsValidator.ValidateLadder()` (already exists at [VERIFIED: codebase] `src/GameKit.Matchmaking/MatchmakingOptionsValidator.cs`): `MaxBracketWidth must be null or > 0`.
**Warning signs:** Bracket always returns `BracketStart` when MaxBracketWidth is set below BracketStart (effectively no expansion).

### Pitfall 6: `AsNoTracking` on `PlayerRank` Prevents Placement Decrement Tracking
**What goes wrong:** The current `PendingRatingUpdatesAdapter` loads `playerRank` with `AsNoTracking()`. If the placement decrement uses `rank.PlacementMatchesRemaining--; await _ctx.SaveChangesAsync()`, the change tracker has no record of the entity and SaveChanges writes nothing.
**How to avoid:** Use `ExecuteUpdateAsync` for the decrement (stateless update by WHERE predicate) rather than entity mutation. Consistent with the existing `RatingBefore` update in the same method. [VERIFIED: codebase] `PendingRatingUpdatesAdapter.cs` lines 96-100.
**Warning signs:** `PlacementMatchesRemaining` never decrements; `IsInPlacement` never flips to false.

---

## Code Examples

Verified patterns from actual codebase:

### Glicko-2 Inactivity Step (Scale-Correct)
```csharp
// Source: src/GameKit.Rankings/Glicko2/RatingCalculator.cs lines 29, 236, 256-264
const double Multiplier = 173.7178;

// phi_g2 is the RD on Glicko-2 internal scale
double phiG2 = rank.RatingDeviation / Multiplier;
// sigma is already on Glicko-2 scale (dimensionless, stored directly in Volatility column)
double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + rank.Volatility * rank.Volatility);
// Convert back to original Glicko-1 scale for storage
rank.RatingDeviation = phiPrimeG2 * Multiplier;
// rank.Rating and rank.Volatility are UNCHANGED
rank.LastDecayAt = now;
```

### Leader Election Pattern (from RankingsTickerLeaseHelper)
```csharp
// Source: src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs
// InstanceId = $"{Environment.MachineName}:{Guid.NewGuid()}"
// _polly = ResiliencePipelineBuilder().AddRetry(3, Exponential, Jitter, on RedisConnectionException/RedisTimeoutException)

var acquired = await db.LockTakeAsync(
    _opts.Decay.LockKey,
    InstanceId,
    TimeSpan.FromSeconds(_opts.Decay.LockTtlSeconds));
if (!acquired) return DecayResult.LockNotAcquired;
```

### Bracket Formula with MaxBracketWidth Cap
```csharp
// Source: src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs:173 (modified)
public static double Bracket(MatchmakingLadderConfig cfg, double secondsInQueue)
{
    if (secondsInQueue < 0) secondsInQueue = 0;
    var raw = cfg.BracketStart + (cfg.BracketEnd - cfg.BracketStart)
              * secondsInQueue / cfg.BracketRampSeconds;
    var capped = Math.Min(raw, cfg.BracketEnd);
    if (cfg.MaxBracketWidth.HasValue)
        capped = Math.Min(capped, cfg.MaxBracketWidth.Value);
    return capped;
}
```

### Rating Provider Registration
```csharp
// Source: pattern from 07-review IN-02 (STATE.md line 98)
// In RankingsBuilderExtensions.RatingSource.cs:
services.RemoveAll<IPlayerRatingProvider>();
services.AddScoped<IPlayerRatingProvider, RankingsRatingSource>();
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `MatchmakingService` hardcodes `Rating: 0` for all members | Resolve real ratings via `IPlayerRatingProvider` seam | Phase 8 (this phase) | EloRange strategy uses real Glicko-2 ratings |
| `player_ranks` has no decay or placement columns | `last_decay_at`, `placement_matches_remaining`, `is_in_placement` | Phase 8 (this phase) | Schema frozen for Phase 10 account-merge reads (SC#5) |
| No decay job | `RankDecayBackgroundService` applies φ'=√(φ²+σ²) for inactive high-rated players | Phase 8 | Leaderboard hygiene; RD inflation signals uncertainty |
| New players always visible on leaderboard | `IsInPlacement` suppresses visible rating for N games | Phase 8 | Smoother new-player experience |

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Existing players with Wins > 0 OR Losses > 0 OR Draws > 0 are treated as "not in placement" and the migration sets IsInPlacement=false for them | §RANK-16 Migration, Pitfall 2 | Incorrect threshold could leave long-time players stuck in placement permanently, or could prematurely end placement for players who only played friendly/unranked games (if such a concept exists) |
| A2 | `RankingsRatingSource` should be registered as Scoped (not Singleton) despite Context.md saying Singleton+AddSingleton | §RANK-17 Lifetime Decision | If Singleton is actually required (e.g., future cache layer), the DbContext scoping issue must be solved via IServiceScopeFactory; registering as Scoped is the simpler correct default |
| A3 | The decay migration index should use `WHERE "IsInPlacement" = false` to exclude placement players from decay candidate queries | §Migration Details | If placement players should also be decay-immune (reasonable), this is correct. If placement players should decay (they don't appear on the leaderboard anyway), the filter is wrong |

---

## Open Questions

1. **Placement count default and existing-player migration threshold**
   - What we know: Context.md says `placement_matches_remaining` defaults to "configured N". FEATURES.md recommends 5-10 games. PITFALLS.md recommends 10 games.
   - What's unclear: The exact SQL condition for "already completed placement" in the existing-player data migration. Using `Wins + Losses + Draws >= N` would be more precise than `> 0` but requires knowing N at migration time — which is a config value, not a schema constant.
   - Recommendation: Use `Wins + Losses + Draws > 0` as the migration condition (any game played = not in placement). If the operator wants a different threshold, they can manually update rows post-migration.

2. **Decay index filter vs. full composite index**
   - What we know: PITFALLS.md §Performance Traps recommends `(ladder_id, last_played_at)` index.
   - What's unclear: Whether a partial index `WHERE IsInPlacement = false` saves meaningful space given most rows will be out of placement.
   - Recommendation: Use a non-partial composite index on `(LadderId, LastMatchAt)` for simplicity; the planner can add the partial filter as a v2 optimization.

---

## Environment Availability

Step 2.6: No new external dependencies. Redis and Postgres are already required and running per existing integration test infrastructure. SKIPPED (all required dependencies are confirmed present from Phases 1-7).

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | `tests/GameKit.Rankings.Integration.Tests/` (existing) + `tests/GameKit.Matchmaking.Integration.Tests/` (existing) |
| Quick run command | `dotnet test --filter "Category=Unit" --no-build` |
| Full suite command | `dotnet test --no-build` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| RANK-15 | Inactive player above threshold: RD inflates, Rating unchanged | unit | `dotnet test --filter "RankDecay" -x` | ❌ Wave 0 |
| RANK-15 | Glickman inactivity unit test: φ=290, σ=0.06 → φ'=√(290²/173.7178² + 0.06²)*173.7178 | unit | `dotnet test --filter "Glicko2InactivityStep" -x` | ❌ Wave 0 |
| RANK-15 | Player below threshold: decay service skips them | unit | `dotnet test --filter "RankDecayThreshold" -x` | ❌ Wave 0 |
| RANK-15 | Leader election: second instance cannot run decay concurrently | integration (Redis) | `dotnet test --filter "RankDecayLeaderElection" -x` | ❌ Wave 0 |
| RANK-15 | `last_decay_at` stamped after decay run | integration (Postgres) | `dotnet test --filter "RankDecayStampsLastDecayAt" -x` | ❌ Wave 0 |
| RANK-16 | Session complete decrements `placement_matches_remaining` by 1 | integration (Postgres) | `dotnet test --filter "PlacementDecrement" -x` | ❌ Wave 0 |
| RANK-16 | At 0, `is_in_placement` flips to false | integration (Postgres) | `dotnet test --filter "PlacementComplete" -x` | ❌ Wave 0 |
| RANK-16 | Rank-read DTO returns null rating while `is_in_placement` | unit | `dotnet test --filter "PlacementHidesRating" -x` | ❌ Wave 0 |
| RANK-17 | `GetRatingsAsync` returns empty dict for unknown players | unit | `dotnet test --filter "RankingsRatingSource_UnknownPlayer" -x` | ❌ Wave 0 |
| RANK-17 | `GetRatingsAsync` returns correct values from player_ranks | integration (Postgres) | `dotnet test --filter "RankingsRatingSource_KnownPlayers" -x` | ❌ Wave 0 |
| RANK-17 | `RemoveAll+AddScoped` overrides Core null-object | unit | `dotnet test --filter "WithRatingsFrom_OverridesNullObject" -x` | ❌ Wave 0 |
| MATCH-16 | `EnqueueAsync` writes real Rating into Redis ticket hash `members` field | integration (Postgres+Redis) | `dotnet test --filter "EnqueueWritesRealRating" -x` | ❌ Wave 0 |
| MATCH-16 | `EnqueueAsync` with no Rankings installed (null-object): rating=0, no exception | unit | `dotnet test --filter "EnqueueFallsBackToZeroRating" -x` | ❌ Wave 0 |
| MATCH-17 | `EloRangeMatchmakingStrategy.Bracket()` never exceeds MaxBracketWidth | unit | `dotnet test --filter "BracketDoesNotExceedMaxWidth" -x` | ❌ Wave 0 |
| MATCH-17 | Pool depth < MinPoolDepthBeforeBracketExpansion: bracket stays at BracketStart | unit | `dotnet test --filter "BracketNoExpansionOnSparsePool" -x` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test --filter "Category=Unit" --no-build -x`
- **Per wave merge:** `dotnet test --no-build`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps

All test files above are new. The planning waves should allocate a Wave 0 plan that creates:

- [ ] `tests/GameKit.Rankings.Integration.Tests/` — `RankDecayTests.cs` (RANK-15 integration)
- [ ] `tests/GameKit.Rankings.Tests/` — `Glicko2InactivityTests.cs` (RANK-15 unit; Glickman worked example)
- [ ] `tests/GameKit.Rankings.Integration.Tests/` — `PlacementMatchTests.cs` (RANK-16)
- [ ] `tests/GameKit.Rankings.Integration.Tests/` — `RankingsRatingSourceTests.cs` (RANK-17)
- [ ] `tests/GameKit.Matchmaking.Integration.Tests/` — `RatingAwareEnqueueTests.cs` (MATCH-16)
- [ ] `tests/GameKit.Matchmaking.Tests/` — `EloRangeGuardrailTests.cs` (MATCH-17 unit)

---

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No new auth surface |
| V3 Session Management | no | No session changes |
| V4 Access Control | yes | `RankDecayBackgroundService` is internal leader-only; no new HTTP endpoints expose decay controls |
| V5 Input Validation | yes | `MatchmakingOptionsValidator` must validate `MaxBracketWidth > 0` and `MinPoolDepthBeforeBracketExpansion > 0` |
| V6 Cryptography | no | No cryptographic operations |

### Known Threat Patterns for This Stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Placement bypass — client claims pre-existing rank to skip N games | Tampering | `IsInPlacement` is server-authoritative; placement state is set by migration + session-complete service only; no client endpoint can set it |
| Decay denial — forcing the decay lock to stay held | Denial of Service | Decay lock TTL (120s default) ensures natural expiry; Polly backoff on the decay runner prevents Redis-hammering |
| Rating inflation via MaxBracketWidth=0 config | Tampering | Options validator rejects MaxBracketWidth < 1; `AddValidateOnStart()` catches at startup |

---

## Sources

### Primary (HIGH confidence — verified by direct file reads)

| File | What was verified |
|------|-------------------|
| `src/GameKit.Core/Services/IPlayerRatingProvider.cs` | Interface contract, `PlayerRatingValue` record, null-object XML doc |
| `src/GameKit.Rankings/Entities/PlayerRank.cs` | Current columns: Id, PlayerId, LadderId, Rating, RatingDeviation, Volatility, Wins, Losses, Draws, LastMatchAt — no decay/placement columns yet |
| `src/GameKit.Rankings/Data/Configurations/PlayerRankConfiguration.cs` | EF config, existing indexes, `HasColumnType("double precision")` pattern |
| `src/GameKit.Rankings/Data/RankingsMigrationConstants.cs` | Advisory lock key = -156812172L; history table = `__ef_migrations_rankings` |
| `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs` | Existing table definition, index names, FK constraints, timestamp = 20260515000000 |
| `src/GameKit.Rankings/Glicko2/RatingCalculator.cs` | `CalculateNewRatingDeviation` formula (line 236); `Multiplier = 173.7178` (line 29); scale conversion methods (lines 256-264); inactivity step in `UpdateRatings` (lines 78-82) |
| `src/GameKit.Rankings/Services/RankingsTickerLeaseHelper.cs` | Exact leader-election pattern to copy: `LockTakeAsync`, Polly pipeline, `InstanceId` format |
| `src/GameKit.Rankings/Services/RankingsTickerService.cs` | BackgroundService structure, `IServiceScopeFactory` usage, per-scope `GameKitDbContext` resolution |
| `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` | Session-complete hook: runs inside caller's ambient tx, `AsNoTracking()` on playerRank load, `ExecuteUpdateAsync` for RatingBefore |
| `src/GameKit.Rankings/Builder/RankingsBuilderExtensions.cs` | `AddRankings` extension: `TryAddEnumerable` pattern, `AddHostedService`, builder construction |
| `src/GameKit.Rankings/GameKitRankingsOptions.cs` | Existing options structure to extend with `DecayOptions` |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs:198-215` | Exact Step 4 injection point; `QueuedPartyMember` construction with hardcoded Rating=0 |
| `src/GameKit.Matchmaking/Strategy/EloRangeMatchmakingStrategy.cs` | `Bracket()` formula (line 173); `Match()` symmetric-overlap loop; `FindLadderConfig()` |
| `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` | Existing fields; no MaxBracketWidth/MinPoolDepthBeforeBracketExpansion yet |
| `.planning/research/ARCHITECTURE.md` | Rating seam design, zero-fill injection point, Redis caching analysis |
| `.planning/research/PITFALLS.md` | Pitfall §6 (feedback loop), §7 (decay double-penalty), §8 (placement smurf) |
| `.planning/STATE.md` | Advisory lock keys, IPlayerRatingProvider TryAdd vs. RemoveAll IN-02 |

### Secondary (MEDIUM confidence)

- `.planning/research/FEATURES.md` §Rank Decay, §Placement Matches — feature design rationale
- `.planning/research/SUMMARY.md` — build order and dependency graph

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; all versions already pinned
- Architecture (migration): HIGH — exact table, advisory lock, timestamp all code-verified
- Glicko-2 inactivity step: HIGH — formula from vendored `RatingCalculator.cs` line 236
- Leader election: HIGH — `RankingsTickerLeaseHelper` is the exact template
- MATCH-16 injection point: HIGH — line 202-204 in `MatchmakingService.cs` verified
- MATCH-17 guardrails: HIGH — `EloRangeMatchmakingStrategy.Bracket()` is the exact site
- Lifetime decision (Scoped vs Singleton): MEDIUM — correct by v1 precedent but deviates from Context.md wording; requires planner decision

**Research date:** 2026-06-05
**Valid until:** 2026-07-05 (30 days — stable .NET 10 stack; no fast-moving deps introduced)
