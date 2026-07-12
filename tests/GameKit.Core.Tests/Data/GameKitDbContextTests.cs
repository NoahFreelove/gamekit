// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameKit.Core.Tests.Data;

public class GameKitDbContextTests
{
    /// <summary>
    /// Creates a GameKitDbContext configured with Npgsql (no live connection needed for model inspection).
    /// </summary>
    private static GameKitDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GameKitDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=test;Password=test")
            .Options;
        return new GameKitDbContext(options);
    }

    [Fact]
    public void Context_IsSealedClass()
    {
        Assert.True(typeof(GameKitDbContext).IsSealed);
    }

    [Fact]
    public void Context_InheritsFromDbContext()
    {
        Assert.True(typeof(DbContext).IsAssignableFrom(typeof(GameKitDbContext)));
    }

    [Fact]
    public void Context_HasPlayersDbSet()
    {
        using var ctx = CreateContext();
        Assert.NotNull(ctx.Players);
        Assert.IsAssignableFrom<DbSet<Player>>(ctx.Players);
    }

    [Fact]
    public void Context_HasGameSessionsDbSet()
    {
        using var ctx = CreateContext();
        Assert.NotNull(ctx.GameSessions);
        Assert.IsAssignableFrom<DbSet<GameSession>>(ctx.GameSessions);
    }

    [Fact]
    public void Context_HasSessionParticipantsDbSet()
    {
        using var ctx = CreateContext();
        Assert.NotNull(ctx.SessionParticipants);
        Assert.IsAssignableFrom<DbSet<SessionParticipant>>(ctx.SessionParticipants);
    }

    [Fact]
    public void Context_HasAdminAuditLogDbSet()
    {
        using var ctx = CreateContext();
        Assert.NotNull(ctx.AdminAuditLog);
        Assert.IsAssignableFrom<DbSet<AdminAuditLog>>(ctx.AdminAuditLog);
    }

    [Fact]
    public void OnModelCreating_SetsDefaultSchema_ToGamekit()
    {
        using var ctx = CreateContext();
        var model = ctx.Model;
        Assert.Equal("gamekit", model.GetDefaultSchema());
    }

    [Fact]
    public void OnModelCreating_PicksUpEntityConfigurations()
    {
        using var ctx = CreateContext();
        var model = ctx.Model;

        // All four entities should be registered in the model.
        Assert.NotNull(model.FindEntityType(typeof(Player)));
        Assert.NotNull(model.FindEntityType(typeof(GameSession)));
        Assert.NotNull(model.FindEntityType(typeof(SessionParticipant)));
        Assert.NotNull(model.FindEntityType(typeof(AdminAuditLog)));
    }
}
