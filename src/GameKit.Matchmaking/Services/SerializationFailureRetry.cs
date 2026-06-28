// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.Retry;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Polly v8 retry pipeline for Postgres <c>40001 serialization_failure</c> errors thrown
/// by SERIALIZABLE transactions in <see cref="PartyService"/>. Mirrors the analogous
/// helper in <c>GameKit.Rankings.Services</c> (Phase 4 / RANK-03).
/// </summary>
/// <remarks>
/// <para>
/// SERIALIZABLE transactions in Postgres can fail with <c>40001</c> at COMMIT time when
/// the planner detects a serialization anomaly. Retrying after a small backoff almost
/// always succeeds. This pipeline retries up to 3 times with exponential backoff +
/// jitter. Without retry, two concurrent party-create / party-join operations targeting
/// the same player race condition produces an HTTP 500 (the exception bubbles past
/// endpoint handlers that catch <see cref="PartyConflictException"/> only).
/// </para>
/// </remarks>
internal static class SerializationFailureRetry
{
    /// <summary>Builds the Polly retry pipeline.</summary>
    /// <param name="logger">Logger used to record retry attempts; may be null for design-time uses.</param>
    /// <param name="operationName">Short identifier included in retry warning logs (e.g. "PartyCreate").</param>
    /// <returns>The configured <see cref="ResiliencePipeline"/>.</returns>
    public static ResiliencePipeline Build(ILogger? logger, string operationName)
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                // 40001 can arrive bare, wrapped by SaveChanges in a DbUpdateException, or
                // further wrapped by EF's execution strategy in an InvalidOperationException
                // ("...likely due to a transient failure") — handle all three forms.
                ShouldHandle = new PredicateBuilder()
                    .Handle<DbUpdateException>(ex =>
                        ex.InnerException is PostgresException { SqlState: "40001" })
                    .Handle<PostgresException>(ex => ex.SqlState == "40001")
                    .Handle<InvalidOperationException>(ex => IsSerializationFailure(ex.InnerException)),
                OnRetry = args =>
                {
                    logger?.LogWarning(
                        args.Outcome.Exception,
                        "{Operation}: Postgres serialization_failure (40001) retry {Attempt} after {Delay}ms.",
                        operationName,
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                },
            })
            .Build();
    }

    /// <summary>
    /// True if a Postgres <c>40001 serialization_failure</c> appears anywhere in the given
    /// exception's inner-exception chain. EF's execution strategy re-wraps the transient 40001 in
    /// an <see cref="InvalidOperationException"/> ("...likely due to a transient failure"), so the
    /// retry predicate must see through that outer layer or it never fires — and the failure
    /// escapes as an unhandled exception under concurrent SERIALIZABLE access.
    /// </summary>
    private static bool IsSerializationFailure(Exception? exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is PostgresException { SqlState: "40001" })
            {
                return true;
            }
        }

        return false;
    }
}
