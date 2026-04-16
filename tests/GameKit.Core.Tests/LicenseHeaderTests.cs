// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.IO;
using System.Linq;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Core.Tests;

/// <summary>
/// Verifies every <c>.cs</c> source file in <c>src/</c>, <c>tests/</c>, and <c>samples/</c>
/// carries the required SPDX GPL-3.0-or-later license header. Fast-feedback supplement to the
/// CI <c>fsfe/reuse-action@v6</c> check.
/// </summary>
public class LicenseHeaderTests
{
    [Fact]
    public void Every_CSharp_Source_File_Has_SPDX_GPL_Header()
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
            return lines.Length < 2
                || !lines[0].TrimStart('\uFEFF').Contains("SPDX-License-Identifier: GPL-3.0-or-later")
                || !lines[1].Contains("Copyright");
        }).ToList();

        Assert.True(missing.Count == 0,
            $"Files missing SPDX header:\n{string.Join("\n", missing.Select(p => Path.GetRelativePath(root, p)))}");
    }
}
