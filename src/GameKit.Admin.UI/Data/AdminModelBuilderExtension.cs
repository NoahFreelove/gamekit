// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Admin.UI.Data.Configurations;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Admin.UI.Data;

/// <summary>
/// Sibling-package <see cref="IModelBuilderExtension"/> that contributes the <c>AdminUser</c>
/// entity to the shared <c>GameKitDbContext</c> model. Registered via <c>TryAddEnumerable</c>
/// in <c>AdminBuilderExtensions.AddGameKitAdmin</c> (plan 03-03). Resolved lazily from the
/// application service provider via <c>CoreOptionsExtension.ApplicationServiceProvider</c>
/// (FOLLOW-UP-02-03-01 pattern — closed by plan 02-08).
/// </summary>
internal sealed class AdminModelBuilderExtension : IModelBuilderExtension
{
    /// <inheritdoc />
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new AdminUserConfiguration());
    }
}
