// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using MudBlazor;

namespace GameKit.Admin.UI;

/// <summary>
/// MudBlazor theme singleton that encodes the UI-SPEC §Color palette exactly (indigo-600
/// primary, slate neutrals, red-600 danger). The theme is referenced from
/// <c>MainLayout.razor</c> and <c>LoginLayout.razor</c> on <c>MudThemeProvider Theme=...</c>.
/// </summary>
/// <remarks>
/// <para>
/// Hex values are sourced from <c>.planning/phases/03-admin-ui/03-UI-SPEC.md</c> §Color. Each
/// named slot maps 1:1 to a UI-SPEC token: <c>Primary</c> = <c>--gk-color-primary</c>,
/// <c>Background</c> = <c>--gk-color-bg</c>, etc. Do not edit values here in isolation — the
/// authoritative palette lives in UI-SPEC; CSS custom properties in
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
            // Accent (UI-SPEC §Color — Accent 10%)
            Primary = "#4263EB",
            PrimaryDarken = "#364FC7",
            PrimaryLighten = "#EDF2FF",

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
