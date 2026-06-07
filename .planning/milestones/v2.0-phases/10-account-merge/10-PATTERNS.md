# Phase 10: Account Merge (Isolated High-Risk) - Pattern Map

**Mapped:** 2026-06-06
**Files analyzed:** 19 new/modified files
**Analogs found:** 19 / 19

---

## File Classification

| New/Modified File | Role | Data Flow | Closest Analog | Match Quality |
|---|---|---|---|---|
| `src/GameKit.Auth/Services/IAccountMergeService.cs` | service-interface | request-response | `src/GameKit.Auth/Services/IRefreshTokenService.cs` | exact |
| `src/GameKit.Auth/Services/AccountMergeService.cs` | service | CRUD + batch (FK surgery) | `src/GameKit.Auth/Services/IdentityLinker.cs` + `src/GameKit.Rankings/Services/EndSeasonService.cs` | exact (composite) |
| `src/GameKit.Auth/Services/MergeResult.cs` | utility (result type) | — | `src/GameKit.Auth/Services/LinkResult.cs` (pattern) | role-match |
| `src/GameKit.Auth/Entities/AccountMerge.cs` | model/entity | CRUD | `src/GameKit.Auth/Entities/PlayerIdentity.cs` | role-match |
| `src/GameKit.Auth/Data/Configurations/AccountMergeConfiguration.cs` | config | — | `src/GameKit.Auth/Data/Configurations/PlayerIdentityConfiguration.cs` | exact |
| `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` | config (modify) | — | itself (extend `AuthMigrationModelCustomizer` exclusion list) | exact |
| `src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs` | migration | — | `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs` | exact |
| `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` | config (modify) | — | itself (add `IAccountMergeService` registration) | exact |
| `src/GameKit.Auth/AssemblyInfo.cs` | config (modify) | — | itself (add `InternalsVisibleTo` grant) | exact |
| `src/GameKit.Core/Entities/Player.cs` | model (modify) | — | itself (add `MergedIntoPlayerId` + `DeletedAt`) | exact |
| `src/GameKit.Core/Data/Configurations/PlayerConfiguration.cs` | config (modify) | — | itself (add property/FK mapping) | exact |
| `src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs` | config (modify) | — | itself (add `HasOne<Player>()` FK ON DELETE SET NULL) | exact |
| `src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.cs` | migration | — | `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` | exact |
| `src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs` | migration | — | `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` | exact |
| `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` | controller (modify) | request-response | itself (add `POST /players/merge` endpoint) | exact |
| `src/GameKit.Admin.UI/Http/Contracts/MergePlayersRequest.cs` | model/DTO | — | `src/GameKit.Admin.UI/Http/Contracts/BanPlayerRequest.cs` | exact |
| `src/GameKit.Admin.UI/Http/Validators/MergePlayersRequestValidator.cs` | utility (validator) | — | `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs` | exact |
| `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs` | config (modify) | — | itself (add merge policy) | exact |
| `tests/GameKit.Auth.AccountMerge.Integration.Tests/` | test (new project) | — | `tests/GameKit.Auth.Integration.Tests/` | exact |

---

## Pattern Assignments

### `src/GameKit.Auth/Services/IAccountMergeService.cs` (service-interface, request-response)

**Analog:** `src/GameKit.Auth/Services/IRefreshTokenService.cs`

**Imports pattern** (lines 1-8):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Auth.Services;
```

**Core interface pattern** (lines 19-48):
```csharp
// IRefreshTokenService.cs — one method per concern, XML docs on every member
public interface IRefreshTokenService
{
    Task<TokenPair> IssueRootAsync(Guid playerId, string provider, string? fingerprint, CancellationToken cancellationToken = default);
    Task<TokenPair> RotateAsync(string rawRefreshToken, string? fingerprint, CancellationToken cancellationToken = default);
    Task RevokeFamilyAsync(string rawRefreshToken, string reason, CancellationToken cancellationToken = default);
    Task RevokeAllForPlayerAsync(Guid playerId, string reason, CancellationToken cancellationToken = default);
}
```

**Apply to `IAccountMergeService`:**
```csharp
/// <summary>Merges <paramref name="sourcePlayerId"/> into <paramref name="targetPlayerId"/>...</summary>
Task<MergeResult> MergeAsync(Guid sourcePlayerId, Guid targetPlayerId, Guid actorId, CancellationToken cancellationToken = default);
```

---

### `src/GameKit.Auth/Services/AccountMergeService.cs` (service, CRUD + batch)

**Analog A:** `src/GameKit.Auth/Services/IdentityLinker.cs` (SERIALIZABLE + retry loop + TryFindPostgresException)
**Analog B:** `src/GameKit.Rankings/Services/EndSeasonService.cs` (Polly retry pipeline + direct AdminAuditLog write)

**GPL header** (lines 1-3, both analogs):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
```

**Imports pattern** (from both analogs, merged for AccountMergeService):
```csharp
using System;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
```

**Retry approach decision** (per RESEARCH.md Open Questions #1 and State of Art table): The manual loop pattern from `IdentityLinker` is preferred for `AccountMergeService` to stay consistent with Auth's existing style. Do NOT use Polly here (Polly is Rankings' style; Auth uses the manual loop).

**SERIALIZABLE + retry loop core pattern** (`IdentityLinker.cs` lines 74-176):
```csharp
private const int MaxRetries = 3;

// ...

for (var attempt = 0; attempt < MaxRetries; attempt++)
{
    await using var tx = await _ctx.Database
        .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
        .ConfigureAwait(false);

    try
    {
        // transaction body ...
        await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
    catch (Exception ex) when (TryFindPostgresException(ex) is { } pg)
    {
        await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);

        // Detach in-flight entities so the scoped DbContext stays usable on retry.
        foreach (var entry in _ctx.ChangeTracker.Entries())
        {
            entry.State = EntityState.Detached;
        }

        if (pg.SqlState == "23505") { /* handle unique violation per-case */ }
        if (pg.SqlState == "40001" && attempt < MaxRetries - 1) continue;
        throw;
    }
}

throw new InvalidOperationException("AccountMergeService: SERIALIZABLE retries exhausted.");
```

**TryFindPostgresException helper** (`IdentityLinker.cs` lines 187-195 — copy verbatim into `AccountMergeService`):
```csharp
private static PostgresException? TryFindPostgresException(Exception? ex)
{
    for (var i = 0; i < 8 && ex is not null; i++)
    {
        if (ex is PostgresException pg) return pg;
        ex = ex.InnerException;
    }
    return null;
}
```

**Change-tracker detach on retry** (`GuestUpgradeService.cs` lines 112-115 — VERIFIED critical for retry safety, Pitfall 5):
```csharp
foreach (var entry in _ctx.ChangeTracker.Entries())
{
    entry.State = EntityState.Detached;
}
```

**Direct AdminAuditLog write pattern** (`EndSeasonService.cs` lines 193-217):
```csharp
// Audit action literal — mirrors AdminAuditActions.AccountMerge in GameKit.Admin.UI.
// Duplicated here as a literal to avoid the circular dependency. The value MUST stay in sync.
private const string AccountMergeAction = "auth.account_merge";

// Inside SERIALIZABLE tx, after all FK re-points:
_ctx.Set<AdminAuditLog>().Add(new AdminAuditLog
{
    Id = _ids.NewId(),
    ActorId = actorId,
    Action = AccountMergeAction,
    TargetType = "player",
    TargetId = targetPlayerId,    // surviving player — NEVER the source
    Before = JsonDocument.Parse(JsonSerializer.Serialize(new { /* source snapshot */ })),
    After  = JsonDocument.Parse(JsonSerializer.Serialize(new { /* target snapshot */ })),
    Reason = null,
    CreatedAt = _clock.UtcNow,
});
await _ctx.SaveChangesAsync(ct).ConfigureAwait(false);
```

**Refresh token revocation** (`RefreshTokenService.cs` lines 242-260):
```csharp
// IRefreshTokenService.RevokeAllForPlayerAsync — call with reason "account_merge":
// Implementation uses ExecuteUpdateAsync on refresh_tokens WHERE player_id = playerId AND RevokedAt IS NULL.
await _refresh.RevokeAllForPlayerAsync(sourcePlayerId, "account_merge", cancellationToken)
    .ConfigureAwait(false);
```

**Constructor pattern** (`IdentityLinker.cs` lines 45-62):
```csharp
public AccountMergeService(
    GameKitDbContext ctx,
    IClock clock,
    IIdGenerator ids,
    IRefreshTokenService refresh,
    ILogger<AccountMergeService>? logger = null)
{
    ArgumentNullException.ThrowIfNull(ctx);
    ArgumentNullException.ThrowIfNull(clock);
    ArgumentNullException.ThrowIfNull(ids);
    ArgumentNullException.ThrowIfNull(refresh);
    _ctx = ctx;
    _clock = clock;
    _ids = ids;
    _refresh = refresh;
}
```

---

### `src/GameKit.Auth/Services/MergeResult.cs` (utility, result discriminated union)

**Analog:** Pattern implied by `LinkResult` in GameKit.Auth (returned by `IdentityLinker`).

**Pattern:** A sealed class or record with a status enum + factory methods. Mirror the discriminated-result style already used in Auth:
```csharp
// Enum for merge status (integer-backed per project convention):
public enum MergeResultKind { Merged = 0, AlreadyMerged = 1 }

public sealed class MergeResult
{
    public MergeResultKind Kind { get; }
    public Guid TargetPlayerId { get; }

    private MergeResult(MergeResultKind kind, Guid targetPlayerId) { ... }
    public static MergeResult Merged(Guid targetPlayerId) => new(MergeResultKind.Merged, targetPlayerId);
    public static MergeResult AlreadyMerged(Guid targetPlayerId) => new(MergeResultKind.AlreadyMerged, targetPlayerId);
}
```

---

### `src/GameKit.Auth/Entities/AccountMerge.cs` (model, CRUD)

**Analog:** `src/GameKit.Auth/Entities/PlayerIdentity.cs`

**Entity pattern** (`PlayerIdentity.cs` lines 1-43):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Text.Json;

namespace GameKit.Auth.Entities;

/// <summary>XML doc comment required on every public type.</summary>
public sealed class PlayerIdentity
{
    /// <summary>Row id — UUIDv7 assigned by <c>IIdGenerator</c>.</summary>
    public Guid Id { get; set; }

    /// <summary>FK → <c>players.id</c>. ON DELETE CASCADE — ...</summary>
    public Guid PlayerId { get; set; }

    // ... sparse JSONB for Metadata:
    public JsonDocument? Metadata { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
```

**Apply to `AccountMerge.cs`:** Use `MergeStatus` integer enum (not string) per project convention. All nullable `DateTimeOffset?` columns use `DateTimeOffset?`. `JsonDocument?` for `Metadata`. Copy the `sealed class` + XML docs structure verbatim.

---

### `src/GameKit.Auth/Data/Configurations/AccountMergeConfiguration.cs` (config)

**Analog:** `src/GameKit.Auth/Data/Configurations/PlayerIdentityConfiguration.cs`

**EF configuration pattern** (`PlayerIdentityConfiguration.cs` lines 1-38):
```csharp
internal sealed class PlayerIdentityConfiguration : IEntityTypeConfiguration<PlayerIdentity>
{
    public void Configure(EntityTypeBuilder<PlayerIdentity> b)
    {
        b.ToTable("player_identities");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();   // UUIDv7 assigned at service layer

        b.Property(p => p.Provider).IsRequired().HasMaxLength(16);
        b.Property(p => p.Metadata).HasColumnType("jsonb");
        b.Property(p => p.CreatedAt).IsRequired();

        // UNIQUE index:
        b.HasIndex(p => new { p.Provider, p.ExternalId }).IsUnique();
        b.HasIndex(p => p.PlayerId);

        // FK with cascade behavior:
        b.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

**Apply to `AccountMergeConfiguration.cs`:**
- `b.ToTable("account_merges")`
- `b.HasKey(a => a.Id); b.Property(a => a.Id).ValueGeneratedNever()`
- `b.Property(a => a.Status).IsRequired()` (integer enum — no `.HasConversion()` needed, EF maps `int` enum directly)
- `b.Property(a => a.Metadata).HasColumnType("jsonb")`
- UNIQUE index on `SourcePlayerId` (prevents double-merge)
- Indexes on `SourcePlayerId` and `TargetPlayerId`
- FK: `b.HasOne<Player>().WithMany().HasForeignKey(a => a.TargetPlayerId).OnDelete(DeleteBehavior.Restrict)` — RESTRICT so target cannot be GDPR-deleted while merge record exists
- NO FK on `SourcePlayerId` — the source player is soft-deleted (not hard-deleted), but keeping it as a bare UUID column avoids FK constraint issues if the source row is later hard-deleted by GDPR

---

### `src/GameKit.Auth/Data/AuthDesignTimeDbContextFactory.cs` (modify: add AccountMerge to AuthMigrationModelCustomizer)

**Analog:** Itself — `AuthMigrationModelCustomizer.Customize()` method (`AuthDesignTimeDbContextFactory.cs` lines 97-123).

**Pattern for adding new Auth entity** (lines 102-104):
```csharp
// Existing lines add three Auth entity configurations:
modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());

// ADD the new configuration:
modelBuilder.ApplyConfiguration(new AccountMergeConfiguration());
```

The Core exclusion list (`coreEntityTypes` array) does NOT need to change — `AccountMerge` is an Auth entity, not a Core entity.

---

### `src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs` (migration)

**Analog:** `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs`

**Migration class pattern** (`AuthInitial.cs` lines 1-100 — key excerpts):
```csharp
// NOTE: No GPL header in generated migration files (EF-generated boilerplate).
using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Auth.Migrations
{
    public partial class AddAccountMerges : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_merges",
                schema: "gamekit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RedisCleanedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_merges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_merges_players_TargetPlayerId",
                        column: x => x.TargetPlayerId,
                        principalSchema: "gamekit",
                        principalTable: "players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_merges_SourcePlayerId",
                schema: "gamekit",
                table: "account_merges",
                column: "SourcePlayerId",
                unique: true);   // prevents double-merge

            migrationBuilder.CreateIndex(
                name: "IX_account_merges_TargetPlayerId",
                schema: "gamekit",
                table: "account_merges",
                column: "TargetPlayerId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "account_merges", schema: "gamekit");
        }
    }
}
```

**Advisory lock key:** Uses `AuthMigrationConstants.AdvisoryLockKey` = `-298890956L` (same key as existing Auth migrations — the migration hosted service already uses it).

---

### `src/GameKit.Core/Entities/Player.cs` (modify: add MergedIntoPlayerId + DeletedAt)

**Analog:** Itself — `Player.cs` lines 1-47.

**Existing property pattern** (lines 19-47):
```csharp
/// <summary>Player id — UUIDv7 (time-ordered) generated by the registered <c>IIdGenerator</c>.</summary>
public Guid Id { get; set; }
// ...
public bool IsBanned { get; set; }
public DateTimeOffset? BannedAt { get; set; }
public string? BanReason { get; set; }
```

**ADD two properties** after `BanReason` (per RESEARCH.md `merged_into_player_id` Tombstone section):
```csharp
/// <summary>
/// When non-null, this player has been merged into the referenced target player.
/// The row is retained as a tombstone; <c>DeletedAt</c> will also be set.
/// </summary>
public Guid? MergedIntoPlayerId { get; set; }

/// <summary>
/// UTC timestamp of soft-delete (account merge tombstone). Null for active players.
/// Per GDPR design decision D-13, hard erasure uses <c>ExecuteDeleteAsync</c> — this column
/// is only for merge tombstones, not general GDPR soft-delete.
/// </summary>
public DateTimeOffset? DeletedAt { get; set; }
```

**Update class remarks** — the class currently states "players are hard-deleted on erasure request; there is no `deleted_at` soft-delete column". Update to clarify `DeletedAt` is only for merge tombstones, GDPR erasure remains a hard-delete.

---

### `src/GameKit.Core/Data/Configurations/PlayerConfiguration.cs` (modify: map new columns + self-FK)

**Analog:** Itself — `PlayerConfiguration.cs` lines 1-32.

**Existing mapping pattern** (lines 23-28):
```csharp
b.Property(p => p.IsBanned).IsRequired().HasDefaultValue(false);
b.Property(p => p.BannedAt);
b.Property(p => p.BanReason).HasMaxLength(500);
b.Property(p => p.Metadata).HasColumnType("jsonb");
```

**ADD after existing properties** (per RESEARCH.md EF configuration addition):
```csharp
b.Property(p => p.MergedIntoPlayerId);
b.Property(p => p.DeletedAt);

// Self-referential FK: merged_into_player_id → players.id ON DELETE SET NULL
// (if the target player is later GDPR-deleted, the tombstone reference becomes NULL).
b.HasOne<Player>()
    .WithMany()
    .HasForeignKey(p => p.MergedIntoPlayerId)
    .OnDelete(DeleteBehavior.SetNull);
```

---

### `src/GameKit.Core/Data/Configurations/AdminAuditLogConfiguration.cs` (modify: add actor_id FK)

**Analog:** Itself — `AdminAuditLogConfiguration.cs` lines 1-35.

**Current state of actor_id** (line 21 — confirmed no FK):
```csharp
b.Property(a => a.ActorId);
// ...
b.HasIndex(a => a.ActorId);   // bare index, NO HasOne<Player>()
```

**ADD after existing configuration** (per RESEARCH.md `admin_audit_log.actor_id` FK section):
```csharp
// Add FK ON DELETE SET NULL so tombstoning a player does not orphan audit history.
// This corresponds to Core migration 20260606100000_AddAuditActorIdFk.
b.HasOne<Player>()
    .WithMany()
    .HasForeignKey(a => a.ActorId)
    .OnDelete(DeleteBehavior.SetNull);
```

---

### `src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.cs` (Core migration)

**Analog:** `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs`

**Core migration pattern** (lines 1-37 — complete file):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Core.Migrations
{
    /// <summary>
    /// Adds the <c>merged_into_player_id</c> tombstone column and <c>deleted_at</c> column to
    /// <c>gamekit.players</c> (Phase 10 account merge). Core is the sole owner of this column
    /// per CLAUDE.md per-package boundary rule.
    /// </summary>
    public partial class AddMergedIntoPlayerId : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoPlayerId",
                schema: "gamekit",
                table: "players",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                schema: "gamekit",
                table: "players",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_players_players_MergedIntoPlayerId",
                schema: "gamekit",
                table: "players",
                column: "MergedIntoPlayerId",
                principalSchema: "gamekit",
                principalTable: "players",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_players_players_MergedIntoPlayerId",
                schema: "gamekit",
                table: "players");
            migrationBuilder.DropColumn(name: "MergedIntoPlayerId", schema: "gamekit", table: "players");
            migrationBuilder.DropColumn(name: "DeletedAt", schema: "gamekit", table: "players");
        }
    }
}
```

**Advisory lock key:** `GameKitMigrationConstants.AdvisoryLockKey` = `1800940027L` (Core key).

---

### `src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs` (Core migration)

**Analog:** `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs`

**Pattern** (same structure as above, different body):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameKit.Core.Migrations
{
    /// <summary>
    /// Adds <c>FK_admin_audit_log_players_ActorId</c> ON DELETE SET NULL to <c>gamekit.admin_audit_log</c>.
    /// This FK did not exist in CoreInitial — actor_id was a bare column. Required by Phase 10
    /// account-merge so tombstoning the source player does not orphan audit history (SC#4).
    /// </summary>
    public partial class AddAuditActorIdFk : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_admin_audit_log_players_ActorId",
                schema: "gamekit",
                table: "admin_audit_log",
                column: "ActorId",
                principalSchema: "gamekit",
                principalTable: "players",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_admin_audit_log_players_ActorId",
                schema: "gamekit",
                table: "admin_audit_log");
        }
    }
}
```

---

### `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` (modify: add POST /players/merge)

**Analog:** Itself — `AdminEndpoints.cs` lines 97-99 (the GDPR-delete endpoint registration, nearest superadmin+antiforgery+validator pattern).

**Superadmin + antiforgery + validator endpoint registration pattern** (lines 97-99):
```csharp
// POST /players/{id}/gdpr-delete — superadmin + antiforgery (T-03-07-07).
group.MapPost("/players/{id:guid}/gdpr-delete", GdprDeletePlayerAsync)
    .RequireAuthorization(AdminPolicies.Superadmin)
    .AddEndpointFilter<AntiforgeryValidationFilter>();
```

**Apply to merge endpoint** (add in `Map()` method, after gdpr-delete registration):
```csharp
// POST /players/merge — superadmin + antiforgery + validator. Rate-limited (A4).
group.MapPost("/players/merge", MergePlayersAsync)
    .RequireAuthorization(AdminPolicies.Superadmin)
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .AddEndpointFilter<ValidationEndpointFilter<MergePlayersRequest>>()
    .RequireRateLimiting(AdminRateLimitRegistrations.AdminMergePolicy);
```

**Handler pattern** (from `GdprDeletePlayerAsync` lines 252-283 — adapt shape):
```csharp
private static async Task<IResult> MergePlayersAsync(
    MergePlayersRequest req,
    HttpContext http,
    IAccountMergeService mergeSvc,
    CancellationToken ct)
{
    var actorId = GetAdminId(http);
    try
    {
        var result = await mergeSvc.MergeAsync(req.SourcePlayerId, req.TargetPlayerId, actorId, ct)
            .ConfigureAwait(false);
        // SC#5: NEVER include SourcePlayerId in the response.
        return Results.Ok(new MergePlayersResponse(result.TargetPlayerId,
            result.Kind == MergeResultKind.AlreadyMerged ? "already_merged" : "merged"));
    }
    catch (MergeConflictException ex)
    {
        return Results.Conflict(new { error = ex.Reason.ToString().ToLowerInvariant() });
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = "player_not_found", detail = ex.Message });
    }
}
```

**GetAdminId helper** (lines 496-501 — reuse as-is):
```csharp
private static Guid GetAdminId(HttpContext http)
{
    var nameId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    return Guid.TryParse(nameId, out var id)
        ? id
        : throw new UnauthorizedAccessException("Admin id claim is missing or malformed.");
}
```

---

### `src/GameKit.Admin.UI/Http/Contracts/MergePlayersRequest.cs` (DTO)

**Analog:** `src/GameKit.Admin.UI/Http/Contracts/BanPlayerRequest.cs`

**DTO pattern** (`BanPlayerRequest.cs` lines 1-12):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Admin.UI.Http.Contracts;

/// <summary>
/// Request body for <c>POST /admin/api/players/merge</c>. Both GUIDs are required and
/// validated by <see cref="Validators.MergePlayersRequestValidator"/> before the merge
/// transaction opens.
/// </summary>
/// <param name="SourcePlayerId">Player to absorb (will be soft-deleted).</param>
/// <param name="TargetPlayerId">Player that survives the merge.</param>
public sealed record MergePlayersRequest(Guid SourcePlayerId, Guid TargetPlayerId);
```

Also add the response DTO (same file or adjacent file):
```csharp
/// <summary>HTTP response for a successful or idempotent merge. Never includes <c>SourcePlayerId</c> (SC#5).</summary>
/// <param name="TargetPlayerId">The surviving player's id.</param>
/// <param name="Status"><c>merged</c> or <c>already_merged</c>.</param>
public sealed record MergePlayersResponse(Guid TargetPlayerId, string Status);
```

---

### `src/GameKit.Admin.UI/Http/Validators/MergePlayersRequestValidator.cs` (validator)

**Analog:** `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs`

**Validator pattern** (`BanPlayerRequestValidator.cs` lines 1-24):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Http.Validators;

/// <summary>
/// Validator for <see cref="MergePlayersRequest"/>. Enforces: both GUIDs are non-empty,
/// and source != target (merging a player into themselves is meaningless).
/// </summary>
public sealed class MergePlayersRequestValidator : AbstractValidator<MergePlayersRequest>
{
    public MergePlayersRequestValidator()
    {
        RuleFor(x => x.SourcePlayerId).NotEmpty().WithMessage("SourcePlayerId is required.");
        RuleFor(x => x.TargetPlayerId).NotEmpty().WithMessage("TargetPlayerId is required.");
        RuleFor(x => x).Must(r => r.SourcePlayerId != r.TargetPlayerId)
            .WithMessage("Source and target player must be different.");
    }
}
```

---

### `src/GameKit.Admin.UI/Http/RateLimiting/AdminRateLimitRegistrations.cs` (modify: add merge policy)

**Analog:** Itself — `AdminRateLimitRegistrations.cs` lines 1-49.

**Existing policy registration pattern** (lines 33-46):
```csharp
opts.AddPolicy(AdminLoginPolicy, httpContext =>
    RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
```

**ADD merge policy constant + registration** (5 req/min/IP — conservative for destructive operation per A4):
```csharp
/// <summary>Policy name for the account-merge endpoint — 5 per minute per IP (A4).</summary>
public const string AdminMergePolicy = "gamekit:admin:merge";

// Inside AddAdminRateLimits → services.Configure<RateLimiterOptions>(opts => { ... }):
opts.AddPolicy(AdminMergePolicy, httpContext =>
    RateLimitPartition.GetSlidingWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
```

---

### `src/GameKit.Auth/Builder/AuthBuilderExtensions.cs` (modify: register IAccountMergeService)

**Analog:** Itself — `AuthBuilderExtensions.cs` lines 80+ (service registration block).

**Scoped service registration pattern** (lines 80-90 — typical scoped service registration):
```csharp
// Scoped — touches DbContext (request-scoped):
builder.Services.AddScoped<IAccountMergeService, AccountMergeService>(); // NOTE: AddScoped (not TryAddScoped) to match AuthBuilderExtensions' existing registration style
```

---

### `src/GameKit.Auth/AssemblyInfo.cs` (modify: add InternalsVisibleTo)

**Analog:** Itself — `AssemblyInfo.cs` lines 1-41.

**Grant pattern** (existing lines 6-7):
```csharp
[assembly: InternalsVisibleTo("GameKit.Auth.Tests")]
[assembly: InternalsVisibleTo("GameKit.Auth.Integration.Tests")]
```

**ADD:**
```csharp
// Plan 10: AccountMerge integration tests compose Core + Auth + Rankings + Matchmaking.
[assembly: InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")]
```

---

### `src/GameKit.Rankings/AssemblyInfo.cs` and `src/GameKit.Matchmaking/AssemblyInfo.cs` (modify: add InternalsVisibleTo)

**Analog:** `src/GameKit.Auth/AssemblyInfo.cs` pattern above.

**ADD to each:**
```csharp
[assembly: InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")]
```

---

### `tests/GameKit.Auth.AccountMerge.Integration.Tests/` (new test project)

**Analog:** `tests/GameKit.Auth.Integration.Tests/` (full project structure)

**CollectionDefinitions.cs pattern** (`tests/GameKit.Auth.Integration.Tests/CollectionDefinitions.cs` lines 1-24):
```csharp
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Auth.AccountMerge.Integration.Tests;

[CollectionDefinition("AccountMerge")]
public sealed class AccountMergeCollection
    : ICollectionFixture<PostgresFixture>,
      ICollectionFixture<RedisFixture> { }

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
```

**TestHelpers.cs ApplyMigrations pattern** (`tests/GameKit.Auth.Integration.Tests/TestHelpers.cs` lines 91-132 — extend to apply Core + Auth + Rankings + Matchmaking migrations):
```csharp
// Apply Core migrations (same as Auth.Integration.Tests TestHelpers lines 93-117):
var coreServices = new ServiceCollection();
coreServices.AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; });
// ... (suppress PendingModelChangesWarning, apply Core migration) ...

// Apply Auth migrations (same as Auth.Integration.Tests TestHelpers lines 119-131):
var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
    .UseNpgsql(cs, npg => { npg.MigrationsAssembly(...); npg.MigrationsHistoryTable(...); })
    .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
    .Options;
await using var authCtx = new GameKitDbContext(authOpts);
await authCtx.Database.MigrateAsync();

// NEW: Apply Rankings migrations (pattern from SessionLifecycleTestHelpers.cs lines 72-84):
var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
    .UseNpgsql(cs, npg =>
    {
        npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
        npg.MigrationsHistoryTable(RankingsMigrationConstants.MigrationsHistoryTable,
            GameKitMigrationConstants.SchemaName);
    })
    .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
    .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
    .Options;
await using var rankingsCtx = new GameKitDbContext(rankingsOpts);
await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);

// NEW: Apply Matchmaking migrations (same pattern, use MatchmakingMigrationConstants).
```

**Integration test class structure** (`tests/GameKit.Auth.Integration.Tests/GuestUpgradeServiceTests.cs` lines 1-72):
```csharp
[Collection("AccountMerge")]    // or "Postgres" for lighter tests
[Trait("Category", "Integration")]
public sealed class AccountMergeServiceTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public AccountMergeServiceTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact]
    public async Task MergeAsync_SC1_CrashResume_Pending_ReRunsTransaction_Idempotently()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        // ... seed players, insert pending account_merges row, call MergeAsync, assert idempotent
    }
}
```

---

## Shared Patterns

### SERIALIZABLE Transaction + 40001 Retry
**Source:** `src/GameKit.Auth/Services/IdentityLinker.cs` lines 74-176
**Apply to:** `AccountMergeService.cs`

The manual loop (not Polly) is the Auth-package standard. Three key elements must ALL be present:
1. `BeginTransactionAsync(IsolationLevel.Serializable, ...)` — not `ReadCommitted`
2. On `40001`: detach all change-tracker entries before `continue` (prevents stale entity-state on retry)
3. On `23505`: handle per-table (player_credentials PK conflict, player_ranks UNIQUE conflict, party_members UNIQUE conflict)

```csharp
// TryFindPostgresException (copy verbatim from IdentityLinker.cs lines 187-195):
private static PostgresException? TryFindPostgresException(Exception? ex)
{
    for (var i = 0; i < 8 && ex is not null; i++)
    {
        if (ex is PostgresException pg) return pg;
        ex = ex.InnerException;
    }
    return null;
}
```

### Direct AdminAuditLog Write (No IAdminAuditWriter)
**Source:** `src/GameKit.Rankings/Services/EndSeasonService.cs` lines 193-217
**Apply to:** `AccountMergeService.cs`

Auth must NOT reference `IAdminAuditWriter` (lives in Admin.UI — circular dep). Use `_ctx.Set<AdminAuditLog>()` directly. `AdminAuditLog` is a Core entity, accessible to Auth with no new dependency. Duplicate the action literal as a private const with a sync-comment pointing to the Admin.UI `AdminAuditActions` class.

### Change-Tracker Detach on Retry
**Source:** `src/GameKit.Auth/Services/GuestUpgradeService.cs` lines 112-115 and `src/GameKit.Auth/Services/IdentityLinker.cs` lines 145-148
**Apply to:** `AccountMergeService.cs` — every `catch` block that will `continue` to retry

```csharp
foreach (var entry in _ctx.ChangeTracker.Entries())
    entry.State = EntityState.Detached;
```

### Superadmin Policy + Antiforgery Guard
**Source:** `src/GameKit.Admin.UI/Http/AdminEndpoints.cs` lines 97-99 and `src/GameKit.Admin.UI/Authorization/AdminPolicies.cs` lines 1-14
**Apply to:** merge endpoint registration in `AdminEndpoints.Map()`

```csharp
.RequireAuthorization(AdminPolicies.Superadmin)   // "gamekit.admin.superadmin"
.AddEndpointFilter<AntiforgeryValidationFilter>()
```

### FluentValidation Validator
**Source:** `src/GameKit.Admin.UI/Http/Validators/BanPlayerRequestValidator.cs`
**Apply to:** `MergePlayersRequestValidator.cs`

```csharp
public sealed class MergePlayersRequestValidator : AbstractValidator<MergePlayersRequest>
{
    public MergePlayersRequestValidator()
    {
        RuleFor(x => x.SourcePlayerId).NotEmpty();
        RuleFor(x => x.TargetPlayerId).NotEmpty();
        RuleFor(x => x).Must(r => r.SourcePlayerId != r.TargetPlayerId)
            .WithMessage("Source and target player must be different.");
    }
}
```

### EF Integer Enum Convention
**Source:** `src/GameKit.Rankings/Entities/PlayerRank.cs` + `src/GameKit.Core/Entities/` (all enums)
**Apply to:** `MergeStatus` enum in `AccountMerge.cs`

Project convention: enum properties backed by `integer` in Postgres. EF Core maps CLR `int`-backed enums directly. No `.HasConversion()` needed. Use `public enum MergeStatus { Pending = 0, Committed = 1, RedisCleaned = 2 }` (integer values, not string).

### Per-Package Migration Boundary
**Source:** `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` + `CLAUDE.md`
**Apply to:** All four new migrations

- Core owns `players` and `admin_audit_log` → Core migrations `20260606000000` and `20260606100000` use `GameKitMigrationConstants.AdvisoryLockKey = 1800940027L`
- Auth owns `account_merges` → Auth migration `20260606200000` uses `AuthMigrationConstants.AdvisoryLockKey = -298890956L`
- Auth's `AuthMigrationModelCustomizer` must include `AccountMergeConfiguration` in its `ApplyConfiguration` block
- All other package MigrationModelCustomizers (`AdminMigrationModelCustomizer`, Rankings, Matchmaking) already exclude `AdminAuditLog` and `Player` via their `ExcludeFromMigrations` lists — adding a new FK to `admin_audit_log` (owned by Core) is transparent to them

### Refresh Token Revocation
**Source:** `src/GameKit.Auth/Services/IRefreshTokenService.cs` line 48 + `src/GameKit.Auth/Services/RefreshTokenService.cs` lines 242-260
**Apply to:** Step 6 of merge transaction in `AccountMergeService`

```csharp
// Reason string for account_merge:
await _refresh.RevokeAllForPlayerAsync(sourcePlayerId, "account_merge", cancellationToken)
    .ConfigureAwait(false);
// Implementation uses ExecuteUpdateAsync (bulk update, no change-tracker involvement).
```

### InternalsVisibleTo Grant
**Source:** `src/GameKit.Auth/AssemblyInfo.cs` lines 6-36
**Apply to:** `GameKit.Auth`, `GameKit.Rankings`, `GameKit.Matchmaking` AssemblyInfo.cs files

```csharp
[assembly: InternalsVisibleTo("GameKit.Auth.AccountMerge.Integration.Tests")]
```

---

## No Analog Found

All files have close analogs in the codebase. No files require falling back to RESEARCH.md external patterns.

---

## Metadata

**Analog search scope:** `src/GameKit.Auth/`, `src/GameKit.Core/`, `src/GameKit.Rankings/`, `src/GameKit.Admin.UI/`, `src/GameKit.Matchmaking/`, `tests/GameKit.Auth.Integration.Tests/`, `tests/GameKit.Rankings.Integration.Tests/`, `tests/GameKit.TestFixtures/`
**Files scanned:** 28 source files read in full
**Pattern extraction date:** 2026-06-06

**Critical precedents honored:**
- SERIALIZABLE + manual 40001 retry + change-tracker detach: `IdentityLinker.cs` (exact pattern)
- Direct `_ctx.Set<AdminAuditLog>()` write with private action const: `EndSeasonService.cs` (exact pattern)
- Per-package migration boundary (Core owns players/admin_audit_log columns): `AddSessionParticipationFraction.cs` (exact precedent)
- Auth advisory lock key `-298890956L` for `account_merges` migration: `AuthMigrationConstants.cs`
- Core advisory lock key `1800940027L` for the two Core migrations: `GameKitMigrationConstants.cs`
- `AuthMigrationModelCustomizer` in `AuthDesignTimeDbContextFactory.cs` must be extended to include `AccountMergeConfiguration`
- `AdminMigrationModelCustomizer` already excludes `AdminAuditLog` — new `HasOne<Player>()` FK on it is invisible to Admin's migration diff (VERIFIED)
- Integer enum storage (not string): all existing EF entity enums
- `IRefreshTokenService.RevokeAllForPlayerAsync(playerId, "account_merge", ct)` — existing method, no modification needed
- SHA-256 token storage: refresh tokens already stored as SHA-256 hex; `RevokeAllForPlayerAsync` handles this internally — no raw token handling in merge service
