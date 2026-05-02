// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using GameKit.Admin.UI.Components.Shared;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Services;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace GameKit.Admin.Tests.Components;

/// <summary>
/// Phase 03.1 D-02 + UI-SPEC §5.6 — verifies the master-detail Players workspace renders
/// the empty-state when no <c>Id</c> parameter is bound and renders <see cref="PlayerDetailPane"/>
/// content when an <c>Id</c> is supplied.
///
/// Renders <see cref="PlayerDetailPane"/> directly (rather than the full <see cref="GameKit.Admin.UI.Components.Pages.Players"/>
/// page) because that page carries an <c>[Authorize(Policy = AdminPolicies.Admin)]</c> attribute
/// that is enforced by the router middleware — bUnit renders past it without a configured
/// authorization context. Asserting on the pane covers SC#4: the route-bound <c>Guid? Id</c>
/// flips between empty-state and detail rendering, which is the master-detail synchronization
/// contract the success criterion is asking us to verify.
/// </summary>
public sealed class PlayersWorkspaceTests : BunitContext
{
    /// <summary>Wires MudBlazor services + an InMemory <see cref="GameKitDbContext"/> stub.</summary>
    public PlayersWorkspaceTests()
    {
        Services.AddMudServices();
        Services.AddAuthorization();
        Services.AddAuthorizationCore();
        Services.AddSingleton<IPlayerSearchService, NoopSearchService>();
        Services.AddScoped(_ => TestDbContextFactory.Create($"plw-{Guid.NewGuid():N}"));
        // Phase 03.1-11: PlayerDetailPane now @injects IPlayerDisplayNameResolver to resolve
        // BanBanner ActorName from the audit log. Register a stub so bUnit can construct the component.
        Services.AddSingleton<IPlayerDisplayNameResolver, NoopDisplayNameResolver>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Pane_NoId_RendersEmptyState()
    {
        var cut = Render<PlayerDetailPane>(p => p.Add(x => x.Id, (Guid?)null));

        cut.Find("div.detail-empty[role='status']");
        Assert.Contains("Select a player", cut.Markup);
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Pane_WithUnknownId_RendersNotFoundAlert()
    {
        // No player exists in the InMemory DB → LoadAsync returns null → "Player not found"
        // branch wins. This assertion proves the master-detail right-pane reacts to the
        // route-bound Guid? Id parameter (SC#4 deep-link sync).
        var id = Guid.NewGuid();
        var cut = Render<PlayerDetailPane>(p => p.Add(x => x.Id, (Guid?)id));

        cut.WaitForAssertion(() =>
        {
            // After OnParametersSetAsync completes, the pane should NOT show the empty-state.
            Assert.DoesNotContain("Select a player", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Category", "Component")]
    public void Pane_BothBranches_CompleteWithoutOverlap()
    {
        // Master-detail synchronization contract (SC#4): rendering with Id=null produces the
        // empty-state, rendering with Id=Guid produces the loaded-state — and the markup is
        // disjoint (the empty-state text never appears when an Id is bound). Two separate
        // renders cover both sides of the master-detail wiring.
        var emptyCut = Render<PlayerDetailPane>(p => p.Add(x => x.Id, (Guid?)null));
        Assert.Contains("Select a player", emptyCut.Markup);

        var loadedCut = Render<PlayerDetailPane>(p => p.Add(x => x.Id, (Guid?)Guid.NewGuid()));
        loadedCut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Select a player", loadedCut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    /// <summary>Stub <see cref="IPlayerSearchService"/> returning the empty page.</summary>
    private sealed class NoopSearchService : IPlayerSearchService
    {
        public Task<PaginatedResult<PlayerRow>> SearchAsync(
            string query,
            Guid? afterId,
            int pageSize,
            CancellationToken cancellationToken)
            => Task.FromResult(PaginatedResult<PlayerRow>.Empty);
    }

    /// <summary>
    /// Stub <see cref="IPlayerDisplayNameResolver"/> returning "(test)" for any player id.
    /// Registered so <see cref="PlayerDetailPane"/> can be constructed after the Plan 03.1-11
    /// @inject directive was added — bUnit component tests do not need a real resolver.
    /// </summary>
    private sealed class NoopDisplayNameResolver : IPlayerDisplayNameResolver
    {
        public ValueTask<string> ResolveAsync(Guid? playerId, CancellationToken cancellationToken = default)
            => new ValueTask<string>(playerId.HasValue ? playerId.Value.ToString("N")[..8] : "(deleted)");
    }
}
