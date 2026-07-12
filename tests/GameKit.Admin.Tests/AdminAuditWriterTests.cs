// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Admin.UI.Services;
using GameKit.Core.Entities;
using GameKit.Core.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace GameKit.Admin.Tests;

public class AdminAuditWriterTests
{
    [Fact]
    public async Task WriteAsync_Inserts_Row_With_Namespaced_Action_And_JsonPayloads()
    {
        // Arrange: minimal DbContext via InMemory provider (via TestDbContextFactory which
        // wires a JsonDocument value converter — InMemory does not support jsonb natively).
        await using var ctx = TestDbContextFactory.Create($"admin-audit-{Guid.NewGuid()}");

        var clock = new Mock<IClock>();
        var nowUtc = new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero);
        clock.SetupGet(c => c.UtcNow).Returns(nowUtc);
        var ids = new Mock<IIdGenerator>();
        var auditId = Guid.Parse("01960000-0000-7000-8000-000000000001");
        ids.Setup(g => g.NewId()).Returns(auditId);

        var sut = new AdminAuditWriter(ctx, clock.Object, ids.Object);
        var actor = Guid.NewGuid();
        var target = Guid.NewGuid();

        // Act
        await sut.WriteAsync(
            action: AdminAuditActions.PlayerBan,
            targetType: "player",
            targetId: target,
            actorId: actor,
            before: new { is_banned = false },
            after: new { is_banned = true, ban_reason = "spam" },
            reason: "spam",
            cancellationToken: default);

        // Assert
        var row = await ctx.Set<AdminAuditLog>().SingleAsync();
        Assert.Equal(auditId, row.Id);
        Assert.Equal("admin.player.ban", row.Action);
        Assert.Equal("player", row.TargetType);
        Assert.Equal(target, row.TargetId);
        Assert.Equal(actor, row.ActorId);
        Assert.Equal("spam", row.Reason);
        Assert.Equal(nowUtc, row.CreatedAt);
        Assert.NotNull(row.Before);
        Assert.NotNull(row.After);
        Assert.Contains("is_banned", row.Before!.RootElement.ToString());
    }

    [Fact]
    public void AdminAuditActions_Contains_All_Nine_Namespaced_Actions()
    {
        // Compile-time assertion that the 9 action constants exist with exact literal values.
        Assert.Equal("admin.player.ban", AdminAuditActions.PlayerBan);
        Assert.Equal("admin.player.unban", AdminAuditActions.PlayerUnban);
        Assert.Equal("admin.player.gdpr_delete", AdminAuditActions.PlayerGdprDelete);
        Assert.Equal("admin.player.rank_adjust", AdminAuditActions.PlayerRankAdjust);
        Assert.Equal("admin.admin.create", AdminAuditActions.AdminCreate);
        Assert.Equal("admin.admin.delete", AdminAuditActions.AdminDelete);
        Assert.Equal("admin.signing_key.rotate", AdminAuditActions.SigningKeyRotate);
        Assert.Equal("admin.session.login.success", AdminAuditActions.SessionLoginSuccess);
        Assert.Equal("admin.session.login.failure", AdminAuditActions.SessionLoginFailure);
    }
}
