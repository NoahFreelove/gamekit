// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.RateLimiting;
using Xunit;

namespace GameKit.Core.Tests.RateLimiting;

public class GameKitRateLimitPoliciesTests
{
    [Fact]
    public void AuthLogin_IsCorrectPolicyName()
    {
        var policies = new GameKitRateLimitPolicies();
        Assert.Equal("gamekit:auth:login", policies.AuthLogin);
    }

    [Fact]
    public void AuthRefresh_IsCorrectPolicyName()
    {
        var policies = new GameKitRateLimitPolicies();
        Assert.Equal("gamekit:auth:refresh", policies.AuthRefresh);
    }

    [Fact]
    public void AuthRegister_IsCorrectPolicyName()
    {
        var policies = new GameKitRateLimitPolicies();
        Assert.Equal("gamekit:auth:register", policies.AuthRegister);
    }

    [Fact]
    public void MmEnqueue_IsCorrectPolicyName()
    {
        var policies = new GameKitRateLimitPolicies();
        Assert.Equal("gamekit:mm:enqueue", policies.MmEnqueue);
    }

    [Fact]
    public void PresenceHeartbeat_IsCorrectPolicyName()
    {
        var policies = new GameKitRateLimitPolicies();
        Assert.Equal("gamekit:presence:heartbeat", policies.PresenceHeartbeat);
    }

    [Fact]
    public void Constants_MatchInstanceProperties()
    {
        var policies = new GameKitRateLimitPolicies();
        Assert.Equal(GameKitRateLimitPolicies.AuthLoginPolicy, policies.AuthLogin);
        Assert.Equal(GameKitRateLimitPolicies.AuthRefreshPolicy, policies.AuthRefresh);
        Assert.Equal(GameKitRateLimitPolicies.AuthRegisterPolicy, policies.AuthRegister);
        Assert.Equal(GameKitRateLimitPolicies.MmEnqueuePolicy, policies.MmEnqueue);
        Assert.Equal(GameKitRateLimitPolicies.PresenceHeartbeatPolicy, policies.PresenceHeartbeat);
    }
}
