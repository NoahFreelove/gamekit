// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Moq;
using Xunit;

namespace GameKit.Core.Tests.Services;

public class GdprDeleteServiceTests
{
    private static GameKitDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new GameKitDbContext(options);
    }

    [Fact]
    public async Task DeletePlayerAsync_ThrowsPlayerNotFoundException_WhenPlayerDoesNotExist()
    {
        using var ctx = CreateInMemoryContext(nameof(DeletePlayerAsync_ThrowsPlayerNotFoundException_WhenPlayerDoesNotExist));
        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var ids = new Mock<IIdGenerator>();
        ids.Setup(i => i.NewId()).Returns(Guid.NewGuid());

        var svc = new GdprDeleteService(ctx, clock.Object, ids.Object);

        await Assert.ThrowsAsync<PlayerNotFoundException>(
            () => svc.DeletePlayerAsync(Guid.NewGuid(), null, "test", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePlayerAsync_DeletesPlayer_AndCreatesAuditLog()
    {
        using var ctx = CreateInMemoryContext(nameof(DeletePlayerAsync_DeletesPlayer_AndCreatesAuditLog));
        var playerId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var auditId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        ctx.Players.Add(new Player
        {
            Id = playerId,
            DisplayName = "TestPlayer",
            CreatedAt = now.AddDays(-1),
        });
        await ctx.SaveChangesAsync();

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(now);
        var ids = new Mock<IIdGenerator>();
        ids.Setup(i => i.NewId()).Returns(auditId);

        var svc = new GdprDeleteService(ctx, clock.Object, ids.Object);
        await svc.DeletePlayerAsync(playerId, actorId, "GDPR request", CancellationToken.None);

        // Player should be gone
        Assert.Null(await ctx.Players.FindAsync(playerId));

        // Audit log should exist
        var audit = await ctx.AdminAuditLog.FirstOrDefaultAsync(a => a.TargetId == playerId);
        Assert.NotNull(audit);
        Assert.Equal("gdpr.delete", audit.Action);
        Assert.Equal("player", audit.TargetType);
        Assert.Equal(actorId, audit.ActorId);
        Assert.Equal("GDPR request", audit.Reason);
        Assert.Equal(now, audit.CreatedAt);
    }

    [Fact]
    public async Task DeletePlayerAsync_WritesAuditBeforeDelete()
    {
        // Validates audit row is written BEFORE delete (survives the delete).
        // In InMemory provider, we can verify by checking the audit row exists after deletion.
        using var ctx = CreateInMemoryContext(nameof(DeletePlayerAsync_WritesAuditBeforeDelete));
        var playerId = Guid.NewGuid();

        ctx.Players.Add(new Player
        {
            Id = playerId,
            DisplayName = "AuditBeforeDelete",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await ctx.SaveChangesAsync();

        var clock = new Mock<IClock>();
        clock.Setup(c => c.UtcNow).Returns(DateTimeOffset.UtcNow);
        var ids = new Mock<IIdGenerator>();
        ids.Setup(i => i.NewId()).Returns(Guid.NewGuid());

        var svc = new GdprDeleteService(ctx, clock.Object, ids.Object);
        await svc.DeletePlayerAsync(playerId, null, "test-audit-before", CancellationToken.None);

        // Audit log should have a Before snapshot with the player's display name
        var audit = await ctx.AdminAuditLog.FirstAsync(a => a.TargetId == playerId);
        Assert.NotNull(audit.Before);
        var beforeJson = audit.Before!.RootElement;
        Assert.True(beforeJson.TryGetProperty("DisplayName", out var dn) || beforeJson.TryGetProperty("displayName", out dn));
    }
}
