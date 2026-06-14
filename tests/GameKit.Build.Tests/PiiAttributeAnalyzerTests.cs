// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using GameKit.Build;

namespace GameKit.Build.Tests;

/// <summary>
/// OBS-07 PII lint gate — verifies <see cref="PiiAttributeAnalyzer"/> reports GK0001
/// on PII-containing span attribute keys and GK0002 on non-literal keys, with correct
/// false-positive guards (whole-token match, not substring match).
/// </summary>
/// <remarks>
/// Tests use <c>CSharpAnalyzerTest&lt;TAnalyzer, DefaultVerifier&gt;</c> (not the
/// obsolete single-arg <c>AnalyzerVerifier&lt;T&gt;</c> helper, and not
/// <c>XUnitVerifier</c> which requires xUnit 3.x ABI incompatible with the repo-pinned
/// xUnit 2.9.2). <c>DefaultVerifier</c> is framework-agnostic and throws standard
/// exceptions that xUnit catches as test failures.
/// Allow-list tests inject the pii-allowlist.txt content via
/// <c>TestState.AdditionalFiles</c> to simulate the AdditionalFiles wiring in
/// Directory.Build.props without needing the full solution build.
/// Expected diagnostics are expressed using markup syntax ({|DiagId:text|}) so the
/// test framework can verify both the ID and the source span automatically.
/// </remarks>
public class PiiAttributeAnalyzerTests
{
    // Stub Activity class in the System.Diagnostics namespace.
    // The Roslyn test harness compiles against a minimal reference set that bundles only an old
    // System.Diagnostics.DiagnosticSource shim (v4) which lacks Activity.SetTag (added in .NET 5).
    // Injecting a stub type in the correct namespace satisfies the semantic model used by
    // PiiAttributeAnalyzer.IsActivityTagMethod without pulling in the runtime ref pack.
    private const string ActivityStub = @"
namespace System.Diagnostics
{
    public sealed class Activity
    {
        public Activity(string operationName) { }
        public Activity SetTag(string key, object? value) => this;
        public Activity AddTag(string key, object? value) => this;
    }
    public sealed class ActivityTagsCollection
    {
        public void Add(string key, object? value) { }
    }
}
";

    // Helper factory that creates a pre-configured test instance.
    // Expected diagnostics should be embedded in source as markup: {|GK0001:expression|}
    private static CSharpAnalyzerTest<PiiAttributeAnalyzer, DefaultVerifier> CreateTest(
        string markedUpSource,
        string allowListContent = "")
    {
        var test = new CSharpAnalyzerTest<PiiAttributeAnalyzer, DefaultVerifier>
        {
            TestCode = markedUpSource,
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
        // {|GK0001:...|} markup tells the framework: expect GK0001 at the key argument span.
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        activity.SetTag({|GK0001:""player.id""|}, ""some-guid"");
    }
}
" + ActivityStub;

        // The {|GK0001:...|} markup registers the expected diagnostic ID+span.
        // No explicit ExpectedDiagnostics.Add needed — the markup is sufficient.
        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 4: camelCase "playerCount" → GK0001 (case-boundary exposes "player").</summary>
    [Fact]
    public async Task PlayerCount_CamelCase_ReportsGK0001()
    {
        // Token split: "playerCount" → ["player", "Count"] → lower → ["player", "count"] → "player" ∈ denylist
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        activity.SetTag({|GK0001:""playerCount""|}, ""5"");
    }
}
" + ActivityStub;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 5: "client.ip" → GK0001 ("ip" is a whole token in denylist).</summary>
    [Fact]
    public async Task ClientDotIp_Literal_ReportsGK0001()
    {
        // Token split: "client.ip" → ["client", "ip"] → "ip" ∈ denylist → GK0001
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        activity.SetTag({|GK0001:""client.ip""|}, ""1.2.3.4"");
    }
}
" + ActivityStub;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // Clean keys — NO diagnostic (whole-token match, not substring)
    // -----------------------------------------------------------------------

    /// <summary>OBS-07 behavior row 2: "ladder.id" → no diagnostic.</summary>
    [Fact]
    public async Task LadderId_Clean_NoDiagnostic()
    {
        // "ladder.id" → tokens ["ladder", "id"] — neither in denylist
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        activity.SetTag(""ladder.id"", ""guid-value"");
    }
}
" + ActivityStub;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 3a: "recipient.count" → no diagnostic (whole-token match avoids "ip" in "recIPient").</summary>
    [Fact]
    public async Task RecipientCount_Clean_NoDiagnostic()
    {
        // "recipient.count" → tokens ["recipient", "count"] — "ip" is NOT a whole token
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        activity.SetTag(""recipient.count"", 5);
    }
}
" + ActivityStub;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>OBS-07 behavior row 3b: "zip.code" → no diagnostic.</summary>
    [Fact]
    public async Task ZipCode_Clean_NoDiagnostic()
    {
        // "zip.code" → tokens ["zip", "code"] — no denylist match
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        activity.SetTag(""zip.code"", ""90210"");
    }
}
" + ActivityStub;

        await CreateTest(source).RunAsync(CancellationToken.None);
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
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        activity.SetTag(""player.self"", ""own-player-context"");
    }
}
" + ActivityStub;

        await CreateTest(source, allowListContent: "player.self\n").RunAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // GK0002 — non-literal key → WARNING (build still passes)
    // -----------------------------------------------------------------------

    /// <summary>OBS-07 behavior row 7: non-literal first arg → GK0002 warning.</summary>
    [Fact]
    public async Task NonLiteralKey_Variable_ReportsGK0002()
    {
        // someVariableKey is not a compile-time constant → GK0002 Warning (not GK0001 Error)
        var source = @"
using System.Diagnostics;
class C
{
    void M()
    {
        var activity = new Activity(""test"");
        var someVariableKey = ""dynamic-key"";
        activity.SetTag({|GK0002:someVariableKey|}, ""value"");
    }
}
" + ActivityStub;

        // The {|GK0002:...|} markup registers the expected diagnostic ID+span.
        await CreateTest(source).RunAsync(CancellationToken.None);
    }
}
