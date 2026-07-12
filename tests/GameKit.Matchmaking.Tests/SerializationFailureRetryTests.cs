// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Matchmaking.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace GameKit.Matchmaking.Tests;

/// <summary>
/// Unit tests for the <see cref="SerializationFailureRetry"/> Polly pipeline. The regression of
/// interest: a Postgres <c>40001</c> can reach the pipeline wrapped by EF's execution strategy in
/// an <see cref="InvalidOperationException"/> ("...likely due to a transient failure") rather than
/// as a bare <c>PostgresException</c> / <c>DbUpdateException</c>. The pipeline must still retry it,
/// otherwise concurrent SERIALIZABLE operations (e.g. PartyService.CreateAsync) leak the raw error.
/// </summary>
public sealed class SerializationFailureRetryTests
{
    private static PostgresException SerializationFailure() =>
        new("could not serialize access due to read/write dependencies among transactions",
            "ERROR", "ERROR", "40001");

    [Fact]
    public async Task Retries_When_40001_Is_Wrapped_By_Execution_Strategy()
    {
        var pipeline = SerializationFailureRetry.Build(logger: null, operationName: "test");
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 3)
            {
                // EF execution strategy → DbUpdateException → PostgresException(40001).
                throw new InvalidOperationException(
                    "An exception has been raised that is likely due to a transient failure.",
                    new DbUpdateException("An error occurred while saving the entity changes.",
                        SerializationFailure()));
            }

            await Task.CompletedTask;
            return "ok";
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(3, attempts); // 2 retries + the succeeding attempt
    }

    [Fact]
    public async Task Retries_When_40001_Is_A_Bare_DbUpdateException()
    {
        var pipeline = SerializationFailureRetry.Build(logger: null, operationName: "test");
        var attempts = 0;

        var result = await pipeline.ExecuteAsync(async _ =>
        {
            attempts++;
            if (attempts < 2)
            {
                throw new DbUpdateException("save failed", SerializationFailure());
            }

            await Task.CompletedTask;
            return "ok";
        }, CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Does_Not_Retry_Unrelated_InvalidOperationException()
    {
        var pipeline = SerializationFailureRetry.Build(logger: null, operationName: "test");
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pipeline.ExecuteAsync(async _ =>
            {
                attempts++;
                await Task.CompletedTask;
                throw new InvalidOperationException("not a serialization failure");
            }, CancellationToken.None));

        Assert.Equal(1, attempts); // no retry for a non-40001 error
    }
}
