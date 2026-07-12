// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Security.Claims;
using Bunit;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Components.Shared;
using GameKit.Admin.UI.Services;
using Xunit;

namespace GameKit.Admin.Tests.Components;

/// <summary>
/// Phase 03.1 D-09 / D-11 — verifies the command palette renders the role-filtered command
/// set inside a server-rendered <c>role="dialog"</c> shell. Plan 04 ships these assertions
/// (the file existed as a Skipped placeholder shipped by Plan 01). Three live facts:
///   1. Renders the dialog ARIA shell + scrim + input.
///   2. Plain admin role hides every <c>RequiresSuperadmin</c> row (D-11; never grayed).
///   3. Superadmin role sees every row in the registry.
/// </summary>
public sealed class CommandPaletteTests : BunitContext
{
    [Fact]
    [Trait("Category", "Component")]
    public void Renders_DialogShell_WithAriaModalAndLabel()
    {
        AddAuthorization().SetAuthorized("test-admin");
        // Role claim is read by CommandPalette.OnInitializedAsync — set it via SetClaims so
        // the role-filter branch fires the same way it would in production.
        AddAuthorizationClaims(this, AdminRoles.Admin);

        var cut = Render<CommandPalette>();

        cut.Find("div.palette[role='dialog'][aria-modal='true'][aria-label='Command palette']");
        cut.Find("div.palette-scrim");
        cut.Find("input[type='text']");
    }

    [Fact]
    [Trait("Category", "Component")]
    public void AdminRole_HidesSuperadminOnlyRows()
    {
        AddAuthorization().SetAuthorized("test-admin");
        AddAuthorizationClaims(this, AdminRoles.Admin);

        var cut = Render<CommandPalette>();

        var expectedCount = AdminCommandRegistry.AllCommands.Count(c => !c.RequiresSuperadmin);
        // WaitForAssertion absorbs the async OnInitializedAsync projection — without it,
        // FindAll runs before the role-filter completes and asserts a partial row count
        // (W6 — checker flagged race in PATTERNS.md).
        cut.WaitForAssertion(() =>
            Assert.Equal(expectedCount, cut.FindAll("button.palette-row").Count));

        var rows = cut.FindAll("button.palette-row");
        var superOnlyIds = AdminCommandRegistry.AllCommands
            .Where(c => c.RequiresSuperadmin)
            .Select(c => c.Id)
            .ToArray();
        foreach (var id in superOnlyIds)
        {
            Assert.DoesNotContain(rows, r => r.GetAttribute("data-command-id") == id);
        }
    }

    [Fact]
    [Trait("Category", "Component")]
    public void SuperadminRole_SeesAllRows()
    {
        AddAuthorization().SetAuthorized("test-superadmin");
        AddAuthorizationClaims(this, AdminRoles.Superadmin);

        var cut = Render<CommandPalette>();

        // WaitForAssertion absorbs the async OnInitializedAsync projection (W6).
        cut.WaitForAssertion(() =>
            Assert.Equal(
                AdminCommandRegistry.AllCommands.Count,
                cut.FindAll("button.palette-row").Count));
    }

    // -------------------------------------------------------------------------
    // Gap-closure 03.1-10: registry invariants for nav.* routing (BLOCKER-04)
    // -------------------------------------------------------------------------

    /// <summary>
    /// GAP-1 gap-closure: every nav.* row in the registry must declare a non-null Url
    /// that starts with "/admin" so the palette JS can route via window.location.href.
    /// Pure unit test — no bUnit rendering required.
    /// </summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void Registry_NavRows_AllHaveAbsoluteAdminUrl()
    {
        var navRows = AdminCommandRegistry.AllCommands
            .Where(c => c.Id.StartsWith("nav.", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(navRows);
        foreach (var row in navRows)
        {
            Assert.False(
                string.IsNullOrEmpty(row.Url),
                $"nav.* row '{row.Id}' must declare a non-null Url for palette routing.");
            Assert.StartsWith("/admin", row.Url, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// REVIEW-04 regression guard: nav.player-detail was removed from the registry
    /// (meaningless without a target; had RequiresTarget: false). Ensure it never
    /// reappears.
    /// </summary>
    [Fact]
    [Trait("Category", "Unit")]
    public void Registry_DoesNotContainPlayerDetailNavRow()
    {
        Assert.DoesNotContain(
            AdminCommandRegistry.AllCommands,
            c => c.Id == "nav.player-detail");
    }

    /// <summary>
    /// GAP-1 gap-closure (bUnit): the CommandPalette SSR markup emits a non-empty
    /// <c>data-url</c> attribute on every <c>button.palette-row</c> whose
    /// <c>data-command-id</c> starts with <c>"nav."</c>.
    /// </summary>
    [Fact]
    [Trait("Category", "Component")]
    public void Palette_RendersDataUrlOnNavRows()
    {
        AddAuthorization().SetAuthorized("test-superadmin");
        AddAuthorizationClaims(this, AdminRoles.Superadmin);

        var cut = Render<CommandPalette>();

        // WaitForAssertion absorbs the async OnInitializedAsync projection (W6).
        cut.WaitForAssertion(() =>
        {
            var navButtons = cut.FindAll("button.palette-row")
                .Where(b => (b.GetAttribute("data-command-id") ?? string.Empty)
                             .StartsWith("nav.", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(navButtons);
            foreach (var btn in navButtons)
            {
                var dataUrl = btn.GetAttribute("data-url");
                Assert.False(
                    string.IsNullOrEmpty(dataUrl),
                    $"nav row '{btn.GetAttribute("data-command-id")}' is missing data-url");
                Assert.StartsWith("/admin", dataUrl, StringComparison.Ordinal);
            }
        });
    }

    /// <summary>
    /// Helper: re-runs <see cref="BunitContext.AddAuthorization"/> to obtain the live
    /// <c>BunitAuthorizationContext</c> and sets a single role claim (matching the shape
    /// produced by <c>AdminAuthService.VerifyPasswordAsync</c> — see <c>AdminEndpoints.SignInCoreAsync</c>).
    /// </summary>
    private static void AddAuthorizationClaims(BunitContext ctx, string role)
    {
        ctx.AddAuthorization().SetClaims(new Claim(ClaimTypes.Role, role));
    }
}
