// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using Xunit;

namespace GameKit.Admin.Tests.Components;

/// <summary>
/// Phase 03.1 SC#6 — verifies WCAG 2.1 AA-relevant CSS rules and computed contrast ratios
/// for the violet palette. The deeper sweep (axe DevTools at 1280px on 8+ pages) is the
/// manual checkpoint in plan 03.1-09.
/// </summary>
public sealed class AccessibilityTests
{
    private const string CssRelativePath =
        "../../../../../src/GameKit.Admin.UI/wwwroot/gamekit-admin.css";

    private static string ReadCss()
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, CssRelativePath));
        return File.ReadAllText(fullPath);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void FocusVisibleRule_UsesAccentTokenAndOffset()
    {
        var css = ReadCss();
        // W8 — assert that BOTH `outline: 2px solid var(--accent)` AND `outline-offset: 2px`
        // appear within a single :focus-visible declaration block. The previous two-substring
        // form would falsely pass when those tokens lived in unrelated rules.
        Assert.Matches(
            new System.Text.RegularExpressions.Regex(
                @":focus-visible\s*\{[^}]*outline:\s*2px\s+solid\s+var\(--accent\)[^}]*outline-offset:\s*2px"),
            css);
    }

    [Theory]
    [Trait("Category", "Component")]
    // Format: foregroundHex, backgroundHex, minRatio (RESEARCH §Accessibility table)
    [InlineData("#7C3AED", "#FFFFFF", 3.0)]    // --accent on --surface — focus ring
    [InlineData("#7C3AED", "#F8FAFC", 3.0)]    // --accent on --bg
    [InlineData("#7C3AED", "#F1F5F9", 3.0)]    // --accent on --surface-2
    [InlineData("#6D28D9", "#FFFFFF", 4.5)]    // --accent-700 link text
    [InlineData("#64748B", "#F8FAFC", 4.5)]    // --fg-3 (slate-500 divergence) on --bg
    public void ContrastRatio_MeetsWcagThreshold(string fgHex, string bgHex, double minRatio)
    {
        var ratio = ComputeContrastRatio(fgHex, bgHex);
        Assert.True(ratio >= minRatio,
            $"Contrast {fgHex} on {bgHex} = {ratio:F2}:1, below threshold {minRatio}:1");
    }

    private static double ComputeContrastRatio(string hexA, string hexB)
    {
        var lumA = RelativeLuminance(hexA);
        var lumB = RelativeLuminance(hexB);
        var lighter = Math.Max(lumA, lumB);
        var darker = Math.Min(lumA, lumB);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        // Strip leading '#' and parse 3 channels.
        var h = hex.TrimStart('#');
        var r = Convert.ToInt32(h.Substring(0, 2), 16) / 255.0;
        var g = Convert.ToInt32(h.Substring(2, 2), 16) / 255.0;
        var b = Convert.ToInt32(h.Substring(4, 2), 16) / 255.0;
        return 0.2126 * Linearize(r) + 0.7152 * Linearize(g) + 0.0722 * Linearize(b);
    }

    private static double Linearize(double c)
        => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
}
