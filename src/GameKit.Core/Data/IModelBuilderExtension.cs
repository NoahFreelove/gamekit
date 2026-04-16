// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using Microsoft.EntityFrameworkCore;

namespace GameKit.Core.Data;

/// <summary>
/// Contract sibling GameKit packages implement to contribute entities to the shared
/// <c>GameKitDbContext</c>. Core's <c>GameKitModelCustomizer</c> iterates every registered
/// implementation during <c>OnModelCreating</c>, so siblings never need to subclass the context.
/// </summary>
/// <remarks>
/// Register at startup via:
/// <code>
/// services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IModelBuilderExtension, AuthModelBuilderExtension&gt;());
/// </code>
/// Implementations must only ADD entities or ADD FK columns referencing existing Core entities.
/// Never modify Core-owned entity configurations — that is reserved for the Core package itself
/// (enforced by code review and documented in OPS-09).
/// </remarks>
public interface IModelBuilderExtension
{
    /// <summary>Apply this package's entity configurations to the shared model.</summary>
    /// <param name="modelBuilder">The <see cref="ModelBuilder"/> in the <c>GameKitDbContext.OnModelCreating</c> pipeline.</param>
    void ApplyTo(ModelBuilder modelBuilder);
}
