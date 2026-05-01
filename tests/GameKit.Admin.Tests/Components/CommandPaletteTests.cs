// SPDX-License-Identifier: GPL-3.0-or-later
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
