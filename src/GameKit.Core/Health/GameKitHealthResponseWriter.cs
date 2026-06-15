// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GameKit.Core.Health;

/// <summary>
/// Custom health-check response writer that emits a whitelist-only JSON payload (D-12 / HLTH-05).
/// </summary>
/// <remarks>
/// <para>
/// Emits only <c>{ "status": "…", "checks": [ { "name": "…", "status": "…", "description": "…" } ] }</c>.
/// <see cref="HealthReportEntry.Exception"/>, <see cref="HealthReportEntry.Data"/>, and
/// <see cref="HealthReportEntry.Tags"/> are <em>intentionally omitted</em> — the default ASP.NET
/// Core writer would include these fields, allowing Npgsql-embedded <c>host:port</c> strings
/// and other infra details to leak into unauthenticated health payloads.
/// </para>
/// <para>
/// Uses <see cref="Utf8JsonWriter"/> over <see cref="MemoryStream"/> for efficient, low-allocation
/// JSON serialization without pulling in <c>System.Text.Json.JsonSerializer</c> (which would
/// transitively serialize the <c>Exception</c> property).
/// </para>
/// <para>
/// Descriptions are hand-authored constants in each <see cref="IHealthCheck"/> implementation.
/// </para>
/// </remarks>
internal static class GameKitHealthResponseWriter
{
    /// <summary>
    /// Writes the health report as a JSON object containing <c>status</c> and a <c>checks</c>
    /// array. Compatible with <see cref="Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions.ResponseWriter"/>.
    /// </summary>
    /// <param name="ctx">The HTTP context for the current request.</param>
    /// <param name="report">The aggregated health report from all registered checks.</param>
    /// <returns>A task that completes once the response body has been written.</returns>
    internal static Task WriteAsync(HttpContext ctx, HealthReport report)
    {
        ctx.Response.ContentType = "application/json; charset=utf-8";

        var options = new JsonWriterOptions { Indented = false };
        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms, options))
        {
            writer.WriteStartObject();
            writer.WriteString("status", report.Status.ToString());
            writer.WriteStartArray("checks");

            foreach (var (name, entry) in report.Entries)
            {
                writer.WriteStartObject();
                writer.WriteString("name", name);
                writer.WriteString("status", entry.Status.ToString());
                // D-12: description is the only additional field per check.
                // Exception, Data, and Tags are intentionally OMITTED to prevent
                // infra-detail leakage (HLTH-05: Npgsql embeds host:port in exceptions).
                writer.WriteString("description", entry.Description ?? string.Empty);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return ctx.Response.WriteAsync(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
