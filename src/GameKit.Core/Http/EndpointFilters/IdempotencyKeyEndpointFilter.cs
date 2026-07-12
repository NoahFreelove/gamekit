// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace GameKit.Core.Http.EndpointFilters;

/// <summary>
/// Generic <see cref="IEndpointFilter"/> that validates the presence and length of the
/// <c>Idempotency-Key</c> header (D-08 / T-04-05-MK).
/// </summary>
/// <remarks>
/// <para>
/// This is a generic Core primitive. Any Core or downstream-package endpoint can opt in via
/// <c>.AddEndpointFilter&lt;IdempotencyKeyEndpointFilter&gt;()</c>. When the header is present
/// and within the configured bounds, the validated value is stored on
/// <c>HttpContext.Items["GameKit.IdempotencyKey"]</c> so the downstream handler can access it
/// without re-reading the header.
/// </para>
/// <para>
/// Rejection produces a 400 Bad Request with a machine-readable JSON body:
/// <c>{ "error": "idempotency_key_required" }</c> (T-04-05-MK mitigation).
/// </para>
/// </remarks>
public sealed class IdempotencyKeyEndpointFilter : IEndpointFilter
{
    /// <summary>The <c>HttpContext.Items</c> key under which the validated idempotency key is stored.</summary>
    public const string ItemsKey = "GameKit.IdempotencyKey";

    /// <summary>Header name expected on the incoming request.</summary>
    public const string HeaderName = "Idempotency-Key";

    /// <summary>Minimum accepted key length (inclusive).</summary>
    public const int MinLength = 8;

    /// <summary>Maximum accepted key length (inclusive).</summary>
    public const int MaxLength = 128;

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var key = ctx.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrWhiteSpace(key) || key.Length < MinLength || key.Length > MaxLength)
        {
            return Results.Json(
                new { error = "idempotency_key_required" },
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Store on Items so the endpoint handler can retrieve it without re-reading the header.
        ctx.HttpContext.Items[ItemsKey] = key;

        return await next(ctx).ConfigureAwait(false);
    }
}
