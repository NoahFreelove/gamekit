// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Reflection;
using Xunit;

namespace GameKit.OpenApi.Integration.Tests;

/// <summary>
/// Wave 0 smoke test (Phase 6, Plan 06-03 Task 3): proves the OpenAPI
/// integration-test project loads and that the <c>GameKit.OpenApi</c>
/// assembly is resolvable. Plan 06-06 fills this assembly with
/// <c>OpenApiCoverageTests</c> (D-09 EndpointDataSource contract test)
/// + <c>OpenApiBearerSchemeTests</c> + <c>OpenApiAdminRouteExclusionTests</c>.
/// </summary>
public sealed class SmokeTests
{
    /// <summary>
    /// Asserts the integration-test project loads AND its primary
    /// ProjectReferences resolve at runtime (sentinel for missing refs).
    /// </summary>
    [Fact]
    public void TestProject_Loads()
    {
        Assert.NotNull(Assembly.Load("GameKit.OpenApi"));
        Assert.NotNull(Assembly.Load("GameKit.Core"));
    }
}
