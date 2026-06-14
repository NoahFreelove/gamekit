// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Xunit;
using GameKit.Build;

namespace GameKit.Build.Tests;

/// <summary>
/// OBS-07 PII lint gate — verifies <see cref="PiiAttributeAnalyzer"/> reports GK0001
/// on PII-containing span attribute keys and GK0002 on non-literal keys, with correct
/// false-positive guards (whole-token match, not substring match).
/// </summary>
/// <remarks>
/// Tests use <c>CSharpAnalyzerTest&lt;TAnalyzer, TVerifier&gt;</c> directly (not the
/// obsolete single-arg <c>AnalyzerVerifier&lt;T&gt;</c> helper).
/// Allow-list tests inject the pii-allowlist.txt content via
/// <c>TestState.AdditionalFiles</c> to simulate the AdditionalFiles wiring in
/// Directory.Build.props without needing the full solution build.
/// </remarks>
public class PiiAttributeAnalyzerTests
{
    // Helper to build a compilable source fragment that puts `activity` in scope.
    private static string Wrap(string statement) => $@"
using System.Diagnostics;
class C
{{
    void M()
    {{
        var activity = new Activity(""test"");
        {statement}
    }}
}}
";

    // Helper factory that creates a pre-configured test instance.
    private static CSharpAnalyzerTest<PiiAttributeAnalyzer, XUnitVerifier> CreateTest(
        string source,
        string allowListContent = "")
    {
        var test = new CSharpAnalyzerTest<PiiAttributeAnalyzer, XUnitVerifier>
        {
            TestCode = source,
        };
        if (!string.IsNullOrEmpty(allowListContent))
        {
            test.TestState.AdditionalFiles.Add(("pii-allowlist.txt", allowListContent));
        }
        return test;
    }

    // -----------------------------------------------------------------------
    // GK0001 — PII literal keys MUST be blocked
    // -----------------------------------------------------------------------

    /// <summary>OBS-07 behavior row 1: SetTag with "player.id" literal → GK0001.</summary>
    [Fact]
    public async Task PlayerDotId_Literal_ReportsGK0001()
    {
        // "player.id" → tokens ["player", "id"] → "player" ∈ denylist → GK0001
        var source = Wrap(@"activity.SetTag(""player.id"", ""some-guid"");");
        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("GK0001", DiagnosticSeverity.Error)
                .WithArguments("player.id", "player"));
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 4: camelCase "playerCount" → GK0001 (case-boundary exposes "player").</summary>
    [Fact]
    public async Task PlayerCount_CamelCase_ReportsGK0001()
    {
        // Token split: "playerCount" → ["player", "Count"] → lower → ["player", "count"] → "player" ∈ denylist
        var source = Wrap(@"activity.SetTag(""playerCount"", ""5"");");
        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("GK0001", DiagnosticSeverity.Error)
                .WithArguments("playerCount", "player"));
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 5: "client.ip" → GK0001 ("ip" is a whole token in denylist).</summary>
    [Fact]
    public async Task ClientDotIp_Literal_ReportsGK0001()
    {
        // Token split: "client.ip" → ["client", "ip"] → "ip" ∈ denylist → GK0001
        var source = Wrap(@"activity.SetTag(""client.ip"", ""1.2.3.4"");");
        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("GK0001", DiagnosticSeverity.Error)
                .WithArguments("client.ip", "ip"));
        await test.RunAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // Clean keys — NO diagnostic (whole-token match, not substring)
    // -----------------------------------------------------------------------

    /// <summary>OBS-07 behavior row 2: "ladder.id" → no diagnostic.</summary>
    [Fact]
    public async Task LadderId_Clean_NoDiagnostic()
    {
        // "ladder.id" → tokens ["ladder", "id"] — neither in denylist
        var source = Wrap(@"activity.SetTag(""ladder.id"", ""guid-value"");");
        var test = CreateTest(source);
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 3a: "recipient.count" → no diagnostic (whole-token match avoids "ip" in "recIPient").</summary>
    [Fact]
    public async Task RecipientCount_Clean_NoDiagnostic()
    {
        // "recipient.count" → tokens ["recipient", "count"] — "ip" is NOT a whole token
        var source = Wrap(@"activity.SetTag(""recipient.count"", 5);");
        var test = CreateTest(source);
        await test.RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 3b: "zip.code" → no diagnostic.</summary>
    [Fact]
    public async Task ZipCode_Clean_NoDiagnostic()
    {
        // "zip.code" → tokens ["zip", "code"] — no denylist match
        var source = Wrap(@"activity.SetTag(""zip.code"", ""90210"");");
        var test = CreateTest(source);
        await test.RunAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // Allow-list — key with PII token but listed in pii-allowlist.txt → NO GK0001
    // -----------------------------------------------------------------------

    /// <summary>OBS-07 behavior row 6: key in pii-allowlist.txt → no diagnostic despite denylist token.</summary>
    [Fact]
    public async Task AllowListed_Key_NoDiagnostic()
    {
        // "player.self" would normally trigger GK0001 for "player",
        // but it is listed in pii-allowlist.txt → exempt.
        var source = Wrap(@"activity.SetTag(""player.self"", ""own-player-context"");");
        var test = CreateTest(source, allowListContent: "player.self\n");
        await test.RunAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // GK0002 — non-literal key → WARNING (build still passes)
    // -----------------------------------------------------------------------

    /// <summary>OBS-07 behavior row 7: non-literal first arg → GK0002 warning.</summary>
    [Fact]
    public async Task NonLiteralKey_Variable_ReportsGK0002()
    {
        // someVariableKey is not a compile-time constant → GK0002 Warning (not GK0001 Error)
        var source = Wrap(@"
        var someVariableKey = ""dynamic-key"";
        activity.SetTag(someVariableKey, ""value"");");
        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult("GK0002", DiagnosticSeverity.Warning));
        await test.RunAsync(CancellationToken.None);
    }
}
