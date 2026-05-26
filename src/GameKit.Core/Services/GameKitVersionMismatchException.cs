// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;

namespace GameKit.Core.Services;

/// <summary>
/// Thrown at <c>IHost.StartAsync</c> by <c>GameKitVersionAssertionHostedService</c> (D-16, OPS-05)
/// when two or more loaded <c>GameKit.*</c> assemblies report divergent
/// <c>GameKitMarker.GameKitVersion</c> constants.
/// </summary>
/// <remarks>
/// <para>
/// GameKit ships every sibling package on a coupled release train — every <c>GameKit.*</c> NuGet
/// package built from the same Git tag stamps the same MinVer-derived version into its assembly.
/// At runtime, mismatched versions almost always indicate the consumer's <c>PackageReference</c>
/// graph has restored sibling packages at different versions (NuGet wildcard / floating
/// version / transitive ref to an older sibling). The MSBuild pack-time exact-pin enforcement
/// (<c>GameKit.targets</c>, D-17) catches this at build; this exception is the runtime fallback
/// that fails fast BEFORE Kestrel accepts traffic so the consumer never serves a request from
/// an inconsistent assembly set.
/// </para>
/// <para>
/// The <see cref="Exception.Message"/> contains a human-readable summary of the per-assembly version map
/// (sorted by assembly name for stable output). The full map is also available via
/// <see cref="VersionsByAssembly"/> for structured logging.
/// </para>
/// </remarks>
public sealed class GameKitVersionMismatchException : Exception
{
    /// <summary>
    /// Map of <c>GameKit.*</c> assembly name → reported <c>GameKitVersion</c> at process startup.
    /// Always non-empty when this exception is thrown.
    /// </summary>
    public IReadOnlyDictionary<string, string> VersionsByAssembly { get; }

    /// <summary>
    /// Constructs the exception with the per-assembly version map. The inherited
    /// <see cref="Exception.Message"/> property is populated with a human-readable summary
    /// sorted by assembly name.
    /// </summary>
    /// <param name="versionsByAssembly">
    /// Map of <c>GameKit.*</c> assembly name → reported version. MUST contain at least two
    /// distinct values (otherwise no mismatch exists and the exception should not have been
    /// constructed).
    /// </param>
    public GameKitVersionMismatchException(IReadOnlyDictionary<string, string> versionsByAssembly)
        : base(BuildMessage(versionsByAssembly))
    {
        VersionsByAssembly = versionsByAssembly;
    }

    private static string BuildMessage(IReadOnlyDictionary<string, string> versionsByAssembly)
    {
        ArgumentNullException.ThrowIfNull(versionsByAssembly);
        var pairs = versionsByAssembly
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}");
        return "GameKit version mismatch detected across loaded assemblies: " +
               string.Join(", ", pairs) +
               ". All GameKit.* packages must be pinned to the same version (see " +
               "MSBuild pack-time exact-pin enforcement in GameKit.targets, D-17).";
    }
}
