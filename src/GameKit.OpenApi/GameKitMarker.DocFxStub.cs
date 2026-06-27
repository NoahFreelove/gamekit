// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

// Compile-only stand-in for the GameKit.Build-generated
// GameKit.OpenApi.Internal.GameKitMarker. Compiled ONLY during docfx's metadata
// pass (DocFxMetadata=true, set in docfx.json), which cannot load the GameKit.Build
// Roslyn source generator (docfx reports FailedToResolveAnalyzer), leaving the real
// generated GameKitMarker absent and GameKitInfoTransformer failing to compile (CS0234).
//
// This file is EXCLUDED from every real build (see GameKit.OpenApi.csproj), where the
// source generator emits the authentic, MinVer-stamped GameKitMarker instead — so the
// placeholder values below never ship and never appear in the generated API reference
// (GameKitMarker is internal; includePrivateMembers=false).
namespace GameKit.OpenApi.Internal;

internal static class GameKitMarker
{
    public const string GameKitVersion = "0.0.0-docfx";
    public const string AssemblyName = "GameKit.OpenApi";
}
