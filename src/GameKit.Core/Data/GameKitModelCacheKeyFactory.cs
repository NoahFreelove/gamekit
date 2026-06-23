// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Core.Data;

/// <summary>
/// EF Core <see cref="IModelCacheKeyFactory"/> that incorporates the set of registered
/// <see cref="IModelBuilderExtension"/> types into the model cache key.
/// </summary>
/// <remarks>
/// <para>
/// The default EF Core relational model cache key is keyed only by
/// <c>(contextType, modelCustomizerType, designTime)</c>. Because every
/// <c>GameKitDbContext</c> instance — whether a Core-only migration context or a
/// full-runtime context that includes Auth, Rankings, Matchmaking, and Lobby entities —
/// uses the same context type and model customizer type, the first-built model (typically
/// Core-only, built during the migration step) is incorrectly reused for the full-runtime
/// context, which causes <c>InvalidOperationException: Cannot create a DbSet for 'Ladder'</c>
/// (and any other sibling-package entity type).
/// </para>
/// <para>
/// This factory reads the registered <see cref="IModelBuilderExtension"/> types from
/// <c>CoreOptionsExtension.ApplicationServiceProvider</c> (set by <c>UseApplicationServiceProvider</c>)
/// and appends them to the cache key. Core-only migration contexts have no app provider or an
/// app provider with no extensions → empty extension list. Full-runtime contexts have all
/// sibling-package extensions registered → non-empty list. The resulting cache keys are distinct,
/// so EF builds a correct, fully-populated model for each configuration.
/// </para>
/// <para>
/// Registered via <c>dbOpts.ReplaceService&lt;IModelCacheKeyFactory, GameKitModelCacheKeyFactory&gt;()</c>
/// inside <c>AddGameKit</c>'s <c>AddDbContext</c> call — this is the correct EF Core mechanism
/// for replacing infrastructure-level services on a per-context-options basis.
/// </para>
/// </remarks>
public sealed class GameKitModelCacheKeyFactory : IModelCacheKeyFactory
{
    /// <inheritdoc />
    public object Create(DbContext context, bool designTime)
    {
        // Read the application service provider set by UseApplicationServiceProvider(sp).
        // This is the app's IServiceProvider that contains all TryAddEnumerable IModelBuilderExtension
        // registrations from sibling packages (AddAuth, AddRankings, AddMatchmaking, AddLobby).
        var appProvider = context.GetService<IDbContextOptions>()
            .FindExtension<CoreOptionsExtension>()?
            .ApplicationServiceProvider;

        string[] extensionTypes;
        if (appProvider is null)
        {
            // Design-time / direct-construction migration paths: no app provider → Core-only model.
            extensionTypes = [];
        }
        else
        {
            extensionTypes = appProvider
                .GetServices<IModelBuilderExtension>()
                .Select(e => e.GetType().FullName ?? e.GetType().Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        return new GameKitModelCacheKey(context.GetType(), designTime, extensionTypes);
    }

    private sealed class GameKitModelCacheKey : IEquatable<GameKitModelCacheKey>
    {
        private readonly Type _contextType;
        private readonly bool _designTime;
        private readonly string[] _extensionTypes;

        public GameKitModelCacheKey(Type contextType, bool designTime, string[] extensionTypes)
        {
            _contextType = contextType;
            _designTime = designTime;
            _extensionTypes = extensionTypes;
        }

        public bool Equals(GameKitModelCacheKey? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return _contextType == other._contextType
                && _designTime == other._designTime
                && _extensionTypes.SequenceEqual(other._extensionTypes, StringComparer.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as GameKitModelCacheKey);

        public override int GetHashCode()
        {
            var hash = HashCode.Combine(_contextType, _designTime);
            foreach (var ext in _extensionTypes)
                hash = HashCode.Combine(hash, ext.GetHashCode(StringComparison.Ordinal));
            return hash;
        }
    }
}
