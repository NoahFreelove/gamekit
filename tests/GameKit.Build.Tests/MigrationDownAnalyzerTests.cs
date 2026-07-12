// SPDX-License-Identifier: Apache-2.0
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
/// DR-04 Down() policy gate — verifies <see cref="MigrationDownMethodAnalyzer"/> reports GK0003
/// on non-conforming migration Down() method bodies and stays silent on conforming ones.
/// </summary>
/// <remarks>
/// Tests use <c>CSharpAnalyzerTest&lt;TAnalyzer, DefaultVerifier&gt;</c> (same harness as
/// <see cref="PiiAttributeAnalyzerTests"/>). Each test compiles a small in-memory source string
/// that defines a minimal <c>Migration</c> base class and <c>MigrationBuilder</c> type so the
/// semantic model can verify type inheritance without pulling in the EF Core assembly.
/// Expected diagnostics are embedded using markup syntax ({|GK0003:expression|}) so the test
/// framework verifies both the ID and the source span automatically.
/// </remarks>
public class MigrationDownAnalyzerTests
{
    // Minimal EF Core stubs so the analyzer's semantic BaseType check resolves correctly
    // without referencing the full Microsoft.EntityFrameworkCore package.
    // MigrationBuilder must include DropTable so test code calling it compiles cleanly.
    private const string EfStubs = @"
namespace Microsoft.EntityFrameworkCore.Migrations
{
    public class MigrationBuilder
    {
        public MigrationBuilder DropTable(string name, string? schema = null) => this;
        public MigrationBuilder DropColumn(string name, string table, string? schema = null) => this;
    }
    public abstract class Migration
    {
        protected abstract void Up(MigrationBuilder migrationBuilder);
        protected virtual void Down(MigrationBuilder migrationBuilder) { }
    }
}
";

    // Helper factory — works exactly like PiiAttributeAnalyzerTests.CreateTest.
    private static CSharpAnalyzerTest<MigrationDownMethodAnalyzer, DefaultVerifier> CreateTest(
        string markedUpSource)
    {
        return new CSharpAnalyzerTest<MigrationDownMethodAnalyzer, DefaultVerifier>
        {
            TestCode = markedUpSource,
        };
    }

    // -----------------------------------------------------------------------
    // CONFORMING cases — GK0003 must NOT fire
    // -----------------------------------------------------------------------

    /// <summary>
    /// Behavior: a Down() body containing exactly <c>throw new NotSupportedException(...)</c>
    /// is the conforming case — GK0003 must not be emitted.
    /// </summary>
    [Fact]
    public async Task ConformingDown_SingleThrowNotSupportedException_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;
using System;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(""Rollback disabled."");
    }
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Behavior: the <c>Up()</c> method body is never inspected by GK0003, even when
    /// it contains destructive calls such as <c>DropTable</c>.
    /// </summary>
    [Fact]
    public async Task ConformingDown_UpWithDropTable_NoDiagnosticOnUp()
    {
        // Up() has a "destructive" call — GK0003 must not fire because it only watches Down().
        // Down() is conforming (single throw new NotSupportedException).
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;
using System;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: ""some_table"");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException(""Rollback disabled."");
    }
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Behavior: a <c>Down()</c>-shaped method on a type that does NOT inherit from
    /// <c>Microsoft.EntityFrameworkCore.Migrations.Migration</c> (e.g. a ModelSnapshot-style
    /// class) must not trigger GK0003. This is the Pitfall-7 guard.
    /// </summary>
    [Fact]
    public async Task NonMigrationClass_DownMethod_NoDiagnostic()
    {
        // ModelSnapshotStyle inherits from object (not Migration) — GK0003 must not fire
        // even though the method signature matches "Down(MigrationBuilder)".
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;

class ModelSnapshotStyle
{
    public void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: ""snapshot_table"");
    }
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // IN-01: expression-bodied Down() — relaxed policy
    // -----------------------------------------------------------------------

    /// <summary>
    /// IN-01 conforming: an expression-bodied <c>Down()</c> whose expression is exactly
    /// <c>throw new NotSupportedException(...)</c> must NOT emit GK0003.
    /// This is the idiomatic single-line form of the same policy.
    /// </summary>
    [Fact]
    public async Task ExpressionBodied_ThrowNotSupportedException_NoDiagnostic()
    {
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;
using System;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void Down(MigrationBuilder migrationBuilder) => throw new NotSupportedException(""Rollback disabled."");
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// IN-01 non-conforming: an expression-bodied <c>Down()</c> whose expression is a
    /// destructive call (not <c>throw new NotSupportedException(...)</c>) must still emit GK0003.
    /// </summary>
    [Fact]
    public async Task ExpressionBodied_DestructiveCall_ReportsGK0003()
    {
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void {|GK0003:Down|}(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: ""players"");
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // NON-CONFORMING cases — GK0003 must fire at the Down identifier
    // -----------------------------------------------------------------------

    /// <summary>
    /// Behavior: a Down() body containing a destructive <c>DropTable</c> call
    /// (any non-throw statement) must emit GK0003.
    /// </summary>
    [Fact]
    public async Task DestructiveDown_DropTable_ReportsGK0003()
    {
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;
using System;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void {|GK0003:Down|}(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: ""players"");
    }
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Behavior: an empty Down() body (zero statements — e.g. a historical no-op migration)
    /// must emit GK0003 (Pitfall 4 — policy requires explicit throw, not silent empty body).
    /// </summary>
    [Fact]
    public async Task EmptyDown_NoDiagnosticExpected_ReportsGK0003()
    {
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void {|GK0003:Down|}(MigrationBuilder migrationBuilder)
    {
    }
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Behavior: a Down() throwing a DIFFERENT exception type (e.g.
    /// <c>InvalidOperationException</c>) must emit GK0003 — only
    /// <c>NotSupportedException</c> is the conforming throw.
    /// </summary>
    [Fact]
    public async Task WrongException_InvalidOperationException_ReportsGK0003()
    {
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;
using System;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void {|GK0003:Down|}(MigrationBuilder migrationBuilder)
    {
        throw new InvalidOperationException(""wrong exception"");
    }
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }

    /// <summary>
    /// Behavior: a Down() body with two real statements (e.g. a comment is fine as trivia,
    /// but two executable statements) must emit GK0003.
    /// </summary>
    [Fact]
    public async Task MultipleStatements_DropThenThrow_ReportsGK0003()
    {
        // Two real statements: DropTable + throw. Body does not conform.
        var source = @"
using Microsoft.EntityFrameworkCore.Migrations;
using System;

class MyMigration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) { }
    protected override void {|GK0003:Down|}(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: ""players"");
        throw new NotSupportedException(""Rollback disabled."");
    }
}
" + EfStubs;

        await CreateTest(source).RunAsync(CancellationToken.None);
    }
}
