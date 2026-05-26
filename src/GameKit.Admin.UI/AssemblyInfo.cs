// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Admin.Tests")]
[assembly: InternalsVisibleTo("GameKit.Admin.Integration.Tests")]
// Plan 03-11: grants the GameKit.Cli dotnet-tool access to AdminModelBuilderExtension
// (internal sealed — a reflection-scanned IModelBuilderExtension the CLI needs at
// migration apply time, but that consumers of the public NuGet surface must not call
// directly). Keeps the Admin package's public surface minimal while unblocking the
// `dotnet gamekit admin create` bootstrap command which must register the extension in
// its ServiceCollection. Both names are required:
//   * "gamekit" — the actual AssemblyName of src/GameKit.Cli/GameKit.Cli.csproj
//     (driven by its <AssemblyName>gamekit</AssemblyName> to match the ToolCommandName);
//     InternalsVisibleTo is checked against assembly name, so this grant is the runtime
//     gate.
//   * "GameKit.Cli" — the csproj + RootNamespace name; kept for plan 03-11 verification
//     literals and for any future restructuring that aligns AssemblyName with csproj name.
[assembly: InternalsVisibleTo("gamekit")]
[assembly: InternalsVisibleTo("GameKit.Cli")]
// Plan 06-06: OpenApi contract tests need AdminMigrationModelCustomizer to compose Admin
// entity configurations into the runtime DbContext (FOLLOW-UP-02-03-01 ApplicationServiceProvider
// workaround). Same precedent as the Admin.Integration.Tests grant above.
[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]

namespace GameKit.Admin.UI;

/// <summary>Marker type so other assemblies can pin a reference to <c>GameKit.Admin.UI</c> at compile time.</summary>
internal static class AdminUiMarker { }
