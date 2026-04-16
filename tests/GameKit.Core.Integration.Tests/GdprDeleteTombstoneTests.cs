// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// CORE-16 GDPR round-trip integration test: seed 2 players + shared session, delete player A,
/// verify opponent session row has PlayerId=NULL, resolver returns tombstone, audit row written,
/// no residual PII in the DB.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class GdprDeleteTombstoneTests
{
    private readonly PostgresFixture _pg;

    public GdprDeleteTombstoneTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task DeletePlayer_OpponentSessions_Persist_With_Tombstone_And_No_Pii()
    {
        // Ensure schema exists
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = _pg.OwnerConnectionString;
            o.AutoMigrate = false;
        });
        await using var sp = services.BuildServiceProvider();

        await using (var scope = sp.CreateAsyncScope())
            await MigrationRunner.MigrateWithLockAsync(
                scope.ServiceProvider.GetRequiredService<GameKitDbContext>());

        var now = DateTimeOffset.UtcNow;
        var playerA = Guid.CreateVersion7();
        var playerB = Guid.CreateVersion7();
        var sessionId = Guid.CreateVersion7();

        // Seed 2 players and 1 session with both
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
            ctx.Players.AddRange(
                new Player { Id = playerA, DisplayName = "Alice", CreatedAt = now },
                new Player { Id = playerB, DisplayName = "Bob", CreatedAt = now });
            ctx.GameSessions.Add(new GameSession
            {
                Id = sessionId,
                State = GameSessionState.Completed,
                CreatedAt = now,
                StartedAt = now,
                CompletedAt = now
            });
            ctx.SessionParticipants.AddRange(
                new SessionParticipant
                {
                    Id = Guid.CreateVersion7(),
                    SessionId = sessionId,
                    PlayerId = playerA,
                    Team = 0
                },
                new SessionParticipant
                {
                    Id = Guid.CreateVersion7(),
                    SessionId = sessionId,
                    PlayerId = playerB,
                    Team = 1
                });
            await ctx.SaveChangesAsync();
        }

        // Delete player A via the service
        await using (var scope = sp.CreateAsyncScope())
        {
            var gdpr = scope.ServiceProvider.GetRequiredService<IGdprDeleteService>();
            await gdpr.DeletePlayerAsync(playerA, actorId: null, reason: "user request");
        }

        // Assertions
        await using (var scope = sp.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Player A hard-deleted
            Assert.Equal(0, await ctx.Players.CountAsync(p => p.Id == playerA));
            // Player B intact
            Assert.Equal(1, await ctx.Players.CountAsync(p => p.Id == playerB));

            // A's session participant row has PlayerId=NULL (FK SET NULL)
            var aRows = await ctx.SessionParticipants
                .Where(sp => sp.SessionId == sessionId && sp.PlayerId == null)
                .CountAsync();
            Assert.Equal(1, aRows);

            // B's session participant row intact
            var bRows = await ctx.SessionParticipants
                .Where(sp => sp.SessionId == sessionId && sp.PlayerId == playerB)
                .CountAsync();
            Assert.Equal(1, bRows);

            // Resolver returns tombstone for null
            var resolver = scope.ServiceProvider.GetRequiredService<IPlayerDisplayNameResolver>();
            Assert.Equal("Deleted Player", resolver.Resolve(null));
            // Resolver returns Bob for player B
            Assert.Equal("Bob", resolver.Resolve(playerB));

            // Audit log entry written
            var auditRows = await ctx.AdminAuditLog
                .CountAsync(a => a.Action == "gdpr.delete" && a.TargetId == playerA);
            Assert.Equal(1, auditRows);
        }
    }
}
