// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bunit;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Components.Shared;
using GameKit.Admin.UI.Entities;
using GameKit.Admin.UI.Http.Contracts;
using GameKit.Admin.UI.Services;
using GameKit.Core.Data;
using GameKit.Core.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using Xunit;

namespace GameKit.Admin.Tests.Components;

/// <summary>
/// Regression for BLOCKER-GAP-01 from <c>03.1-VERIFICATION.md</c>: <see cref="PlayerDetailPane"/>
/// must resolve the banning admin's username from <c>admin_users</c>, not from <c>players</c>.
/// Admin accounts are documented to never overlap with player accounts (AdminUser.cs:11), so
/// the prior <c>IPlayerDisplayNameResolver</c> path always missed and rendered the deleted-player
/// tombstone for every human-issued ban.
///
/// Uses a local <see cref="BunitContext"/> with <c>await using</c> rather than inheriting from
/// <see cref="BunitContext"/>. The full PlayerDetailPane renders <see cref="MudBlazor.MudTabs"/>,
/// which causes <c>MudBlazor.KeyInterceptorService</c> to be resolved; that service is
/// <see cref="IAsyncDisposable"/>-only, and xUnit's synchronous test-class disposal throws on it.
/// A local context disposed via <c>await using</c> takes the async-dispose path and avoids that.
/// </summary>
public sealed class PlayerDetailPaneBanAttributionTests
{
    [Fact]
    [Trait("Category", "Component")]
    public async Task BanBanner_RendersAdminUsername_NotTombstone_WhenAuditRowHasActorId()
    {
        await using var ctx = NewContext(out var dbName);

        var admin = new AdminUser
        {
            Id = Guid.NewGuid(),
            Username = "alice-admin",
            PasswordHash = "$2a$11$placeholder.placeholder.placeholder.placeholder.placeho",
            Role = "admin",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
        };
        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "banned-bob",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            IsBanned = true,
            BannedAt = DateTimeOffset.UtcNow.AddHours(-1),
            BanReason = "abusive language",
        };
        var auditRow = new AdminAuditLog
        {
            Id = Guid.NewGuid(),
            ActorId = admin.Id,
            Action = AdminAuditActions.PlayerBan,
            TargetType = "player",
            TargetId = player.Id,
            Reason = player.BanReason,
            CreatedAt = player.BannedAt!.Value,
        };

        await using (var seed = TestDbContextFactory.Create(dbName))
        {
            seed.Set<AdminUser>().Add(admin);
            seed.Players.Add(player);
            seed.AdminAuditLog.Add(auditRow);
            await seed.SaveChangesAsync();
        }

        var cut = ctx.Render<PlayerDetailPane>(p => p.Add(x => x.Id, (Guid?)player.Id));

        cut.WaitForAssertion(() =>
        {
            // The banning admin's username must appear in the rendered BanBanner.
            Assert.Contains("alice-admin", cut.Markup);
            // The deleted-player tombstone must NOT appear — that was the symptom of
            // BLOCKER-GAP-01 where the resolver fell through to DeletedPlayerDisplayName.
            Assert.DoesNotContain("Deleted Player", cut.Markup);
            // The pre-Plan-11 "system" hardcode must also be absent.
            Assert.DoesNotContain(">system<", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    [Trait("Category", "Component")]
    public async Task BanBanner_FallsBackToUnknownActor_WhenNoAuditRowExists()
    {
        await using var ctx = NewContext(out var dbName);

        // A banned player with no audit row (e.g. data imported pre-Phase 03) must render
        // the "unknown actor" fallback — never crash, never tombstone-leak.
        var player = new Player
        {
            Id = Guid.NewGuid(),
            DisplayName = "banned-no-audit",
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-7),
            IsBanned = true,
            BannedAt = DateTimeOffset.UtcNow.AddHours(-1),
            BanReason = "legacy ban",
        };

        await using (var seed = TestDbContextFactory.Create(dbName))
        {
            seed.Players.Add(player);
            await seed.SaveChangesAsync();
        }

        var cut = ctx.Render<PlayerDetailPane>(p => p.Add(x => x.Id, (Guid?)player.Id));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("unknown actor", cut.Markup);
            Assert.DoesNotContain("Deleted Player", cut.Markup);
        }, TimeSpan.FromSeconds(2));
    }

    private static BunitContext NewContext(out string dbName)
    {
        dbName = $"banattr-{Guid.NewGuid():N}";
        var capturedName = dbName;
        var ctx = new BunitContext();
        ctx.Services.AddMudServices();
        ctx.Services.AddAuthorization();
        ctx.Services.AddAuthorizationCore();
        ctx.Services.AddSingleton<IPlayerSearchService, NoopSearchService>();
        // EF Core InMemory shares state across instances built with the same database name —
        // the seed block and the @inject GameKitDbContext below use distinct instances but
        // identical backing store.
        ctx.Services.AddScoped(_ => TestDbContextFactory.Create(capturedName));
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        // Supply the cascading auth state — PlayerDetailPane renders an AuthorizeView
        // (Superadmin-only GDPR button) that throws without it.
        ctx.AddAuthorization().SetAuthorized("test-admin");
        ctx.AddAuthorization().SetClaims(new Claim(ClaimTypes.Role, AdminRoles.Admin));
        return ctx;
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
}
