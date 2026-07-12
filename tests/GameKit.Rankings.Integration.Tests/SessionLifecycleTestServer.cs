// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net.Http;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Data;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Data;
using StackExchange.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameKit.Rankings.Integration.Tests;

/// <summary>
/// In-process <see cref="TestServer"/> for the session-lifecycle endpoint integration tests
/// (<c>/start</c> + <c>/abandon</c> — Phase 6 PRES-05, D-20). Mirrors
/// <see cref="SessionCompleteTestServer"/> from Plan 04-05 with the same auth + rate-limit
/// + DbContext model-customizer wiring.
/// </summary>
internal sealed class SessionLifecycleTestServer : IAsyncDisposable
{
    private readonly IHost _host;

    private SessionLifecycleTestServer(IHost host) => _host = host;

    public IServiceProvider Services => _host.Services;

    public HttpClient CreateClient() => _host.GetTestServer().CreateClient();

    public static async Task<SessionLifecycleTestServer> CreateAsync(
        string cs, string ladderName, string redisCs)
    {
        var builder = new HostBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureLogging(l => l.SetMinimumLevel(LogLevel.Warning))
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();
                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();

                    services
                        .AddGameKit(o => { o.ConnectionString = cs; o.AutoMigrate = false; })
                        .AddRankings(o => { })
                        .AddLadder(ladderName);

                    services.AddLogging();

                    // Override DbContext to include Rankings entities (bypass global EF model cache).
                    services.AddDbContext<GameKitDbContext>((_, dbOpts) =>
                        dbOpts
                            .UseNpgsql(cs)
                            .ReplaceService<IModelCustomizer, SessionLifecycleTestModelCustomizer>()
                            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));

                    // AddRankings (via the ticker + lease helper) requires IConnectionMultiplexer.
                    // The /start + /abandon happy-path doesn't touch Redis at all (no observers
                    // are registered in this Rankings-only test host), but the ticker BackgroundService
                    // resolves the multiplexer at startup — wire it to the shared Testcontainer.
                    services.AddSingleton<IConnectionMultiplexer>(_ =>
                        ConnectionMultiplexer.Connect(redisCs));
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();
                    app.UseRateLimiter();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGameKit();
                    });
                });
            });

        var host = builder.Build();
        await host.StartAsync();
        return new SessionLifecycleTestServer(host);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}

/// <summary>
/// Test-only model customizer that applies Rankings entities (bypasses EF global cache — Pitfall 3).
/// Mirrors <c>SessionCompleteTestModelCustomizer</c>.
/// </summary>
internal sealed class SessionLifecycleTestModelCustomizer : RelationalModelCustomizer
{
    public SessionLifecycleTestModelCustomizer(ModelCustomizerDependencies dependencies)
        : base(dependencies) { }

    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        new RankingsModelBuilderExtension().ApplyTo(modelBuilder);
    }
}
