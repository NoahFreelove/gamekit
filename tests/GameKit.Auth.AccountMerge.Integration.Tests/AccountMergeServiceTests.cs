// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Auth.Data.Configurations;
using GameKit.Auth.Entities;
using AccountMergeEntity = GameKit.Auth.Entities.AccountMerge;
using GameKit.Auth.Services;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Matchmaking.Data;
using GameKit.Matchmaking.Data.Configurations;
using GameKit.Rankings.Data;
using GameKit.Rankings.Data.Configurations;
using GameKit.Rankings.Entities;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Auth.AccountMerge.Integration.Tests;

/// <summary>
/// Service-level integration proofs for AUTH-23/24/25/26:
/// SC#1 crash-resume, SC#2 full FK re-pointing, SC#3 rank conflict resolution, SC#4 audit + actor_id FK.
/// Uses Testcontainers Postgres + Redis — no skip-if-no-docker.
/// </summary>
[Collection("AccountMerge")]
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

    // ─── SC#1 CRASH-RESUME ──────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "SC#1: Pending row present → MergeAsync re-runs transaction idempotently, produces exactly one audit row")]
    public async Task SC1_CrashResume_PendingRow_Reruns_Transaction_Idempotently()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        // Seed two players + a Pending account_merges row to simulate a crash mid-merge.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc1-source-pending", "sc1-target-pending");

            // Insert the Pending row directly — simulates an in-progress merge that crashed.
            ctx.Set<AccountMergeEntity>().Add(new AccountMergeEntity
            {
                Id = Guid.CreateVersion7(),
                SourcePlayerId = sourceId,
                TargetPlayerId = targetId,
                Status = MergeStatus.Pending,
                ActorId = actorId,
                RequestedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // Call MergeAsync — must resume the pending merge and complete it.
        MergeResult result;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            result = await svc.MergeAsync(sourceId, targetId, actorId);
        }

        // SC#1: merged (not AlreadyMerged — the Pending path re-runs the tx and produces Merged).
        Assert.Equal(MergeResultKind.Merged, result.Kind);
        Assert.Equal(targetId, result.TargetPlayerId);

        // SC#1: exactly ONE audit row written (idempotent — no double-write).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var auditCount = await ctx.AdminAuditLog
                .CountAsync(a => a.Action == "auth.account_merge" && a.TargetId == targetId);
            Assert.Equal(1, auditCount);
        }
    }

    [Fact(DisplayName = "SC#1: Committed row → MergeAsync skips DB transaction, runs Redis cleanup only, returns AlreadyMerged")]
    public async Task SC1_CommittedRow_SkipsDbTransaction_RunsRedisCleanup_ReturnsAlreadyMerged()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        Guid mergeRowId = Guid.CreateVersion7();

        // Seed players + a Committed account_merges row (simulate crash after DB commit but before Redis cleanup).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc1-source-committed", "sc1-target-committed");

            ctx.Set<AccountMergeEntity>().Add(new AccountMergeEntity
            {
                Id = mergeRowId,
                SourcePlayerId = sourceId,
                TargetPlayerId = targetId,
                Status = MergeStatus.Committed,
                ActorId = actorId,
                RequestedAt = DateTimeOffset.UtcNow,
                CommittedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // Call MergeAsync — must skip DB work and proceed to Redis cleanup only.
        MergeResult result;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            result = await svc.MergeAsync(sourceId, targetId, actorId);
        }

        Assert.Equal(MergeResultKind.AlreadyMerged, result.Kind);
        Assert.Equal(targetId, result.TargetPlayerId);

        // The account_merges row should be advanced to RedisCleaned.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var mergeRow = await ctx.Set<AccountMergeEntity>().AsNoTracking()
                .FirstAsync(am => am.Id == mergeRowId);
            Assert.Equal(MergeStatus.RedisCleaned, mergeRow.Status);
        }
    }

    [Fact(DisplayName = "SC#1: RedisCleaned (fully complete) merge → MergeAsync returns AlreadyMerged with no DB re-run")]
    public async Task SC1_RedisCleanedMerge_ReturnsAlreadyMerged_NoWork()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        // Run a full merge first.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc1-source-redis", "sc1-target-redis");
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        // Re-request the same merge — must return AlreadyMerged without touching the DB.
        MergeResult secondResult;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            secondResult = await svc.MergeAsync(sourceId, targetId, actorId);
        }

        Assert.Equal(MergeResultKind.AlreadyMerged, secondResult.Kind);

        // SC#1: exactly ONE audit row total — the second request must not double-write.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var auditCount = await ctx.AdminAuditLog
                .CountAsync(a => a.Action == "auth.account_merge" && a.TargetId == targetId);
            Assert.Equal(1, auditCount);
        }
    }

    // ─── SC#2 FULL FK RE-POINTING ──────────────────────────────────────────────────────────────

    [Fact(DisplayName = "SC#2: After merge, source player_identities count = 0; target gained them (incl. provider identities)")]
    public async Task SC2_PlayerIdentities_Repointed_To_Target()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc2-id-source", "sc2-id-target");

            // Seed Phase-7-style provider identity rows on source (google/apple/epic).
            ctx.Set<PlayerIdentity>().AddRange(
                MakeIdentity(sourceId, "google", "google-uid-1"),
                MakeIdentity(sourceId, "apple", "apple-uid-1"),
                MakeIdentity(sourceId, "epic", "epic-uid-1")
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            var sourceIdentityCount = await ctx.Set<PlayerIdentity>()
                .CountAsync(pi => pi.PlayerId == sourceId);
            Assert.Equal(0, sourceIdentityCount); // source has none

            var targetIdentityCount = await ctx.Set<PlayerIdentity>()
                .CountAsync(pi => pi.PlayerId == targetId);
            Assert.Equal(3, targetIdentityCount); // target gained all 3
        }
    }

    [Fact(DisplayName = "SC#2: After merge, ALL source session_participants (active AND completed) reference target")]
    public async Task SC2_SessionParticipants_AllRepointed_ActiveAndCompleted()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc2-sess-source", "sc2-sess-target");

            // Seed one active session + one completed session for source.
            var session1 = new GameSession { Id = Guid.CreateVersion7(), State = GameSessionState.Active, CreatedAt = DateTimeOffset.UtcNow };
            var session2 = new GameSession { Id = Guid.CreateVersion7(), State = GameSessionState.Completed, CreatedAt = DateTimeOffset.UtcNow, CompletedAt = DateTimeOffset.UtcNow };
            ctx.Set<GameSession>().AddRange(session1, session2);

            ctx.Set<SessionParticipant>().AddRange(
                new SessionParticipant { Id = Guid.CreateVersion7(), SessionId = session1.Id, PlayerId = sourceId, Team = 1 },
                new SessionParticipant { Id = Guid.CreateVersion7(), SessionId = session2.Id, PlayerId = sourceId, Team = 1 }
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // No source session participants should remain.
            var sourceCount = await ctx.Set<SessionParticipant>()
                .CountAsync(sp => sp.PlayerId == (Guid?)sourceId);
            Assert.Equal(0, sourceCount);

            // Target should now have 2 participants (both sessions).
            var targetCount = await ctx.Set<SessionParticipant>()
                .CountAsync(sp => sp.PlayerId == (Guid?)targetId);
            Assert.Equal(2, targetCount);
        }
    }

    [Fact(DisplayName = "SC#2: After merge, source refresh tokens all revoked; source player tombstoned with merged_into_player_id + deleted_at")]
    public async Task SC2_RefreshTokensRevoked_And_SourceTombstoned()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc2-tok-source", "sc2-tok-target");

            // Seed two active refresh tokens for source.
            var now = DateTimeOffset.UtcNow;
            ctx.Set<RefreshToken>().AddRange(
                new RefreshToken
                {
                    Id = Guid.CreateVersion7(),
                    PlayerId = sourceId,
                    FamilyId = Guid.CreateVersion7(),
                    TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())),
                    Provider = "password",
                    IssuedAt = now,
                    ExpiresAt = now.AddDays(30),
                },
                new RefreshToken
                {
                    Id = Guid.CreateVersion7(),
                    PlayerId = sourceId,
                    FamilyId = Guid.CreateVersion7(),
                    TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())),
                    Provider = "password",
                    IssuedAt = now,
                    ExpiresAt = now.AddDays(30),
                }
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // All source refresh tokens must be revoked.
            var unrevokedCount = await ctx.Set<RefreshToken>()
                .CountAsync(rt => rt.PlayerId == sourceId && rt.RevokedAt == null);
            Assert.Equal(0, unrevokedCount);

            // The revoked tokens should all have RevokedAt set.
            var revokedCount = await ctx.Set<RefreshToken>()
                .CountAsync(rt => rt.PlayerId == sourceId && rt.RevokedAt != null);
            Assert.Equal(2, revokedCount);

            // Source player tombstoned.
            var source = await ctx.Set<Player>().AsNoTracking()
                .FirstAsync(p => p.Id == sourceId);
            Assert.Equal(targetId, source.MergedIntoPlayerId);
            Assert.NotNull(source.DeletedAt);
        }
    }

    // ─── SC#3 RANK CONFLICT RESOLUTION ─────────────────────────────────────────────────────────

    [Fact(DisplayName = "SC#3: Higher source rating wins — target ends with source Rating after merge")]
    public async Task SC3_HigherSourceRating_TargetEndsWithSourceRating()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        Guid ladderId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc3-high-src", "sc3-high-tgt");
            ladderId = await SeedLadderAsync(ctx, "ladder-sc3-highsrc");

            // Source has higher rating (2000 > 1500).
            ctx.Set<PlayerRank>().AddRange(
                MakeRank(sourceId, ladderId, rating: 2000, wins: 10, losses: 5, draws: 2),
                MakeRank(targetId, ladderId, rating: 1500, wins: 3, losses: 8, draws: 1)
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Target should have source's rating (higher wins the comparison).
            var targetRank = await ctx.Set<PlayerRank>().AsNoTracking()
                .FirstAsync(r => r.PlayerId == targetId && r.LadderId == ladderId);
            Assert.Equal(2000, targetRank.Rating, precision: 0);

            // W/L/D summed (10+3=13, 5+8=13, 2+1=3).
            Assert.Equal(13, targetRank.Wins);
            Assert.Equal(13, targetRank.Losses);
            Assert.Equal(3, targetRank.Draws);

            // No source rank row should remain.
            var sourceRankCount = await ctx.Set<PlayerRank>()
                .CountAsync(r => r.PlayerId == sourceId);
            Assert.Equal(0, sourceRankCount);
        }
    }

    [Fact(DisplayName = "SC#3: Higher target rating wins — target keeps own Rating; gains summed W/L/D")]
    public async Task SC3_HigherTargetRating_TargetKeepsRating_GainsSummedWLD()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        Guid ladderId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc3-high-tgt-src", "sc3-high-tgt-tgt");
            ladderId = await SeedLadderAsync(ctx, "ladder-sc3-hightgt");

            // Target has higher rating (2200 > 1800).
            ctx.Set<PlayerRank>().AddRange(
                MakeRank(sourceId, ladderId, rating: 1800, wins: 5, losses: 3, draws: 1),
                MakeRank(targetId, ladderId, rating: 2200, wins: 12, losses: 2, draws: 0)
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            var targetRank = await ctx.Set<PlayerRank>().AsNoTracking()
                .FirstAsync(r => r.PlayerId == targetId && r.LadderId == ladderId);

            // Target keeps its higher rating.
            Assert.Equal(2200, targetRank.Rating, precision: 0);

            // W/L/D summed: 5+12=17, 3+2=5, 1+0=1.
            Assert.Equal(17, targetRank.Wins);
            Assert.Equal(5, targetRank.Losses);
            Assert.Equal(1, targetRank.Draws);

            // No source rank row should remain.
            var sourceRankCount = await ctx.Set<PlayerRank>()
                .CountAsync(r => r.PlayerId == sourceId);
            Assert.Equal(0, sourceRankCount);
        }
    }

    [Fact(DisplayName = "SC#3: W/L/D are summed across both players when ratings are merged")]
    public async Task SC3_WinLossDraws_AreSummed()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        Guid ladderId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc3-wld-src", "sc3-wld-tgt");
            ladderId = await SeedLadderAsync(ctx, "ladder-sc3-wld");

            // Equal ratings — source wins tie-break (>= means target wins, so equal means target wins).
            ctx.Set<PlayerRank>().AddRange(
                MakeRank(sourceId, ladderId, rating: 1600, wins: 7, losses: 4, draws: 3),
                MakeRank(targetId, ladderId, rating: 1800, wins: 2, losses: 9, draws: 0)
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            var targetRank = await ctx.Set<PlayerRank>().AsNoTracking()
                .FirstAsync(r => r.PlayerId == targetId && r.LadderId == ladderId);

            // W/L/D must be summed.
            Assert.Equal(7 + 2, targetRank.Wins);
            Assert.Equal(4 + 9, targetRank.Losses);
            Assert.Equal(3 + 0, targetRank.Draws);
        }
    }

    [Fact(DisplayName = "SC#3: Source token revoked after merge (no valid tokens remain for source)")]
    public async Task SC3_SourceTokensRevoked_NoValidTokensRemain()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc3-tok-src", "sc3-tok-tgt");

            var now = DateTimeOffset.UtcNow;
            ctx.Set<RefreshToken>().Add(new RefreshToken
            {
                Id = Guid.CreateVersion7(),
                PlayerId = sourceId,
                FamilyId = Guid.CreateVersion7(),
                TokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Guid.NewGuid().ToByteArray())),
                Provider = "guest",
                IssuedAt = now,
                ExpiresAt = now.AddDays(30),
            });
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var unrevoked = await ctx.Set<RefreshToken>()
                .CountAsync(rt => rt.PlayerId == sourceId && rt.RevokedAt == null);
            Assert.Equal(0, unrevoked);
        }
    }

    [Fact(DisplayName = "SC#3: Party conflict — source + target in same party throws PlayersInSameParty, no mutation")]
    public async Task SC3_PartyConflict_ThrowsPlayersInSameParty_NoMutation()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc3-party-src", "sc3-party-tgt");

            // Seed both players in the same party via raw Npgsql to avoid Matchmaking entity dependency.
            await SeedSamePartyAsync(ctx, sourceId, targetId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var ex = await Assert.ThrowsAsync<MergeConflictException>(
                () => svc.MergeAsync(sourceId, targetId, actorId));
            Assert.Equal(MergeConflictReason.PlayersInSameParty, ex.Reason);
        }

        // No audit row written for THIS specific merge (conflict aborted before any mutation).
        // Filter by TargetId to avoid counting rows from other tests sharing the same DB container.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var auditCount = await ctx.AdminAuditLog
                .CountAsync(a => a.Action == "auth.account_merge" && a.TargetId == targetId);
            Assert.Equal(0, auditCount);
        }
    }

    // ─── SC#4 AUDIT + ACTOR_ID FK ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "SC#4: Exactly one admin_audit_log row action='auth.account_merge' with non-null Before/After JSON")]
    public async Task SC4_AuditRow_WrittenExactlyOnce_WithBeforeAfterJson()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc4-audit-src", "sc4-audit-tgt");
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            var auditRows = await ctx.AdminAuditLog
                .Where(a => a.Action == "auth.account_merge" && a.TargetId == targetId)
                .ToListAsync();

            // Exactly one audit row.
            Assert.Single(auditRows);

            var audit = auditRows[0];
            Assert.Equal(actorId, audit.ActorId);
            Assert.NotNull(audit.Before);
            Assert.NotNull(audit.After);

            // Before JSON should contain source player context.
            var beforeJson = audit.Before!.RootElement.ToString();
            Assert.Contains("source_player_id", beforeJson);

            // After JSON should contain target player context.
            var afterJson = audit.After!.RootElement.ToString();
            Assert.Contains("target_player_id", afterJson);
            Assert.Contains("tokens_revoked", afterJson);
        }
    }

    [Fact(DisplayName = "SC#4: actor_id — deleting the actor player preserves audit row (no FK cascade; actor_id retains UUID)")]
    public async Task SC4_ActorId_FK_OnDeleteSetNull_AuditRowPreserved()
    {
        // NOTE (Plan 10-04 deviation): The original test spec expected a FK ON DELETE SET NULL on
        // admin_audit_log.actor_id → players.id so that GDPR-deleting the actor would auto-null the
        // audit row. That FK was reverted because actor_id stores BOTH player IDs AND admin user IDs —
        // a strict FK to players rejects every admin-initiated audit entry (23503). Without the FK,
        // the audit row is preserved but actor_id retains the deleted player's UUID rather than being
        // nulled. The audit trail is still intact; the actor identity becomes opaque after hard-delete.
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;

        // Seed a separate actor player (not source or target — just the admin performing the merge).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "sc4-fk-src", "sc4-fk-tgt");
        }

        Guid auditRowId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);

            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var audit = await ctx.AdminAuditLog
                .FirstAsync(a => a.Action == "auth.account_merge" && a.TargetId == targetId);
            auditRowId = audit.Id;
        }

        // Hard-delete the source player (tombstoned at this point, actorId = source player row).
        // Without a FK, this does not affect the audit row.
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await ctx.Database.ExecuteSqlAsync(
                $"DELETE FROM gamekit.players WHERE \"Id\" = {actorId}");
        }

        // The audit row must still exist and actor_id must still be the original actor UUID (no cascade).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            var audit = await ctx.AdminAuditLog.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == auditRowId);
            Assert.NotNull(audit);                                   // row preserved
            Assert.Equal(actorId, audit!.ActorId);                   // actor_id retains UUID (no FK cascade)
        }
    }

    // ─── GUARD TESTS ───────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Guard: self-merge → MergeConflictException(SelfMerge)")]
    public async Task Guard_SelfMerge_Throws()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid playerId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            playerId = await SeedPlayerAsync(ctx, "self-merge-player");
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var ex = await Assert.ThrowsAsync<MergeConflictException>(
                () => svc.MergeAsync(playerId, playerId, Guid.NewGuid()));
            Assert.Equal(MergeConflictReason.SelfMerge, ex.Reason);
        }
    }

    [Fact(DisplayName = "Guard: target banned → MergeConflictException(TargetBanned)")]
    public async Task Guard_TargetBanned_Throws()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            sourceId = await SeedPlayerAsync(ctx, "banned-merge-src");
            targetId = await SeedPlayerAsync(ctx, "banned-merge-tgt", banned: true);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var ex = await Assert.ThrowsAsync<MergeConflictException>(
                () => svc.MergeAsync(sourceId, targetId, Guid.NewGuid()));
            Assert.Equal(MergeConflictReason.TargetBanned, ex.Reason);
        }
    }

    [Fact(DisplayName = "Guard: already-merged source → MergeConflictException(SourceAlreadyMerged)")]
    public async Task Guard_SourceAlreadyMerged_Throws()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "already-merged-src", "already-merged-tgt");
        }

        // First merge completes successfully.
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        // Attempting to merge the (now-tombstoned) source into a DIFFERENT player throws SourceAlreadyMerged.
        Guid thirdPlayerId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            thirdPlayerId = await SeedPlayerAsync(ctx, "third-player-am");
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var ex = await Assert.ThrowsAsync<MergeConflictException>(
                () => svc.MergeAsync(sourceId, thirdPlayerId, actorId));
            Assert.Equal(MergeConflictReason.SourceAlreadyMerged, ex.Reason);
        }
    }

    // ─── CR-02: REDIS PRESENCE KEY CLEANUP ─────────────────────────────────────────────────────

    [Fact(DisplayName = "CR-02: After merge, presence:{sourceId} Redis key is deleted")]
    public async Task CR02_RedisPresenceKey_DeletedAfterMerge()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "cr02-redis-src", "cr02-redis-tgt");
        }

        // Seed the presence key in Redis using the correct format that PresenceRedisKeys.Player uses.
        var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
        var db = multiplexer.GetDatabase();
        var presenceKey = $"presence:{sourceId}";
        await db.StringSetAsync(presenceKey, "online", TimeSpan.FromMinutes(5));
        Assert.True(await db.KeyExistsAsync(presenceKey), "Precondition: presence key must exist before merge.");

        // Execute the merge.
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var result = await svc.MergeAsync(sourceId, targetId, actorId);
            Assert.Equal(MergeResultKind.Merged, result.Kind);
        }

        // The presence key for the source player must have been deleted.
        Assert.False(await db.KeyExistsAsync(presenceKey),
            "presence:{sourceId} Redis key must be deleted after merge (CR-02).");
    }

    [Fact(DisplayName = "CR-02: Merge succeeds even when no presence key exists in Redis (graceful no-op)")]
    public async Task CR02_Merge_Succeeds_WhenNoPresenceKeyInRedis()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "cr02-nok-src", "cr02-nok-tgt");
        }

        // Ensure the presence key does NOT exist before merge.
        var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
        var db = multiplexer.GetDatabase();
        var presenceKey = $"presence:{sourceId}";
        await db.KeyDeleteAsync(presenceKey);

        // Merge must still succeed (Redis cleanup is best-effort).
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var result = await svc.MergeAsync(sourceId, targetId, actorId);
            Assert.Equal(MergeResultKind.Merged, result.Kind);
        }
    }

    // ─── CR-03: SEASON_RANK_ARCHIVE DEDUPLICATION ──────────────────────────────────────────────

    [Fact(DisplayName = "CR-03: Both players have archive row for same (season, ladder) — after merge target has exactly ONE row (higher-rated source wins)")]
    public async Task CR03_ArchiveConflict_HigherSourceRating_TargetHasOneRow()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        Guid ladderId, seasonId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "cr03-high-src", "cr03-high-tgt");
            ladderId = await SeedLadderAsync(ctx, "ladder-cr03-high");
            seasonId = await SeedSeasonAsync(ctx, ladderId);

            // Both players have an archive row for the same (season, ladder).
            // Source has higher rating (2000 > 1200).
            ctx.Set<SeasonRankArchive>().AddRange(
                MakeArchive(sourceId, ladderId, seasonId, rating: 2000),
                MakeArchive(targetId, ladderId, seasonId, rating: 1200)
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Target must have exactly ONE archive row for this (season, ladder) pair.
            var archiveRows = await ctx.Set<SeasonRankArchive>()
                .Where(a => a.PlayerId == (Guid?)targetId && a.LadderId == ladderId && a.SeasonId == seasonId)
                .ToListAsync();
            Assert.Single(archiveRows); // CR-03: no duplicate

            // The surviving row must have the source's higher rating (2000 wins).
            Assert.Equal(2000, archiveRows[0].Rating, precision: 0);

            // No archive rows for source.
            var sourceArchive = await ctx.Set<SeasonRankArchive>()
                .CountAsync(a => a.PlayerId == (Guid?)sourceId);
            Assert.Equal(0, sourceArchive);
        }
    }

    [Fact(DisplayName = "CR-03: Both players have archive row for same (season, ladder) — after merge target has exactly ONE row (higher-rated target wins)")]
    public async Task CR03_ArchiveConflict_HigherTargetRating_TargetHasOneRow()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        Guid ladderId, seasonId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "cr03-hightgt-src", "cr03-hightgt-tgt");
            ladderId = await SeedLadderAsync(ctx, "ladder-cr03-hightgt");
            seasonId = await SeedSeasonAsync(ctx, ladderId);

            // Target has higher rating (1800 > 900).
            ctx.Set<SeasonRankArchive>().AddRange(
                MakeArchive(sourceId, ladderId, seasonId, rating: 900),
                MakeArchive(targetId, ladderId, seasonId, rating: 1800)
            );
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Target must have exactly ONE archive row.
            var archiveRows = await ctx.Set<SeasonRankArchive>()
                .Where(a => a.PlayerId == (Guid?)targetId && a.LadderId == ladderId && a.SeasonId == seasonId)
                .ToListAsync();
            Assert.Single(archiveRows); // CR-03: no duplicate

            // The surviving row retains the target's higher rating (1800 wins).
            Assert.Equal(1800, archiveRows[0].Rating, precision: 0);

            // No archive rows remain for source.
            var sourceArchive = await ctx.Set<SeasonRankArchive>()
                .CountAsync(a => a.PlayerId == (Guid?)sourceId);
            Assert.Equal(0, sourceArchive);
        }
    }

    [Fact(DisplayName = "CR-03: Source-only archive row (no target conflict) is re-pointed to target")]
    public async Task CR03_SourceOnlyArchiveRow_Repointed_ToTarget()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        Guid ladderId, seasonId;

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "cr03-srconly-src", "cr03-srconly-tgt");
            ladderId = await SeedLadderAsync(ctx, "ladder-cr03-srconly");
            seasonId = await SeedSeasonAsync(ctx, ladderId);

            // Only source has an archive row — no conflict.
            ctx.Set<SeasonRankArchive>().Add(MakeArchive(sourceId, ladderId, seasonId, rating: 1500));
            await ctx.SaveChangesAsync();
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            await svc.MergeAsync(sourceId, targetId, actorId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // The archive row must now belong to target (re-pointed).
            var targetArchive = await ctx.Set<SeasonRankArchive>()
                .Where(a => a.PlayerId == (Guid?)targetId && a.LadderId == ladderId)
                .ToListAsync();
            Assert.Single(targetArchive);
            Assert.Equal(1500, targetArchive[0].Rating, precision: 0);

            // No archive rows remain for source.
            var sourceArchive = await ctx.Set<SeasonRankArchive>()
                .CountAsync(a => a.PlayerId == (Guid?)sourceId);
            Assert.Equal(0, sourceArchive);
        }
    }

    // ─── WR-02: TOCTOU SAME-TARGET IDEMPOTENT RE-ENTRY ─────────────────────────────────────────

    [Fact(DisplayName = "WR-02: MergeAsync with same-target after source already tombstoned returns AlreadyMerged (not 409 SourceAlreadyMerged)")]
    public async Task WR02_SameTarget_SourceAlreadyTombstoned_ReturnsAlreadyMerged()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "wr02-src", "wr02-tgt");
        }

        // First merge completes successfully.
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var first = await svc.MergeAsync(sourceId, targetId, actorId);
            Assert.Equal(MergeResultKind.Merged, first.Kind);
        }

        // Simulate the TOCTOU scenario: source is tombstoned (MergedIntoPlayerId set) AND the
        // account_merges row is at RedisCleaned. The outer crash-resume read returns the
        // RedisCleaned row (same target → short-circuit to AlreadyMerged before the tx body).
        // To test the inner-tx path (MergeTransactionBodyAsync returning Guid.Empty), we manually
        // reset the account_merges status to Pending and set CommittedAt = null so the resume
        // ladder falls through to the retry loop (simulating the TOCTOU case where the outer read
        // saw Pending but the merge completed concurrently before the tx body ran).
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            await ctx.Database.ExecuteSqlAsync(
                $"""
                UPDATE gamekit.account_merges
                SET "Status" = 0, "CommittedAt" = NULL, "RedisCleanedAt" = NULL
                WHERE "SourcePlayerId" = {sourceId}
                """);
        }

        // Re-request the merge — source is tombstoned but account_merges shows Pending.
        // The SERIALIZABLE tx body will detect source.MergedIntoPlayerId == targetPlayerId
        // and return Guid.Empty → AlreadyMerged (not SourceAlreadyMerged exception).
        MergeResult secondResult;
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            secondResult = await svc.MergeAsync(sourceId, targetId, actorId);
        }

        Assert.Equal(MergeResultKind.AlreadyMerged, secondResult.Kind);
        Assert.Equal(targetId, secondResult.TargetPlayerId);
    }

    // ─── HELPERS ────────────────────────────────────────────────────────────────────────────────

    private static AsyncServiceScope CreateAsyncScopeFromSp(ServiceProvider sp) => sp.CreateAsyncScope();

    private static async Task<(Guid source, Guid target, Guid actor)> SeedTwoPlayersAsync(
        GameKitDbContext ctx,
        string sourceName,
        string targetName)
    {
        var sourceId = Guid.CreateVersion7();
        var targetId = Guid.CreateVersion7();
        var actorId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        // Actor is a separate player (simulates the admin user who initiated the merge).
        ctx.Set<Player>().AddRange(
            new Player { Id = sourceId, DisplayName = sourceName, CreatedAt = now },
            new Player { Id = targetId, DisplayName = targetName, CreatedAt = now },
            new Player { Id = actorId, DisplayName = $"actor-{sourceName}", CreatedAt = now }
        );
        await ctx.SaveChangesAsync();

        return (sourceId, targetId, actorId);
    }

    private static async Task<Guid> SeedPlayerAsync(
        GameKitDbContext ctx,
        string displayName,
        bool banned = false)
    {
        var playerId = Guid.CreateVersion7();
        ctx.Set<Player>().Add(new Player
        {
            Id = playerId,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.UtcNow,
            IsBanned = banned,
        });
        await ctx.SaveChangesAsync();
        return playerId;
    }

    private static async Task<Guid> SeedLadderAsync(GameKitDbContext ctx, string name)
    {
        var ladderId = Guid.CreateVersion7();
        ctx.Set<Ladder>().Add(new Ladder
        {
            Id = ladderId,
            Name = name,
            Algorithm = "glicko2",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();
        return ladderId;
    }

    private static PlayerRank MakeRank(Guid playerId, Guid ladderId, double rating, int wins, int losses, int draws) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            PlayerId = playerId,
            LadderId = ladderId,
            Rating = rating,
            RatingDeviation = 200,
            Volatility = 0.06,
            Wins = wins,
            Losses = losses,
            Draws = draws,
            IsInPlacement = false,
            PlacementMatchesRemaining = 0,
        };

    private static PlayerIdentity MakeIdentity(Guid playerId, string provider, string externalId) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            PlayerId = playerId,
            Provider = provider,
            ExternalId = externalId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task<Guid> SeedSeasonAsync(GameKitDbContext ctx, Guid ladderId)
    {
        var seasonId = Guid.CreateVersion7();
        ctx.Set<LadderSeason>().Add(new LadderSeason
        {
            Id = seasonId,
            LadderId = ladderId,
            SeasonNumber = 1,
            StartedAt = DateTimeOffset.UtcNow.AddDays(-7),
            EndedAt = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync();
        return seasonId;
    }

    private static SeasonRankArchive MakeArchive(Guid playerId, Guid ladderId, Guid seasonId, double rating) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            PlayerId = playerId,
            LadderId = ladderId,
            SeasonId = seasonId,
            Rating = rating,
            RatingDeviation = 200,
            Volatility = 0.06,
            Wins = 5,
            Losses = 3,
            Draws = 1,
            ArchivedAt = DateTimeOffset.UtcNow,
        };

    // Seed source + target into the same party using raw Npgsql (no Matchmaking entity dep).
    private static async Task SeedSamePartyAsync(GameKitDbContext ctx, Guid sourceId, Guid targetId)
    {
        var partyId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;

        await ctx.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO gamekit.parties ("Id", "OwnerPlayerId", "PartyCode", "State", "CreatedAt")
            VALUES ({partyId}, {sourceId}, 'PRTY1', 0, {now})
            """);

        await ctx.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO gamekit.party_members ("Id", "PartyId", "PlayerId", "JoinedAt")
            VALUES ({Guid.CreateVersion7()}, {partyId}, {sourceId}, {now}),
                   ({Guid.CreateVersion7()}, {partyId}, {targetId}, {now})
            """);
    }

    // ─── W-2: LOBBY_MEMBERS RE-POINT ───────────────────────────────────────────────────────────
    // lobby_members does not exist in this test project's schema — the AccountMerge test project
    // does not reference GameKit.Lobby and TestHelpers.ApplyMigrations does not apply the Lobby
    // migration. The tables are created via raw DDL (IF NOT EXISTS) in EnsureLobbyTablesAsync.

    /// <summary>
    /// Creates <c>gamekit.lobbies</c> and <c>gamekit.lobby_members</c> tables via raw DDL if they
    /// do not already exist. Mirrors the production schema from the Lobby migration. Safe to call
    /// multiple times across tests sharing the same Testcontainers PostgresFixture database.
    /// </summary>
    private static async Task EnsureLobbyTablesAsync(GameKitDbContext ctx)
    {
        await ctx.Database.ExecuteSqlAsync(
            $"""
            CREATE TABLE IF NOT EXISTS gamekit.lobbies (
                "Id"         uuid         PRIMARY KEY,
                "OwnerId"    uuid         NOT NULL,
                "LadderId"   uuid         NULL,
                "State"      int          NOT NULL DEFAULT 0,
                "MaxMembers" int          NOT NULL DEFAULT 8,
                "CreatedAt"  timestamptz  NOT NULL,
                "UpdatedAt"  timestamptz  NOT NULL
            )
            """);

        await ctx.Database.ExecuteSqlAsync(
            $"""
            CREATE TABLE IF NOT EXISTS gamekit.lobby_members (
                "Id"       uuid         PRIMARY KEY,
                "LobbyId"  uuid         NOT NULL REFERENCES gamekit.lobbies("Id") ON DELETE CASCADE,
                "PlayerId" uuid         NOT NULL REFERENCES gamekit.players("Id") ON DELETE CASCADE,
                "Ready"    boolean      NOT NULL DEFAULT false,
                "JoinedAt" timestamptz  NOT NULL
            )
            """);

        await ctx.Database.ExecuteSqlAsync(
            $"""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_lobby_members_LobbyId_PlayerId"
                ON gamekit.lobby_members ("LobbyId", "PlayerId")
            """);
    }

    /// <summary>
    /// Seeds a lobby (if needed) and a <c>lobby_members</c> row linking <paramref name="playerId"/>
    /// to <paramref name="lobbyId"/>. Creates the lobby row owned by <paramref name="ownerId"/>
    /// using INSERT ... ON CONFLICT DO NOTHING so the same lobby id can be seeded for multiple
    /// members without a duplicate-key error.
    /// </summary>
    private static async Task SeedLobbyMemberAsync(
        GameKitDbContext ctx,
        Guid lobbyId,
        Guid playerId,
        Guid ownerId)
    {
        var now = DateTimeOffset.UtcNow;

        // Upsert the lobby row (idempotent across multiple callers for the same lobbyId).
        await ctx.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO gamekit.lobbies ("Id", "OwnerId", "LadderId", "State", "MaxMembers", "CreatedAt", "UpdatedAt")
            VALUES ({lobbyId}, {ownerId}, NULL, 1, 8, {now}, {now})
            ON CONFLICT ("Id") DO NOTHING
            """);

        // Insert the member row.
        await ctx.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO gamekit.lobby_members ("Id", "LobbyId", "PlayerId", "Ready", "JoinedAt")
            VALUES ({Guid.CreateVersion7()}, {lobbyId}, {playerId}, false, {now})
            """);
    }

    [Fact(DisplayName = "W-2: simple lobby_members re-point — source in lobby A only → target inherits membership")]
    public async Task W2_LobbyMembersRepoint_SourceOnly_TargetInherits()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        var lobbyId = Guid.CreateVersion7();

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "w2-src-only-source", "w2-src-only-target");
            await EnsureLobbyTablesAsync(ctx);
            // Only source is in lobby A.
            await SeedLobbyMemberAsync(ctx, lobbyId, sourceId, sourceId);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var result = await svc.MergeAsync(sourceId, targetId, actorId);
            Assert.Equal(MergeResultKind.Merged, result.Kind);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Target must now be the sole member of lobby A.
            var targetCount = await ctx.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM gamekit.lobby_members
                    WHERE "LobbyId" = {lobbyId} AND "PlayerId" = {targetId}
                    """)
                .FirstOrDefaultAsync();
            Assert.Equal(1, targetCount);

            // Source must have no lobby_members rows.
            var sourceCount = await ctx.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM gamekit.lobby_members
                    WHERE "PlayerId" = {sourceId}
                    """)
                .FirstOrDefaultAsync();
            Assert.Equal(0, sourceCount);
        }
    }

    [Fact(DisplayName = "W-2: same-lobby dedup — source + target both in lobby B → source row deleted, target's single row remains, no UNIQUE violation")]
    public async Task W2_LobbyMembersDedup_SameLobby_NoUniqueViolation()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var sp = BuildProvider(_pg.OwnerConnectionString, _redis.ConnectionString);

        Guid sourceId, targetId, actorId;
        var lobbyId = Guid.CreateVersion7();

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            (sourceId, targetId, actorId) = await SeedTwoPlayersAsync(ctx, "w2-dedup-source", "w2-dedup-target");
            await EnsureLobbyTablesAsync(ctx);
            // Both source AND target are in lobby B — this is the UNIQUE-violation scenario.
            await SeedLobbyMemberAsync(ctx, lobbyId, sourceId, sourceId);
            await SeedLobbyMemberAsync(ctx, lobbyId, targetId, sourceId);
        }

        // Merge must succeed with no 23505 UNIQUE violation.
        await using (var scope = sp.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IAccountMergeService>();
            var result = await svc.MergeAsync(sourceId, targetId, actorId);
            Assert.Equal(MergeResultKind.Merged, result.Kind);
        }

        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Target must have exactly ONE lobby_members row for lobby B.
            var targetCount = await ctx.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM gamekit.lobby_members
                    WHERE "LobbyId" = {lobbyId} AND "PlayerId" = {targetId}
                    """)
                .FirstOrDefaultAsync();
            Assert.Equal(1, targetCount);

            // Source must have zero lobby_members rows.
            var sourceCount = await ctx.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM gamekit.lobby_members
                    WHERE "PlayerId" = {sourceId}
                    """)
                .FirstOrDefaultAsync();
            Assert.Equal(0, sourceCount);

            // Total lobby_members for lobby B: exactly one row.
            var lobbyTotal = await ctx.Database
                .SqlQuery<int>(
                    $"""
                    SELECT COUNT(*)::int AS "Value"
                    FROM gamekit.lobby_members
                    WHERE "LobbyId" = {lobbyId}
                    """)
                .FirstOrDefaultAsync();
            Assert.Equal(1, lobbyTotal);
        }
    }

    /// <summary>
    /// Builds a DI service provider configured with Core + Auth (with SkipAuthenticationSchemeRegistration=true)
    /// and the full-scope runtime query customizer for the merge service.
    /// </summary>
    private static ServiceProvider BuildProvider(string connectionString, string redisConnectionString)
    {
        var keyDir = Path.Combine(Path.GetTempPath(), $"gk-merge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDir);
        var privPath = Path.Combine(keyDir, "priv.pem");
        var pubPath = Path.Combine(keyDir, "pub.pem");
        using (var rsa = RSA.Create(2048))
        {
            File.WriteAllText(privPath, rsa.ExportRSAPrivateKeyPem());
            File.WriteAllText(pubPath, rsa.ExportRSAPublicKeyPem());
        }

        var services = new ServiceCollection();
        var gkBuilder = services.AddGameKit(o =>
        {
            o.ConnectionString = connectionString;
            o.AutoMigrate = false;
        });
        gkBuilder.AddAuth(o =>
        {
            o.SkipAuthenticationSchemeRegistration = true;
            o.Jwt.Issuer = "gk-test";
            o.Jwt.Audience = "gk-test";
            o.Jwt.PrivateKeyPemPath = privPath;
            o.Jwt.PublicKeyPemPath = pubPath;
            o.Jwt.Kid = "test-kid-merge";
            o.Jwt.RefreshTokenLifetime = TimeSpan.FromDays(30);
        });

        // Provide Redis for post-commit cleanup.
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));

        // Re-register the DbContext with a runtime query customizer that applies Auth + Rankings
        // + Matchmaking entity configurations so queries against PlayerIdentity, PlayerRank, etc. work.
        services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
            dbOpts.UseNpgsql(connectionString)
                  .ReplaceService<IModelCustomizer, MergeTestRuntimeQueryCustomizer>());

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Runtime query customizer for merge service integration tests. Applies Core (via base),
    /// Auth, Rankings, and Matchmaking entity configurations so the merge service's cross-package
    /// SQL queries are backed by correctly-mapped entity types.
    /// </summary>
    internal sealed class MergeTestRuntimeQueryCustomizer : RelationalModelCustomizer
    {
        public MergeTestRuntimeQueryCustomizer(ModelCustomizerDependencies dependencies)
            : base(dependencies) { }

        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            // Auth entities.
            modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
            modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
            modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
            modelBuilder.ApplyConfiguration(new AccountMergeConfiguration());
            // Rankings entities — needed for PlayerRank query/assertions.
            new GameKit.Rankings.Data.RankingsModelBuilderExtension().ApplyTo(modelBuilder);
            // Matchmaking entities — needed for party_members party conflict check.
            new GameKit.Matchmaking.Data.MatchmakingModelBuilderExtension().ApplyTo(modelBuilder);
        }
    }
}
