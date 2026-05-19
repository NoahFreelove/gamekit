// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Matchmaking;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Strategy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameKit.Matchmaking.Tests.Builder;

/// <summary>
/// Verifies that the <c>services.AddGameKit(...).AddMatchmaking(...).AddLadder(...)</c>
/// fluent chain compiles and executes as documented in plan 05-03 success criteria
/// ("a downstream consumer's Program.cs can compile <c>AddMatchmaking()</c>").
/// </summary>
public sealed class AddMatchmakingFluentChainTests
{
    [Fact]
    public void AddMatchmaking_With_AddLadder_Registers_Options_And_Builder()
    {
        var services = new ServiceCollection();
        // Mimic the consumer pattern: IGameKitBuilder hand-rolled (avoids the full Core
        // hosting bootstrap; the Phase-3 Rankings tests use the same lightweight surface).
        var fakeBuilder = new TestGameKitBuilder(services);

        var mmBuilder = fakeBuilder
            .AddMatchmaking(opts =>
            {
                opts.Ticker.TickIntervalMs = 250; // override default
            })
            .AddLadder("main", c =>
            {
                c.BracketRampSeconds = 60;
                c.PartyRatingAggregator = PartyRatingAggregator.GlickoWeighted;
            })
            .AddLadder("tournament");

        Assert.NotNull(mmBuilder);
        Assert.Equal(2, mmBuilder.RegisteredLadders.Count);

        using var sp = services.BuildServiceProvider();

        // Options are bound + validator runs successfully (default + override applied).
        var opts = sp.GetRequiredService<IOptions<GameKitMatchmakingOptions>>().Value;
        Assert.Equal(250, opts.Ticker.TickIntervalMs);
        Assert.Equal(10, opts.AcceptTimeoutSeconds); // untouched default

        // The matchmaking builder + ladder list singletons are both registered.
        var resolvedBuilder = sp.GetRequiredService<IGameKitMatchmakingBuilder>();
        Assert.Same(mmBuilder, resolvedBuilder);

        var ladders = sp.GetRequiredService<IReadOnlyList<MatchmakingLadderConfig>>();
        Assert.Equal(2, ladders.Count);
        Assert.Equal("main", ladders[0].Name);
        Assert.Equal(60, ladders[0].BracketRampSeconds);
        Assert.Equal(PartyRatingAggregator.GlickoWeighted, ladders[0].PartyRatingAggregator);
        Assert.Equal("tournament", ladders[1].Name);
    }

    [Fact]
    public void MapMatchmaking_Returns_Same_RouteBuilder()
    {
        // Plan 05-08 wires MapMatchmaking to actually map the party + matchmaking endpoint
        // groups. This test verifies the return identity contract — consumers can chain
        // `app.MapMatchmaking().MapMyOwnRoutes()` without rebinding.
        //
        // The endpoint mapping itself is exercised end-to-end by the integration tests
        // (Party/LongPoll/HappyPath); this unit test pins the IEndpointRouteBuilder return
        // identity invariant.
        var routes = new TestEndpointRouteBuilder();
        var returned = routes.MapMatchmaking();
        Assert.Same(routes, returned);
        // After Plan 05-08 lands, MapMatchmaking adds endpoint groups — DataSources is
        // populated. The exact count is implementation-detail-fragile; the invariant
        // worth pinning is "at least one mapped".
        Assert.NotEmpty(routes.DataSources);
    }

    /// <summary>Test double for <see cref="IGameKitBuilder"/> — avoids the full <c>AddGameKit</c> bootstrap.</summary>
    private sealed class TestGameKitBuilder : IGameKitBuilder
    {
        public TestGameKitBuilder(IServiceCollection services)
        {
            Services = services;
            Options = new GameKitOptions { ConnectionString = "Host=localhost;Database=test;Username=t;Password=t" };
        }

        public IServiceCollection Services { get; }
        public GameKitOptions Options { get; }
    }

    /// <summary>Test double for <see cref="Microsoft.AspNetCore.Routing.IEndpointRouteBuilder"/>.</summary>
    private sealed class TestEndpointRouteBuilder : Microsoft.AspNetCore.Routing.IEndpointRouteBuilder
    {
        private readonly ServiceCollection _services = new();

        public TestEndpointRouteBuilder()
        {
            // MatchmakingEndpoints.MapMatchmakingEndpoints needs IGameKitRateLimitPolicies
            // to resolve the per-endpoint rate-limit policy names at registration time.
            // The TestEndpointRouteBuilder lives outside AddGameKit's full DI bootstrap so
            // we supply the canonical default policies directly.
            _services.AddSingleton<GameKit.Core.RateLimiting.IGameKitRateLimitPolicies,
                GameKit.Core.RateLimiting.GameKitRateLimitPolicies>();
            ServiceProvider = _services.BuildServiceProvider();
        }

        public Microsoft.AspNetCore.Builder.IApplicationBuilder CreateApplicationBuilder() =>
            throw new System.NotSupportedException("Not needed for MapMatchmaking() return-identity test.");

        public ICollection<Microsoft.AspNetCore.Routing.EndpointDataSource> DataSources { get; } =
            new List<Microsoft.AspNetCore.Routing.EndpointDataSource>();

        public System.IServiceProvider ServiceProvider { get; }
    }
}
