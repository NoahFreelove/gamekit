// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.OpenApi.Integration.Tests;

/// <summary>
/// D-08 contract test — asserts the GameKitBearerSchemeTransformer injects
/// the <c>bearerAuth</c> security scheme into <c>components.securitySchemes</c>
/// AND that it is applied globally to every operation.
/// </summary>
[Collection("OpenApi")]
[Trait("Category", "Integration")]
public sealed class OpenApiBearerSchemeTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public OpenApiBearerSchemeTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact]
    public async Task SecuritySchemes_Contains_BearerAuth()
    {
        await using var app = new OpenApiTestApp();
        await app.StartAsync(_pg, _redis);

        var resp = await app.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("components", out var components),
            "OpenAPI doc missing `components`: " + json);
        Assert.True(components.TryGetProperty("securitySchemes", out var schemes),
            "OpenAPI doc missing `components.securitySchemes`: " + json);
        Assert.True(schemes.TryGetProperty("bearerAuth", out var bearer),
            "OpenAPI doc missing `components.securitySchemes.bearerAuth`: " + json);

        // type=http, scheme=bearer, bearerFormat=JWT (D-08).
        Assert.Equal("http", bearer.GetProperty("type").GetString(), ignoreCase: true);
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString(), ignoreCase: true);
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString(), ignoreCase: true);
    }

    [Fact]
    public async Task BearerAuth_Is_Applied_To_Every_Operation()
    {
        await using var app = new OpenApiTestApp();
        await app.StartAsync(_pg, _redis);

        var resp = await app.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        var paths = doc.RootElement.GetProperty("paths");
        var operationCount = 0;
        var operationsWithBearer = 0;
        foreach (var pathProp in paths.EnumerateObject())
        {
            foreach (var methodProp in pathProp.Value.EnumerateObject())
            {
                // OpenAPI v3 path-item siblings include $ref / summary / description / servers /
                // parameters — only HTTP-method keys are operation objects.
                var name = methodProp.Name.ToLowerInvariant();
                if (name is not ("get" or "put" or "post" or "delete" or "options" or "head" or "patch" or "trace"))
                {
                    continue;
                }
                operationCount++;
                if (!methodProp.Value.TryGetProperty("security", out var security))
                {
                    continue;
                }
                foreach (var req in security.EnumerateArray())
                {
                    if (req.TryGetProperty("bearerAuth", out _))
                    {
                        operationsWithBearer++;
                        break;
                    }
                }
            }
        }

        Assert.True(operationCount > 0, "OpenAPI document has no operations to assert against.");
        // GameKitBearerSchemeTransformer applies the bearerAuth requirement to EVERY operation
        // (Pitfall 7 acknowledged — anonymous endpoints inherit a misleading requirement; the
        // v1 contract is global). Assert the global-application invariant holds (all operations
        // covered).
        Assert.Equal(operationCount, operationsWithBearer);
    }
}
