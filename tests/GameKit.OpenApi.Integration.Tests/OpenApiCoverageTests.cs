// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.OpenApi.Integration.Tests;

/// <summary>
/// D-09 contract test for OPEN-01 — enumerates the host's
/// <see cref="EndpointDataSource"/> and asserts every non-admin endpoint
/// is described in <c>/openapi/v1.json</c>. Empirically validates the
/// invariant that "future endpoint additions are auto-covered" by failing
/// loudly when a developer forgets to add OpenAPI-compatible metadata.
/// </summary>
/// <remarks>
/// <para>
/// Filter list — skips paths that are intentionally NOT in the document:
/// <list type="bullet">
///   <item><c>/admin/*</c> — admin surface excluded by D-19 ShouldInclude.</item>
///   <item><c>/openapi/*</c> — the doc itself.</item>
///   <item><c>/_blazor/*</c> + <c>/_framework/*</c> + <c>/_content/*</c> — Blazor static-asset routes.</item>
/// </list>
/// </para>
/// </remarks>
[Collection("OpenApi")]
[Trait("Category", "Integration")]
public sealed class OpenApiCoverageTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public OpenApiCoverageTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact]
    public async Task Every_NonAdmin_Endpoint_Is_In_OpenApi_Document()
    {
        await using var app = new OpenApiTestApp();
        await app.StartAsync(_pg, _redis);

        // 1. Enumerate routes from the host's EndpointDataSource(s).
        var sources = app.Services.GetServices<EndpointDataSource>();
        var registered = new List<(string Method, string Path)>();
        foreach (var source in sources)
        {
            foreach (var ep in source.Endpoints.OfType<RouteEndpoint>())
            {
                var raw = ep.RoutePattern.RawText;
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                var path = NormalizeRoutePattern(raw);

                if (IsFilteredPath(path))
                {
                    continue;
                }

                var methodMeta = ep.Metadata.GetMetadata<HttpMethodMetadata>();
                if (methodMeta is null || methodMeta.HttpMethods.Count == 0)
                {
                    continue;
                }
                foreach (var method in methodMeta.HttpMethods)
                {
                    registered.Add((method.ToUpperInvariant(), path));
                }
            }
        }

        Assert.NotEmpty(registered);

        // 2. Fetch /openapi/v1.json and parse.
        var resp = await app.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("paths", out var pathsEl),
            "OpenAPI document is missing `paths` element. Body: " + json);

        // Build a lookup of path → set of methods present in the document.
        var documented = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pathProp in pathsEl.EnumerateObject())
        {
            var methodSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var methodProp in pathProp.Value.EnumerateObject())
            {
                methodSet.Add(methodProp.Name.ToUpperInvariant());
            }
            documented[pathProp.Name] = methodSet;
        }

        // 3. For every enumerated endpoint, assert the (method, path) tuple is documented.
        var missing = new List<string>();
        foreach (var (method, path) in registered.Distinct())
        {
            if (!documented.TryGetValue(path, out var methods) || !methods.Contains(method))
            {
                missing.Add($"{method} {path}");
            }
        }

        Assert.True(missing.Count == 0,
            "OpenAPI document is missing the following non-admin endpoints registered in the host:" +
            Environment.NewLine + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// Skip predicate — paths that are intentionally NOT in /openapi/v1.json.
    /// Matches the D-19 filter literal ("admin" without trailing slash so the
    /// bare /admin Blazor root is also caught) + Blazor / Razor Class Library
    /// content prefixes that the host registers but are not first-class API endpoints.
    /// </summary>
    private static bool IsFilteredPath(string path)
    {
        // Admin filter — verbatim mirror of D-19's StartsWith("admin", OrdinalIgnoreCase) check
        // applied after the leading slash is trimmed.
        var noLead = path.TrimStart('/');
        if (noLead.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // The OpenAPI doc itself.
        if (noLead.StartsWith("openapi", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // Blazor framework routes — registered but not API surface.
        if (noLead.StartsWith("_blazor", StringComparison.Ordinal) ||
            noLead.StartsWith("_framework", StringComparison.Ordinal) ||
            noLead.StartsWith("_content", StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Normalize an ASP.NET Core route pattern to match the OpenAPI document's
    /// path representation:
    /// <list type="bullet">
    ///   <item>Strip route-constraint suffixes — <c>{id:guid}</c> → <c>{id}</c></item>
    ///   <item>Trim a trailing slash on non-root paths — <c>/api/players/</c> → <c>/api/players</c></item>
    ///   <item>Ensure the path is leading-slashed.</item>
    /// </list>
    /// </summary>
    private static string NormalizeRoutePattern(string raw)
    {
        var path = raw.StartsWith('/') ? raw : "/" + raw;
        // Strip route constraints like {id:guid}, {ticketId:guid}, {count:int}, etc.
        path = RouteConstraintRegex.Replace(path, "{$1}");
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path[..^1];
        }
        return path;
    }

    private static readonly Regex RouteConstraintRegex = new(
        @"\{([A-Za-z_][A-Za-z0-9_]*)(?::[^}]+)?\}",
        RegexOptions.Compiled);
}
