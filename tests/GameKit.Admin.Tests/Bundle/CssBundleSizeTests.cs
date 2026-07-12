// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors
using System.IO;
using System.IO.Compression;
using Xunit;

namespace GameKit.Admin.Tests.Bundle;

/// <summary>
/// Enforces ROADMAP Phase 03.1 SC#5 — CSS payload increase ≤ 25 KB gzipped above the
/// pre-redesign baseline (1 339 B gzip measured 2026-05-01 against
/// <c>src/GameKit.Admin.UI/wwwroot/gamekit-admin.css</c>). After plan 03.1-02 ports the
/// sketch CSS the file should gzip to ~6 581 B — well under the 27 583 B ceiling.
/// </summary>
public sealed class CssBundleSizeTests
{
    // Baseline 1 339 B + 25 * 1 024 B allowance = 27 939 B. Round up to 27 941 to absorb
    // the 8-byte gzip header variance across compression levels (RESEARCH §Performance
    // Budgets). Failing this test means the redesign blew the SC#5 budget.
    private const long GzipBudgetBytes = 27_941;

    // Path is relative to the test bin/Debug/net10.0 directory after `dotnet test` runs.
    // Five "../" hops reach repo root; then descend into the source tree.
    private const string CssRelativePath =
        "../../../../../src/GameKit.Admin.UI/wwwroot/gamekit-admin.css";

    [Fact]
    [Trait("Category", "Bundle")]
    public void GzippedSize_BelowBudget()
    {
        var fullPath = Path.GetFullPath(
            Path.Combine(System.AppContext.BaseDirectory, CssRelativePath));
        Assert.True(File.Exists(fullPath),
            $"Could not locate gamekit-admin.css at: {fullPath}");

        var bytes = File.ReadAllBytes(fullPath);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(bytes, 0, bytes.Length);
        }
        var gzipBytes = ms.Length;

        Assert.True(
            gzipBytes <= GzipBudgetBytes,
            $"gamekit-admin.css gzip size {gzipBytes} B exceeds {GzipBudgetBytes} B " +
            $"budget (baseline 1 339 B + 25 KB allowance per SC#5).");
    }
}
