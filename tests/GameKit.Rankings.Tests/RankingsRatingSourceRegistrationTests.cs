// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Linq;
using GameKit.Core.Services;
using GameKit.Rankings.Builder;
using GameKit.Rankings.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GameKit.Rankings.Tests;

/// <summary>
/// Unit tests proving RANK-17 <c>.WithRatingsFrom&lt;T&gt;()</c> overrides the Core
/// <c>NullPlayerRatingProvider</c> null-object via <c>RemoveAll + AddScoped</c>,
/// and that omitting the call leaves the null-object fallback intact (T-08-03-03).
/// </summary>
public sealed class RankingsRatingSourceRegistrationTests
{
    /// <summary>
    /// <c>.WithRatingsFrom&lt;RankingsRatingSource&gt;()</c> resolves <see cref="IPlayerRatingProvider"/>
    /// to <see cref="RankingsRatingSource"/> (not the Core null-object).
    /// The resulting registration is Scoped.
    /// </summary>
    [Fact]
    public void WithRatingsFrom_Overrides_NullPlayerRatingProvider()
    {
        // Arrange: simulate AddGameKit() registering NullPlayerRatingProvider via TryAddSingleton.
        var services = new ServiceCollection();
        services.TryAddSingleton<IPlayerRatingProvider, StubNullProvider>();

        // Act: simulate .WithRatingsFrom<RankingsRatingSource>() replacing the null-object.
        services.RemoveAll<IPlayerRatingProvider>();
        services.AddScoped<IPlayerRatingProvider, RankingsRatingSource>();

        // Assert: the registration was replaced with RankingsRatingSource, Scoped.
        var descriptors = services.Where(d => d.ServiceType == typeof(IPlayerRatingProvider)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(RankingsRatingSource), descriptors[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptors[0].Lifetime);
    }

    /// <summary>
    /// Without <c>.WithRatingsFrom&lt;T&gt;()</c>, <see cref="IPlayerRatingProvider"/> remains
    /// registered to the null-object (the v1 zero-rating fallback).
    /// </summary>
    [Fact]
    public void WithoutWithRatingsFrom_LeavesNullObjectAsDefault()
    {
        // Arrange: simulate AddGameKit() only.
        var services = new ServiceCollection();
        services.TryAddSingleton<IPlayerRatingProvider, StubNullProvider>();

        // Assert: without calling WithRatingsFrom, the null-object is the registered impl.
        var descriptor = services.Single(d => d.ServiceType == typeof(IPlayerRatingProvider));
        Assert.Equal(typeof(StubNullProvider), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
    }

    /// <summary>
    /// The actual <see cref="RankingsBuilderExtensions.WithRatingsFrom{T}(IGameKitRankingsBuilder)"/>
    /// extension method uses <c>RemoveAll</c> (not <c>TryAdd</c>) so it succeeds even when the
    /// null-object is already registered, and the resulting descriptor is Scoped.
    /// </summary>
    [Fact]
    public void WithRatingsFrom_BuilderExtension_OverridesNullObject_ViaRemoveAllAddScoped()
    {
        // Arrange: simulate AddGameKit() registering null-object via TryAddSingleton.
        var services = new ServiceCollection();
        services.TryAddSingleton<IPlayerRatingProvider, StubNullProvider>();

        // Build a minimal stub builder (does NOT require full EF Core / Postgres host).
        var stub = new MinimalRankingsBuilderStub(services);

        // Act: call the actual extension method under test.
        stub.WithRatingsFrom<RankingsRatingSource>();

        // Assert: exactly one IPlayerRatingProvider descriptor, Scoped, RankingsRatingSource.
        var descriptors = services.Where(d => d.ServiceType == typeof(IPlayerRatingProvider)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(RankingsRatingSource), descriptors[0].ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptors[0].Lifetime);
    }

    // -------------------------------------------------------------------------
    // Minimal builder stub — wraps a real IServiceCollection to allow calling the
    // actual RankingsBuilderExtensions.WithRatingsFrom<T> extension method
    // without requiring a full AddGameKit host or DI container build.
    // -------------------------------------------------------------------------

    private sealed class MinimalRankingsBuilderStub : IGameKitRankingsBuilder
    {
        public IServiceCollection Services { get; }

        public System.Collections.Generic.IReadOnlyList<LadderConfig> RegisteredLadders
            => System.Collections.Immutable.ImmutableList<LadderConfig>.Empty;

        public IGameKitRankingsBuilder AddLadder(string name, System.Action<LadderConfig>? configure = null)
            => this;

        public MinimalRankingsBuilderStub(IServiceCollection services) => Services = services;
    }

    // Stub null-object — stands in for the internal Core NullPlayerRatingProvider in unit tests.
    private sealed class StubNullProvider : IPlayerRatingProvider
    {
        public System.Threading.Tasks.ValueTask<System.Collections.Generic.IReadOnlyDictionary<System.Guid, PlayerRatingValue>> GetRatingsAsync(
            System.Collections.Generic.IReadOnlyCollection<System.Guid> playerIds,
            System.Guid ladderId,
            System.Threading.CancellationToken ct = default)
            => throw new System.NotImplementedException("Stub — should not be called in unit tests");
    }
}
