// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using System.Reflection;
using GameKit.Core.Data;
using Xunit;

namespace GameKit.Core.Integration.Tests;

/// <summary>
/// OPS-07 two-layer egress guard: verifies GameKit.Core assembly has zero HTTP assembly
/// references (Layer 1 — reflection) and no types with System.Net.Http fields (Layer 2 — type scan).
/// </summary>
[Trait("Category", "Integration")]
public class EgressGuardTests
{
    [Fact]
    public void Layer1_Core_Assembly_References_No_Http_Assembly()
    {
        var asm = typeof(GameKitDbContext).Assembly;
        var refs = asm.GetReferencedAssemblies();
        var offenders = refs.Where(a =>
                a.Name == "System.Net.Http"
                || a.Name == "Microsoft.Extensions.Http"
                || a.Name == "Microsoft.AspNetCore.Http.Connections.Client")
            .Select(a => a.Name!)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void Layer2_No_HttpClient_Instantiated_By_Core_Types()
    {
        // Reflection scan: any type in GameKit.Core with a field/property of System.Net.Http.HttpClient?
        var asm = typeof(GameKitDbContext).Assembly;
        var suspicious = asm.GetTypes()
            .SelectMany(t => t.GetFields(
                BindingFlags.Instance
                | BindingFlags.Static
                | BindingFlags.Public
                | BindingFlags.NonPublic))
            .Where(f => f.FieldType.Namespace == "System.Net.Http")
            .Select(f => $"{f.DeclaringType?.FullName}.{f.Name}")
            .ToList();
        Assert.Empty(suspicious);
    }
}
