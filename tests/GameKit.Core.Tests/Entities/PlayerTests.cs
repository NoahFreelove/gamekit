// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using GameKit.Core.Entities;
using Xunit;

namespace GameKit.Core.Tests.Entities;

public class PlayerTests
{
    [Fact]
    public void Player_Has_Required_Properties()
    {
        var player = new Player { DisplayName = "TestPlayer" };

        // Verify all expected properties exist and are settable
        player.Id = Guid.NewGuid();
        player.CreatedAt = DateTimeOffset.UtcNow;
        player.LastSeenAt = DateTimeOffset.UtcNow;
        player.IsBanned = true;
        player.BannedAt = DateTimeOffset.UtcNow;
        player.BanReason = "Cheating";
        player.Metadata = null;

        Assert.Equal("TestPlayer", player.DisplayName);
        Assert.True(player.IsBanned);
        Assert.Equal("Cheating", player.BanReason);
    }

    [Fact]
    public void Player_Has_Nullable_DeletedAt_For_MergeTombstone()
    {
        // Phase 10 (AUTH-25): DeletedAt was added as an account-merge tombstone timestamp.
        // D-13 hard-delete still applies for GDPR erasure; DeletedAt is NOT a general soft-delete
        // flag — it is set only when MergedIntoPlayerId is non-null (merge tombstone path).
        var props = typeof(Player).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var deletedAt = Array.Find(props, p => p.Name == "DeletedAt");
        Assert.NotNull(deletedAt);
        Assert.Equal(typeof(DateTimeOffset?), deletedAt!.PropertyType);
    }

    [Fact]
    public void Player_DisplayName_Is_Required()
    {
        // The required keyword on DisplayName means it must be set at construction
        var prop = typeof(Player).GetProperty(nameof(Player.DisplayName));
        Assert.NotNull(prop);
        Assert.Contains(prop!.GetCustomAttributes(true), a => a.GetType().Name == "RequiredMemberAttribute");
    }

    [Fact]
    public void Player_Metadata_Is_Nullable_JsonDocument()
    {
        var prop = typeof(Player).GetProperty(nameof(Player.Metadata));
        Assert.NotNull(prop);
        Assert.Equal(typeof(System.Text.Json.JsonDocument), Nullable.GetUnderlyingType(prop!.PropertyType) ?? prop.PropertyType);
    }
}
