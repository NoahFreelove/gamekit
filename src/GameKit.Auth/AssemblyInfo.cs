// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Auth.Tests")]
[assembly: InternalsVisibleTo("GameKit.Auth.Integration.Tests")]

namespace GameKit.Auth;

/// <summary>Marker type so other assemblies can pin a reference to GameKit.Auth at compile time.</summary>
internal static class AuthMarker { }
