// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Admin.Tests")]
[assembly: InternalsVisibleTo("GameKit.Admin.Integration.Tests")]

namespace GameKit.Admin.UI;

/// <summary>Marker type so other assemblies can pin a reference to <c>GameKit.Admin.UI</c> at compile time.</summary>
internal static class AdminUiMarker { }
