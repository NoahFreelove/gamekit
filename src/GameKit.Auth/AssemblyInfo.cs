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
// Plan 06-05 (SessionsLifecycleObserverTests): the cross-package lifecycle observer test
// composes Core + Auth + Rankings + Presence in one hybrid host and needs
// AuthModelBuilderExtension to compose all three packages' entities into a single test
// DbContext (mirrors the Admin.Integration.Tests IVT precedent above).
[assembly: InternalsVisibleTo("GameKit.Presence.Integration.Tests")]
// Plan 06-06: OpenApi contract tests (D-09 EndpointDataSource enumeration) boot a hybrid
// host composing Core + Auth + Rankings + Matchmaking + Presence + Admin + OpenApi so the
// contract test enumerates the full sample's endpoint surface. The OpenApiTestApp's
// runtime IModelCustomizer applies AuthMigrationModelCustomizer / Auth entity configurations
// to bypass the FOLLOW-UP-02-03-01 ApplicationServiceProvider capture issue. Mirrors the
// Admin.Integration.Tests + Presence.Integration.Tests grants above.
[assembly: InternalsVisibleTo("GameKit.OpenApi.Integration.Tests")]

namespace GameKit.Auth;

/// <summary>Marker type so other assemblies can pin a reference to GameKit.Auth at compile time.</summary>
internal static class AuthMarker { }
