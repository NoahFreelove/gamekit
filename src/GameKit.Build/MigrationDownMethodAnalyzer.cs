// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameKit.Build;

/// <summary>
/// Roslyn <c>DiagnosticAnalyzer</c> that enforces the DR-04 Down() throw-only policy (GK0003).
/// Fires on any <c>Down(MigrationBuilder)</c> method declared on a type that inherits from
/// <c>Microsoft.EntityFrameworkCore.Migrations.Migration</c> whose body is not exactly a single
/// <c>throw new NotSupportedException(...);</c> statement.
/// </summary>
/// <remarks>
/// <para>
/// <b>Diagnostic ID:</b> <c>GK0003</c> (Error, enabled by default).
/// </para>
/// <para>
/// <b>Policy (DR-04):</b> Every migration <c>Down()</c> method must contain only a
/// <c>throw new NotSupportedException(...)</c>. Destructive rollback is not supported —
/// restore from a Postgres backup instead (see <c>docs/runbooks/postgres-backup-restore.md</c>).
/// </para>
/// <para>
/// <b>Non-conforming cases (all emit GK0003):</b>
/// <list type="bullet">
/// <item>Empty body <c>{ }</c> (Pitfall 4 — must throw, not be silent)</item>
/// <item>Body containing destructive calls such as <c>migrationBuilder.DropTable(...)</c></item>
/// <item>Body throwing a different exception (e.g. <c>InvalidOperationException</c>)</item>
/// <item>Body with two or more real statements</item>
/// <item>Expression-bodied form (not block-bodied)</item>
/// </list>
/// </para>
/// <para>
/// <b>Exempt (GK0003 never fires):</b>
/// <list type="bullet">
/// <item><c>Up()</c> method bodies — only <c>Down()</c> is gated</item>
/// <item>Types that do NOT inherit from <c>Migration</c> (e.g. <c>ModelSnapshot</c>, Designer-generated classes)</item>
/// </list>
/// </para>
/// <para>
/// <b>Wiring:</b> compiled into <c>GameKit.Build.dll</c> which is referenced as
/// <c>OutputItemType="Analyzer"</c> in every <c>src/GameKit.*.csproj</c>.
/// This analyzer is NOT shipped in consumer NuGet packages.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationDownMethodAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Diagnostic ID for a non-conforming migration Down() method body.</summary>
    public const string DiagnosticId = "GK0003";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        id: DiagnosticId,
        title: "Migration Down() must contain only throw new NotSupportedException(...)",
        messageFormat: "Migration Down() method '{0}' must contain exactly one statement: " +
                       "throw new NotSupportedException(...). " +
                       "Destructive rollback is disabled in GameKit — restore from a Postgres backup instead " +
                       "(see docs/runbooks/postgres-backup-restore.md).",
        category: "GameKit.Security",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Enforces the DR-04 policy: every EF Core migration Down() method must contain only " +
                     "a throw new NotSupportedException(...) statement. " +
                     "This prevents destructive schema rollback from being silently re-introduced. " +
                     "To roll back, restore from a Postgres backup per docs/runbooks/postgres-backup-restore.md.");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // Do not analyze generated code (Designer.cs, ModelSnapshot.cs are generated code
        // and will be excluded via this flag, providing defense-in-depth alongside the
        // semantic BaseType check below).
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);

        // Enable concurrent execution for performance on large solutions.
        context.EnableConcurrentExecution();

        // Register on every method declaration.
        context.RegisterSyntaxNodeAction(AnalyzeMethodDeclaration, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // Step 1: Cheap syntactic pre-filter — method must be named "Down" with exactly
        // one parameter whose simple type name is "MigrationBuilder".
        if (method.Identifier.Text != "Down")
        {
            return;
        }

        var parameters = method.ParameterList.Parameters;
        if (parameters.Count != 1)
        {
            return;
        }

        // Match by simple type name text — avoids full symbol resolution for the pre-filter.
        // The semantic Migration-inheritance check below is the definitive discriminator.
        var paramTypeName = GetSimpleTypeName(parameters[0].Type);
        if (paramTypeName != "MigrationBuilder")
        {
            return;
        }

        // Step 2: Semantic check — verify the declaring type inherits from
        // Microsoft.EntityFrameworkCore.Migrations.Migration (transitively).
        // This is the Pitfall-7 guard: ModelSnapshot, Designer partials, and arbitrary
        // classes named with a "Down" method are excluded here.
        var containingTypeSymbol = context.SemanticModel
            .GetDeclaredSymbol(method, context.CancellationToken)
            ?.ContainingType;

        if (containingTypeSymbol == null || !InheritsFromMigration(containingTypeSymbol))
        {
            return;
        }

        // Step 3: Check the method body. Expression-bodied Down() is non-conforming.
        if (method.Body == null)
        {
            // Expression-bodied member (=>). Non-conforming — only block bodies with a single throw are accepted.
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                method.Identifier.GetLocation(),
                GetDeclaringTypeName(method)));
            return;
        }

        var statements = method.Body.Statements;

        // Step 4: Body must have exactly ONE statement.
        if (statements.Count != 1)
        {
            // Empty body (Pitfall 4) or multiple statements.
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                method.Identifier.GetLocation(),
                GetDeclaringTypeName(method)));
            return;
        }

        // Step 5: The single statement must be a ThrowStatement.
        if (statements[0] is not ThrowStatementSyntax throwStatement)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                method.Identifier.GetLocation(),
                GetDeclaringTypeName(method)));
            return;
        }

        // Step 6: The thrown expression must be an object creation of NotSupportedException.
        if (!IsNotSupportedExceptionCreation(throwStatement.Expression))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                method.Identifier.GetLocation(),
                GetDeclaringTypeName(method)));
        }
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="typeSymbol"/> inherits transitively from
    /// <c>Microsoft.EntityFrameworkCore.Migrations.Migration</c>.
    /// Walks the <c>BaseType</c> chain until it finds <c>Migration</c> or reaches <c>null</c>.
    /// </summary>
    private static bool InheritsFromMigration(INamedTypeSymbol typeSymbol)
    {
        var current = typeSymbol.BaseType;
        while (current != null)
        {
            // Match by fully-qualified name to avoid collisions with any user-defined "Migration" class.
            if (current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    == "global::Microsoft.EntityFrameworkCore.Migrations.Migration")
            {
                return true;
            }

            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="expression"/> is a
    /// <c>new NotSupportedException(...)</c> or <c>new System.NotSupportedException(...)</c>
    /// object creation expression.
    /// </summary>
    private static bool IsNotSupportedExceptionCreation(ExpressionSyntax? expression)
    {
        if (expression == null)
        {
            return false;
        }

        // Handle both:
        //   new NotSupportedException(...)
        //   new System.NotSupportedException(...)
        //   new global::System.NotSupportedException(...)
        TypeSyntax? typeSyntax = null;

        if (expression is ObjectCreationExpressionSyntax objectCreation)
        {
            typeSyntax = objectCreation.Type;
        }
        else if (expression is ImplicitObjectCreationExpressionSyntax)
        {
            // new(...) { } form — cannot determine type syntactically; treat as non-conforming.
            return false;
        }

        if (typeSyntax == null)
        {
            return false;
        }

        // Extract the rightmost identifier from the type syntax.
        // Handles: NotSupportedException, System.NotSupportedException, global::System.NotSupportedException
        var lastName = GetLastIdentifier(typeSyntax);
        return lastName == "NotSupportedException";
    }

    /// <summary>
    /// Returns the last simple identifier text of a type syntax node.
    /// For <c>System.NotSupportedException</c> → <c>"NotSupportedException"</c>.
    /// For <c>NotSupportedException</c> → <c>"NotSupportedException"</c>.
    /// </summary>
    private static string? GetLastIdentifier(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            QualifiedNameSyntax qualified => GetLastIdentifier(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => GetLastIdentifier(aliasQualified.Name),
            _ => null,
        };
    }

    /// <summary>
    /// Returns the simple type name of a parameter's type annotation, or <c>null</c>.
    /// </summary>
    private static string? GetSimpleTypeName(TypeSyntax? typeSyntax)
    {
        if (typeSyntax == null) return null;

        return typeSyntax switch
        {
            IdentifierNameSyntax identifierName => identifierName.Identifier.Text,
            QualifiedNameSyntax qualified => GetLastIdentifier(qualified.Right),
            _ => null,
        };
    }

    /// <summary>
    /// Returns the declaring type name for use in the diagnostic message format argument.
    /// </summary>
    private static string GetDeclaringTypeName(MethodDeclarationSyntax method)
    {
        if (method.Parent is ClassDeclarationSyntax classDecl)
        {
            return classDecl.Identifier.Text;
        }

        return "<unknown>";
    }
}
