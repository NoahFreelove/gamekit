// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Admin.UI.Services;
using Xunit;

namespace GameKit.Admin.Tests;

public class PlayerSearchInputDetectionTests
{
    [Theory]
    [InlineData("0196e1a2-0c7a-7b6f-8101-deadbeef1234")]
    [InlineData("0196e1a20c7a7b6f8101deadbeef1234")] // 32-char no-dash
    public void UuidInput_Classifies_As_Id(string q)
    {
        var c = PlayerSearchService.ClassifyInput(q);
        Assert.Equal(SearchMode.Id, c.Mode);
        Assert.NotEqual(Guid.Empty, c.Id);
    }

    [Theory]
    [InlineData("steam:76561198012345678", "steam", "76561198012345678")]
    [InlineData("discord:1234567890", "discord", "1234567890")]
    public void ProviderExternalId_Classifies_As_Identity(string q, string p, string ext)
    {
        var c = PlayerSearchService.ClassifyInput(q);
        Assert.Equal(SearchMode.Identity, c.Mode);
        Assert.Equal(p, c.Provider);
        Assert.Equal(ext, c.ExternalId);
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("player1_42")]
    [InlineData("Bob")]
    public void FreeText_Classifies_As_DisplayName(string q)
    {
        var c = PlayerSearchService.ClassifyInput(q);
        Assert.Equal(SearchMode.DisplayName, c.Mode);
        Assert.Equal(q, c.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Empty_Classifies_As_None(string? q)
    {
        var c = PlayerSearchService.ClassifyInput(q!);
        Assert.Equal(SearchMode.None, c.Mode);
    }
}
