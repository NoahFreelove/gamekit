// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using Bunit;
using GameKit.Admin.UI.Components.Shared;
using Microsoft.AspNetCore.Components;
using MudBlazor.Services;
using Xunit;

namespace GameKit.Admin.Tests.Components;

/// <summary>
/// Phase 03.1 D-07 + UI-SPEC §5.3 — verifies <see cref="BanBanner"/> renders the canonical
/// alert markup regardless of loudness (loudness is CSS-driven via [data-ban-loud] on
/// <c>&lt;html&gt;</c>; component does not branch).
/// </summary>
public sealed class BanBannerTests : BunitContext
{
    /// <summary>Initializes the bUnit context with MudBlazor services.</summary>
    public BanBannerTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Renders_AlertRole_AndAriaLive()
    {
        var cut = Render<BanBanner>(p => p
            .Add(b => b.Reason, "spam")
            .Add(b => b.ActorName, "alice")
            .Add(b => b.At, DateTimeOffset.UtcNow));

        cut.Find("div.ban-banner[role='alert'][aria-live='polite']");
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Renders_AllThreeParameters()
    {
        var when = new DateTimeOffset(2026, 5, 1, 12, 34, 56, TimeSpan.Zero);
        var cut = Render<BanBanner>(p => p
            .Add(b => b.Reason, "abusive language")
            .Add(b => b.ActorName, "alice")
            .Add(b => b.At, when));

        Assert.Contains("abusive language", cut.Markup);
        Assert.Contains("alice", cut.Markup);
        Assert.Contains("2026-05-01 12:34:56 UTC", cut.Markup);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void HidesUnbanButton_WhenNoCallbackProvided()
    {
        var cut = Render<BanBanner>(p => p
            .Add(b => b.Reason, "spam")
            .Add(b => b.ActorName, "alice")
            .Add(b => b.At, DateTimeOffset.UtcNow));

        var unbanBtns = cut.FindAll("button.ban-banner-unban");
        Assert.Empty(unbanBtns);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void ShowsUnbanButton_WhenCallbackProvided()
    {
        var cut = Render<BanBanner>(p => p
            .Add(b => b.Reason, "spam")
            .Add(b => b.ActorName, "alice")
            .Add(b => b.At, DateTimeOffset.UtcNow)
            .Add(b => b.OnUnbanRequested, EventCallback.Factory.Create(this, () => { })));

        var unbanBtns = cut.FindAll("button.ban-banner-unban");
        Assert.Single(unbanBtns);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void EscapesHtmlInReason_NoMarkupString()
    {
        // T-03.1-06-01 mitigation — Razor @Reason auto-HTML-encodes; embedded <script>
        // renders as text, not as a script tag. The component must NOT use MarkupString.
        var cut = Render<BanBanner>(p => p
            .Add(b => b.Reason, "<script>alert('x')</script>")
            .Add(b => b.ActorName, "alice")
            .Add(b => b.At, DateTimeOffset.UtcNow));

        // Encoded form should be present in the rendered markup; raw <script> tag must not.
        Assert.Contains("&lt;script&gt;", cut.Markup);
        Assert.DoesNotContain("<script>alert", cut.Markup);
    }
}
