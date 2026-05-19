// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using GameKit.Matchmaking;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameKit.Matchmaking.Tests.Builder;

/// <summary>
/// Validates that <see cref="GameKitMatchmakingOptions"/> rejects misconfigured values at
/// host startup (via <c>ValidateOnStart()</c>). Each test exercises exactly one rule from
/// the plan body's behavior contract:
/// <list type="bullet">
///   <item>AcceptTimeoutSeconds &gt;= 1 (D-07)</item>
///   <item>MatchmakingEnqueueRatePerMinute &gt;= 1 (RESEARCH §Decision 10)</item>
///   <item>Analytics.ChannelCapacity &gt;= 100 (RESEARCH §Decision 7)</item>
///   <item>Reconciler.SweepIntervalSeconds &gt;= 5 (RESEARCH §Decision 6)</item>
///   <item>Ticker.TickIntervalMs &gt;= 1 (defensive — RESEARCH §Architecture diagram)</item>
///   <item>Ticker.LockTtlSeconds &gt;= 1 (defensive)</item>
///   <item>TicketRetentionDays &gt;= 1 (D-17)</item>
///   <item>Cooldown.WindowMinutes &gt;= 1 (D-08)</item>
///   <item>Default options validate successfully (sanity check)</item>
/// </list>
/// </summary>
public sealed class MatchmakingOptionsValidationTests
{
    [Fact]
    public void Default_Options_Pass_Validation()
    {
        var services = BuildServices(_ => { });
        // Resolving IOptions<T> with ValidateOnStart triggers validation eagerly via host startup;
        // for a unit-level assertion we resolve the value and trip the IValidateOptions explicitly
        // by retrieving the options instance.
        using var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<GameKitMatchmakingOptions>>().Value;
        Assert.Equal(10, opts.AcceptTimeoutSeconds);
        Assert.Equal(5, opts.MatchmakingEnqueueRatePerMinute);
        Assert.Equal(30, opts.TicketRetentionDays);
        Assert.Equal(500, opts.Ticker.TickIntervalMs);
        Assert.Equal(90, opts.Ticker.LockTtlSeconds);
        Assert.Equal(50, opts.Ticker.MaxIterationBudgetMs);
        Assert.Equal("gamekit:matchmaking:matcher:lock", opts.Ticker.LockKey);
        Assert.Equal(60, opts.Cooldown.WindowMinutes);
        Assert.Equal(3, opts.Cooldown.Step1Minutes);
        Assert.Equal(15, opts.Cooldown.Step2Minutes);
        Assert.Equal(30, opts.Cooldown.Step3Minutes);
        Assert.Equal(10_000, opts.Analytics.ChannelCapacity);
        Assert.Equal(100, opts.Analytics.DrainBatchSize);
        Assert.Equal(5, opts.Analytics.DrainIntervalSeconds);
        Assert.Equal(4, opts.Analytics.PollyMaxRetryAttempts);
        Assert.Equal(500, opts.Analytics.PollyBaseDelayMs);
        Assert.Equal(30, opts.Analytics.PollyTimeoutSeconds);
        Assert.Equal(30, opts.Reconciler.SweepIntervalSeconds);
        Assert.Equal(5, opts.Reconciler.StaleTicketThresholdMinutes);
        Assert.Equal(10, opts.Reconciler.OrphanSessionThresholdMinutes);
        Assert.True(opts.Reconciler.LeaderOnly);
    }

    [Fact]
    public void AcceptTimeoutSeconds_Below_One_Throws()
    {
        var services = BuildServices(o => o.AcceptTimeoutSeconds = 0);
        AssertValidationFails(services, nameof(GameKitMatchmakingOptions.AcceptTimeoutSeconds));
    }

    [Fact]
    public void MatchmakingEnqueueRatePerMinute_Below_One_Throws()
    {
        var services = BuildServices(o => o.MatchmakingEnqueueRatePerMinute = 0);
        AssertValidationFails(services, nameof(GameKitMatchmakingOptions.MatchmakingEnqueueRatePerMinute));
    }

    [Fact]
    public void Analytics_ChannelCapacity_Below_One_Hundred_Throws()
    {
        var services = BuildServices(o => o.Analytics.ChannelCapacity = 99);
        AssertValidationFails(services, nameof(GameKitMatchmakingAnalyticsOptions.ChannelCapacity));
    }

    [Fact]
    public void Reconciler_SweepIntervalSeconds_Below_Five_Throws()
    {
        var services = BuildServices(o => o.Reconciler.SweepIntervalSeconds = 4);
        AssertValidationFails(services, nameof(GameKitMatchmakingReconcilerOptions.SweepIntervalSeconds));
    }

    [Fact]
    public void Ticker_TickIntervalMs_Below_One_Throws()
    {
        var services = BuildServices(o => o.Ticker.TickIntervalMs = 0);
        AssertValidationFails(services, nameof(GameKitMatchmakingTickerOptions.TickIntervalMs));
    }

    [Fact]
    public void Ticker_LockTtlSeconds_Below_One_Throws()
    {
        var services = BuildServices(o => o.Ticker.LockTtlSeconds = 0);
        AssertValidationFails(services, nameof(GameKitMatchmakingTickerOptions.LockTtlSeconds));
    }

    [Fact]
    public void TicketRetentionDays_Below_One_Throws()
    {
        var services = BuildServices(o => o.TicketRetentionDays = 0);
        AssertValidationFails(services, nameof(GameKitMatchmakingOptions.TicketRetentionDays));
    }

    [Fact]
    public void Cooldown_WindowMinutes_Below_One_Throws()
    {
        var services = BuildServices(o => o.Cooldown.WindowMinutes = 0);
        AssertValidationFails(services, nameof(GameKitMatchmakingCooldownOptions.WindowMinutes));
    }

    private static IServiceCollection BuildServices(Action<GameKitMatchmakingOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions<GameKitMatchmakingOptions>()
            .Configure(configure)
            .Validate(opts => MatchmakingOptionsValidator.Validate(opts, out _),
                "GameKitMatchmakingOptions failed validation.");
        services.AddSingleton<IValidateOptions<GameKitMatchmakingOptions>, MatchmakingOptionsValidator>();
        return services;
    }

    private static void AssertValidationFails(IServiceCollection services, string fieldHint)
    {
        using var sp = services.BuildServiceProvider();
        var ex = Assert.Throws<OptionsValidationException>(() =>
            sp.GetRequiredService<IOptions<GameKitMatchmakingOptions>>().Value);
        // Surface useful diagnostic text — the exception message should mention the offending field.
        Assert.Contains(fieldHint, ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
