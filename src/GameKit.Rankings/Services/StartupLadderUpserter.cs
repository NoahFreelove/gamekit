// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Data;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Data;
using GameKit.Core.Services;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;

namespace GameKit.Rankings.Services;

/// <summary>
/// Startup <see cref="IHostedService"/> that idempotently upserts ladder rows registered
/// via <c>AddRankings().AddLadder("name", ...)</c> (D-21 / RANK-09). Runs after
/// <c>RankingsMigrationHostedService</c> (registration order guarantees ordering).
/// </summary>
/// <remarks>
/// <para>
/// Uses a SERIALIZABLE transaction to safely handle first-deploy race conditions when
/// multiple application replicas start simultaneously. The SELECT-then-INSERT pattern
/// inside SERIALIZABLE gives a serializable snapshot: if two replicas both see "no row"
/// and both attempt INSERT, Postgres serialization failure will cause one to retry;
/// on retry, the row already exists and we skip (idempotent).
/// </para>
/// <para>
/// Existing ladder rows are never updated — runtime ladder CRUD is deferred to v2
/// per D-21. Config JSONB represents the build-time defaults only.
/// </para>
/// </remarks>
public sealed class StartupLadderUpserter : IHostedService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<StartupLadderUpserter> _logger;
    private readonly ResiliencePipeline _serializationRetry;

    /// <summary>Constructs the upserter with a root service provider and logger.</summary>
    public StartupLadderUpserter(IServiceProvider sp, ILogger<StartupLadderUpserter> logger)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(logger);
        _sp = sp;
        _logger = logger;
        _serializationRetry = SerializationFailureRetry.Build(logger, nameof(StartupLadderUpserter));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();
        var rankingsBuilder = scope.ServiceProvider.GetRequiredService<IGameKitRankingsBuilder>();
        var idGenerator = scope.ServiceProvider.GetRequiredService<IIdGenerator>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var configs = rankingsBuilder.RegisteredLadders;
        if (configs.Count == 0)
        {
            _logger.LogInformation("StartupLadderUpserter: no ladders registered — skipping.");
            return;
        }

        _logger.LogInformation("StartupLadderUpserter: upserting {Count} ladder(s).", configs.Count);

        // Wrap the SERIALIZABLE transaction body in a Polly retry pipeline (CR-03) so two
        // application replicas booting concurrently do not crash the host on 40001.
        await _serializationRetry.ExecuteAsync(async ct =>
        {
            await using var tx = await ctx.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct)
                .ConfigureAwait(false);

            try
            {
                foreach (var config in configs)
                {
                    var exists = await ctx.Set<Ladder>()
                        .AnyAsync(l => l.Name == config.Name, ct)
                        .ConfigureAwait(false);

                    if (exists)
                    {
                        _logger.LogDebug(
                            "StartupLadderUpserter: ladder '{Name}' already exists — skipping insert.",
                            config.Name);
                        continue;
                    }

                    var configJson = JsonSerializer.SerializeToDocument(new
                    {
                        config.DefaultRating,
                        config.DefaultRd,
                        config.DefaultVolatility,
                        RatingPeriodSeconds = (long)config.RatingPeriod.TotalSeconds,
                        ResetPolicy = config.ResetPolicy.ToString(),
                        config.RegressionFactor,
                        config.RdCeiling,
                        config.RdBump,
                        config.MinParticipationFractionForRating,
                    });

                    var ladder = new Ladder
                    {
                        Id = idGenerator.NewId(),
                        Name = config.Name,
                        Algorithm = config.Algorithm,
                        IsActive = true,
                        Config = configJson,
                        CreatedAt = clock.UtcNow,
                    };

                    ctx.Set<Ladder>().Add(ladder);
                    _logger.LogInformation(
                        "StartupLadderUpserter: inserting ladder '{Name}' (algorithm={Algorithm}).",
                        config.Name, config.Algorithm);
                }

                await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
                await tx.CommitAsync(ct).ConfigureAwait(false);
                _logger.LogInformation("StartupLadderUpserter: completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartupLadderUpserter: failed; rolling back.");
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                throw;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
