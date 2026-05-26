// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Builder;
using GameKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameKit.Distribution.Integration.Tests;

/// <summary>
/// OPS-05 (Plan 06-08 Task 3, D-16): empirically proves
/// <c>GameKitVersionAssertionHostedService</c> throws
/// <see cref="GameKitVersionMismatchException"/> at <c>IHost.StartAsync</c>
/// when a loaded GameKit.* assembly reports a divergent <c>GameKitVersion</c>.
/// </summary>
/// <remarks>
/// <para>
/// Strategy A (per the plan + RESEARCH §Pitfall 3 mitigation): synthesize a
/// dynamic in-memory assembly named <c>GameKit.SyntheticTest</c> containing a
/// <c>GameKit.SyntheticTest.Internal.GameKitMarker</c> static class with a
/// <c>GameKitVersion</c> literal of <c>"99.99.99"</c>. The dynamic assembly is
/// loaded into a collectible <see cref="AssemblyLoadContext"/> so it can be
/// unloaded after the test — otherwise the synthetic marker lingers in
/// <see cref="AppDomain.CurrentDomain.GetAssemblies"/> and pollutes
/// OPS06_CleanInstallMigrationTests (which would then see the synthetic
/// mismatch and fail with an unrelated VersionMismatchException).
/// </para>
/// <para>
/// Validates PATTERNS warning #2 (the assertion service is at index 0 of the
/// hosted-service collection so it runs BEFORE any other startup work).
/// </para>
/// </remarks>
public sealed class OPS05_VersionMismatchAssertionThrowsTests
{
    [Fact]
    public async Task Mismatched_Synthetic_Assembly_Throws_GameKitVersionMismatchException_On_HostStart()
    {
        WeakReference asmWeak;

        // Build the synthetic GameKit.SyntheticTest assembly. AssemblyBuilderAccess.RunAndCollect
        // creates the assembly inside a private collectible context so it can be unloaded after
        // the assertion fires — required to prevent the synthetic marker from polluting
        // OPS06_CleanInstallMigrationTests which also scans AppDomain.CurrentDomain.GetAssemblies().
        // Scope to an inner block so the AssemblyBuilder reference goes out of scope before the
        // GC tickle loop runs (otherwise the WeakReference would always be alive).
        {
            var syntheticAsm = BuildSyntheticAssembly();
            asmWeak = new WeakReference(syntheticAsm);

            var builder = Host.CreateApplicationBuilder();

            // AddGameKit inserts GameKitVersionAssertionHostedService at index 0 of the
            // hosted-service collection (PATTERNS warning #2 + Plan 06-02). ConnectionString
            // must be non-empty for options validation, but the assertion runs BEFORE any
            // DB I/O — the literal value never reaches Postgres.
            builder.Services.AddGameKit(o =>
            {
                o.ConnectionString = "Host=invalid;Port=5432;Database=gamekit;Username=test;Password=test";
                o.AutoMigrate = false;
            });

            using var host = builder.Build();

            var ex = await Assert.ThrowsAnyAsync<Exception>(
                () => host.StartAsync(CancellationToken.None));

            // Unwrap AggregateException if the host wraps the hosted-service exception.
            var mismatch = ex as GameKitVersionMismatchException
                           ?? FindInnerOfType<GameKitVersionMismatchException>(ex);

            Assert.NotNull(mismatch);
            Assert.Contains(
                "GameKit.SyntheticTest",
                mismatch!.VersionsByAssembly.Keys);
            Assert.Equal("99.99.99", mismatch.VersionsByAssembly["GameKit.SyntheticTest"]);

            // Sanity — at least one real GameKit.* package must also be present in the
            // map (otherwise the mismatch would have a single member and shouldn't fire).
            Assert.Contains(
                "GameKit.Core",
                mismatch.VersionsByAssembly.Keys);
        }

        // Tickle the GC up to ~50 times so the collectible synthetic assembly drains from
        // AppDomain.CurrentDomain.GetAssemblies(). 50 iterations is the recommended ceiling per
        // https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability.
        // If unload doesn't complete within the budget (rare — collectible AssemblyBuilders
        // typically unload in 1-2 cycles) OPS-06 will see the synthetic marker and fail; the
        // OPS-06 test class documents this cross-test dependency.
        for (var attempt = 0; attempt < 50 && asmWeak.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// Walks <see cref="Exception.InnerException"/> recursively (handling
    /// <see cref="AggregateException"/>) until it finds an exception of the
    /// requested type, or returns <c>null</c>.
    /// </summary>
    private static T? FindInnerOfType<T>(Exception? ex) where T : Exception
    {
        while (ex is not null)
        {
            if (ex is T match) return match;
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    var found = FindInnerOfType<T>(inner);
                    if (found is not null) return found;
                }
            }
            ex = ex.InnerException;
        }
        return null;
    }

    /// <summary>
    /// Builds a dynamic in-memory assembly named <c>GameKit.SyntheticTest</c> containing
    /// <c>GameKit.SyntheticTest.Internal.GameKitMarker</c> with a divergent
    /// <c>GameKitVersion</c> constant of <c>"99.99.99"</c>. Must be called from
    /// within an <c>EnterContextualReflection</c> scope of a collectible ALC so
    /// the produced assembly can be unloaded after the assertion fires.
    /// </summary>
    private static Assembly BuildSyntheticAssembly()
    {
        var asmName = new AssemblyName("GameKit.SyntheticTest");
        var asmBuilder = AssemblyBuilder.DefineDynamicAssembly(
            asmName,
            AssemblyBuilderAccess.RunAndCollect);

        var moduleBuilder = asmBuilder.DefineDynamicModule("MainModule");

        // Mirror the source-generator output: internal static class with the
        // marker constant. The assertion uses
        // BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static so
        // either visibility is acceptable; matching the generator output keeps
        // the synthetic shape true to the real one.
        var typeBuilder = moduleBuilder.DefineType(
            "GameKit.SyntheticTest.Internal.GameKitMarker",
            TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.Class);

        var fieldBuilder = typeBuilder.DefineField(
            "GameKitVersion",
            typeof(string),
            FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault);
        fieldBuilder.SetConstant("99.99.99");

        typeBuilder.CreateType();

        return asmBuilder;
    }
}
