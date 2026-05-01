// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using Bunit;
using GameKit.Admin.UI.Components.Shared;
using Xunit;

namespace GameKit.Admin.Tests.Components;

/// <summary>
/// Phase 03.1 D-06 / D-08 — verifies the Tweaks panel ships 5 radiogroups (accent,
/// density, sidebar, banLoud, dashDir), with dashDir A/B/C rendered as disabled
/// "coming soon" buttons, and a reset-to-defaults footer button. Covers SC#4
/// (component test); the markup foundation for SC#2 (localStorage round-trip + first-
/// paint application) is JS-side and verified manually in Plan 09 walkthrough.
///
/// Uses bUnit 2.0.66 BunitContext + Render&lt;T&gt; (the older TestContext +
/// RenderComponent API is obsolete in this version — see CommandPaletteTests for the
/// established repo pattern).
/// </summary>
public sealed class TweaksPanelTests : BunitContext
{
    [Fact]
    [Trait("Category", "Component")]
    public void Renders_FiveRadiogroups()
    {
        var cut = Render<TweaksPanel>();
        var groups = cut.FindAll("[role='radiogroup']");
        Assert.Equal(5, groups.Count);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Accent_HasFivePresetOptions()
    {
        var cut = Render<TweaksPanel>();
        var accentBtns = cut.FindAll("[data-tweak='accent']");
        Assert.Equal(5, accentBtns.Count);
        var values = accentBtns.Select(b => b.GetAttribute("data-value")).ToArray();
        Assert.Contains("violet", values);
        Assert.Contains("indigo", values);
        Assert.Contains("teal", values);
        Assert.Contains("slate", values);
        Assert.Contains("orange", values);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void DashboardDirection_AbcDisabled_WithComingSoonTooltip()
    {
        var cut = Render<TweaksPanel>();
        // The dashDir radiogroup contains 4 buttons; D is live with data-tweak,
        // A/B/C are disabled coming-soon with no data-tweak (D-08 graceful degrade).
        var disabled = cut.FindAll("button[disabled][title='coming soon']");
        Assert.Equal(3, disabled.Count);
        // Verify D is the only live option in dashDir.
        var dashLive = cut.FindAll("[data-tweak='dashDir']");
        Assert.Single(dashLive);
        Assert.Equal("D", dashLive[0].GetAttribute("data-value"));
    }

    [Fact]
    [Trait("Category", "Component")]
    public void HasResetButton_WithDataTweakActionReset()
    {
        var cut = Render<TweaksPanel>();
        var resetBtn = cut.Find("[data-tweak-action='reset']");
        Assert.NotNull(resetBtn);
        Assert.Contains("Reset", resetBtn.TextContent);
    }
}
