// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
// REUSE-IgnoreStart

using System.IO;
using System.Linq;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Core.Tests;

/// <summary>
/// Verifies every <c>.cs</c> source file in <c>src/</c>, <c>tests/</c>, and <c>samples/</c>
/// carries the required SPDX Apache-2.0 license header. Fast-feedback supplement to the
/// CI <c>fsfe/reuse-action@v6</c> check.
/// </summary>
/// <remarks>
/// Dual-licensed vendored source (e.g. <c>src/GameKit.Rankings/Glicko2/*.cs</c>) carries a
/// combined SPDX identifier such as <c>BSD-3-Clause AND Apache-2.0</c>. These files are
/// explicitly allowed via the <see cref="_dualLicensePaths"/> allow-list and must contain both
/// identifiers on the first line.
/// </remarks>
public class LicenseHeaderTests
{
    /// <summary>
    /// Path segments (relative to repo root) whose files carry a dual SPDX identifier.
    /// The first line of these files must contain <c>Apache-2.0</c> AND one of the
    /// additional upstream identifiers listed per entry.
    /// </summary>
    private static readonly (string PathSegment, string[] AdditionalIdentifiers)[] _dualLicensePaths =
    [
        // Vendored Glicko-2 algorithm — BSD-3-Clause AND Apache-2.0
        (Path.Combine("src", "GameKit.Rankings", "Glicko2"), new[] { "BSD-3-Clause" }),
    ];

    [Fact]
    public void Every_CSharp_Source_File_Has_SPDX_Apache_Header()
    {
        var root = GitRootLocator.FindRepoRoot();

        var targets = Enumerable.Empty<string>();

        var srcDir = Path.Combine(root, "src");
        if (Directory.Exists(srcDir))
            targets = targets.Concat(Directory.EnumerateFiles(srcDir, "*.cs", SearchOption.AllDirectories));

        var testsDir = Path.Combine(root, "tests");
        if (Directory.Exists(testsDir))
            targets = targets.Concat(Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories));

        var samplesDir = Path.Combine(root, "samples");
        if (Directory.Exists(samplesDir))
            targets = targets.Concat(Directory.EnumerateFiles(samplesDir, "*.cs", SearchOption.AllDirectories));

        var sep = Path.DirectorySeparatorChar;
        targets = targets
            .Where(p => !p.Contains($"{sep}obj{sep}"))
            .Where(p => !p.Contains($"{sep}bin{sep}"))
            .Where(p => !p.Contains("Migrations")); // EF-generated files; reuse lint covers them

        var missing = targets.Where(p =>
        {
            var lines = File.ReadAllLines(p);
            if (lines.Length < 2)
                return true;

            var firstLine = lines[0].TrimStart('﻿');
            var secondLine = lines[1];

            // Check if this file is in a dual-license allow-list path.
            foreach (var (pathSegment, additionalIdentifiers) in _dualLicensePaths)
            {
                if (p.Contains(pathSegment))
                {
                    // Dual-licensed files must contain Apache-2.0 AND the upstream identifier.
                    bool hasApache = firstLine.Contains("Apache-2.0");
                    bool hasUpstream = additionalIdentifiers.Any(id => firstLine.Contains(id));
                    bool hasCopyright = secondLine.Contains("Copyright");
                    return !(hasApache && hasUpstream && hasCopyright);
                }
            }

            // Standard files: first line must contain the exact Apache-2.0 SPDX identifier.
            return !firstLine.Contains("SPDX-License-Identifier: Apache-2.0")
                || !secondLine.Contains("Copyright");
        }).ToList();

        Assert.True(missing.Count == 0,
            $"Files missing SPDX header:\n{string.Join("\n", missing.Select(p => Path.GetRelativePath(root, p)))}");
    }
}
// REUSE-IgnoreEnd
