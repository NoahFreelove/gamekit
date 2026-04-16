// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Entities;
using Xunit;

namespace GameKit.Core.Tests.Entities;

public class AdminAuditLogTests
{
    [Fact]
    public void AdminAuditLog_Has_Required_Properties()
    {
        var log = new AdminAuditLog
        {
            Action = "player.ban",
            TargetType = "player"
        };

        log.Id = Guid.NewGuid();
        log.ActorId = Guid.NewGuid();
        log.TargetId = Guid.NewGuid();
        log.Before = null;
        log.After = null;
        log.Reason = "Toxic behavior";
        log.CreatedAt = DateTimeOffset.UtcNow;

        Assert.Equal("player.ban", log.Action);
        Assert.Equal("player", log.TargetType);
        Assert.Equal("Toxic behavior", log.Reason);
    }

    [Fact]
    public void ActorId_Is_Nullable_For_System_Actions()
    {
        var log = new AdminAuditLog
        {
            Action = "gdpr.delete",
            TargetType = "player"
        };

        log.ActorId = null;
        Assert.Null(log.ActorId);
    }

    [Fact]
    public void Action_And_TargetType_Are_Required()
    {
        // The 'required' keyword on Action and TargetType means they must be set at construction
        var actionProp = typeof(AdminAuditLog).GetProperty(nameof(AdminAuditLog.Action));
        var targetTypeProp = typeof(AdminAuditLog).GetProperty(nameof(AdminAuditLog.TargetType));

        Assert.NotNull(actionProp);
        Assert.NotNull(targetTypeProp);
        Assert.Contains(actionProp!.GetCustomAttributes(true), a => a.GetType().Name == "RequiredMemberAttribute");
        Assert.Contains(targetTypeProp!.GetCustomAttributes(true), a => a.GetType().Name == "RequiredMemberAttribute");
    }
}
