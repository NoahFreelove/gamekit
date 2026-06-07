# Phase 9: Regional Matchmaking Pools + Backfill — Pattern Map

**Mapped:** 2026-06-06
**Files analyzed:** 16 new/modified files
**Analogs found:** 16 / 16

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|-------------------|------|-----------|----------------|---------------|
| `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` | config | — | self (modify) | exact |
| `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` | config/validation | — | self (modify) | exact |
| `src/GameKit.Matchmaking/Entities/MatchmakingTicketType.cs` | model | — | `TicketStatus.cs`, `TicketEventType.cs` | exact |
| `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` | model | — | self (modify) | exact |
| `src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs` | model | request-response | self (modify) | exact |
| `src/GameKit.Matchmaking/Http/Contracts/BackfillRequest.cs` | model | request-response | `EnqueueRequest.cs` | exact |
| `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs` | utility | request-response | self (modify) | exact |
| `src/GameKit.Matchmaking/Http/Validators/BackfillRequestValidator.cs` | utility | request-response | `EnqueueRequestValidator.cs` | exact |
| `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` | controller | request-response | self (modify) | exact |
| `src/GameKit.Matchmaking/Services/IBackfillService.cs` | service | request-response | `IMatchmakingService.cs`, `IPartyService.cs` | role-match |
| `src/GameKit.Matchmaking/Services/BackfillService.cs` | service | request-response | `MatchmakingService.cs` | role-match |
| `src/GameKit.Matchmaking/Services/MatchmakingService.cs` | service | request-response | self (modify) | exact |
| `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` | service | event-driven | self (modify) | exact |
| `src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs` | config | — | self (modify) | exact |
| `src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs` | migration | — | `20260517000000_RankingsDecayPlacement.cs` | exact |
| `src/GameKit.Core/Entities/SessionParticipant.cs` | model | — | self (modify) | exact |
| `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` | service | event-driven | self (modify) | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/RegionalPoolTests.cs` | test | request-response | `MatchmakingHappyPathTests.cs` | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/BackfillTests.cs` | test | request-response | `MatchmakingHappyPathTests.cs` | exact |
| `tests/GameKit.Matchmaking.Integration.Tests/BackfillParticipationTests.cs` | test | event-driven | `MatchmakingHappyPathTests.cs` | role-match |

---

## Pattern Assignments

### `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs` (config, modify)

**Analog:** self — `src/GameKit.Matchmaking/Builder/MatchmakingLadderConfig.cs`

**Core pattern — existing properties (lines 24-82):**
```csharp
public sealed class MatchmakingLadderConfig
{
    public required string Name { get; set; }
    public int BracketStart { get; set; } = 100;
    public int? MaxBracketWidth { get; set; }
    public int? MinPoolDepthBeforeBracketExpansion { get; set; }
    // ...
}
```

**New properties to add (copy existing nullable-property pattern):**
```csharp
/// <summary>
/// Allowed region names for this ladder. When non-null and non-empty, enqueue requests
/// with a <c>RegionName</c> absent from this list are rejected with HTTP 400
/// <c>region_not_allowed</c>. When null or empty, all regions route to the <c>"default"</c>
/// pool (backwards-compatible v1 behaviour). Entries must be non-empty strings of at most
/// 64 characters; <c>"default"</c> (case-insensitive) is reserved and must not appear here.
/// </summary>
public IReadOnlyList<string>? AllowedRegions { get; set; }

/// <summary>
/// Minimum fraction [0.0–1.0] of the session a backfill player must have participated in
/// to receive a rating change. When null, no participation guard is applied (all participants
/// receive rating updates). Written to the ladder's JSONB <c>Config</c> at startup via
/// <see cref="StartupLadderUpserter"/> and read in
/// <see cref="PendingRatingUpdatesAdapter.OnCompletedAsync"/>.
/// </summary>
public double? MinParticipationFractionForRating { get; set; }
```

---

### `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` (config/validation, modify)

**Analog:** self — `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs`

**Existing ValidateLadderConfig pattern (lines 67-98) — extend this method:**
```csharp
private static void ValidateLadderConfig(MatchmakingLadderConfig config)
{
    if (config.BracketRampSeconds <= 0)
        throw new ArgumentException(
            $"{nameof(config.BracketRampSeconds)} must be > 0 (got {config.BracketRampSeconds}).",
            nameof(config));

    if (config.MaxBracketWidth.HasValue && config.MaxBracketWidth.Value < config.BracketStart)
        throw new ArgumentException(
            $"{nameof(config.MaxBracketWidth)} ({config.MaxBracketWidth.Value}) must be >= " +
            $"{nameof(config.BracketStart)} ({config.BracketStart}) when set, ...",
            nameof(config));

    if (config.MinPoolDepthBeforeBracketExpansion.HasValue && config.MinPoolDepthBeforeBracketExpansion.Value <= 0)
        throw new ArgumentException(
            $"{nameof(config.MinPoolDepthBeforeBracketExpansion)} must be > 0 when set ...",
            nameof(config));
    // Phase 9 adds AllowedRegions validation after this block.
}
```

**New AllowedRegions validation to add inside ValidateLadderConfig (follow exact same throw pattern):**
```csharp
if (config.AllowedRegions is { Count: > 0 })
{
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var region in config.AllowedRegions)
    {
        if (string.IsNullOrWhiteSpace(region))
            throw new ArgumentException(
                $"{nameof(config.AllowedRegions)} must not contain null, empty, or whitespace-only entries.",
                nameof(config));

        if (region.Length > 64)
            throw new ArgumentException(
                $"{nameof(config.AllowedRegions)} entry '{region}' exceeds the 64-character maximum (PoolName column constraint).",
                nameof(config));

        if (region.Equals("default", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"Region name 'default' is reserved. Use null or omit {nameof(config.AllowedRegions)} to allow unrouted tickets.",
                nameof(config));

        if (!seen.Add(region))
            throw new ArgumentException(
                $"{nameof(config.AllowedRegions)} contains duplicate region name '{region}' (case-insensitive).",
                nameof(config));
    }
}

if (config.MinParticipationFractionForRating.HasValue
    && (config.MinParticipationFractionForRating.Value < 0.0
        || config.MinParticipationFractionForRating.Value > 1.0))
    throw new ArgumentException(
        $"{nameof(config.MinParticipationFractionForRating)} must be between 0.0 and 1.0 when set (got {config.MinParticipationFractionForRating.Value}).",
        nameof(config));
```

---

### `src/GameKit.Matchmaking/Entities/MatchmakingTicketType.cs` (model, NEW)

**Analog:** `src/GameKit.Matchmaking/Entities/TicketStatus.cs` (lines 1-41) + `TicketEventType.cs` (lines 1-37)

**Imports pattern (lines 1-6 of TicketStatus.cs):**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Entities;
```

**Core integer-enum pattern (copy directly from TicketStatus.cs):**
```csharp
/// <summary>
/// Type of a <see cref="MatchmakingTicket"/>. Stored as <c>integer</c> at the SQL level
/// (Phase 5 mandatory; integer storage convention). <see cref="Normal"/> is the default (0).
/// </summary>
public enum MatchmakingTicketType
{
    /// <summary>Standard player-initiated matchmaking ticket. Score = Unix milliseconds.</summary>
    Normal = 0,

    /// <summary>
    /// Backfill ticket created via <c>POST /api/mm/backfill</c>. Inserted into the Redis
    /// sorted set with score <c>0</c> (Unix epoch) so it sorts before all Normal tickets
    /// and is processed with higher priority by the matcher.
    /// </summary>
    Backfill = 1,
}
```

**Critical:** No `HasConversion<string>()` — same mandatory rule as `TicketStatus`. Stored as `integer NOT NULL DEFAULT 0`.

---

### `src/GameKit.Matchmaking/Entities/MatchmakingTicket.cs` (model, modify)

**Analog:** self — add `TicketType` property following existing property pattern (lines 34-68)

**Existing property pattern:**
```csharp
/// <summary>
/// Ticket status. Stored as <c>integer</c> (Phase 5 mandatory) — no
/// <c>HasConversion&lt;string&gt;()</c> applied.
/// </summary>
public TicketStatus Status { get; set; }
```

**New property to add (copy Status pattern exactly):**
```csharp
/// <summary>
/// Ticket type. Stored as <c>integer</c> (Phase 5 mandatory) — no
/// <c>HasConversion&lt;string&gt;()</c> applied. Default <see cref="MatchmakingTicketType.Normal"/>.
/// <see cref="MatchmakingTicketType.Backfill"/> tickets are inserted into the Redis queue
/// with score <c>0</c> so they are processed before all Normal tickets.
/// </summary>
public MatchmakingTicketType TicketType { get; set; } = MatchmakingTicketType.Normal;
```

---

### `src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs` (model, modify)

**Analog:** self (lines 1-18) — add `RegionName` parameter

**Existing record pattern:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/mm/queue</c>. ...
/// </summary>
public sealed record EnqueueRequest(Guid LadderId, string? PoolName = null, Guid? PartyId = null);
```

**Add RegionName parameter:**
```csharp
/// <summary>
/// Request body for <c>POST /api/mm/queue</c>. Player id is sourced from the JWT
/// <c>NameIdentifier</c> claim.
/// </summary>
/// <param name="LadderId">Ladder identifier; must reference a configured matchmaking ladder.</param>
/// <param name="PoolName">Optional pool name within the ladder (defaults to <c>"default"</c>).</param>
/// <param name="PartyId">Optional party id. Solo enqueue when null.</param>
/// <param name="RegionName">
/// Optional region name for regional pool routing (MATCH-18). When null, routes to the
/// <c>"default"</c> pool (backwards-compatible v1 behaviour). When non-null, must be
/// present in the ladder's <c>AllowedRegions</c> list or the request is rejected with
/// HTTP 400 <c>region_not_allowed</c>.
/// </param>
public sealed record EnqueueRequest(
    Guid LadderId,
    string? PoolName = null,
    Guid? PartyId = null,
    string? RegionName = null);
```

---

### `src/GameKit.Matchmaking/Http/Contracts/BackfillRequest.cs` (model, NEW)

**Analog:** `src/GameKit.Matchmaking/Http/Contracts/EnqueueRequest.cs` (lines 1-18)

**Copy pattern exactly:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/mm/backfill</c>. Creates a
/// <see cref="Entities.MatchmakingTicketType.Backfill"/> ticket for a player rejoining an
/// in-progress session. The backfill ticket is inserted at score <c>0</c> in the Redis queue
/// so it is processed with higher priority than normal tickets (MATCH-19 SC#3).
/// Player id is sourced from the JWT <c>NameIdentifier</c> claim.
/// </summary>
/// <param name="LadderId">Ladder identifier; must reference a configured matchmaking ladder.</param>
/// <param name="SessionId">The active <c>game_session</c> the player is rejoining.</param>
/// <param name="RegionName">
/// Optional region name. When null, routes to the <c>"default"</c> pool.
/// When non-null, must be in the ladder's <c>AllowedRegions</c>.
/// </param>
public sealed record BackfillRequest(Guid LadderId, Guid SessionId, string? RegionName = null);
```

---

### `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs` (utility, modify)

**Analog:** self (lines 1-32) — add `RegionName` rule

**Existing validator pattern:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Matchmaking.Http.Contracts;

namespace GameKit.Matchmaking.Http.Validators;

public sealed class EnqueueRequestValidator : AbstractValidator<EnqueueRequest>
{
    public EnqueueRequestValidator()
    {
        RuleFor(x => x.LadderId)
            .NotEmpty().WithMessage("LadderId must be a non-empty Guid.");

        RuleFor(x => x.PoolName)
            .MaximumLength(64).WithMessage("PoolName must be at most 64 characters.");

        When(x => x.PartyId.HasValue, () =>
        {
            RuleFor(x => x.PartyId!.Value)
                .NotEmpty().WithMessage("PartyId must be a non-empty Guid when supplied.");
        });
    }
}
```

**Add RegionName rule (follow PoolName pattern exactly):**
```csharp
RuleFor(x => x.RegionName)
    .MaximumLength(64).WithMessage("RegionName must be at most 64 characters.")
    .Matches(@"^[a-zA-Z0-9\-]+$").When(x => x.RegionName is not null)
    .WithMessage("RegionName may only contain alphanumeric characters and hyphens (security: used as Redis key component).");
```

---

### `src/GameKit.Matchmaking/Http/Validators/BackfillRequestValidator.cs` (utility, NEW)

**Analog:** `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs` (lines 1-32)

**Copy structure exactly:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Matchmaking.Http.Contracts;

namespace GameKit.Matchmaking.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="BackfillRequest"/>. Enforces non-empty
/// <c>LadderId</c> and <c>SessionId</c>, and bounds <c>RegionName</c> length.
/// </summary>
public sealed class BackfillRequestValidator : AbstractValidator<BackfillRequest>
{
    /// <summary>Constructs the validator.</summary>
    public BackfillRequestValidator()
    {
        RuleFor(x => x.LadderId)
            .NotEmpty().WithMessage("LadderId must be a non-empty Guid.");

        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage("SessionId must be a non-empty Guid.");

        RuleFor(x => x.RegionName)
            .MaximumLength(64).WithMessage("RegionName must be at most 64 characters.")
            .Matches(@"^[a-zA-Z0-9\-]+$").When(x => x.RegionName is not null)
            .WithMessage("RegionName may only contain alphanumeric characters and hyphens.");
    }
}
```

---

### `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` (controller, modify)

**Analog:** self (lines 1-225) — add `POST /api/mm/backfill` route + handler

**Existing route registration pattern (lines 45-64):**
```csharp
routes.MapPost("/api/mm/queue", EnqueueAsync)
    .RequireAuthorization()
    .RequireRateLimiting(names.MmEnqueue)
    .AddEndpointFilter<ValidationEndpointFilter<EnqueueRequest>>();

routes.MapGet("/api/mm/queue/{ticketId:guid}/status", LongPollStatusAsync)
    .RequireAuthorization();
```

**New route to add (after existing routes, before `return routes`):**
```csharp
routes.MapPost("/api/mm/backfill", BackfillAsync)
    .RequireAuthorization()
    .RequireRateLimiting(names.MmEnqueue)   // reuse the enqueue rate-limit policy
    .AddEndpointFilter<ValidationEndpointFilter<BackfillRequest>>();
```

**New handler to add (follow EnqueueAsync pattern exactly, lines 69-105):**
```csharp
private static async Task<IResult> BackfillAsync(
    BackfillRequest req,
    HttpContext http,
    IBackfillService svc,
    CancellationToken ct)
{
    if (!TryGetPlayerId(http, out var playerId))
        return Results.Forbid();

    var result = await svc.BackfillAsync(playerId, req.LadderId, req.SessionId, req.RegionName, ct)
        .ConfigureAwait(false);
    return result.Outcome switch
    {
        BackfillOutcome.Queued => Results.Ok(new { ticketId = result.TicketId!.Value, status = "queued" }),
        BackfillOutcome.UnknownLadder => Results.BadRequest(new { error = "unknown_ladder", detail = result.Detail }),
        BackfillOutcome.SessionNotFound => Results.NotFound(new { error = "session_not_found", detail = result.Detail }),
        BackfillOutcome.SessionNotActive => Results.BadRequest(new { error = "session_not_active", detail = result.Detail }),
        BackfillOutcome.InvalidRegion => Results.BadRequest(new { error = "region_not_allowed", detail = result.Detail }),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };
}
```

**Existing TryGetPlayerId helper (lines 217-224) — reuse unchanged:**
```csharp
private static bool TryGetPlayerId(HttpContext http, out Guid playerId)
{
    playerId = default;
    var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
              ?? http.User.FindFirst("sub")?.Value;
    return sub is not null && Guid.TryParse(sub, out playerId);
}
```

---

### `src/GameKit.Matchmaking/Services/IBackfillService.cs` (service interface, NEW)

**Analog:** `src/GameKit.Matchmaking/Services/IMatchmakingService.cs` (lines 28-131)

**Interface pattern — copy header + result-record structure:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Application service driving the backfill flow (MATCH-19 SC#3). Creates
/// <see cref="Entities.MatchmakingTicketType.Backfill"/> tickets at Redis score <c>0</c>
/// so they are processed before all normal tickets by the existing ticker.
/// </summary>
public interface IBackfillService
{
    /// <summary>
    /// Create a backfill ticket for <paramref name="playerId"/> targeting the specified
    /// active session. The ticket is inserted into the Redis queue at score <c>0</c>
    /// (higher priority than all normal tickets).
    /// </summary>
    Task<BackfillResult> BackfillAsync(
        Guid playerId,
        Guid ladderId,
        Guid sessionId,
        string? regionName,
        CancellationToken ct = default);
}

/// <summary>Outcome of <see cref="IBackfillService.BackfillAsync"/>.</summary>
public enum BackfillOutcome
{
    /// <summary>Backfill ticket was queued.</summary>
    Queued = 0,
    /// <summary>The supplied ladder id is not registered.</summary>
    UnknownLadder = 1,
    /// <summary>The target session does not exist.</summary>
    SessionNotFound = 2,
    /// <summary>The target session is not in <c>Active</c> state.</summary>
    SessionNotActive = 3,
    /// <summary>The supplied region name is not in the ladder's <c>AllowedRegions</c>.</summary>
    InvalidRegion = 4,
}

/// <summary>Structured result of <see cref="IBackfillService.BackfillAsync"/>.</summary>
/// <param name="Outcome">High-level outcome.</param>
/// <param name="TicketId">Populated on <see cref="BackfillOutcome.Queued"/>.</param>
/// <param name="Detail">Optional detail string.</param>
public sealed record BackfillResult(
    BackfillOutcome Outcome,
    Guid? TicketId = null,
    string? Detail = null);
```

---

### `src/GameKit.Matchmaking/Services/BackfillService.cs` (service, NEW)

**Analog:** `src/GameKit.Matchmaking/Services/MatchmakingService.cs` — specifically the EnqueueAsync pattern (lines 62-326)

**Imports pattern (lines 1-22 of MatchmakingService.cs):**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
```

**Constructor DI pattern (lines 91-121) — copy then slim to BackfillService's needs:**
```csharp
public sealed class BackfillService : IBackfillService
{
    private readonly GameKitDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ChannelWriter<TicketEvent> _events;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IReadOnlyList<MatchmakingLadderConfig> _ladders;
    private readonly ILogger<BackfillService>? _logger;

    public BackfillService(
        GameKitDbContext db,
        IConnectionMultiplexer redis,
        ChannelWriter<TicketEvent> events,
        IClock clock,
        IIdGenerator ids,
        IReadOnlyList<MatchmakingLadderConfig> ladders,
        ILogger<BackfillService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        // ... (same guard pattern)
    }
```

**Core BackfillAsync implementation — key differences from EnqueueAsync:**
```csharp
public async Task<BackfillResult> BackfillAsync(
    Guid playerId, Guid ladderId, Guid sessionId, string? regionName, CancellationToken ct = default)
{
    // Ladder config lookup (same pattern as MatchmakingService lines 184-191)
    MatchmakingLadderConfig? cfg = _ladders.FirstOrDefault(l =>
        l.Name.Equals(/* ladder name from ladderId */..., StringComparison.OrdinalIgnoreCase));
    if (cfg is null)
        return new BackfillResult(BackfillOutcome.UnknownLadder, Detail: "no_ladders_registered");

    // Region validation — same logic as MatchmakingService EnqueueAsync (Phase 9 addition)
    var pool = string.IsNullOrWhiteSpace(regionName) ? "default" : regionName!;
    if (cfg.AllowedRegions is { Count: > 0 } && pool != "default"
        && !cfg.AllowedRegions.Contains(pool, StringComparer.OrdinalIgnoreCase))
        return new BackfillResult(BackfillOutcome.InvalidRegion, Detail: $"region_not_allowed:{pool}");

    // Session existence + Active check (Active == "Active" string — see SeedActiveGameSessionAsync)
    var session = await _db.Set<GameSession>()
        .AsNoTracking()
        .FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
    if (session is null)
        return new BackfillResult(BackfillOutcome.SessionNotFound);
    if (session.State != GameSessionState.Active)
        return new BackfillResult(BackfillOutcome.SessionNotActive);

    // Create ticket with TicketType = Backfill (lines 264-276 of MatchmakingService — copy shape)
    var ticketId = _ids.NewId();
    var now = _clock.UtcNow;
    var ticketRow = new MatchmakingTicket
    {
        Id = ticketId,
        PartyId = null,
        LadderId = ladderId,
        PoolName = pool,
        Status = TicketStatus.Queued,
        TicketType = MatchmakingTicketType.Backfill,   // KEY DIFFERENCE
        QueuedAt = now,
    };
    _db.Set<MatchmakingTicket>().Add(ticketRow);
    await _db.SaveChangesAsync(ct).ConfigureAwait(false);

    // Redis writes — same key helpers as MatchmakingService (lines 280-301)
    var db = _redis.GetDatabase();
    var ticketKey = MatchmakingRedisKeys.Ticket(ticketId);
    var queueKey = MatchmakingRedisKeys.Queue(ladderId, pool);

    await db.HashSetAsync(ticketKey,
        [
            new HashEntry("status", "queued"),
            new HashEntry("ladderId", ladderId.ToString()),
            new HashEntry("poolName", pool),
            new HashEntry("queuedAt", 0),           // score is also 0
            new HashEntry("aggregateRating", "0"),
            new HashEntry("partyId", string.Empty),
            new HashEntry("playerId", playerId.ToString()),
            new HashEntry("members", JsonSerializer.Serialize(new[] { new { PlayerId = playerId, Rating = 0.0 } })),
            new HashEntry("ticketType", "backfill"),
        ]).ConfigureAwait(false);

    // MATCH-19 SC#3 CRITICAL: score = 0 (not DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
    // Score 0 < any real Unix-ms timestamp (~1.75e12), so backfill tickets sort FIRST.
    await db.SortedSetAddAsync(queueKey, ticketId.ToString(), score: 0).ConfigureAwait(false);

    return new BackfillResult(BackfillOutcome.Queued, TicketId: ticketId);
}
```

---

### `src/GameKit.Matchmaking/Services/MatchmakingService.cs` (service, modify)

**Analog:** self — extend EnqueueAsync with region routing

**Existing pool-computation line (line 131):**
```csharp
var pool = string.IsNullOrWhiteSpace(poolName) ? "default" : poolName!;
```

**Phase 9 extension — insert after line 131, after ladder config lookup (step 3, ~line 191):**
```csharp
// MATCH-18: region validation against AllowedRegions
// pool is already set from poolName; for EnqueueRequest, regionName overrides poolName when provided.
// (Compute pool from req.RegionName at the HTTP handler layer and pass as poolName arg — see endpoint.)
// Guard: AllowedRegions is non-null/non-empty AND pool != "default" AND not in list → reject.
if (cfg.AllowedRegions is { Count: > 0 } && pool != "default"
    && !cfg.AllowedRegions.Contains(pool, StringComparer.OrdinalIgnoreCase))
{
    return new EnqueueResult(
        EnqueueOutcome.InvalidRegion,
        Detail: $"region_not_allowed:{pool}");
}
```

**Also add `InvalidRegion = 8` to `EnqueueOutcome` enum in `IMatchmakingService.cs`** (after existing value 7), and handle it in `MatchmakingEndpoints.EnqueueAsync` switch:
```csharp
EnqueueOutcome.InvalidRegion => Results.BadRequest(new { error = "region_not_allowed", detail = result.Detail }),
```

**EnqueueAsync HTTP handler call — region-name to pool resolution at the endpoint layer (lines 78-79):**
```csharp
// Compute pool from RegionName at HTTP layer; pass as poolName so IMatchmakingService stays stable.
var resolvedPool = req.RegionName ?? req.PoolName;
var result = await svc.EnqueueAsync(playerId, req.LadderId, resolvedPool, req.PartyId, ct).ConfigureAwait(false);
```

---

### `src/GameKit.Matchmaking/Services/MatchmakerTickerService.cs` (service, modify)

**Analog:** self — extend the `foreach (var ladderCfg in _ladders)` loop (lines 213-236)

**Existing loop structure (lines 213-236):**
```csharp
foreach (var ladderCfg in _ladders)
{
    ct.ThrowIfCancellationRequested();
    var renewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
    if (!renewed) return MatcherTickResult.LeaseLost;

    var poolResult = await ProcessPoolAsync(ladderCfg, now, ct).ConfigureAwait(false);
    if (poolResult == MatcherTickResult.LeaseLost) return MatcherTickResult.LeaseLost;
    if (poolResult == MatcherTickResult.Matched) anyMatch = true;
}
```

**Phase 9 extension — replace the single `ProcessPoolAsync` call with a loop over pool names:**
```csharp
foreach (var ladderCfg in _ladders)
{
    ct.ThrowIfCancellationRequested();

    foreach (var poolName in GetPoolNamesForLadder(ladderCfg))
    {
        var renewed = await _lease.RenewLeaseAsync(ct).ConfigureAwait(false);
        if (!renewed)
        {
            _logger.LogWarning(
                "MatchmakerTickerService: lease lost mid-tick before pool '{Pool}'. Bailing.",
                poolName);
            return MatcherTickResult.LeaseLost;
        }

        var poolResult = await ProcessPoolAsync(ladderCfg, poolName, now, ct).ConfigureAwait(false);
        if (poolResult == MatcherTickResult.LeaseLost) return MatcherTickResult.LeaseLost;
        if (poolResult == MatcherTickResult.Matched) anyMatch = true;
    }
}
```

**New static helper to add (private, after ProcessPoolAsync):**
```csharp
/// <summary>
/// Returns the set of pool names to scan for <paramref name="cfg"/> on each tick.
/// Always includes <c>"default"</c> (backwards-compat null-region route) plus all
/// entries in <see cref="MatchmakingLadderConfig.AllowedRegions"/> (MATCH-18 SC#2).
/// </summary>
private static IEnumerable<string> GetPoolNamesForLadder(MatchmakingLadderConfig cfg)
{
    yield return "default";
    if (cfg.AllowedRegions is { Count: > 0 })
        foreach (var r in cfg.AllowedRegions)
            yield return r;
}
```

**ProcessPoolAsync signature change (line 287-288) — add explicit `poolName` parameter:**
```csharp
// Before (line 297): var poolName = ladderCfg.Name;
// After: poolName is passed in, remove the var poolName = ladderCfg.Name; line
private async Task<MatcherTickResult> ProcessPoolAsync(
    MatchmakingLadderConfig ladderCfg, string poolName, DateTimeOffset now, CancellationToken ct)
```

---

### `src/GameKit.Matchmaking/Data/Configurations/MatchmakingTicketConfiguration.cs` (config, modify)

**Analog:** self (lines 33-75) — add `TicketType` property mapping

**Existing integer-enum property pattern (line 46):**
```csharp
// Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
b.Property(t => t.Status).IsRequired();
```

**New property to add (copy pattern exactly, after Status):**
```csharp
// Integer enum storage — DO NOT add HasConversion<string>() (Phase 5 mandatory).
// DEFAULT 0 (Normal) — existing tickets receive TicketType = 0 via migration DEFAULT clause.
b.Property(t => t.TicketType).IsRequired();
```

---

### `src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs` (migration, NEW)

**Analog:** `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs` (lines 1-61)

**File structure — copy exactly:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Matchmaking.Migrations
{
    /// <inheritdoc />
    public partial class MatchmakingBackfillRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) Add TicketType to matchmaking_tickets (MATCH-19 backfill priority).
            // integer NOT NULL DEFAULT 0 — existing tickets = Normal (0) without data fixup.
            migrationBuilder.Sql(@"
                ALTER TABLE gamekit.matchmaking_tickets
                    ADD COLUMN ""TicketType"" integer NOT NULL DEFAULT 0;");

            // (2) Add ParticipationFraction to session_participants (MATCH-19 rating guard).
            // Per-package boundary rule: Matchmaking adds columns to Core-owned table via raw SQL.
            // Nullable — existing rows get NULL (treated as full participation by the guard).
            migrationBuilder.Sql(@"
                ALTER TABLE gamekit.session_participants
                    ADD COLUMN ""ParticipationFraction"" double precision;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TicketType",
                schema: "gamekit",
                table: "matchmaking_tickets");

            migrationBuilder.DropColumn(
                name: "ParticipationFraction",
                schema: "gamekit",
                table: "session_participants");
        }
    }
}
```

**Critical conventions from analog:**
- Use raw `migrationBuilder.Sql()` — NOT `migrationBuilder.AddColumn()` (design-time factory does not apply Core/Rankings configs)
- PascalCase quoted identifiers: `"TicketType"`, `"ParticipationFraction"`
- Schema prefix: `gamekit.matchmaking_tickets`, `gamekit.session_participants`
- `#nullable disable` at file top (EF-generated convention)
- Namespace: `GameKit.Matchmaking.Migrations` (not GameKit.Rankings.Migrations)

---

### `src/GameKit.Core/Entities/SessionParticipant.cs` (model, modify)

**Analog:** self (lines 18-46) — add `ParticipationFraction` property

**Existing nullable-double property pattern (lines 39-45):**
```csharp
/// <summary>Rating snapshot at session start. Populated at completion by <c>GameKit.Rankings</c> (Phase 4).</summary>
public double? RatingBefore { get; set; }

/// <summary>Rating snapshot at session end. Populated at completion by <c>GameKit.Rankings</c> (Phase 4).</summary>
public double? RatingAfter { get; set; }

/// <summary>Rating delta (<see cref="RatingAfter"/> - <see cref="RatingBefore"/>). Denormalized for leaderboard speed.</summary>
public double? RatingDelta { get; set; }
```

**New property to add (copy nullable double pattern):**
```csharp
/// <summary>
/// Fraction [0.0–1.0] of the session this participant was present for. Populated by the
/// game server at session completion. When null, the participant is treated as fully
/// present (no rating penalty). Used by <c>GameKit.Rankings.PendingRatingUpdatesAdapter</c>
/// to apply the <c>MinParticipationFractionForRating</c> guard (MATCH-19 SC#4).
/// Column added by <c>GameKit.Matchmaking</c> migration <c>20260520000000</c> per the
/// per-package migration boundary rule.
/// </summary>
public double? ParticipationFraction { get; set; }
```

**EF configuration note:** The existing `SessionParticipantConfiguration` (in Core) must also map this column: `b.Property(sp => sp.ParticipationFraction);` — nullable double, no constraints.

---

### `src/GameKit.Rankings/Services/PendingRatingUpdatesAdapter.cs` (service, modify)

**Analog:** self (lines 46-146) — add participation-fraction guard before the `PendingRatingUpdate` INSERT

**Existing per-participant loop with playerRank read (lines 78-121):**
```csharp
foreach (var participant in participants)
{
    if (participant.LadderId.HasValue)
    {
        var playerRank = await _ctx.Set<PlayerRank>()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                r => r.PlayerId == participant.PlayerId && r.LadderId == participant.LadderId.Value,
                ct);

        if (playerRank is not null)
        {
            await _ctx.SessionParticipants
                .Where(sp => sp.SessionId == sessionId && sp.PlayerId == participant.PlayerId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(sp => sp.RatingBefore, playerRank.Rating),
                    ct);
            // ... placement decrement ...
        }
    }

    if (!participant.LadderId.HasValue)
        continue;

    var row = new PendingRatingUpdate { ... };
    _ctx.Set<PendingRatingUpdate>().Add(row);
}
```

**Phase 9 participation-fraction guard — insert AFTER playerRank block, BEFORE `PendingRatingUpdate` INSERT:**
```csharp
// MATCH-19 SC#4: participation-fraction guard.
// Re-read ParticipationFraction from the session_participants row (added by Matchmaking
// migration 20260520000000). The column is null for pre-Phase-9 rows → guard is skipped.
// MinParticipationFractionForRating is read from the ladder's JSONB Config (same
// mechanism as RatingPeriodSeconds in RankingsTickerService.ReadRatingPeriod).
var sp = await _ctx.SessionParticipants
    .AsNoTracking()
    .Where(s => s.SessionId == sessionId && s.PlayerId == participant.PlayerId)
    .Select(s => new { s.ParticipationFraction })
    .FirstOrDefaultAsync(ct)
    .ConfigureAwait(false);

if (sp?.ParticipationFraction.HasValue == true)
{
    // Read MinParticipationFractionForRating from ladder JSONB Config.
    var ladder = await _ctx.Set<Ladder>()
        .AsNoTracking()
        .FirstOrDefaultAsync(l => l.Id == participant.LadderId!.Value, ct)
        .ConfigureAwait(false);
    var minFraction = ReadMinParticipationFraction(ladder);
    if (minFraction.HasValue && sp.ParticipationFraction.Value < minFraction.Value)
        continue; // Skip PendingRatingUpdate — no rating change for this participant.
}
```

**JSONB config read helper to add (follow RankingsTickerService.ReadRatingPeriod pattern, lines 536-555):**
```csharp
/// <summary>
/// Reads <c>MinParticipationFractionForRating</c> from the ladder's JSONB Config.
/// Returns null when absent or unparseable — null means no guard is applied.
/// </summary>
private static double? ReadMinParticipationFraction(Ladder? ladder)
{
    if (ladder?.Config is null) return null;
    try
    {
        if (ladder.Config.RootElement.TryGetProperty("MinParticipationFractionForRating", out var elem)
            && elem.TryGetDouble(out var value))
            return value;
    }
    catch { /* ignore JSON parse errors */ }
    return null;
}
```

---

## Integration Test Files

### `tests/GameKit.Matchmaking.Integration.Tests/RegionalPoolTests.cs` (test, NEW)

**Analog:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingHappyPathTests.cs` (lines 1-100+)

**Test class structure to copy:**
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.Matchmaking.Redis;
using GameKit.TestFixtures;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class RegionalPoolTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;
    private ConnectionMultiplexer? _mux;

    public RegionalPoolTests(PostgresFixture pg, RedisFixture redis) { _pg = pg; _redis = redis; }

    public async Task InitializeAsync()
    {
        _app = new MatchmakingTestApp();
        await _app.StartAsync(_pg, _redis);
        _mux = await ConnectionMultiplexer.ConnectAsync(_redis.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null) await _mux.DisposeAsync();
        if (_app is not null) await _app.DisposeAsync();
    }

    [Fact]
    public async Task SC1_Enqueue_MismatchedRegionName_Returns400()
    { /* POST /api/mm/queue with RegionName="eu-west" on a ladder with AllowedRegions=["us-east"] → 400 */ }

    [Fact]
    public async Task SC1_NullRegion_RoutesToDefaultPool()
    { /* POST /api/mm/queue with RegionName=null → key mm:queue:{id}:default */ }

    [Fact]
    public async Task SC2_RegionalKey_IsDistinctFromDefaultKey()
    { /* POST /api/mm/queue with RegionName="us-east" → key mm:queue:{id}:us-east, not mm:queue:{id}:default */ }

    [Fact]
    public async Task SC2_TickerGlob_PicksUpBothRegionalAndDefaultKeys()
    { /* Ticker IMatchmakerTicker.RunOnceAsync() picks up both us-east and default pool keys */ }
}
```

**Key pattern from MatchmakingHappyPathTests for Redis assertion (lines 77-101):**
```csharp
var db = _mux!.GetDatabase();
var score = await db.SortedSetScoreAsync(
    MatchmakingRedisKeys.Queue(_app.TestLadderId, "us-east"), ticketId.ToString());
Assert.NotNull(score);  // ticket is in the us-east pool, not default
```

### `tests/GameKit.Matchmaking.Integration.Tests/BackfillTests.cs` (test, NEW)

**Analog:** `MatchmakingHappyPathTests.cs` — same class skeleton, same [Collection("Matchmaking")] attribute

**Key backfill-specific assertions:**
```csharp
[Fact]
public async Task SC3_Backfill_CreatesBackfillTypedTicket() { /* ticket type in Postgres = 1 (Backfill) */ }

[Fact]
public async Task SC3_Priority_BackfillTicket_ProcessedBeforeNormalTicket()
{
    // 1. Enqueue a Normal ticket (score = now ms)
    // 2. POST /api/mm/backfill (score = 0)
    // 3. Assert: SortedSetRangeByScoreAsync returns backfill ticket first (score 0 < now ms)
    var db = _mux!.GetDatabase();
    var queueKey = MatchmakingRedisKeys.Queue(ladderId, "default");
    var members = await db.SortedSetRangeByScoreWithScoresAsync(
        queueKey, double.NegativeInfinity, double.PositiveInfinity, Exclude.None, Order.Ascending, 0, 2);
    Assert.Equal(backfillTicketId.ToString(), members[0].Element.ToString());
    Assert.Equal(0, members[0].Score);
}
```

### `tests/GameKit.Matchmaking.Integration.Tests/BackfillParticipationTests.cs` (test, NEW)

**Analog:** `MatchmakingHappyPathTests.cs` skeleton + cross-package Rankings session-complete path

**Key participation-fraction assertions:**
```csharp
[Fact]
public async Task SC4_ParticipationFractionBelowMinimum_SkipsRatingChange()
{
    // 1. Seed session_participants with ParticipationFraction = 0.3
    // 2. Configure ladder MinParticipationFractionForRating = 0.5 in JSONB Config
    // 3. Run PendingRatingUpdatesAdapter.OnCompletedAsync
    // 4. Assert: no pending_rating_updates row inserted for that player
    var count = await db.Set<PendingRatingUpdate>()
        .CountAsync(r => r.PlayerId == playerId && r.SessionId == sessionId);
    Assert.Equal(0, count);  // guard fired — no rating change
}
```

---

## Shared Patterns

### Integer Enum Storage (Phase 5 mandatory)
**Source:** `src/GameKit.Matchmaking/Entities/TicketStatus.cs` (lines 1-41) + `MatchmakingTicketConfiguration.cs` (line 46)
**Apply to:** `MatchmakingTicketType.cs` (new enum) + `MatchmakingTicketConfiguration.cs` (TicketType column mapping)
```csharp
// Entity config — NEVER HasConversion<string>():
b.Property(t => t.TicketType).IsRequired();

// Enum definition — explicit integer values:
public enum MatchmakingTicketType { Normal = 0, Backfill = 1 }
```

### SPDX License Header
**Source:** Every source file in `/src/GameKit.Matchmaking/`
**Apply to:** All new files
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

### Builder-Time Fail-Fast Validation
**Source:** `src/GameKit.Matchmaking/Builder/GameKitMatchmakingBuilder.cs` `ValidateLadderConfig` (lines 67-98)
**Apply to:** `GameKitMatchmakingBuilder.cs` AllowedRegions validation
```csharp
// Pattern: throw ArgumentException with paramName: nameof(config)
throw new ArgumentException(
    $"{nameof(config.AllowedRegions)} entry ...",
    nameof(config));
```

### Raw SQL Migration Pattern
**Source:** `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs` (lines 14-37)
**Apply to:** `20260520000000_MatchmakingBackfillRegions.cs`
```csharp
// Use migrationBuilder.Sql() with raw ALTER TABLE — not migrationBuilder.AddColumn()
migrationBuilder.Sql(@"
    ALTER TABLE gamekit.matchmaking_tickets
        ADD COLUMN ""TicketType"" integer NOT NULL DEFAULT 0;");
```

### Redis Key Format
**Source:** `src/GameKit.Matchmaking/Redis/MatchmakingRedisKeys.cs` (lines 68-72)
**Apply to:** BackfillService.cs + MatchmakingService.cs (region routing)
```csharp
// Always use the MatchmakingRedisKeys helper — never hand-craft keys:
MatchmakingRedisKeys.Queue(ladderId, pool)   // pool = "default" | regionName
MatchmakingRedisKeys.Ticket(ticketId)
```

### JSONB Config Read Pattern
**Source:** `src/GameKit.Rankings/Services/RankingsTickerService.cs` (lines 536-583)
**Apply to:** `PendingRatingUpdatesAdapter.cs` ReadMinParticipationFraction helper
```csharp
private static double? ReadMinParticipationFraction(Ladder? ladder)
{
    if (ladder?.Config is null) return null;
    try
    {
        if (ladder.Config.RootElement.TryGetProperty("MinParticipationFractionForRating", out var elem)
            && elem.TryGetDouble(out var value))
            return value;
    }
    catch { }
    return null;
}
```

### FluentValidation Validator Pattern
**Source:** `src/GameKit.Matchmaking/Http/Validators/EnqueueRequestValidator.cs` (lines 1-32)
**Apply to:** `BackfillRequestValidator.cs`
```csharp
public sealed class XxxValidator : AbstractValidator<XxxRequest>
{
    public XxxValidator()
    {
        RuleFor(x => x.SomeGuid).NotEmpty().WithMessage("...");
        RuleFor(x => x.SomeString).MaximumLength(64).WithMessage("...");
    }
}
```

### Endpoint Handler Pattern
**Source:** `src/GameKit.Matchmaking/Http/MatchmakingEndpoints.cs` EnqueueAsync (lines 69-105)
**Apply to:** BackfillAsync handler in `MatchmakingEndpoints.cs`
```csharp
private static async Task<IResult> XxxAsync(XxxRequest req, HttpContext http, IXxxService svc, CancellationToken ct)
{
    if (!TryGetPlayerId(http, out var playerId))
        return Results.Forbid();
    var result = await svc.XxxAsync(...).ConfigureAwait(false);
    return result.Outcome switch { ... };
}
```

### Test Class Structure
**Source:** `tests/GameKit.Matchmaking.Integration.Tests/MatchmakingHappyPathTests.cs` (lines 35-60)
**Apply to:** All three new test files
```csharp
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class XxxTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;
    private ConnectionMultiplexer? _mux;
    // InitializeAsync / DisposeAsync with _app + _mux lifecycle
}
```

### Integration Test Seed Helpers
**Source:** `tests/GameKit.Matchmaking.Integration.Tests/IntegrationTestHelpers.cs` (lines 109-175)
**Apply to:** `BackfillParticipationTests.cs` (seed session participants with ParticipationFraction)
```csharp
// Pattern: raw NpgsqlCommand INSERT — matches SeedTicketAsync/SeedPlayerAsync shape
public static async Task<Guid> SeedSessionParticipantAsync(
    string cs, Guid sessionId, Guid playerId, double? participationFraction)
{
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();
    // INSERT INTO gamekit.session_participants (..., "ParticipationFraction") VALUES (...)
}
```

---

## No Analog Found

All files have clear analogs in the existing codebase. No "no analog" entries.

---

## Metadata

**Analog search scope:** `src/GameKit.Matchmaking/`, `src/GameKit.Core/Entities/`, `src/GameKit.Rankings/`, `tests/GameKit.Matchmaking.Integration.Tests/`
**Files scanned:** 18 source files read directly
**Pattern extraction date:** 2026-06-06

**Advisory lock key:** `388956820` (MatchmakingMigrationConstants.AdvisoryLockKey — do not change)
**Migration history table:** `__ef_migrations_matchmaking` in schema `gamekit`
**Migration timestamp:** `20260520000000`
