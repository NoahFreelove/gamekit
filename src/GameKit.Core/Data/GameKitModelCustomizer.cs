// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GameKit.Core.Data;

/// <summary>
/// EF Core <see cref="IModelCustomizer"/> replacement that invokes every DI-registered
/// <see cref="IModelBuilderExtension"/>. Sibling GameKit packages register their extension
/// via <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IModelBuilderExtension, FooModelBuilderExtension&gt;())</c>
/// and this customizer picks them up when <see cref="GameKitDbContext"/> builds its model.
/// </summary>
/// <remarks>
/// Inherits <see cref="RelationalModelCustomizer"/> rather than implementing
/// <see cref="IModelCustomizer"/> directly so the base customizer's relational work
/// (convention application, etc.) still runs. Sibling extensions run AFTER the base —
/// the Core entity surface is established first.
///
/// Tracked deprecation: <see href="https://github.com/dotnet/efcore/issues/30061"/> proposes
/// <c>ConfigureDbModel&lt;TContext&gt;()</c> as the long-term replacement, still <c>needs-design</c>
/// as of 2026-04-15. When the new API ships, this single class is the migration point.
/// </remarks>
public sealed class GameKitModelCustomizer : RelationalModelCustomizer
{
    private readonly IEnumerable<IModelBuilderExtension> _extensions;

    /// <summary>Constructs the customizer with the injected sibling-extension collection.</summary>
    public GameKitModelCustomizer(
        ModelCustomizerDependencies dependencies,
        IEnumerable<IModelBuilderExtension> extensions) : base(dependencies)
    {
        _extensions = extensions;
    }

    /// <inheritdoc />
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        foreach (var extension in _extensions)
            extension.ApplyTo(modelBuilder);
    }
}
