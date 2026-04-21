// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("GameKit.Auth.Tests")]
[assembly: InternalsVisibleTo("GameKit.Auth.Integration.Tests")]
// Plan 03-06: Admin.Integration.Tests derives a runtime model customizer that composes Core +
// Auth + Admin entity configurations to bypass the FOLLOW-UP-02-03-01 ApplicationServiceProvider
// path which captures the wrong service provider under Host.CreateDefaultBuilder +
// ConfigureWebHostDefaults. Auth's entity configurations are internal sealed; granting
// InternalsVisibleTo lets the test customizer apply them directly.
[assembly: InternalsVisibleTo("GameKit.Admin.Integration.Tests")]

namespace GameKit.Auth;

/// <summary>Marker type so other assemblies can pin a reference to GameKit.Auth at compile time.</summary>
internal static class AuthMarker { }
