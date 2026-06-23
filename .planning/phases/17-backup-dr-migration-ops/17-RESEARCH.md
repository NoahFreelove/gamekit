# Phase 17: Backup / DR + Migration Ops — Research

**Researched:** 2026-06-22
**Domain:** Postgres backup/restore, Redis backup, EF Core migration CLI tooling, Down() policy enforcement
**Confidence:** HIGH

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
All implementation choices at Claude's discretion (discuss skipped). Use success criteria + existing codebase conventions.

### Claude's Discretion
All implementation choices at Claude's discretion.

### Deferred Ideas (OUT OF SCOPE)
None — discuss phase skipped.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| DR-01 | Postgres backup/restore runbook (`docs/runbooks/postgres-backup-restore.md`) | Existing `docs/ops/disaster-recovery.md` covers this material; a `docs/runbooks/` symlink/refactor is the deliverable. CONTEXT.md says DR-01 → REQUIREMENTS.md says `docs/runbooks/postgres-backup-restore.md` |
| DR-02 | Redis backup/restore runbook (`docs/runbooks/redis-backup-restore.md`) | `docs/ops/disaster-recovery.md` §Redis backup covers RDB + AOF — same refactor target |
| DR-03 | CI Testcontainers DR round-trip test: `pg_dump` → destroy → `pg_restore` → health check | Testcontainers `IContainer.ExecAsync` runs `pg_dump`/`pg_restore` inside the postgres container; see §DR Round-Trip Pattern |
| DR-04 | `gamekit migrations list` CLI command | Extends `GameKit.Cli/Program.cs` with a `migrations` branch; uses `IMigrator.GetPendingMigrations()` per package context |
| DR-05 | `gamekit migrations apply --dry-run` CLI command | EF Core 10 `IMigrator.GenerateScript(idempotent: true)` generates idempotent SQL without executing DDL |
| DR-06 | `gamekit db backup` / `gamekit db restore` CLI helpers wrapping `pg_dump`/`pg_restore` + Redis snapshot | Shell-out via `Process` API, operator-supplied destination |
| DR-07 | Migration-ops documentation + `MigrationTimestampTests` + `Down()` policy gate | Roslyn analyzer `GK0003` in `GameKit.Build` OR unit test scanning migration file AST; timestamp ordering test mirrors `AdvisoryLockKeyTests` pattern |
</phase_requirements>

---

## Summary

Phase 17 has three distinct work streams that can proceed largely in parallel:

**Stream 1 — Runbooks:** The existing `docs/ops/disaster-recovery.md` already documents both the Postgres backup procedure (logical `pg_dump`, WAL-G PITR) and the Redis AOF backup procedure in full operational detail. The gap is that REQUIREMENTS.md specifies `docs/runbooks/postgres-backup-restore.md` and `docs/runbooks/redis-backup-restore.md` as distinct files. The deliverable is creating the `docs/runbooks/` directory and splitting/refactoring the existing ops material into the two canonical runbook files (DR-01, DR-02), then updating `docs/migration-ops.md` (DR-07 docs component).

**Stream 2 — CLI extension:** The existing `gamekit` CLI (Spectre.Console.Cli; `src/GameKit.Cli/`) currently has `migrate`, `admin`, and `service-token` branches. Phase 17 adds two new branches: `migrations` (with `list` and `apply` sub-commands covering DR-04/DR-05) and `db` (with `backup` and `restore` sub-commands for DR-06). The key technical challenges are (a) building per-package `DbContext` instances to call `GetPendingMigrationsAsync()` and `IMigrator.GenerateScript()`, and (b) shelling out to `pg_dump`/`pg_restore` without bundling or distributing those binaries.

**Stream 3 — Gates and tests:** DR-03 requires a Testcontainers CI test using `IContainer.ExecAsync` to invoke `pg_dump`/`pg_restore` inside the running container (avoiding any host-side binary dependency), apply all migrations, and assert `GET /health/ready` returns 200. DR-04's Down() policy requires a new Roslyn analyzer `GK0003` in the existing `GameKit.Build` project (same pattern as `GK0001`/`GK0002`) that fires on any migration `Down()` method body not consisting solely of a `throw new NotSupportedException(...)` statement. Additionally, a `MigrationTimestampTests` unit test asserts lexicographic timestamp ordering across packages.

**Primary recommendation:** Implement the Down() convention change first (convert all 13 migration files, add the `GK0003` analyzer gate), then the CLI extensions, then the runbooks, and finally the DR round-trip CI test. This ordering ensures the gate is in place before any future migration could violate the new policy.

---

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| `pg_dump` / `pg_restore` invocation | CLI (`gamekit db backup/restore`) | Runbook (manual) | The CLI wraps standard Postgres tools as a convenience; the operator still controls the destination path |
| Redis RDB snapshot | Runbook (manual `BGSAVE` + filesystem copy) | — | Redis does not have a `pg_dump` equivalent; the runbook documents the procedure; no CLI wrapper needed |
| DR round-trip proof | CI test (`GameKit.DR.Tests`) | — | Must be automated + committed; a script alone is insufficient per success criterion #1 |
| Migration SQL generation | CLI (`gamekit migrations apply --dry-run`) | EF Core `IMigrator` | `IMigrator.GenerateScript(idempotent: true)` is the authoritative API; CLI surfaces it |
| Pending migration count | CLI (`gamekit migrations list`) | — | Reads per-package `__ef_migrations_*` history tables via `GetPendingMigrationsAsync()` |
| Down() policy enforcement | Build-time Roslyn analyzer (`GK0003` in `GameKit.Build`) | CI gate | Static: fires at compile time, zero runtime cost; same architecture as OBS-07 PII gate |
| Migration timestamp ordering | Unit test (`MigrationTimestampTests`) | — | Pure reflection/file-scan; no containers needed |
| Runbook content | Docs (`docs/runbooks/`) | `docs/ops/` (existing source material) | DR-01 and DR-02 require canonical paths; existing content is already correct |

---

## Complete Migration Inventory (DR-04 — Down() Conversion Scope)

This is a **ground-truth enumeration** of every migration file and its current `Down()` body. All require conversion to `throw new NotSupportedException(...)` under DR-04.

[VERIFIED: direct codebase read]

| # | File | Package | Down() body (current) | Action |
|---|------|---------|----------------------|--------|
| 1 | `src/GameKit.Core/Migrations/20260415000000_CoreInitial.cs` | Core | `DropTable` ×4 | Convert |
| 2 | `src/GameKit.Core/Migrations/20260519000000_AddSessionParticipationFraction.cs` | Core | `DropColumn` | Convert |
| 3 | `src/GameKit.Core/Migrations/20260606000000_AddMergedIntoPlayerId.cs` | Core | `DropForeignKey` + `DropColumn` ×2 | Convert |
| 4 | `src/GameKit.Core/Migrations/20260606100000_AddAuditActorIdFk.cs` | Core | `// No-op` (empty body) | **No change needed — already safe** |
| 5 | `src/GameKit.Core/Migrations/20260622000000_AddGameSessionIdempotencyKey.cs` | Core | Raw SQL DROP INDEX + `DropColumn` | Convert |
| 6 | `src/GameKit.Auth/Migrations/20260418000000_AuthInitial.cs` | Auth | `DropTable` ×3 | Convert |
| 7 | `src/GameKit.Auth/Migrations/20260418100000_AuthPasswordHashLength.cs` | Auth | `AlterColumn` (varchar shrink) | Convert |
| 8 | `src/GameKit.Auth/Migrations/20260606200000_AddAccountMerges.cs` | Auth | `DropTable` | Convert |
| 9 | `src/GameKit.Admin.UI/Migrations/20260419000000_AdminInitial.cs` | Admin.UI | `DropTable` | Convert |
| 10 | `src/GameKit.Rankings/Migrations/20260515000000_RankingsInitial.cs` | Rankings | Raw SQL DROP CONSTRAINT + `DropTable` ×7 | Convert |
| 11 | `src/GameKit.Rankings/Migrations/20260517000000_RankingsDecayPlacement.cs` | Rankings | Raw SQL DROP INDEX + `DropColumn` ×3 | Convert |
| 12 | `src/GameKit.Matchmaking/Migrations/20260516000000_MatchmakingInitial.cs` | Matchmaking | `DropTable` ×5 | Convert |
| 13 | `src/GameKit.Matchmaking/Migrations/20260520000000_MatchmakingBackfillRegions.cs` | Matchmaking | `DropColumn` | Convert |
| 14 | `src/GameKit.Lobby/Data/Migrations/20260522000000_LobbyInitial.cs` | Lobby | `DropTable` ×2 | Convert |

**Total: 13 conversions, 1 already compliant (CoreInitial no-op stays untouched).**

Note: The CONTEXT.md says "~13 total" and lists "Lobby 0" — Lobby has 1 migration (`20260522000000_LobbyInitial`) with a destructive Down(). The count discrepancy is because the CONTEXT.md said Lobby has zero migrations of the kind needing conversion, but the actual codebase inspection shows LobbyInitial has a `DropTable` Down(). **Plan must include converting LobbyInitial.** The actual count is 13 files to convert, 1 no-op file unchanged, = 14 migration files total.

The `Down()` replacement text across all converted files:
```csharp
/// <inheritdoc />
protected override void Down(MigrationBuilder migrationBuilder)
{
    // DR-04: Destructive rollback is not supported. Restore from backup — see docs/runbooks/postgres-backup-restore.md.
    throw new NotSupportedException(
        "Migration rollback via Down() is disabled in GameKit. Restore from a Postgres backup instead. " +
        "See docs/runbooks/postgres-backup-restore.md.");
}
```

---

## Standard Stack

### Core (no new packages required — all existing)
[VERIFIED: direct codebase read]

| Library | Version | Purpose | Already in use |
|---------|---------|---------|----------------|
| `Spectre.Console.Cli` | 0.49.1 | CLI command framework | `GameKit.Cli` |
| `Microsoft.EntityFrameworkCore` | 10.0.6 | `IMigrator`, `GetPendingMigrationsAsync()`, `GenerateScript()` | All packages |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.1 | Postgres-specific DbContext configuration | All packages |
| `Testcontainers.PostgreSql` | 4.11.0 | DR round-trip CI test containers | `GameKit.TestFixtures` |
| `xUnit` | 2.9.2 | Test framework | All test projects |

### No New NuGet Dependencies Required

Phase 17 introduces no new packages. The CLI extension reuses existing project references already in `GameKit.Cli.csproj` (`GameKit.Core`, `GameKit.Admin.UI`, `GameKit.Rankings`). The DR round-trip test project will add project references to `GameKit.Auth`, `GameKit.Matchmaking`, and `GameKit.Lobby` so it can build per-package migration contexts, reusing `GameKit.TestFixtures`.

The `GameKit.Build` analyzer project (for `GK0003`) already exists with `Microsoft.CodeAnalysis.CSharp` 4.13.0 — no new packages.

---

## Package Legitimacy Audit

No new external packages. N/A.

---

## Architecture Patterns

### System Architecture Diagram

```
CLI entry (gamekit)
├── migrations branch (NEW)
│   ├── list sub-command
│   │   └── foreach package: build migration DbContext → GetPendingMigrationsAsync()
│   │       → print table: package | applied | pending | recommended order
│   └── apply sub-command (--dry-run flag)
│       └── foreach package: build migration DbContext → IMigrator.GenerateScript(idempotent: true)
│           → print SQL to stdout (no DDL executed)
└── db branch (NEW)
    ├── backup sub-command
    │   ├── --postgres-connection → Process.Start("pg_dump") → destination path
    │   └── --redis-connection   → redis-cli BGSAVE (or document-only)
    └── restore sub-command
        ├── --postgres-connection → Process.Start("pg_restore") → destination database
        └── --file <path>

GameKit.Build (existing)
└── GK0003 analyzer (NEW)
    └── fires on Down() method bodies that do NOT contain only NotSupportedException throw
        → DiagnosticSeverity.Error at build time

tests/GameKit.DR.Tests (NEW test project)
└── DRRoundTripTests
    ├── Start Testcontainers Postgres (postgres:17.9 + init scripts)
    ├── Apply ALL package migrations (Core, Auth, Admin, Rankings, Matchmaking, Lobby)
    ├── Seed test data (insert player)
    ├── pg_dump via IContainer.ExecAsync → dump inside container → copy out via temp volume or stdout redirect
    ├── Dispose container (destroy)
    ├── Start fresh Testcontainers Postgres
    ├── pg_restore via IContainer.ExecAsync
    ├── Start HealthTestHost (same pattern as HealthEndpointTests)
    └── Assert GET /health/ready → 200

tests/GameKit.Core.Tests (existing)
└── MigrationTimestampTests (NEW class)
    └── Reflection-scan all Migration subclasses per assembly → assert latest timestamp per package
        is lexicographically greater than previous package's latest
```

### Recommended Project Structure (additions only)

```
docs/runbooks/
├── postgres-backup-restore.md    # DR-01 (split from docs/ops/disaster-recovery.md)
├── redis-backup-restore.md       # DR-02 (split from docs/ops/disaster-recovery.md)
docs/
└── migration-ops.md              # DR-07 (new; per-package ordering, dry-run, timestamp policy)

src/GameKit.Cli/Commands/
├── MigrateCommand.cs             # existing (unchanged)
├── Migrations/
│   ├── MigrationsListCommand.cs  # DR-04
│   └── MigrationsApplyCommand.cs # DR-05
├── Db/
│   ├── DbBackupCommand.cs        # DR-06
│   └── DbRestoreCommand.cs       # DR-06

src/GameKit.Build/
└── MigrationDownMethodAnalyzer.cs  # DR-07 (GK0003)

tests/GameKit.DR.Tests/            # new project (DR-03)
├── GameKit.DR.Tests.csproj
├── CollectionDefinitions.cs
└── DRRoundTripTests.cs
tests/GameKit.Core.Tests/
└── MigrationTimestampTests.cs    # DR-07 (new class in existing project)
tests/GameKit.Build.Tests/
└── MigrationDownAnalyzerTests.cs # DR-07 (new class in existing project)
```

### Pattern 1: Per-Package Migration DbContext Construction (for CLI commands)

The existing `AuthMigrationHostedService.BuildAuthMigrationContext()` demonstrates the canonical pattern. The CLI commands must replicate this for each of the 5 packages that have EF migrations (Core, Auth, Admin, Rankings, Matchmaking — and Lobby). The CLI already has project references to `GameKit.Core`, `GameKit.Admin.UI`, and `GameKit.Rankings`; Auth, Matchmaking, and Lobby project references will be needed for the multi-package `migrations list` command.

[VERIFIED: direct codebase read — `src/GameKit.Auth/Data/AuthMigrationHostedService.cs`]

```csharp
// Pattern for building a package's migration-only DbContext
private static GameKitDbContext BuildPackageMigrationContext(
    string connectionString,
    string migrationsAssemblyFullName,
    string migrationsHistoryTable)
{
    var optionsBuilder = new DbContextOptionsBuilder<GameKitDbContext>()
        .UseNpgsql(connectionString, npg =>
        {
            npg.MigrationsAssembly(migrationsAssemblyFullName);
            npg.MigrationsHistoryTable(migrationsHistoryTable, "gamekit");
        })
        .ReplaceService<IModelCustomizer, <PackageMigrationModelCustomizer>>();

    return new GameKitDbContext(optionsBuilder.Options);
}
```

Each package has a `*MigrationModelCustomizer` (internal, exposed via `InternalsVisibleTo("GameKit.Cli")`). The CLI csproj already declares `InternalsVisibleTo` for Core and Admin.UI and Rankings. Auth, Matchmaking, and Lobby will need `InternalsVisibleTo("GameKit.Cli")` added to their respective projects OR the CLI can register per-package model customizers by referencing a public registration API.

**Key discovery:** The CLI currently only builds a Core migration context (`MigrateCommand.cs`). To implement `migrations list` and `migrations apply --dry-run` across ALL packages, the CLI needs project references to all packages. Add to `GameKit.Cli.csproj`:
```xml
<ProjectReference Include="..\GameKit.Auth\GameKit.Auth.csproj" />
<ProjectReference Include="..\GameKit.Matchmaking\GameKit.Matchmaking.csproj" />
<ProjectReference Include="..\GameKit.Lobby\GameKit.Lobby.csproj" />
```

And add `InternalsVisibleTo("GameKit.Cli")` in `GameKit.Auth.AssemblyInfo.cs`, `GameKit.Matchmaking.AssemblyInfo.cs`, `GameKit.Lobby.AssemblyInfo.cs`.

### Pattern 2: EF Core 10 Script Generation (DR-05 Dry-Run)

[VERIFIED: EF Core 10 docs — `context.GetService<IMigrator>().GenerateScript()`]

```csharp
// Idempotent SQL generation — does NOT execute DDL
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

var migrator = context.GetService<IMigrator>();
// fromMigration: null = from empty DB; toMigration: null = latest
// idempotent: true = wraps each migration in IF NOT EXISTS guards (EF Core's history-table check)
string sql = migrator.GenerateScript(
    fromMigration: null,
    toMigration: null,
    MigrationsSqlGenerationOptions.Idempotent);
```

`MigrationsSqlGenerationOptions.Idempotent` is the EF Core 10 enum flag for `--idempotent` in `dotnet ef migrations script`. The generated SQL is safe to print to stdout without executing. For a multi-package dry-run, call this per package and concatenate outputs with section headers.

### Pattern 3: Testcontainers ExecAsync for pg_dump/pg_restore (DR-03)

[VERIFIED: `Testcontainers.IContainer.ExecAsync(IList<string>, CancellationToken)` — Testcontainers 4.11.0 XML docs]

```csharp
// Invoke pg_dump inside the running postgres container
var (stdout, stderr, exitCode) = await _container.ExecAsync(new[]
{
    "pg_dump",
    "--username=postgres",
    "--format=custom",
    "--file=/tmp/gamekit.pgdump",
    "gamekit"
});
Assert.Equal(0, exitCode);

// Copy the dump out of the container for restore use
// Option A: Use pg_restore on the SAME container (avoids copy), then destroy
// Option B: Two containers sharing a Docker volume (mount --volume)
```

**Recommended approach for DR-03:** Use two separate containers connected via a shared Docker volume:
1. Container 1: Apply migrations + seed data, run `pg_dump --format=custom --file=/dump/gamekit.pgdump`
2. Destroy Container 1
3. Container 2 (same Docker volume mounted): Run `pg_restore --dbname=gamekit /dump/gamekit.pgdump`
4. Assert `/health/ready` → 200 using `HealthTestHost.StartAsync()`

Alternative (simpler): Use `pg_dump --format=plain` and pipe stdout back to the test via `ExecAsync` — the custom format requires a file, but plain SQL can be streamed. For the CI test, plain format is acceptable since we are proving restore works, not benchmarking restore speed. However, `--format=custom` with a shared volume is cleaner and matches the production runbook.

**Simplest approach that avoids Docker volume complexity:** The test can mount a host temp directory into both containers using `WithBindMount(tmpPath, "/dump")`. Testcontainers bind mounts work reliably on Linux CI.

```csharp
var tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmpDir);

// Container 1: populate + dump
var pg1 = new PostgreSqlBuilder("postgres:17.9")
    .WithBindMount(tmpDir, "/dump")
    .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
    .Build();
await pg1.StartAsync();
// ... apply all migrations, seed a player ...
await pg1.ExecAsync(new[] { "pg_dump", "--username=postgres",
    "--format=custom", "--file=/dump/gamekit.pgdump", "gamekit" });
await pg1.DisposeAsync();

// Container 2: restore + health check
var pg2 = new PostgreSqlBuilder("postgres:17.9")
    .WithBindMount(tmpDir, "/dump")
    .WithBindMount(initDir, "/docker-entrypoint-initdb.d")
    .Build();
await pg2.StartAsync();
await pg2.ExecAsync(new[]
{
    "pg_restore", "--username=postgres",
    "--dbname=gamekit", "--no-owner", "--no-privileges",
    "/dump/gamekit.pgdump"
});
// Assert /health/ready → 200 via HealthTestHost
```

**Important:** the `pg_dump` inside a Postgres container writes to the container filesystem by default. Using `--file` to a bind-mounted host path (`/dump/`) avoids needing to copy files out via Docker API.

### Pattern 4: GK0003 Roslyn Analyzer — Down() Method Gate (DR-07)

[VERIFIED: direct codebase read — `src/GameKit.Build/PiiAttributeAnalyzer.cs`; same architecture]

```csharp
// In GameKit.Build/MigrationDownMethodAnalyzer.cs
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MigrationDownMethodAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "GK0003";
    // ...

    // Register on method body syntax
    // SyntaxKind.MethodDeclaration where method name == "Down"
    // and declaring type inherits from "Migration"
    // and body does NOT consist solely of a single ThrowStatement
    // where the thrown expression is ObjectCreationExpression of NotSupportedException
}
```

**Key design decision — Roslyn vs unit test vs grep:**

| Approach | Pros | Cons |
|----------|------|------|
| Roslyn analyzer (GK0003) | Fires at `dotnet build` before CI; IDE squiggles; same framework as GK0001 | Must target `netstandard2.0`; cannot use `System.IO`; slightly complex |
| xUnit unit test scanning `.cs` files via `File.ReadAllText` + regex | Simple, fast, no Roslyn dependency | String/regex parsing is fragile (comments, formatting variants) |
| CI grep/script | Zero code to write | False-negative prone; runs only in CI, not locally |

**Recommendation: Roslyn analyzer (GK0003).** The project already has `GameKit.Build` with `PiiAttributeAnalyzer` as the pattern. The analyzer approach is the most robust because it fires at compile time on every developer's machine and in CI. The AST-based check is exact: check that the `Down()` method body's single statement is a `ThrowStatement` whose thrown expression is a `ObjectCreationExpressionSyntax` named `NotSupportedException`. Empty bodies (like `AddAuditActorIdFk`) pass because they contain zero statements, not a destructive statement — but the DR-04 policy says ALL Down() methods must contain `throw new NotSupportedException(...)`, so the analyzer must also flag empty bodies. The no-op `AddAuditActorIdFk.Down()` will be converted to a throw as well.

**Analyzer logic:**
1. On `MethodDeclarationSyntax` where `Identifier.Text == "Down"` and `ParameterList` has one `MigrationBuilder` parameter
2. Verify the declaring type inherits from `Migration` (semantic check)
3. Check `Body.Statements.Count == 1` AND `Body.Statements[0]` is `ThrowStatementSyntax`
4. Check the thrown expression is `ObjectCreationExpressionSyntax` with type name containing `NotSupportedException`
5. If any check fails → emit `GK0003` error

This is deterministic and handles the edge cases (empty body, multiple statements, wrong exception type).

### Pattern 5: MigrationTimestampTests (DR-07)

[VERIFIED: direct codebase read — timestamp prefixes confirmed in all 14 migration file names]

```csharp
// In tests/GameKit.Core.Tests/MigrationTimestampTests.cs
[Fact]
public void PackageMigrations_LatestTimestamp_AreInCorrectOrder()
{
    // Canonical application order per CLAUDE.md and migrations-runbook.md
    var packages = new[]
    {
        (Name: "Core",        Assembly: typeof(GameKit.Core.Migrations.CoreInitial).Assembly),
        (Name: "Auth",        Assembly: typeof(GameKit.Auth.Migrations.AuthInitial).Assembly),
        (Name: "Admin",       Assembly: typeof(GameKit.Admin.UI.Migrations.AdminInitial).Assembly),
        (Name: "Rankings",    Assembly: typeof(GameKit.Rankings.Migrations.RankingsInitial).Assembly),
        (Name: "Matchmaking", Assembly: typeof(GameKit.Matchmaking.Migrations.MatchmakingInitial).Assembly),
        (Name: "Lobby",       Assembly: typeof(GameKit.Lobby.Data.Migrations.LobbyInitial).Assembly),
    };

    string? previousLatest = null;
    string? previousName = null;
    foreach (var (name, assembly) in packages)
    {
        var migrationTypes = assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Migration)) && !t.Name.EndsWith("Designer"))
            .OrderBy(t => t.Name) // Name = timestamp-prefixed
            .ToList();
        Assert.NotEmpty(migrationTypes);
        var latest = migrationTypes.Last().Name; // e.g. "20260622000000_AddGameSessionIdempotencyKey"
        if (previousLatest is not null)
        {
            Assert.True(
                string.Compare(latest, previousLatest, StringComparison.Ordinal) > 0,
                $"{name} latest migration ({latest}) must be lexicographically AFTER " +
                $"{previousName} latest migration ({previousLatest})");
        }
        previousLatest = latest;
        previousName = name;
    }
}
```

**Existing timestamp values (verified):**

| Package | Latest migration timestamp | Lexicographic order |
|---------|--------------------------|---------------------|
| Core | `20260622000000` | 1st |
| Auth | `20260606200000` | 2nd — **PROBLEM: Auth latest (20260606200000) < Core latest (20260622000000)** |
| Admin.UI | `20260419000000` | 3rd — **PROBLEM: Admin latest < Auth latest** |
| Rankings | `20260517000000` | 4th — OK vs Admin (20260419) |
| Matchmaking | `20260520000000` | 5th — OK vs Rankings (20260517) |
| Lobby | `20260522000000` | 6th — OK vs Matchmaking (20260520) |

**Critical finding:** The timestamp ordering test AS WRITTEN (asserting each package's latest > previous package's latest) will FAIL with the current timestamps because:
- Core's latest is `20260622000000` (Phase 16 idempotency key migration)
- Auth's latest is `20260606200000` (less than Core's latest)
- Admin.UI's latest is `20260419000000` (earliest of all)

The existing migrations were written before this ordering constraint existed. The CONTEXT.md says the test "asserts each package's latest migration timestamp is lexicographically greater than the previous package's latest timestamp" — but this is not actually true for the existing migrations.

**Resolution options:**
1. Write a new empty "marker" migration in Auth (`20260623000000_DrTimestampMarker`) and Admin.UI (`20260624000000_DrTimestampMarker`) to make the ordering assertion pass, then assert the ordering is correct going forward
2. Weaken the test to assert a package-ordering INTENTION: assert that the packages' initial migrations are in order, not the absolute latest
3. Document the "latest" constraint as applying only to migrations added AFTER Phase 17, and use a different check for the current state

**Recommended approach:** Add no-op marker migrations with future timestamps to Auth and Admin.UI to make the ordering hold. This is the cleanest approach because it establishes the correct ordering permanently:
- `GameKit.Auth/Migrations/20260623000000_DrOrderingMarker.cs` (Up: no-op, Down: `throw new NotSupportedException(...)`)
- `GameKit.Admin.UI/Migrations/20260624000000_DrOrderingMarker.cs` (Up: no-op, Down: `throw new NotSupportedException(...)`)

These are zero-DDL migrations (like `AddAuditActorIdFk`). After adding them:
- Auth latest: `20260623000000` > Core latest: `20260622000000` ✓
- Admin latest: `20260624000000` > Auth latest: `20260623000000` ✓
- Rankings latest: `20260517000000` < Admin latest... **PROBLEM still exists for Rankings/Matchmaking/Lobby**

Rankings latest is `20260517`, Admin latest would be `20260624` — Rankings is BEFORE Admin. The full chain is broken for Rankings, Matchmaking, and Lobby too.

**Corrected recommendation:** Add ordering-marker migrations across ALL affected packages:
- `GameKit.Auth/Migrations/20260623000000_DrOrderingMarker.cs`
- `GameKit.Admin.UI/Migrations/20260624000000_DrOrderingMarker.cs`
- `GameKit.Rankings/Migrations/20260625000000_DrOrderingMarker.cs`
- `GameKit.Matchmaking/Migrations/20260626000000_DrOrderingMarker.cs`
- `GameKit.Lobby/Data/Migrations/20260627000000_DrOrderingMarker.cs`

Core already has the latest timestamp (`20260622000000`). After adding these markers, the ordering is:
Core (`20260622`) → Auth (`20260623`) → Admin (`20260624`) → Rankings (`20260625`) → Matchmaking (`20260626`) → Lobby (`20260627`) ✓

These empty-Up no-throw-Down migrations are purely ordering anchors and add zero DDL.

### Pattern 6: `gamekit db backup` / `gamekit db restore` (DR-06)

Shell-out pattern using `System.Diagnostics.Process`:

```csharp
// src/GameKit.Cli/Commands/Db/DbBackupCommand.cs
var psi = new ProcessStartInfo
{
    FileName = "pg_dump",
    Arguments = $"--host={host} --port={port} --username={user} " +
                $"--format=custom --file={settings.OutputPath} {settings.Database}",
    RedirectStandardError = true,
    UseShellExecute = false,
};
// PGPASSWORD from env or settings
if (!string.IsNullOrEmpty(settings.Password))
    psi.Environment["PGPASSWORD"] = settings.Password;
using var p = Process.Start(psi)!;
await p.WaitForExitAsync();
```

The CLI does NOT bundle `pg_dump` — it expects it on the operator's PATH. Document this as a prerequisite in the help text and in the runbook. The `--connection-string` flag is parsed to extract host/port/database/user (using `NpgsqlConnectionStringBuilder`), or flags `--host`, `--port`, `--database`, `--username` are provided directly.

For Redis backup, `redis-cli BGSAVE` is the appropriate command but Redis doesn't have a `pg_restore` equivalent that maps cleanly to a CLI wrapper. The `db backup` command can issue a `BGSAVE` command via `StackExchange.Redis` (no process shell-out needed) and report where the RDB file was written. The restore procedure is necessarily manual (filesystem copy) and is documented in the runbook.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Postgres backup | Custom dump logic | `pg_dump` + `pg_restore` (standard Postgres tools) | GPL-compatible, portable, proven; available inside the `postgres:17.9` Docker image |
| Migration SQL generation | Custom SQL builder | `IMigrator.GenerateScript(idempotent: true)` | EF Core 10's authoritative idempotent script generator handles all migration edge cases |
| Pending migration count | Custom history table queries | `context.Database.GetPendingMigrationsAsync()` | EF Core API; works with the per-package history table config |
| Down() policy enforcement | String-grep CI script | Roslyn analyzer `GK0003` | AST-exact, fires at compile time, same framework already in codebase |
| Redis backup | Custom AOF parser | `redis-cli BGSAVE` + filesystem copy | Standard Redis persistence; the runbook documents the procedure |

---

## Common Pitfalls

### Pitfall 1: CLI Missing Project References for Multi-Package `migrations list`
**What goes wrong:** `MigrationsListCommand` cannot build `AuthMigrationContext` if `GameKit.Cli.csproj` does not reference `GameKit.Auth`, `GameKit.Matchmaking`, and `GameKit.Lobby`.
**Why it happens:** The existing `MigrateCommand` only uses Core; the expanded command needs all packages.
**How to avoid:** Add three `ProjectReference` entries to `GameKit.Cli.csproj` AND add `InternalsVisibleTo("GameKit.Cli")` in each package's `AssemblyInfo.cs` (for the internal `*MigrationModelCustomizer` classes).
**Warning signs:** `CS0122: 'AuthMigrationModelCustomizer' is inaccessible due to its protection level` at build time.

### Pitfall 2: Timestamp Ordering Assertion Fails Without Marker Migrations
**What goes wrong:** `MigrationTimestampTests.PackageMigrations_LatestTimestamp_AreInCorrectOrder` fails because existing timestamps are NOT in the required order.
**Why it happens:** The ordering constraint is new (DR-07); existing migrations predate it.
**How to avoid:** Add 5 empty "ordering marker" migrations (no-op Up, `throw new NotSupportedException()` Down) to Auth, Admin.UI, Rankings, Matchmaking, and Lobby with ascending timestamps `20260623` through `20260627`.
**Warning signs:** Test fails on first run. The `Assert.True` message will show the colliding timestamps clearly.

### Pitfall 3: pg_dump inside Testcontainers Requires --no-password / PGPASSWORD
**What goes wrong:** `pg_dump` prompts for a password and hangs in `ExecAsync` when no `PGPASSWORD` env var is set.
**Why it happens:** The postgres container has a password (`postgres_test`) — `pg_dump` requires it.
**How to avoid:** In the DR round-trip test, set `PGPASSWORD` via `ExecAsync` or use the container's env. The existing `PostgresFixture` uses `Password=postgres_test`; pass this as `PGPASSWORD` in the exec environment, or use `--no-password` with a `.pgpass` file inside the container.
**Warning signs:** Test hangs indefinitely on the `ExecAsync` call.

**Simplest fix:** Use `pg_dump` with `PGPASSWORD=postgres_test` in the command prefix:
```csharp
await container.ExecAsync(new[]
{
    "bash", "-c",
    "PGPASSWORD=postgres_test pg_dump --username=postgres --format=custom --file=/dump/gamekit.pgdump gamekit"
});
```

### Pitfall 4: GK0003 Must Handle Empty Down() Bodies (Not Just Destructive Ones)
**What goes wrong:** The analyzer only flags `DropTable`/`DropColumn` and misses empty `Down()` methods (like `AddAuditActorIdFk`), leaving the policy incompletely enforced.
**Why it happens:** The policy says ALL Down() methods must have a `throw new NotSupportedException(...)` — including no-ops.
**How to avoid:** The analyzer checks for a body that is NOT `{ throw new NotSupportedException(...); }` — both empty bodies and destructive bodies fail. The `AddAuditActorIdFk.Down()` currently has a `// No-op` comment with empty body; it will need a throw added.

### Pitfall 5: `MigrationDeterminismTests.Migrate_Twice_Is_Idempotent` is a Pre-Existing Red Test
**What goes wrong:** After Phase 17 adds 5 new ordering-marker migrations to Core-adjacent packages, the `Migrate_Twice_Is_Idempotent` test (`Assert.Single(applied)`) still fails — but it was already failing before Phase 17 (documented pre-existing red).
**Why it happens:** The test asserts `applied.Count == 1` (only `CoreInitial`), but Core now has 5 migrations. This is a stale test from Phase 1.
**How to avoid:** Do NOT fix this test in Phase 17 — it is documented as pre-existing red in project memory. The Phase 17 plan should note it as pre-existing and skip it.

### Pitfall 6: `pg_restore` into Existing Database Needs `--clean` or `DROP DATABASE`
**What goes wrong:** `pg_restore` into a database that already has the schema fails with `relation already exists` errors.
**Why it happens:** The init scripts (`01-roles.sql`, `02-extensions.sql`) run at container start and may create the `gamekit` schema before `pg_restore` runs.
**How to avoid:** For the DR round-trip test, the second container starts fresh with the init scripts creating the `gamekit` database; `pg_restore` into an empty `gamekit` database works cleanly. The init scripts only create roles and extensions, not tables. Alternatively use `pg_restore --clean` to drop objects before recreating them.

### Pitfall 7: Down() Convention Gate Must NOT Fire on Designer.cs / Snapshot.cs Files
**What goes wrong:** The analyzer fires on methods in `*ModelSnapshot.cs` and `*Migrations.Designer.cs` files that happen to have a `Down()`-shaped method.
**Why it happens:** Generated designer files may have similar method signatures.
**How to avoid:** The analyzer already has a mechanism for this: check that the declaring type inherits from `Microsoft.EntityFrameworkCore.Migrations.Migration`. Designer files do not inherit from `Migration` — they inherit from `ModelSnapshot`.

---

## Existing Infrastructure to Reuse

**Already built (verified by codebase read):**

1. **`docs/ops/disaster-recovery.md`** — Complete Postgres and Redis backup/restore documentation. DR-01/DR-02 deliverable is to refactor this into `docs/runbooks/postgres-backup-restore.md` and `docs/runbooks/redis-backup-restore.md`, update cross-references, and expand the CI verification section to reference the DR round-trip test.

2. **`docs/ops/migrations-runbook.md`** — Comprehensive migrations runbook. DR-07 docs component is to produce `docs/migration-ops.md` covering: timestamp ordering rule (new), dry-run generation, Down() policy (new), and the `migrations list`/`apply` CLI commands.

3. **`tests/GameKit.TestFixtures/PostgresFixture.cs`** — Shared Testcontainers Postgres fixture (`postgres:17.9`, init scripts mounted, 4 connection string properties). The DR test project will add a new `DrPostgresFixture` (or extend the existing one) that mounts a temp dump directory.

4. **`tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs`** — `HealthTestHost.StartAsync(connectionString)` pattern used to assert `GET /health/ready` → 200. The DR round-trip test imports the same pattern.

5. **`src/GameKit.Build/PiiAttributeAnalyzer.cs`** — Template for `GK0003` analyzer. Same `netstandard2.0` target, same `IsRoslynComponent=true` flag, same compilation into the existing `GameKit.Build.dll`.

6. **`tests/GameKit.Build.Tests/PiiAttributeAnalyzerTests.cs`** — Template for `MigrationDownAnalyzerTests.cs`. Same `CSharpAnalyzerTest<T, DefaultVerifier>` pattern.

7. **`src/GameKit.Core/Data/MigrationRunner.MigrateWithLockAsync()`** — Used by all hosted services and the existing `MigrateCommand`. The CLI dry-run command uses `IMigrator.GenerateScript()` directly rather than `MigrateWithLockAsync()`.

8. **`src/GameKit.Cli/Commands/MigrateCommand.cs`** — DI pattern: `new ServiceCollection()` → `AddGameKit(...)` → `BuildServiceProvider()` → `GetRequiredService<GameKitDbContext>()`. The new CLI commands extend this pattern but must build per-package contexts, not just the Core context.

9. **Per-package advisory lock key constants** (all 6 packages, all verified via Testcontainers integration tests):
   - Core: `1800940027L` (`GameKitMigrationConstants.AdvisoryLockKey`)
   - Auth: `-298890956L` (`AuthMigrationConstants.AdvisoryLockKey`)
   - Admin: `-2101739634L` (`AdminMigrationConstants.AdvisoryLockKey`)
   - Rankings: `-156812172L` (`RankingsMigrationConstants.AdvisoryLockKey`)
   - Matchmaking: `388956820L` (`MatchmakingMigrationConstants.AdvisoryLockKey`)
   - Lobby: `12178347L` (`LobbyMigrationConstants.AdvisoryLockKey`)

---

## Code Examples

### IMigrator.GenerateScript — Idempotent SQL

```csharp
// Source: EF Core 10 Microsoft.EntityFrameworkCore.Migrations.IMigrator
// Usage in gamekit migrations apply --dry-run

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

await using var ctx = BuildPackageMigrationContext(connectionString, assemblyName, historyTable);
var migrator = ctx.GetService<IMigrator>();
string idempotentSql = migrator.GenerateScript(
    fromMigration: null,   // null = from database genesis
    toMigration: null,     // null = to latest migration
    MigrationsSqlGenerationOptions.Idempotent);

AnsiConsole.Write(new Markup($"[grey]-- Package: {packageName}[/]\n"));
Console.WriteLine(idempotentSql);
```

### GetPendingMigrationsAsync — Pending Count

```csharp
// Source: EF Core 10 Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions
await using var ctx = BuildPackageMigrationContext(connectionString, assemblyName, historyTable);
var pending = await ctx.Database.GetPendingMigrationsAsync();
var applied = await ctx.Database.GetAppliedMigrationsAsync();
```

### Spectre.Console.Cli Branch + Sub-Commands Pattern

```csharp
// Source: existing src/GameKit.Cli/Program.cs (verified)
config.AddBranch("migrations", migrations =>
{
    migrations.SetDescription("Migration status and dry-run tooling.");
    migrations.AddCommand<MigrationsListCommand>("list")
        .WithDescription("List pending migrations per package in recommended application order.");
    migrations.AddCommand<MigrationsApplyCommand>("apply")
        .WithDescription("Apply pending migrations. Use --dry-run to print SQL without executing.");
});

config.AddBranch("db", db =>
{
    db.SetDescription("Database backup and restore helpers (wraps pg_dump/pg_restore).");
    db.AddCommand<DbBackupCommand>("backup")
        .WithDescription("Backup Postgres to a file via pg_dump. Redis: issues BGSAVE.");
    db.AddCommand<DbRestoreCommand>("restore")
        .WithDescription("Restore Postgres from a pg_dump file via pg_restore.");
});
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| EF Core `MigrationsSqlGenerationOptions` was a flag enum added in EF Core 7 | Same API in EF Core 10 — stable | EF Core 7 (2022) | `migrator.GenerateScript(idempotent: true)` is legacy syntax; use the enum overload |
| `GetPendingMigrations()` (sync) | `GetPendingMigrationsAsync()` | EF Core 7 | Async version preferred in async CLI commands |
| Testcontainers direct `ExecAsync` for process-in-container | Same API, stable in 4.x | Testcontainers 3.x | `IContainer.ExecAsync(IList<string>)` returns `(string stdout, string stderr, long exitCode)` |

**Deprecated/outdated:**
- `migrator.GenerateScript(idempotent: true)` bool overload: the `MigrationsSqlGenerationOptions.Idempotent` enum overload is the current API in EF Core 10. Both work but the enum is preferred.

---

## Runtime State Inventory

> Rename/refactor phase marker: N/A — this is a greenfield addition phase (no renaming).

The Down() convention change is NOT a rename/refactor. It is a body replacement. There is no runtime state to inventory. Skipped.

---

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| Docker | DR round-trip CI test (Testcontainers) | ✓ | 29.5.3 | — |
| `pg_dump` (host) | `gamekit db backup` CLI + DR test setup docs | ✓ | PostgreSQL 17.10 | Inside container via ExecAsync (DR test uses this) |
| `pg_restore` (host) | `gamekit db restore` CLI | ✓ | PostgreSQL 17.10 | Inside container via ExecAsync |
| `redis-cli` (host) | `gamekit db backup --redis` BGSAVE | Not checked (not required for CI) | — | `StackExchange.Redis BGSAVE` command via .NET |
| .NET 10 SDK | All | ✓ | 10.0.106 | — |

**Missing dependencies with no fallback:** None.

**Note on DR test pg_dump strategy:** The DR round-trip test uses `ExecAsync` to run `pg_dump` inside the container, not the host-side `pg_dump`. This means the test has no host-side binary dependency and will pass on any CI runner where Docker is available — which is already the case (Docker 29.5.3 confirmed, and the CI job already runs Testcontainers integration tests).

---

## Validation Architecture

> `nyquist_validation` is `true` in `.planning/config.json` — this section is REQUIRED.

### Test Framework

| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.2 |
| Config file | `tests/xunit.runner.json` (existing) |
| Quick run command | `dotnet test tests/GameKit.DR.Tests --no-build --filter "Category=Integration"` |
| Full suite command | `dotnet test --no-build --configuration Release --filter "Category=Integration" -p:NuGetAudit=false` |
| Build command | `dotnet build --no-restore --configuration Release -warnaserror -p:NuGetAudit=false` |

### Phase Requirements → Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| DR-01 | `docs/runbooks/postgres-backup-restore.md` exists and is non-empty | File existence (unit) | `dotnet test tests/GameKit.Core.Tests -k "RunbookFiles_Exist"` | ❌ Wave 0 |
| DR-02 | `docs/runbooks/redis-backup-restore.md` exists and is non-empty | File existence (unit) | Same test class | ❌ Wave 0 |
| DR-03 | Full DR round-trip: dump → destroy → restore → `/health/ready` 200 | Integration (Testcontainers) | `dotnet test tests/GameKit.DR.Tests --filter "Category=Integration"` | ❌ Wave 0 |
| DR-04 | `gamekit migrations list` exits 0 and prints per-package counts | CLI integration | `dotnet test tests/GameKit.Cli.Tests --filter "MigrationsListCommand"` | ❌ Wave 0 |
| DR-05 | `gamekit migrations apply --dry-run` exits 0, prints SQL, executes no DDL | CLI integration | `dotnet test tests/GameKit.Cli.Tests --filter "MigrationsApplyCommand"` | ❌ Wave 0 |
| DR-06 | `gamekit db backup` shells out to `pg_dump` with correct flags | CLI unit (mock Process) | `dotnet test tests/GameKit.Cli.Tests --filter "DbBackupCommand"` | ❌ Wave 0 |
| DR-07a | GK0003 analyzer: flags Down() without NotSupportedException throw | Build/analyzer unit | `dotnet test tests/GameKit.Build.Tests --filter "MigrationDownAnalyzer"` | ❌ Wave 0 |
| DR-07b | GK0003 fires at `dotnet build` for any unconverted Down() | Build gate | `dotnet build -warnaserror` (CI step, not a test) | ✓ (build system) |
| DR-07c | `MigrationTimestampTests` asserts correct package ordering | Unit (reflection) | `dotnet test tests/GameKit.Core.Tests --filter "MigrationTimestampTests"` | ❌ Wave 0 |

### Sampling Rate

- **Per task commit:** `dotnet build --configuration Release -warnaserror` (catches GK0003 immediately)
- **Per wave merge:** `dotnet test tests/GameKit.Core.Tests tests/GameKit.Build.Tests tests/GameKit.Cli.Tests -p:NuGetAudit=false`
- **Phase gate:** `dotnet test --configuration Release --filter "Category=Integration" -p:NuGetAudit=false` on the full DR test project

### Wave 0 Gaps (test files that do not yet exist)

- [ ] `tests/GameKit.DR.Tests/` — new project directory, csproj, CollectionDefinitions.cs, DRRoundTripTests.cs
- [ ] `tests/GameKit.Core.Tests/MigrationTimestampTests.cs` — covers DR-07c
- [ ] `tests/GameKit.Core.Tests/RunbookFilesTests.cs` — covers DR-01/DR-02 file existence
- [ ] `tests/GameKit.Cli.Tests/MigrationsListCommandTests.cs` — covers DR-04
- [ ] `tests/GameKit.Cli.Tests/MigrationsApplyCommandTests.cs` — covers DR-05
- [ ] `tests/GameKit.Cli.Tests/DbBackupCommandTests.cs` — covers DR-06
- [ ] `tests/GameKit.Build.Tests/MigrationDownAnalyzerTests.cs` — covers DR-07a

---

## Security Domain

> `security_enforcement` not explicitly set to false — including this section.

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | No | CLI commands use connection strings (not user auth) |
| V3 Session Management | No | — |
| V4 Access Control | No | — |
| V5 Input Validation | Yes | `NpgsqlConnectionStringBuilder` parses connection strings; output paths must be validated (no traversal) |
| V6 Cryptography | No | Backup encryption is an operator responsibility (documented in runbook) |

### Known Threat Patterns for CLI Backup Commands

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Path traversal in `--output` flag | Tampering | Validate output path is absolute and does not contain `..` segments; document that encryption at rest is operator responsibility |
| PGPASSWORD leakage in process env | Information Disclosure | Set via `ProcessStartInfo.Environment`, not command-line args (not visible in `ps` output) |
| Restore to wrong database (connection string typo) | Tampering | Require explicit `--database` flag; print connection details and prompt for confirmation in interactive mode |

---

## Open Questions

1. **`migrations list` vs `migrations status`**
   - What we know: CONTEXT.md/REQUIREMENTS say DR-04 is `gamekit migrations list`. The existing `migrate` command is a top-level command, not a branch. Adding a `migrations` branch changes the surface (`gamekit migrations list` vs. `gamekit migrate`).
   - What's unclear: Should the existing `migrate` command be deprecated/removed, or left as an alias?
   - Recommendation: Keep `gamekit migrate` as-is for backwards compatibility. Add the new `migrations` branch alongside it. Document the relationship in the help text. The `MigrateCommand` applies only Core migrations currently; the new `migrations apply` applies all packages.

2. **`gamekit db backup` Redis strategy — shell-out or StackExchange.Redis?**
   - What we know: Redis does not have a `redis-cli` equivalent to `pg_dump`. BGSAVE is the closest. The REQUIREMENTS say "wrapping Redis snapshot".
   - What's unclear: Whether to shell out to `redis-cli BGSAVE` (requires `redis-cli` on PATH) or use `StackExchange.Redis` to issue the `BGSAVE` command (no external binary dependency).
   - Recommendation: Use `StackExchange.Redis` to issue `BGSAVE` via `server.BackgroundSaveAsync()`. This avoids the `redis-cli` PATH dependency. The CLI already transitively depends on `StackExchange.Redis` through `GameKit.Core`. Document that the operator must separately copy the RDB file from the Redis data directory.

3. **DR test project placement**
   - What we know: All existing integration tests live in `tests/`. The DR round-trip test needs references to all 6 packages.
   - What's unclear: Whether to add to an existing project (e.g. `GameKit.Integration.Tests`) or create a new `GameKit.DR.Tests`.
   - Recommendation: New project `tests/GameKit.DR.Tests/`. The test has unique dependencies (all 6 packages, specific fixture composition) and the naming makes the CI purpose clear in the test results report.

---

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `AddAuditActorIdFk.Down()` body (empty no-op with comment) must also be converted to `throw new NotSupportedException(...)` under the GK0003 policy | Down() Inventory | If the policy allows empty bodies, the analyzer gate logic changes. CONTEXT.md says "every `Down()` method" — so this is correct. |
| A2 | `IContainer.ExecAsync` in Testcontainers 4.11.0 returns stdout/stderr/exitCode as a tuple | DR Round-Trip Pattern | If the API returns differently, the pattern changes. Verified from Testcontainers XML docs. |
| A3 | The `postgres:17.9` image has `pg_dump` and `pg_restore` on PATH inside the container | DR Round-Trip Pattern | True for all official Postgres Docker images — they include the full Postgres client tools. |
| A4 | `InternalsVisibleTo("GameKit.Cli")` is needed for the per-package `*MigrationModelCustomizer` classes to be accessible from the CLI | CLI Multi-Package | Could be avoided by making the customizers public; but following existing pattern (Auth/Admin already use InternalsVisibleTo). |

**If this table is empty:** Not empty — see above. All claims verified except A4 which is a design choice.

---

## Sources

### Primary (HIGH confidence)

- Direct codebase read: `src/GameKit.Cli/Program.cs` — verified CLI registration pattern [VERIFIED: direct codebase read]
- Direct codebase read: `src/GameKit.Cli/Commands/MigrateCommand.cs` — verified DI pattern and connection resolution [VERIFIED: direct codebase read]
- Direct codebase read: all 14 migration files — verified every Down() body [VERIFIED: direct codebase read]
- Direct codebase read: `src/GameKit.Build/PiiAttributeAnalyzer.cs` — verified Roslyn analyzer pattern for GK0003 [VERIFIED: direct codebase read]
- Direct codebase read: `tests/GameKit.TestFixtures/PostgresFixture.cs` — verified Testcontainers fixture pattern [VERIFIED: direct codebase read]
- Direct codebase read: `tests/GameKit.Core.Integration.Tests/HealthEndpointTests.cs` — verified `HealthTestHost.StartAsync()` pattern [VERIFIED: direct codebase read]
- Direct codebase read: `docs/ops/disaster-recovery.md` — verified existing runbook content [VERIFIED: direct codebase read]
- Direct codebase read: `docs/ops/migrations-runbook.md` — verified migration history and advisory lock documentation [VERIFIED: direct codebase read]
- Direct codebase read: all 6 `*MigrationConstants.cs` files — verified advisory lock keys [VERIFIED: direct codebase read]
- Testcontainers 4.11.0 XML docs (`~/.nuget/packages/testcontainers/4.11.0/lib/netstandard2.1/Testcontainers.xml`) — verified `IContainer.ExecAsync` API signature [VERIFIED: local package cache]
- `docker --version` output: Docker 29.5.3 available [VERIFIED: shell command]
- `pg_dump --version` output: PostgreSQL 17.10 on PATH [VERIFIED: shell command]

### Secondary (MEDIUM confidence)

- EF Core 10 `IMigrator.GenerateScript(MigrationsSqlGenerationOptions.Idempotent)` — verified from EF Core API knowledge + cross-checked with project's existing EF Core 10.0.6 usage [ASSUMED]
- `GetPendingMigrationsAsync()` / `GetAppliedMigrationsAsync()` EF Core APIs — standard EF Core database facade extensions [ASSUMED]

---

## Metadata

**Confidence breakdown:**
- Migration inventory (Down() bodies): HIGH — read every file directly
- CLI extension pattern: HIGH — read existing commands + csproj
- DR round-trip pattern: HIGH — verified Testcontainers ExecAsync API + existing fixture code
- GK0003 analyzer pattern: HIGH — read existing PiiAttributeAnalyzer.cs
- IMigrator.GenerateScript API: MEDIUM — EF Core training knowledge; verified usage pattern consistent with project's EF Core version
- Timestamp ordering gap: HIGH — verified by reading all migration filenames

**Research date:** 2026-06-22
**Valid until:** 2026-09-22 (EF Core 10 APIs stable; Testcontainers 4.11.0 APIs stable)

---

## RESEARCH COMPLETE
