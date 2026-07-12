// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Auth.Data.Configurations;
using GameKit.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Auth.Data;

/// <summary>
/// Sibling-package <see cref="IModelBuilderExtension"/> that contributes Auth entities to
/// the shared <c>GameKitDbContext</c> model. Registered via <c>TryAddEnumerable</c> in
/// <c>AuthBuilderExtensions.AddAuth</c> (plan 02-03).
/// </summary>
/// <remarks>
/// Entity list: <c>PlayerIdentity</c>, <c>PlayerCredential</c>, <c>RefreshToken</c>
/// (plan 02-03), and <c>AccountMerge</c> (plan 10-02, AUTH-24). Adding a new Auth entity
/// requires updating both this class and <c>AuthMigrationModelCustomizer</c>.
/// </remarks>
internal sealed class AuthModelBuilderExtension : IModelBuilderExtension
{
    /// <inheritdoc />
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PlayerIdentityConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        // Plan 10-02: AccountMerge state-machine table (AUTH-24).
        // Without this entry, ctx.Set<AccountMerge>() throws InvalidOperationException at runtime
        // and AuthGdprDeleteExtension (SEC-04 GAP 2) cannot delete account_merges rows.
        modelBuilder.ApplyConfiguration(new AccountMergeConfiguration());
    }
}
