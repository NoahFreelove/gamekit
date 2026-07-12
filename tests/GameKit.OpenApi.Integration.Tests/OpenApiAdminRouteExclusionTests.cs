// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.OpenApi.Integration.Tests;

/// <summary>
/// D-08 + D-19 contract test — asserts NO admin endpoint appears in
/// <c>/openapi/v1.json</c>. The exclusion is empirically validated:
/// the test also confirms that admin endpoints ARE registered in the
/// host (visible via <see cref="EndpointDataSource"/>), so the
/// exclusion assertion is non-vacuous (PATTERNS Critical Misuse Warning #1).
/// </summary>
[Collection("OpenApi")]
[Trait("Category", "Integration")]
public sealed class OpenApiAdminRouteExclusionTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public OpenApiAdminRouteExclusionTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact]
    public async Task No_Admin_Path_Appears_In_OpenApi_Document()
    {
        await using var app = new OpenApiTestApp();
        await app.StartAsync(_pg, _redis);

        var resp = await app.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("paths", out var paths),
            "OpenAPI doc missing `paths`: " + json);

        var leakedAdminPaths = new List<string>();
        foreach (var pathProp in paths.EnumerateObject())
        {
            var noLead = pathProp.Name.TrimStart('/');
            // Mirror D-19 verbatim: StartsWith("admin", OrdinalIgnoreCase) NO trailing slash.
            if (noLead.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
            {
                leakedAdminPaths.Add(pathProp.Name);
            }
        }

        Assert.True(leakedAdminPaths.Count == 0,
            "OpenAPI document leaked admin paths (D-19 ShouldInclude filter regression): " +
            string.Join(", ", leakedAdminPaths));
    }

    [Fact]
    public async Task Host_Registers_Admin_Endpoints_So_Exclusion_Is_Non_Vacuous()
    {
        await using var app = new OpenApiTestApp();
        await app.StartAsync(_pg, _redis);

        // Enumerate routes from EndpointDataSource — admin endpoints should be present here
        // (proving the exclusion in the doc is intentional, not the absence of registrations).
        var sources = app.Services.GetServices<EndpointDataSource>();
        var adminPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var ep in source.Endpoints.OfType<RouteEndpoint>())
            {
                var raw = ep.RoutePattern.RawText;
                if (string.IsNullOrEmpty(raw))
                {
                    continue;
                }
                var noLead = raw.TrimStart('/');
                if (noLead.StartsWith("admin", StringComparison.OrdinalIgnoreCase))
                {
                    adminPaths.Add(raw);
                }
            }
        }

        Assert.NotEmpty(adminPaths);
    }
}
