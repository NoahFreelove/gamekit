// SPDX-License-Identifier: GPL-3.0-or-later
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
    public void Player_Does_Not_Have_DeletedAt_Property()
    {
        // D-13: Player must NOT have a DeletedAt property
        var props = typeof(Player).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.DoesNotContain(props, p => p.Name == "DeletedAt");
    }

    [Fact]
    public void Player_DisplayName_Is_Required()
    {
        // The required keyword on DisplayName means it must be set at construction
        var prop = typeof(Player).GetProperty(nameof(Player.DisplayName));
        Assert.NotNull(prop);
        Assert.True(prop!.GetCustomAttributes().Any(a => a.GetType().Name == "RequiredMemberAttribute"));
    }

    [Fact]
    public void Player_Metadata_Is_Nullable_JsonDocument()
    {
        var prop = typeof(Player).GetProperty(nameof(Player.Metadata));
        Assert.NotNull(prop);
        Assert.Equal(typeof(System.Text.Json.JsonDocument), Nullable.GetUnderlyingType(prop!.PropertyType) ?? prop.PropertyType);
    }
}
