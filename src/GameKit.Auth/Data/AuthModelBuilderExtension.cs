// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Data.Configurations;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Data;

/// <summary>
/// Sibling-package <see cref="IModelBuilderExtension"/> that contributes the three Auth entities to
/// the shared <c>GameKitDbContext</c> model. Registered via <c>TryAddEnumerable</c> in
/// <c>AuthBuilderExtensions.AddAuth</c> (plan 02-03).
/// </summary>
internal sealed class AuthModelBuilderExtension : IModelBuilderExtension
{
    /// <inheritdoc />
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
