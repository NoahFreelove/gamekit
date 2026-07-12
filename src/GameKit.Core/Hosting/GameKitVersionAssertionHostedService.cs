// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameKit.Core.Hosting;

/// <summary>
/// <see cref="IHostedService"/> that fails fast at <c>IHost.StartAsync</c> if any two loaded
/// <c>GameKit.*</c> assemblies report divergent <c>GameKitMarker.GameKitVersion</c> constants
/// (D-16, OPS-05). Registered AT INDEX 0 of the hosted-service list by
/// <c>GameKitServiceCollectionExtensions.AddGameKit</c> (PATTERNS warning #2) so it runs BEFORE
/// every Auth/Rankings/Matchmaking/Admin.UI migration hosted service — version mismatch is
/// surfaced before any schema changes land, never after a partial migration.
/// </summary>
/// <remarks>
/// <para>
/// Mechanism: (1) eager-load every <c>GameKit.*</c> assembly referenced by the entry assembly
/// (D-24 / PATTERNS warning #7 — without this pre-step, packages whose endpoints have not yet
/// been hit are silently missed because .NET defers assembly loading); (2) iterate
/// <see cref="AppDomain.CurrentDomain"/>'s loaded assemblies filtered to <c>GameKit.*</c> names
/// (skipping <c>GameKit.Build</c>, which is the source-generator analyzer); (3) reflect on
/// <c>{AssemblyName}.Internal.GameKitMarker.GameKitVersion</c> (emitted by the
/// <c>GameKit.Build</c> source generator from Plan 06-01); (4) throw
/// <see cref="GameKitVersionMismatchException"/> when distinct version strings &gt; 1.
/// </para>
/// <para>
/// When the source generator has not yet been wired (i.e. assemblies pre-date Plan 06-01) the
/// <c>GameKitMarker</c> type lookup returns <c>null</c> and the assembly is silently skipped.
/// This is intentional during the rollout — Plan 06-02 ships the runtime detector; Plan 06-01
/// ships the marker that gives it teeth.
/// </para>
/// </remarks>
internal sealed class GameKitVersionAssertionHostedService(
    ILogger<GameKitVersionAssertionHostedService> logger) : IHostedService
{
    private const string GameKitPrefix = "GameKit.";
    private const string AnalyzerAssemblyName = "GameKit.Build";
    private const string MarkerTypeSuffix = ".Internal.GameKitMarker";
    private const string MarkerFieldName = "GameKitVersion";

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        EagerLoadReferencedGameKitAssemblies();

        var versionsByAsm = CollectGameKitVersions();

        if (versionsByAsm.Count == 0)
        {
            logger.LogDebug(
                "GameKit version assertion found no GameKitMarker constants. " +
                "This is expected before Plan 06-01 wires the source generator.");
            return Task.CompletedTask;
        }

        var distinct = versionsByAsm.Values.Distinct(StringComparer.Ordinal).ToArray();
        if (distinct.Length > 1)
        {
            throw new GameKitVersionMismatchException(versionsByAsm);
        }

        logger.LogInformation(
            "GameKit version assertion passed: all {Count} GameKit.* assemblies report version {Version}.",
            versionsByAsm.Count,
            distinct[0]);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// D-24 / PATTERNS warning #7: force-load every <c>GameKit.*</c> assembly the entry assembly
    /// references, so the subsequent <see cref="AppDomain.CurrentDomain"/> scan sees the full set.
    /// Without this pre-step, packages whose endpoints have not yet been hit at startup
    /// (e.g. Matchmaking, Presence) are deferred-loaded and silently missed by the assertion.
    /// Wraps each load in a try/catch with a warning — a missing reference should NOT crash
    /// startup; the iteration below will catch any real version drift among the assemblies that
    /// DID load successfully.
    /// </summary>
    private void EagerLoadReferencedGameKitAssemblies()
    {
        var entry = Assembly.GetEntryAssembly();
        if (entry is null)
        {
            logger.LogDebug(
                "GetEntryAssembly() returned null — skipping eager-load pre-step " +
                "(this is expected in test hosts that build a DI container without IHost).");
            return;
        }

        foreach (var name in entry
            .GetReferencedAssemblies()
            .Where(n => n.Name?.StartsWith(GameKitPrefix, StringComparison.Ordinal) == true))
        {
            try
            {
                Assembly.Load(name);
            }
            catch (Exception ex) when (
                ex is FileNotFoundException or BadImageFormatException or FileLoadException)
            {
                logger.LogWarning(
                    ex,
                    "Failed to eager-load referenced GameKit assembly {AssemblyName}; " +
                    "it will be skipped by the version assertion.",
                    name.FullName);
            }
        }
    }

    private static Dictionary<string, string> CollectGameKitVersions()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var asmName = asm.GetName().Name;
            if (asmName is null
                || !asmName.StartsWith(GameKitPrefix, StringComparison.Ordinal)
                || string.Equals(asmName, AnalyzerAssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            var markerTypeName = asmName + MarkerTypeSuffix;
            var markerType = asm.GetType(markerTypeName, throwOnError: false);
            if (markerType is null)
            {
                continue;
            }

            var field = markerType.GetField(
                MarkerFieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field?.GetValue(null) is string version && !string.IsNullOrWhiteSpace(version))
            {
                result[asmName] = version;
            }
        }

        return result;
    }
}
