// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Polly;
using Polly.Retry;

namespace GameKit.Rankings.Services;

/// <summary>
/// Shared Polly v8 retry pipeline for Postgres <c>40001 serialization_failure</c> errors
/// thrown by SERIALIZABLE transactions (CR-03 / D-19).
/// </summary>
/// <remarks>
/// <para>
/// Three SERIALIZABLE services share the same retry semantics:
/// <see cref="RankAdjustService"/>, <see cref="EndSeasonService"/>, and
/// <see cref="StartupLadderUpserter"/>. Without retry, two concurrent admin operations
/// targeting the same row produce a 500 (the exception bubbles past endpoint handlers
/// that only catch <see cref="KeyNotFoundException"/> and <see cref="ArgumentOutOfRangeException"/>).
/// </para>
/// <para>
/// The pipeline retries up to 3 times with exponential backoff and jitter when:
/// <list type="bullet">
/// <item><description>A <see cref="DbUpdateException"/> wraps a <see cref="PostgresException"/> with <c>SqlState == "40001"</c>.</description></item>
/// <item><description>A bare <see cref="PostgresException"/> with <c>SqlState == "40001"</c> escapes.</description></item>
/// </list>
/// </para>
/// </remarks>
internal static class SerializationFailureRetry
{
    /// <summary>
    /// Builds a Polly resilience pipeline that retries Postgres 40001 errors.
    /// </summary>
    /// <param name="logger">Logger used to record retry attempts; may be null for design-time uses.</param>
    /// <param name="operationName">Short identifier included in retry warning logs (e.g. "RankAdjust").</param>
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
