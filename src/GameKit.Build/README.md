<!--
SPDX-License-Identifier: Apache-2.0
Copyright (c) 2026 GameKit contributors
-->

# GameKit.Build

**Build tooling only — NEVER published as a NuGet package.**

`GameKit.Build` is a Roslyn incremental source generator that emits a per-assembly
`internal static partial class GameKitMarker` carrying:

- `public const string GameKitVersion` — MinVer-resolved `$(Version)` of the build.
- `public const string AssemblyName`   — compile-time `$(AssemblyName)`.

These constants are consumed at runtime by `GameKitVersionAssertionHostedService`
(Plan 06-02) to fail fast on any cross-assembly version mismatch in the
coordinated release train (D-15, OPS-04, OPS-05).

## Consumption

Every `src/GameKit.*/*.csproj` (Core, Auth, Rankings, Matchmaking, Admin.UI,
Presence, OpenApi — the 7 shipped packages per D-22) references this project
as a Roslyn analyzer:

```xml
<ItemGroup>
  <ProjectReference Include="..\GameKit.Build\GameKit.Build.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

`OutputItemType="Analyzer"` routes the project output through Roslyn's
analyzer load path (rather than as a regular assembly reference).
`ReferenceOutputAssembly="false"` ensures the consumer does NOT take a
binary dependency on the generator DLL — only the generator runs, only
the generated source flows into the consuming compilation.

## Why this is BUILD TOOLING ONLY

- `IsPackable=false` + `IncludeBuildOutput=false` in the csproj
  short-circuit `dotnet pack`. There is no scenario in which a consumer
  of `GameKit.*` packages should ever see `GameKit.Build.dll` on their
  disk — the generator runs against GameKit's own builds in the GameKit
  CI release train, and the generated `GameKitMarker.g.cs` flows into
  each shipped `GameKit.*.dll` as ordinary IL.

- `TargetFramework=netstandard2.0` is MANDATORY: the Roslyn compiler
  host loads analyzers into a netstandard2.0 AssemblyLoadContext.
  Targeting `net10.0` here would surface
  `"Generator 'GameKitVersionGenerator' failed to initialize"` at every
  consumer build (06-PATTERNS Critical Misuse Warning #3).

- `ManagePackageVersionsCentrally=false` keeps the
  `Microsoft.CodeAnalysis.CSharp` pin inline. The analyzer compiler API
  surface is tightly coupled to the IIncrementalGenerator contract this
  project emits; a CPM pin bump would silently change the generator
  contract (06-PATTERNS Critical Misuse Warning #4).

## MSBuild plumbing the generator depends on

- `$(Version)` — MinVer-resolved at build time. Exposed to the generator
  via `<CompilerVisibleProperty Include="Version" />` in
  `Directory.Build.props` (D-23, Pitfall 1). Without this property the
  generator emits a `"0.0.0"` fallback stamp.

- `$(AssemblyName)` — supplied by the SDK from each consumer's csproj.
  Read via `IIncrementalGenerator.CompilationProvider`.

The generator gates emission on `AssemblyName.StartsWith("GameKit.",
StringComparison.Ordinal)` so it cannot accidentally inject the marker
into a non-GameKit consumer's compilation.
