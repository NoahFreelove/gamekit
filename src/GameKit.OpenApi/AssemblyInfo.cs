// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// Description: GameKit.OpenApi — single combined /openapi/v1.json doc
// transformer pipeline. Phase 6.
//
// Note: [assembly: AssemblyDescription] is NOT declared here — the SDK
// auto-emits AssemblyDescriptionAttribute from <Description> in the
// csproj (see obj/.../GameKit.OpenApi.AssemblyInfo.cs at build time).
// Declaring it manually here would produce CS0579 duplicate-attribute
// errors. The csproj <Description> is the canonical source.

// Plan 06-06 transformer integration tests probe internal types
// (GameKitOpenApiOptions, GameKitInfoTransformer, GameKitBearerSchemeTransformer).
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]
