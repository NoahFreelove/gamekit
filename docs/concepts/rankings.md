# GameKit.Rankings — Concepts

## What It Does

`GameKit.Rankings` provides skill-rating, leaderboard, and season management for ladder-based
competitive games. It uses **Glicko-2** (vendored in-house) as the default rating algorithm
and stores per-player ratings in a `player_ranks` table that sits alongside Core's
`game_sessions` table. Rankings integrates with Core's session lifecycle through
`IPostSessionCompleteHandler`: when a session completes, Rankings enqueues the outcome for
the next rating-period batch run.

## The Replaceable Algorithm

The canonical extension point is **`IRankingAlgorithm`**:

```csharp
public interface IRankingAlgorithm
{
    string Name { get; }   // matched against ladder configuration, e.g. "glicko2", "elo"
    RankingState Apply(RankingState state, RankingBatch batch);
}
```

Implementations are discovered by Scrutor and registered as singletons. The active algorithm
for a ladder is selected by matching `Name` against the ladder's `AlgorithmName` configuration.
You can run different algorithms on different ladders simultaneously.

**Default:** `Glicko2Algorithm` (`Name = "glicko2"`) — a vendored in-house port of Mark
Glickman's 2012 reference implementation (MIT-licensed attribution to `MaartenStaa/glicko2-csharp`).
It operates in batched-only mode: `Apply` must receive the full set of outcomes for a rating
period together — updating per-match produces mathematically incorrect results.

To replace or augment the algorithm:

```csharp
// Register a custom algorithm before AddRankings():
services.AddSingleton<IRankingAlgorithm, EloAlgorithm>();

// Configure a ladder to use it:
gk.AddRankings(r =>
{
    r.AddLadder("casual", ladder =>
    {
        ladder.AlgorithmName = "elo";  // matches EloAlgorithm.Name
    });
});
```

## Key Public Interfaces

### `IRankingAlgorithm`

The primary strategy seam. Replace the Glicko-2 default with any algorithm that accepts a
`RankingBatch` and returns an updated `RankingState`. Must be stateless and deterministic.
See the interface XML doc for the batched-only contract and thread-safety requirements.

### `ILeaderboardService`

Provides top-N and around-player leaderboard queries for live and archived seasons.
Two query modes: `TopAsync` (highest-rated players on a ladder) and `AroundAsync` (window
centered on a specific player). Both support optional `seasonId` to query season archives.

### `IRankAdjustService`

Admin manual rank-adjustment — atomically updates `player_ranks` and writes an audit row in
a SERIALIZABLE transaction. Bypasses the rating-period batch (takes effect immediately).

### `IEndSeasonService`

Archives the current live leaderboard into `season_rank_archive` and resets live ratings
for the new season. Called by the operator via the admin API or CLI.

### `IServiceTokenService`

Issues and manages service-account bearer tokens for machine-to-machine calls (e.g. a game
server submitting session results without a player JWT).

### `IGdprExportService`

Produces a GDPR data export (all rating history, session outcomes) for a given player.
Called by the GDPR export endpoint.

### `IGameKitRankingsBuilder`

The sub-builder returned from `gk.AddRankings(...)`. Exposes ladder configuration methods
(`AddLadder`, decay-tick options, season options).

### `IRankingsTicker`

Internal background service that drives the rating-period batch loop. Not intended for
consumer implementation — exposed as an interface for testing and leader-election purposes.

## Wire-Up

```csharp
gk.AddRankings(r =>
{
    r.AddLadder("competitive", ladder =>
    {
        ladder.AlgorithmName  = "glicko2";     // default
        ladder.RatingPeriodMs = 60_000;        // run batch every 60 s
        ladder.DefaultRating  = 1500;
    });
});

// In the pipeline:
app.MapRankings();  // /api/players/{id}/export + admin rank-adjust endpoints
```

## Library-vs-Consumer Responsibility Line

| GameKit.Rankings owns | Consumer owns |
|-----------------------|---------------|
| Glicko-2 algorithm + rating-period batch loop | Custom algorithm (`IRankingAlgorithm`) |
| Leaderboard storage + pagination | Leaderboard UI / API composition |
| Season archiving + reset | Decision of when to end a season (`IEndSeasonService`) |
| GDPR export for rating data | Full GDPR export coordination (Core pipeline) |
| Service-token issuance | Game-server authorization logic |

## See Also

- [API reference](../api/GameKit.Rankings.yml) — full member-level docs.
- [docs/performance-tuning.md](../performance-tuning.md) — rating batch tuning.
