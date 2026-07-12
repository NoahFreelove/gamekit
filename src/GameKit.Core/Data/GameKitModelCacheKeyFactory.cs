// SPDX-License-Identifier: Apache-2.0
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
/// <c>(contextType, modelCustomizerType, designTime)</c>. When integration tests run both
/// migration contexts and the full-runtime context in the same process, a Core-only migration
/// context can build a model first and have it incorrectly reused for the full-runtime context
/// (which includes Auth, Rankings, Matchmaking, and Lobby entities), causing
/// <c>InvalidOperationException: Cannot create a DbSet for 'Ladder'</c> and similar errors.
/// </para>
/// <para>
/// This factory appends the registered <see cref="IModelBuilderExtension"/> types to the cache
/// key, making migration-context and runtime-context cache entries distinct. Core-only migration
/// contexts have no app provider (or an app provider with no extensions) → empty extension list.
/// Full-runtime contexts have all sibling-package extensions registered → non-empty list.
/// </para>
/// <para>
/// <b>Registration — test fixtures only:</b> This factory is registered in integration-test
/// fixtures (e.g., <c>GdprDeleteCoverageTests</c>) via:
/// <code>
/// dbOpts.ReplaceService&lt;IModelCacheKeyFactory, GameKitModelCacheKeyFactory&gt;()
/// </code>
/// It is <b>NOT</b> registered by <c>AddGameKit()</c> in production.
/// </para>
/// <para>
/// <b>Why production does not need it:</b> In a production deployment each migration context
/// uses a distinct <see cref="Microsoft.EntityFrameworkCore.Infrastructure.IModelCustomizer"/>
/// implementation (<c>AuthMigrationModelCustomizer</c>, <c>MatchmakingMigrationModelCustomizer</c>,
/// etc.), which is already part of the default EF cache key tuple
/// <c>(contextType, modelCustomizerType, designTime)</c>. The runtime context uses
/// <c>(GameKitDbContext, RelationalModelCustomizer, false)</c>; migration contexts use
/// <c>(GameKitDbContext, AuthMigrationModelCustomizer, false)</c> etc. These keys are already
/// distinct — no collision occurs and no custom factory is required.
/// </para>
/// <para>
/// Consumers who write their own integration tests that share an in-process EF model cache
/// across migration and runtime contexts can register this factory in their test
/// <c>AddDbContext</c> call to prevent model-cache collisions.
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
