// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using MudBlazor;

namespace GameKit.Admin.UI;

/// <summary>
/// MudBlazor theme singleton that encodes the Phase 03.1 violet-600 palette (D-03; sketch
/// styles.css lines 64-68). The Primary/PrimaryDarken/PrimaryLighten slots track
/// <c>--accent</c> / <c>--accent-700</c> / <c>--accent-50</c> so MudBlazor surfaces (focus
/// ring, ripple, MudInput chrome) re-color via <c>--mud-palette-primary</c> at runtime.
/// </summary>
/// <remarks>
/// <para>
/// Hex values are sourced from <c>.planning/phases/03.1-admin-ui-redesign-v2/03.1-UI-SPEC.md</c>
/// §1.1. Each named slot maps 1:1 to a sketch token: <c>Primary</c> = <c>--accent</c>,
/// <c>Background</c> = <c>--bg</c>, <c>Surface</c> = <c>--surface</c>, etc. Do not edit values
/// here in isolation — the authoritative palette lives in UI-SPEC; CSS custom properties in
/// <c>wwwroot/gamekit-admin.css</c> mirror these values for non-MudBlazor markup (audit log
/// <c>&lt;pre&gt;</c> blocks, empty states).
/// </para>
/// <para>
/// Only <see cref="PaletteLight"/> is populated in v1 — a dark-mode hook is deferred per
/// CONTEXT deferred list.
/// </para>
/// </remarks>
public static class GameKitAdminTheme
{
    /// <summary>
    /// Default <see cref="MudTheme"/> singleton consumed by the admin layouts. Palette values
    /// track UI-SPEC §Color exactly; do not mutate at runtime.
    /// </summary>
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            // Accent — violet-600 ramp (Phase 03.1 D-03; sketch styles.css lines 64-68)
            // RESEARCH §Pattern 3: keeping --mud-palette-primary === --accent ensures the
            // MudBlazor focus ring / ripple / MudInput chrome track our violet automatically.
            Primary = "#7C3AED",        // violet-600 (was #4263EB indigo)
            PrimaryDarken = "#6D28D9",  // accent-700 (was #364FC7)
            PrimaryLighten = "#F5F3FF", // accent-50  (was #EDF2FF)

            // Neutral surfaces (UI-SPEC §Color — 60/30 split)
            Background = "#F8FAFC",
            Surface = "#FFFFFF",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#0F172A",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#0F172A",

            // Text colors
            TextPrimary = "#0F172A",
            TextSecondary = "#475569",
            TextDisabled = "#94A3B8",

            // Semantic status palette (UI-SPEC §Color — Semantic status)
            Error = "#DC2626",
            ErrorLighten = "#FEE2E2",
            Success = "#16A34A",
            SuccessLighten = "#DCFCE7",
            Warning = "#D97706",
            WarningLighten = "#FEF3C7",
            Info = "#2563EB",
            InfoLighten = "#DBEAFE",

            // Structural lines / borders
            TableLines = "#E2E8F0",
            TableHover = "#F1F5F9",
            Divider = "#E2E8F0",
            LinesDefault = "#E2E8F0",
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "240px",
            DrawerMiniWidthLeft = "64px",
            AppbarHeight = "56px",
        },
        Typography = new Typography
        {
            // UI-SPEC §Typography — system font stack (no Google Fonts CDN).
            Default = new DefaultTypography
            {
                FontFamily = new[] { "-apple-system", "BlinkMacSystemFont", "Segoe UI", "Roboto", "Oxygen", "Ubuntu", "sans-serif" },
                FontSize = "14px",
                LineHeight = "1.5",
                FontWeight = "400",
            },
            // UI-SPEC §Typography — only "Heading" role is declared; page titles are <h1> using H1 tokens.
            H1 = new H1Typography
            {
                FontSize = "20px",
                FontWeight = "600",
                LineHeight = "1.3",
            },
        },
    };
}
