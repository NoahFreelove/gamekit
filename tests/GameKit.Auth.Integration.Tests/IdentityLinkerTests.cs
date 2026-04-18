// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// Integration coverage for <see cref="IIdentityLinker"/> (AUTH-14) — proves ROADMAP success
/// criterion #5 at the service layer: cross-player collision returns
/// <see cref="LinkResultKind.AlreadyLinkedToOtherPlayer"/> with a SHA-256 hash (never the raw
/// external id), and the <c>player_identities</c> table is NOT silently merged. Also covers the
/// idempotent "already-linked-to-self" path.
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class IdentityLinkerTests
{
    private readonly PostgresFixture _pg;

    /// <summary>xUnit-injected fixture.</summary>
    public IdentityLinkerTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task CrossPlayer_Collision_Returns_AlreadyLinkedToOtherPlayer_With_Hash()
    {
        // ROADMAP Success Criterion #5 — serial (not concurrent) cross-player collision.
        // Player A links first; then Player B attempts the same (provider, externalId) and
        // must receive a hash-bearing collision response, not a silent merge.
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        var steamId = $"7656119877777{Random.Shared.Next(10000, 99999):D5}";

        Guid playerA, playerB;
        await using (var scope = tc.CreateAsyncScope())
        {
            var guest = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "guest");
            playerA = (await guest.CompleteLoginAsync(string.Empty, null, null, "dev-A")).PlayerId!.Value;
            playerB = (await guest.CompleteLoginAsync(string.Empty, null, null, "dev-B")).PlayerId!.Value;

            var linker = scope.ServiceProvider.GetRequiredService<IIdentityLinker>();
            var first = await linker.LinkAsync(playerA, "steam", steamId);
            Assert.Equal(LinkResultKind.Linked, first.Kind);
        }

        await using (var scope = tc.CreateAsyncScope())
        {
            var linker = scope.ServiceProvider.GetRequiredService<IIdentityLinker>();
            var second = await linker.LinkAsync(playerB, "steam", steamId);
            Assert.Equal(LinkResultKind.AlreadyLinkedToOtherPlayer, second.Kind);
            Assert.NotNull(second.ExternalIdHash);
            // T-02-10: raw external id must never appear in the hash response.
            Assert.DoesNotContain(steamId, second.ExternalIdHash!);
        }

        await using (var scope = tc.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<GameKitDbContext>();

            // Exactly one row with this external id — owned by Player A (no silent merge).
            var rows = await ctx.Set<PlayerIdentity>()
                .Where(i => i.Provider == "steam" && i.ExternalId == steamId)
                .ToListAsync();
            Assert.Single(rows);
            Assert.Equal(playerA, rows[0].PlayerId);

            // Audit row for the collision attempt was written.
            Assert.True(await ctx.AdminAuditLog.AnyAsync(
                a => a.Action == "auth.identity.link_failed_collision"
                     && a.Reason == "cross_player_collision"
                     && a.ActorId == playerB));
        }
    }

    [Fact]
    public async Task AlreadyLinked_To_Self_Is_Idempotent()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        var discordId = $"999-{Guid.NewGuid():N}"[..16];

        await using var scope = tc.CreateAsyncScope();
        var guest = scope.ServiceProvider.GetServices<IOAuthProvider>()
            .First(p => p.Provider == "guest");
        var pid = (await guest.CompleteLoginAsync(string.Empty, null, null, "d")).PlayerId!.Value;

        var linker = scope.ServiceProvider.GetRequiredService<IIdentityLinker>();
        var r1 = await linker.LinkAsync(pid, "discord", discordId);
        var r2 = await linker.LinkAsync(pid, "discord", discordId);
        Assert.Equal(LinkResultKind.Linked, r1.Kind);
        Assert.Equal(LinkResultKind.AlreadyLinkedToSelf, r2.Kind);
    }
}
