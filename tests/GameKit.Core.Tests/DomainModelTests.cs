// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Core.Entities;
using Xunit;

namespace GameKit.Core.Tests;

/// <summary>
/// Cross-entity domain model invariant tests. Supplements per-entity tests in Entities/.
/// </summary>
public class DomainModelTests
{
    [Fact]
    public void Player_Has_No_DeletedAt_Property()
    {
        // D-13: hard delete, no soft-delete column
        var props = typeof(Player).GetProperties();
        Assert.DoesNotContain(props, p => p.Name == "DeletedAt" || p.Name == "deleted_at");
    }

    [Fact]
    public void SessionParticipant_PlayerId_Is_Nullable_Guid()
    {
        // GDPR FK set-null design: PlayerId must be Guid? not Guid
        var prop = typeof(SessionParticipant).GetProperty(nameof(SessionParticipant.PlayerId));
        Assert.NotNull(prop);
        Assert.Equal(typeof(Guid?), prop!.PropertyType);
    }

    [Fact]
    public void AdminAuditLog_Required_Fields_Present()
    {
        var props = typeof(AdminAuditLog).GetProperties();
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.ActorId));
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.Action));
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.TargetType));
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.TargetId));
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.Before));
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.After));
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.Reason));
        Assert.Contains(props, p => p.Name == nameof(AdminAuditLog.CreatedAt));
    }

    [Fact]
    public void GameSession_Default_State_Is_Pending()
    {
        var session = new GameSession();
        Assert.Equal(GameSessionState.Pending, session.State);
    }

    [Fact]
    public void SessionParticipant_RatingColumns_Are_Nullable()
    {
        var prop = typeof(SessionParticipant).GetProperty(nameof(SessionParticipant.RatingBefore));
        Assert.NotNull(prop);
        Assert.Equal(typeof(double?), prop!.PropertyType);

        prop = typeof(SessionParticipant).GetProperty(nameof(SessionParticipant.RatingAfter));
        Assert.NotNull(prop);
        Assert.Equal(typeof(double?), prop!.PropertyType);

        prop = typeof(SessionParticipant).GetProperty(nameof(SessionParticipant.RatingDelta));
        Assert.NotNull(prop);
        Assert.Equal(typeof(double?), prop!.PropertyType);
    }
}
