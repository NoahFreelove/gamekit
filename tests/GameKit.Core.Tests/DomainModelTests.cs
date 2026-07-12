// SPDX-License-Identifier: Apache-2.0
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
    public void Player_DeletedAt_Is_Nullable_DateTimeOffset()
    {
        // Phase 10 (AUTH-25): DeletedAt is a nullable account-merge tombstone timestamp.
        // D-13 hard-delete still applies for GDPR erasure; DeletedAt is NOT a general soft-delete
        // flag — it is set only when the player row is a merge tombstone (MergedIntoPlayerId != null).
        var prop = typeof(Player).GetProperty("DeletedAt");
        Assert.NotNull(prop);
        Assert.Equal(typeof(DateTimeOffset?), prop!.PropertyType);
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
