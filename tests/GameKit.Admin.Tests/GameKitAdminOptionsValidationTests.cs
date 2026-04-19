// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
using System;
using GameKit.Admin.UI;
using GameKit.Admin.UI.Authentication;
using GameKit.Admin.UI.Authorization;
using Xunit;

namespace GameKit.Admin.Tests;

public class GameKitAdminOptionsValidationTests
{
    [Fact]
    public void Defaults_Match_Context_And_Research()
    {
        var opts = new GameKitAdminOptions();
        Assert.Equal("/admin", opts.MountPath);
        Assert.Equal("gk_admin_session", opts.Cookie.Name);
        Assert.Equal(TimeSpan.FromHours(8), opts.Cookie.ExpireTimeSpan);
        Assert.True(opts.Cookie.SlidingExpiration);
        Assert.Equal(TimeSpan.FromDays(30), opts.Cookie.RememberMeDuration);
        Assert.Equal(TimeSpan.FromSeconds(10), opts.Panel.RefreshInterval);
        Assert.Equal(TimeSpan.FromMinutes(5), opts.Panel.HealthErrorRateWindow);
        Assert.Equal(TimeSpan.FromSeconds(1), opts.Panel.HealthErrorRateBucketSize);
        Assert.False(opts.Csp.ReportOnly);
    }

    [Fact]
    public void AdminRoles_Are_PinnedStrings()
    {
        Assert.Equal("admin", AdminRoles.Admin);
        Assert.Equal("superadmin", AdminRoles.Superadmin);
    }

    [Fact]
    public void AdminAuthenticationScheme_Constants_Are_PinnedStrings()
    {
        Assert.Equal("GameKitAdmin", AdminAuthenticationSchemeConstants.Scheme);
        Assert.Equal("gk_admin_session", AdminAuthenticationSchemeConstants.CookieName);
        Assert.Equal("X-GameKit-Admin-CSRF", AdminAuthenticationSchemeConstants.CsrfHeaderName);
        Assert.Equal("gk_admin_csrf", AdminAuthenticationSchemeConstants.CsrfCookieName);
    }

    [Fact]
    public void AdminPolicies_Are_PinnedStrings()
    {
        Assert.Equal("gamekit.admin.admin", AdminPolicies.Admin);
        Assert.Equal("gamekit.admin.superadmin", AdminPolicies.Superadmin);
    }
}
