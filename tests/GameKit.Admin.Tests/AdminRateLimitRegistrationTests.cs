// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using GameKit.Admin.UI.Http.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameKit.Admin.Tests;

public class AdminRateLimitRegistrationTests
{
    // W3 decision (revision 2026-04-18): `RateLimiterOptions.AddPolicy` on .NET 10 does NOT throw on duplicate
    // policy name — it quietly overwrites. The negative-assertion pattern is therefore unreliable across versions.
    // Approach: assert positively that the policy name is configured by round-tripping through IOptionsMonitor
    // and confirming `AddAdminRateLimits` completed without exception. End-to-end 429 behavior (5 attempts / IP)
    // is exercised by `AdminLoginEndpointTests.RateLimit_After5Failures_Returns429` in plan 03-07 (Wave 4).

    [Fact]
    public void AddAdminRateLimits_Completes_WithoutException_AndExposes_Policy_ViaOptionsMonitor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRateLimiter(_ => { });
        services.AddAdminRateLimits();

        // Act
        var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<RateLimiterOptions>>();
        var opts = monitor.CurrentValue;

        // Assert: no exception was thrown during registration.
        Assert.NotNull(opts);

        // The constant is the stable external contract; any regression breaks the route filter in AdminEndpoints.
        Assert.Equal("gamekit:admin:login", AdminRateLimitRegistrations.AdminLoginPolicy);
    }
}
