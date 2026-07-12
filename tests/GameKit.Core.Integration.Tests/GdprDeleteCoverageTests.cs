// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

/// <summary>
/// SEC-04 GDPR completeness regression test: seeds a player across EVERY FK table and asserts
/// zero residual rows after <c>DeletePlayerAsync</c>.
///
/// <para>
/// <b>RED/GREEN gate:</b> this test is RED before Tasks 1–2 of Plan 18-02 because
/// <c>GdprDeleteService</c> does not yet call the <c>IGdprDeleteExtension</c> hooks — the
/// player delete throws a Postgres 23503 FK violation on either <c>party_members.PlayerId</c>
/// (RESTRICT) or <c>account_merges.TargetPlayerId</c> (RESTRICT). It turns GREEN once
/// <c>AuthGdprDeleteExtension</c> and <c>MatchmakingGdprDeleteExtension</c> are registered
/// and invoked inside the SERIALIZABLE transaction.
/// </para>
/// </summary>

using System;
using System.Threading.Tasks;
using GameKit.Auth.Builder;
using GameKit.Auth.Data;
using GameKit.Auth.Entities;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.Lobby.Builder;
using GameKit.Lobby.Data;
using GameKit.Lobby.Entities;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Data;
using GameKit.Matchmaking.Entities;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using GameKit.Rankings.Entities;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// SEC-04 all-FK-tables GDPR completeness test.
/// Seeds a player across every table with a direct or transitive FK to <c>players</c>,
/// calls <see cref="IGdprDeleteService.DeletePlayerAsync"/>, and asserts zero residual rows
/// in all CASCADE/DELETE tables plus documented SET NULL tombstones in SetNull tables.
/// </summary>
/// <remarks>
/// Registration pattern: <see cref="IGdprDeleteExtension"/> implementations from Auth and
/// Matchmaking are registered via <see cref="ServiceCollectionDescriptorExtensions.TryAddEnumerable"/>
/// alongside each package's <see cref="IModelBuilderExtension"/> — mirroring what <c>AddAuth</c>
/// and <c>AddMatchmaking</c> would do in a full application host.
/// </remarks>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class GdprDeleteCoverageTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private string _cs = string.Empty;

    /// <summary>Constructs with the shared Postgres fixture.</summary>
    public GdprDeleteCoverageTests(PostgresFixture pg) => _pg = pg;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _cs = await CreateFreshDatabaseAsync(_pg);
        await ApplyAllMigrationsAsync(_cs);
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Seeds a player across ALL FK tables (direct and transitive), deletes the player via
    /// <see cref="IGdprDeleteService"/>, and asserts the deletion is complete with correct
    /// SET NULL tombstones preserved.
    /// </summary>
    [Fact]
    public async Task DeletePlayerAsync_ErasesPlayer_AcrossAllFkTables_WithCorrectSetNullTombstones()
    {
        var now = DateTimeOffset.UtcNow;
        var player = Guid.CreateVersion7();
        var opponent = Guid.CreateVersion7(); // survives the delete — sanity check for cascade isolation

        // ── Ancillary rows required by FK references ───────────────────────────
        var ladderId = Guid.CreateVersion7();   // Ladder for PlayerRank + MatchmakingTicket
        var sessionId = Guid.CreateVersion7();  // GameSession for SessionParticipant
        var ownedPartyId = Guid.CreateVersion7(); // Party the player OWNS (OwnerPlayerId CASCADE)
        var foreignPartyId = Guid.CreateVersion7(); // Party the player is a NON-OWNER member of (RESTRICT gap)
        var lobbyId = Guid.CreateVersion7();    // Lobby the player is a member of (CASCADE)

        await using var sp = BuildServiceProvider(_cs);

        // ── Seed ──────────────────────────────────────────────────────────────
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Two players: target (player) and bystander (opponent).
            ctx.Players.AddRange(
                new Player { Id = player,   DisplayName = "GDPR-Target",  CreatedAt = now },
                new Player { Id = opponent, DisplayName = "GDPR-Bystander", CreatedAt = now });

            // GameSession — for SessionParticipant (SET NULL tombstone).
            ctx.GameSessions.Add(new GameSession
            {
                Id = sessionId,
                State = GameSessionState.Completed,
                CreatedAt = now,
                StartedAt = now,
                CompletedAt = now
            });

            // SessionParticipant — player.PlayerId is SET NULL on delete (no PII leaked, but row survives).
            // ON DELETE SET NULL — intentional tombstone behavior.
            ctx.SessionParticipants.Add(new SessionParticipant
            {
                Id = Guid.CreateVersion7(),
                SessionId = sessionId,
                PlayerId = player,
                Team = 0
            });

            // Ladder — required for PlayerRank and MatchmakingTicket FKs (Restrict on LadderId).
            ctx.Set<Ladder>().Add(new Ladder
            {
                Id = ladderId,
                Name = "gdpr-test-ladder",
                Algorithm = "glicko2",
                IsActive = true
            });

            // PlayerRank — ON DELETE CASCADE.
            ctx.Set<PlayerRank>().Add(new PlayerRank
            {
                Id = Guid.CreateVersion7(),
                PlayerId = player,
                LadderId = ladderId,
                Rating = 1500.0,
                RatingDeviation = 350.0,
                Volatility = 0.06
            });

            // Auth entities — all CASCADE (except AccountMerge.TargetPlayerId which is RESTRICT GAP 2).
            ctx.Set<PlayerIdentity>().Add(new PlayerIdentity
            {
                Id = Guid.CreateVersion7(),
                PlayerId = player,
                Provider = "steam",
                ExternalId = "gdpr-test-steam-id",
                CreatedAt = now,
                UpdatedAt = now
            });

            ctx.Set<PlayerCredential>().Add(new PlayerCredential
            {
                PlayerId = player,
                Username = "gdpr-test-user",
                PasswordHash = "$2a$11$test",
                UpdatedAt = now
            });

            ctx.Set<RefreshToken>().Add(new RefreshToken
            {
                Id = Guid.CreateVersion7(),
                PlayerId = player,
                FamilyId = Guid.CreateVersion7(),
                TokenHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                Provider = "password",
                IssuedAt = now,
                ExpiresAt = now.AddDays(30),
                RevokedAt = null
            });

            // AccountMerge WHERE TargetPlayerId = player — RESTRICT GAP 2 (fixed by AuthGdprDeleteExtension).
            ctx.Set<AccountMerge>().Add(new AccountMerge
            {
                Id = Guid.CreateVersion7(),
                SourcePlayerId = Guid.CreateVersion7(), // source is already gone (no FK) — bare UUID
                TargetPlayerId = player,                // RESTRICT FK — blocks player delete without fix
                Status = MergeStatus.RedisCleaned,
                RequestedAt = now,
                CommittedAt = now,
                RedisCleanedAt = now
            });

            // Lobby + LobbyMember — CASCADE.
            ctx.Set<GameKit.Lobby.Entities.Lobby>().Add(new GameKit.Lobby.Entities.Lobby
            {
                Id = lobbyId,
                OwnerId = null,   // lobby owner is null (lobby persists when owner deleted)
                State = LobbyState.Open,
                MaxMembers = 4,
                CreatedAt = now,
                UpdatedAt = now
            });
            ctx.Set<LobbyMember>().Add(new LobbyMember
            {
                Id = Guid.CreateVersion7(),
                LobbyId = lobbyId,
                PlayerId = player,  // CASCADE — deleted automatically
                Ready = false,
                JoinedAt = now
            });

            // Party the player OWNS — CASCADE from players.OwnerPlayerId.
            // When the players row is deleted, parties.OwnerPlayerId CASCADE deletes the party,
            // which cascades to party_members and sets matchmaking_tickets.PartyId = NULL.
            ctx.Set<Party>().Add(new Party
            {
                Id = ownedPartyId,
                OwnerPlayerId = player, // CASCADE — party deleted when player is deleted
                PartyCode = "OWN001",
                State = PartyState.Open,
                CreatedAt = now
            });
            ctx.Set<PartyMember>().Add(new PartyMember
            {
                Id = Guid.CreateVersion7(),
                PartyId = ownedPartyId,
                PlayerId = player,  // This row is CASCADE-deleted via party delete (not directly by player delete)
                JoinedAt = now
            });

            // MatchmakingTicket referencing the owned party — SC#4 / SEC-04 named table.
            // matchmaking_tickets has NO direct PlayerId FK; coverage is TRANSITIVE:
            //   players.Id CASCADE → parties.OwnerPlayerId → party deleted →
            //   matchmaking_tickets.PartyId SET NULL (ticket row SURVIVES with PartyId = NULL).
            ctx.Set<MatchmakingTicket>().Add(new MatchmakingTicket
            {
                Id = Guid.CreateVersion7(),
                PartyId = ownedPartyId, // will become NULL after player delete (SET NULL transitive)
                LadderId = ladderId,
                PoolName = "default",
                Status = TicketStatus.Queued,
                QueuedAt = now
            });

            // Party the player is a NON-OWNER member of — RESTRICT GAP 1 (fixed by MatchmakingGdprDeleteExtension).
            ctx.Set<Party>().Add(new Party
            {
                Id = foreignPartyId,
                OwnerPlayerId = opponent, // opponent owns this party; player is just a member
                PartyCode = "FOR001",
                State = PartyState.Open,
                CreatedAt = now
            });
            ctx.Set<PartyMember>().Add(new PartyMember
            {
                Id = Guid.CreateVersion7(),
                PartyId = foreignPartyId,
                PlayerId = player,   // RESTRICT FK — blocks player delete without fix (GAP 1)
                JoinedAt = now
            });

            // DeclineHistory — CASCADE.
            ctx.Set<DeclineHistory>().Add(new DeclineHistory
            {
                Id = Guid.CreateVersion7(),
                PlayerId = player,  // CASCADE — deleted automatically
                DeclinedAt = now,
                ProposalId = Guid.CreateVersion7()
            });

            await ctx.SaveChangesAsync();
        }

        // ── Act: GDPR delete ──────────────────────────────────────────────────
        await using (var scope = sp.CreateAsyncScope())
        {
            var gdpr = scope.ServiceProvider.GetRequiredService<IGdprDeleteService>();
            await gdpr.DeletePlayerAsync(player, actorId: null, reason: "coverage test");
        }

        // ── Assert ────────────────────────────────────────────────────────────
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // ── CASCADE / hard-delete tables (expect zero rows for the player) ──

            // players — the target row must be gone.
            Assert.Equal(0, await ctx.Players.CountAsync(p => p.Id == player));
            // Bystander must survive.
            Assert.Equal(1, await ctx.Players.CountAsync(p => p.Id == opponent));

            // player_credentials — ON DELETE CASCADE.
            Assert.Equal(0, await ctx.Set<PlayerCredential>().CountAsync(c => c.PlayerId == player));

            // player_identities — ON DELETE CASCADE.
            Assert.Equal(0, await ctx.Set<PlayerIdentity>().CountAsync(i => i.PlayerId == player));

            // refresh_tokens — ON DELETE CASCADE.
            Assert.Equal(0, await ctx.Set<RefreshToken>().CountAsync(t => t.PlayerId == player));

            // player_ranks — ON DELETE CASCADE.
            Assert.Equal(0, await ctx.Set<PlayerRank>().CountAsync(r => r.PlayerId == player));

            // lobby_members — ON DELETE CASCADE.
            Assert.Equal(0, await ctx.Set<LobbyMember>().CountAsync(m => m.PlayerId == player));

            // decline_history — ON DELETE CASCADE.
            Assert.Equal(0, await ctx.Set<DeclineHistory>().CountAsync(d => d.PlayerId == player));

            // ── RESTRICT fixes (zero rows after AuthGdprDeleteExtension + MatchmakingGdprDeleteExtension) ──

            // account_merges WHERE TargetPlayerId = player — fixed by AuthGdprDeleteExtension (SEC-04 GAP 2).
            // ON DELETE RESTRICT → pre-deleted by AuthGdprDeleteExtension before the players row delete.
            Assert.Equal(0, await ctx.Set<AccountMerge>().CountAsync(am => am.TargetPlayerId == player));

            // party_members WHERE PlayerId = player — fixed by MatchmakingGdprDeleteExtension (SEC-04 GAP 1).
            // Includes both the owned-party member row (cascaded via party) AND the non-owner member row
            // (RESTRICT — deleted by the extension). Both must be zero.
            Assert.Equal(0, await ctx.Set<PartyMember>().CountAsync(pm => pm.PlayerId == player));

            // parties WHERE OwnerPlayerId = player — CASCADE deletes the owned party.
            Assert.Equal(0, await ctx.Set<Party>().CountAsync(p => p.OwnerPlayerId == player));

            // ── SET NULL tombstone tables (rows SURVIVE with null FK, confirming analytics preservation) ──

            // session_participants — ON DELETE SET NULL on PlayerId.
            // The player's participation row survives with PlayerId = NULL (tombstone).
            var nullParticipants = await ctx.SessionParticipants
                .Where(sp => sp.SessionId == sessionId && sp.PlayerId == null)
                .CountAsync();
            Assert.Equal(1, nullParticipants); // tombstone row preserved

            // matchmaking_tickets.PartyId — SC#4 / SEC-04 named table.
            // matchmaking_tickets has NO direct PlayerId FK; coverage is transitive:
            //   players → parties.OwnerPlayerId (CASCADE → party deleted) →
            //   matchmaking_tickets.PartyId (SET NULL → ticket row survives with PartyId = NULL).
            // The ticket row must survive (count >= 1) and PartyId must be NULL.
            var survivingTickets = await ctx.Set<MatchmakingTicket>()
                .Where(t => t.PartyId == null && t.LadderId == ladderId)
                .CountAsync();
            Assert.Equal(1, survivingTickets); // SC#4: ticket survives with PartyId = NULL

            // ── Audit log — GdprDeleteService must write the audit row ──────────
            var auditCount = await ctx.AdminAuditLog
                .CountAsync(a => a.Action == "gdpr.delete" && a.TargetId == player);
            Assert.Equal(1, auditCount);

            // ── Opponent / bystander isolation — party he owns must survive ──────
            Assert.Equal(1, await ctx.Set<Party>().CountAsync(p => p.Id == foreignPartyId));
            Assert.Equal(0, await ctx.Set<PartyMember>().CountAsync(pm => pm.PartyId == foreignPartyId));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ServiceProvider BuildServiceProvider(string cs)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // AddGameKit wires UseApplicationServiceProvider so the DbContext picks up all
        // IModelBuilderExtension registrations from sibling packages below via OnModelCreating.
        var gkBuilder = services.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.AutoMigrate = false;
        });

        // AddAuth — registers AuthModelBuilderExtension (IModelBuilderExtension) + AuthGdprDeleteExtension
        // (IGdprDeleteExtension, SEC-04 GAP 2 fix). Use SkipAuthenticationSchemeRegistration=true to
        // avoid loading PEM key files — we only need the entity model and GDPR hook for this test.
        gkBuilder.AddAuth(o =>
        {
            o.Jwt.Issuer = "gdpr-test";
            o.Jwt.Audience = "gdpr-test";
            o.SkipAuthenticationSchemeRegistration = true;
        });

        // AddRankings — registers RankingsModelBuilderExtension (IModelBuilderExtension).
        // No Redis required. ValidateOnStart fires only on IHost.StartAsync, not BuildServiceProvider.
        gkBuilder.AddRankings();

        // AddMatchmaking — registers MatchmakingModelBuilderExtension (IModelBuilderExtension)
        // + MatchmakingGdprDeleteExtension (IGdprDeleteExtension, SEC-04 GAP 1 fix).
        // Redis services are deferred; IConnectionMultiplexer is only resolved at StartAsync time.
        gkBuilder.AddMatchmaking();

        // AddLobby — registers LobbyModelBuilderExtension (IModelBuilderExtension).
        // LobbyRedisBackplanePostConfigure defers IConnectionMultiplexer resolution to IPostConfigureOptions
        // which fires at StartAsync, not BuildServiceProvider — so no Redis is needed here.
        gkBuilder.AddLobby();

        // Override the DbContext registration with a custom IModelCacheKeyFactory that incorporates
        // the set of registered IModelBuilderExtension types into the EF model cache key.
        // Without this, the default cache key (contextType, modelCustomizerType, designTime) is
        // shared between migration contexts (Core-only) and this full-runtime context, so EF reuses
        // a stale Core-only model and throws "Cannot create a DbSet for 'Ladder'" (SEC-04 cache fix).
        // ReplaceService is scoped to THIS AddDbContext registration only and does not affect other
        // service providers created by AddGameKit in ApplyAllMigrationsAsync.
        services.AddDbContext<GameKitDbContext>((sp, dbOpts) =>
            dbOpts.UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(GameKitDbContext).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    GameKitMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .UseApplicationServiceProvider(sp)
            .ReplaceService<IModelCacheKeyFactory, GameKitModelCacheKeyFactory>());

        return services.BuildServiceProvider();
    }

    private static async Task<string> CreateFreshDatabaseAsync(PostgresFixture pg)
    {
        var dbName = "gamekit_gdpr_cov_" + Guid.NewGuid().ToString("N")[..12];
        await using (var bootstrap = new NpgsqlConnection(pg.AdminConnectionString))
        {
            await bootstrap.OpenAsync();
            await using var cmd = bootstrap.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE {dbName} OWNER gamekit_owner";
            await cmd.ExecuteNonQueryAsync();
        }
        var builder = new NpgsqlConnectionStringBuilder(pg.OwnerConnectionString) { Database = dbName };
        var freshCs = builder.ConnectionString;
        await using (var freshConn = new NpgsqlConnection(freshCs))
        {
            await freshConn.OpenAsync();
            await using var cmd = freshConn.CreateCommand();
            cmd.CommandText = "CREATE EXTENSION IF NOT EXISTS citext; CREATE SCHEMA IF NOT EXISTS gamekit;";
            await cmd.ExecuteNonQueryAsync();
        }
        return freshCs;
    }

    /// <summary>
    /// Applies migrations in dependency order: Core → Auth → Rankings → Matchmaking → Lobby.
    /// Each package uses its dedicated migration model customizer (excludes prior-package entities
    /// from its own migration diff per the per-package migration boundary, CLAUDE.md / PITFALLS #3).
    /// </summary>
    private static async Task ApplyAllMigrationsAsync(string cs)
    {
        // 1. Core migration — foundational tables (players, game_sessions, session_participants, admin_audit_log).
        var coreServices = new ServiceCollection();
        coreServices.AddGameKit(o =>
        {
            o.ConnectionString = cs;
            o.MigrationsConnectionString = cs;
            o.AutoMigrate = false;
        });
        await using (var coreSp = coreServices.BuildServiceProvider())
        await using (var scope = coreSp.CreateAsyncScope())
        {
            await MigrationRunner.MigrateWithLockAsync(scope.ServiceProvider.GetRequiredService<GameKitDbContext>());
        }

        // 2. Auth migration — player_identities, player_credentials, refresh_tokens, account_merges.
        var authOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AuthMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    AuthMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, AuthMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var authCtx = new GameKitDbContext(authOpts))
        {
            await MigrationRunner.MigrateWithLockAsync(authCtx, AuthMigrationConstants.AdvisoryLockKey);
        }

        // 3. Rankings migration — ladders, player_ranks, pending_rating_updates, season_rank_archives,
        //    session_complete_idempotency, service_tokens, ladder_seasons.
        var rankingsOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(RankingsMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    RankingsMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, RankingsMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var rankingsCtx = new GameKitDbContext(rankingsOpts))
        {
            await MigrationRunner.MigrateWithLockAsync(rankingsCtx, RankingsMigrationConstants.AdvisoryLockKey);
        }

        // 4. Matchmaking migration — parties, party_members, matchmaking_tickets, ticket_events, decline_history.
        //    Requires Rankings tables (matchmaking_tickets.LadderId → ladders).
        var mmOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(MatchmakingMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    MatchmakingMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, MatchmakingMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var mmCtx = new GameKitDbContext(mmOpts))
        {
            await MigrationRunner.MigrateWithLockAsync(mmCtx, MatchmakingMigrationConstants.AdvisoryLockKey);
        }

        // 5. Lobby migration — lobbies, lobby_members.
        //    Requires Auth + Rankings + Matchmaking tables (widest exclusion list).
        var lobbyOpts = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(LobbyMigrationConstants).Assembly.FullName);
                npg.MigrationsHistoryTable(
                    LobbyMigrationConstants.MigrationsHistoryTable,
                    GameKitMigrationConstants.SchemaName);
            })
            .ReplaceService<IModelCustomizer, LobbyMigrationModelCustomizer>()
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        await using (var lobbyCtx = new GameKitDbContext(lobbyOpts))
        {
            await MigrationRunner.MigrateWithLockAsync(lobbyCtx, LobbyMigrationConstants.AdvisoryLockKey);
        }
    }
}
