// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameKit.Build;

/// <summary>
/// Roslyn <c>DiagnosticAnalyzer</c> that enforces the PII span-attribute denylist (D-06, D-07, OBS-07).
/// Fires on every <c>SetTag</c> or <c>AddTag</c> invocation on <c>System.Diagnostics.Activity</c>
/// whose first argument (the key) contains a whole-token match against the PII denylist.
/// </summary>
/// <remarks>
/// <para>
/// <b>Diagnostic IDs:</b>
/// <list type="bullet">
/// <item><c>GK0001</c> (Error): The key literal contains a PII token from the denylist
///   (<c>player</c>, <c>user</c>, <c>email</c>, <c>token</c>, <c>ip</c>, <c>fingerprint</c>).
///   Add the key to <c>pii-allowlist.txt</c> (via <c>AdditionalFiles</c>) to exempt it.</item>
/// <item><c>GK0002</c> (Warning): The first argument is not a compile-time constant; the
///   analyzer cannot evaluate the key. Prefer using a constant from <c>GameKitTelemetry</c>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Token-split algorithm (D-07):</b> split on dots, then on PascalCase/camelCase boundaries,
/// lowercase each token, then match whole tokens against the denylist. This ensures
/// <c>recipient.count</c> is NOT flagged for containing "ip" as a substring.
/// </para>
/// <para>
/// <b>Wiring:</b> the analyzer is compiled into <c>GameKit.Build.dll</c> which is already
/// referenced as <c>OutputItemType="Analyzer"</c> in every <c>src/GameKit.*.csproj</c>.
/// The allow-list is passed via <c>AdditionalFiles</c> in <c>Directory.Build.props</c>.
/// </para>
/// <para>
/// This analyzer is NOT shipped in consumer NuGet packages — <c>GameKit.Build</c> has
/// <c>IsPackable=false</c> and <c>IncludeBuildOutput=false</c>.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PiiAttributeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for PII key detected in a literal span tag.</summary>
    public const string DiagnosticId = "GK0001";

    /// <summary>Diagnostic ID for non-literal span tag key (analyzer cannot evaluate).</summary>
    public const string NonLiteralDiagnosticId = "GK0002";

    private static readonly DiagnosticDescriptor PiiRule = new DiagnosticDescriptor(
        id: DiagnosticId,
        title: "PII span attribute key",
        messageFormat: "Span tag key '{0}' contains PII token '{1}'. " +
                       "Add to pii-allowlist.txt (via AdditionalFiles) if intentional.",
        category: "GameKit.Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Prevents player identifiers, email addresses, tokens, IP addresses, and " +
                     "fingerprints from being emitted as span attributes (GDPR/OBS-07).");

    private static readonly DiagnosticDescriptor NonLiteralRule = new DiagnosticDescriptor(
        id: NonLiteralDiagnosticId,
        title: "Non-literal span attribute key",
        messageFormat: "Span tag key is not a compile-time constant; the PII check cannot be " +
                       "applied statically. Use a constant from GameKitTelemetry instead.",
        category: "GameKit.Security",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Non-literal keys cannot be statically validated against the PII denylist. " +
                     "Prefer compile-time constants from GameKitTelemetry (OBS-07, D-07).");

    /// <summary>PII token denylist (whole-token, post case+dot split, D-07).</summary>
    private static readonly ImmutableHashSet<string> Denylist =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "player", "user", "email", "token", "ip", "fingerprint");

    /// <summary>Allow-list filename to search for in AdditionalFiles.</summary>
    private const string AllowListFileName = "pii-allowlist.txt";

    // Regex to split on PascalCase/camelCase boundaries:
    //   insert split before each uppercase letter that is followed by a lowercase letter,
    //   and before sequences of uppercase letters followed by a lowercase letter.
    //   e.g. "playerCount" → "player", "Count"; "IPAddress" → "IP", "Address"
    private static readonly Regex CaseBoundaryRegex = new Regex(
        @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])",
        RegexOptions.Compiled);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(PiiRule, NonLiteralRule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // Do not analyze generated code (RESEARCH §Analyzer Implementation Pattern).
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // Enable concurrent execution for performance on large solutions.
        context.EnableConcurrentExecution();

        // Register on every method-call expression.
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // 1. Check that the method name is SetTag or AddTag.
        var methodName = GetMethodName(invocation);
        if (methodName != "SetTag" && methodName != "AddTag")
        {
            return;
        }

        // 2. Use the semantic model to confirm the receiver type is Activity or ActivityTagsCollection.
        //    This avoids false positives on unrelated SetTag/AddTag methods.
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        var resolvedSymbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (!IsActivityTagMethod(resolvedSymbol))
        {
            return;
        }

        // 3. Check the first argument (the key).
        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0)
        {
            return;
        }

        var firstArg = args[0];
        var keyExpr = firstArg.Expression;

        // Try to resolve the key as a compile-time constant.
        var constantValue = context.SemanticModel.GetConstantValue(keyExpr, context.CancellationToken);
        if (!constantValue.HasValue || !(constantValue.Value is string keyString))
        {
            // Non-literal key: report GK0002 (warning) and return.
            context.ReportDiagnostic(Diagnostic.Create(NonLiteralRule, keyExpr.GetLocation()));
            return;
        }

        // 4. Tokenize the key: split on dots, then on case boundaries, lowercase.
        var tokens = Tokenize(keyString);

        // 5. Match whole tokens against the denylist.
        string? matchedToken = null;
        foreach (var token in tokens)
        {
            if (Denylist.Contains(token))
            {
                matchedToken = token;
                break;
            }
        }

        if (matchedToken == null)
        {
            // No denylist match — key is clean.
            return;
        }

        // 6. Check the allow-list (AdditionalFiles: pii-allowlist.txt).
        if (IsAllowListed(keyString, context.Options.AdditionalFiles))
        {
            return;
        }

        // 7. Report GK0001 at the location of the key argument expression.
        context.ReportDiagnostic(Diagnostic.Create(
            PiiRule,
            keyExpr.GetLocation(),
            keyString,
            matchedToken));
    }

    /// <summary>
    /// Determines whether the symbol refers to <c>Activity.SetTag</c>, <c>Activity.AddTag</c>,
    /// or <c>ActivityTagsCollection.Add</c> (i.e., a span-tag method on a telemetry type).
    /// </summary>
    private static bool IsActivityTagMethod(ISymbol? symbol)
    {
        if (symbol is not IMethodSymbol method)
        {
            return false;
        }

        var containingType = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return containingType == "global::System.Diagnostics.Activity" ||
               containingType == "global::System.Diagnostics.ActivityTagsCollection";
    }

    /// <summary>
    /// Extracts the simple method name from an invocation expression.
    /// Handles both <c>obj.Method(...)</c> and bare <c>Method(...)</c> forms.
    /// </summary>
    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                return memberAccess.Name.Identifier.ValueText;
            case IdentifierNameSyntax identifier:
                return identifier.Identifier.ValueText;
            default:
                return null;
        }
    }

    /// <summary>
    /// Tokenizes a span attribute key into lowercase whole-word tokens.
    /// Algorithm (D-07): split on dots, then on PascalCase/camelCase boundaries; lowercase each token.
    /// </summary>
    /// <example>
    /// <list type="bullet">
    /// <item><c>"player.id"</c> → <c>["player", "id"]</c></item>
    /// <item><c>"playerCount"</c> → <c>["player", "count"]</c></item>
    /// <item><c>"recipient.count"</c> → <c>["recipient", "count"]</c> (not ["recip", "ient", "count"])</item>
    /// <item><c>"client.ip"</c> → <c>["client", "ip"]</c></item>
    /// </list>
    /// </example>
    private static IEnumerable<string> Tokenize(string key)
    {
        // Step 1: split on dots.
        foreach (var dotPart in key.Split('.'))
        {
            if (string.IsNullOrEmpty(dotPart))
            {
                continue;
            }

            // Step 2: split on PascalCase/camelCase boundaries.
            foreach (var token in CaseBoundaryRegex.Split(dotPart))
            {
                if (!string.IsNullOrEmpty(token))
                {
                    yield return token.ToLowerInvariant();
                }
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="key"/> appears in the <c>pii-allowlist.txt</c>
    /// AdditionalFile (one fully-qualified key per line; lines starting with <c>#</c> are comments).
    /// </summary>
    private static bool IsAllowListed(string key, ImmutableArray<AdditionalText> additionalFiles)
    {
        foreach (var file in additionalFiles)
        {
            var fileName = Path.GetFileName(file.Path);
            if (!string.Equals(fileName, AllowListFileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = file.GetText();
            if (content == null)
            {
                continue;
            }

            foreach (var line in content.Lines)
            {
                var lineText = line.ToString().Trim();
                if (lineText.Length == 0 || lineText.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.Equals(lineText, key, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
