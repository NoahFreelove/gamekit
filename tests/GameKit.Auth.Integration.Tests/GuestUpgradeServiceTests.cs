// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
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
/// Integration coverage for <see cref="IGuestUpgradeService"/> (AUTH-13) — the happy-path
/// guest → password upgrade, ROADMAP success criterion #4 (concurrent guest-upgrade race at
/// the service layer), and RESEARCH §15 open question #3 (concurrent username-register
/// collision).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class GuestUpgradeServiceTests
{
    private readonly PostgresFixture _pg;

    /// <summary>xUnit-injected fixture.</summary>
    public GuestUpgradeServiceTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task UpgradeToPassword_Happy_Path_Inserts_Credential_And_Issues_Non_Guest_Token()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        var username = $"upg-{Guid.NewGuid():N}"[..16];

        Guid playerId;
        await using (var scope = tc.CreateAsyncScope())
        {
            var guest = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "guest");
            var r = await guest.CompleteLoginAsync(string.Empty, null, null, "dev-1");
            playerId = r.PlayerId!.Value;
        }

        await using (var scope = tc.CreateAsyncScope())
        {
            var upgrade = scope.ServiceProvider.GetRequiredService<IGuestUpgradeService>();
            var tokens = await upgrade.UpgradeToPasswordAsync(playerId, username, "strong-pw-12chars", "dev-1");
            Assert.NotNull(tokens);
            Assert.False(string.IsNullOrEmpty(tokens.AccessJwt));

            var parsed = new JwtSecurityTokenHandler().ReadJwtToken(tokens.AccessJwt);
            Assert.Equal("false", parsed.Claims.First(c => c.Type == "is_guest").Value);
            Assert.Equal("password", parsed.Claims.First(c => c.Type == "provider").Value);
            Assert.Equal(playerId.ToString(), parsed.Claims.First(c => c.Type == "sub").Value);
        }

        await using var verify = tc.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        Assert.Equal(1, await ctx.Set<PlayerCredential>().CountAsync(c => c.PlayerId == playerId));

        // Audit row for the upgrade was written (RESEARCH §8.10).
        Assert.True(await ctx.AdminAuditLog.AnyAsync(
            a => a.Action == "auth.guest.upgraded_password" && a.ActorId == playerId));
    }

    [Fact]
    public async Task ConcurrentGuestLink_Same_Steam_Id_One_Succeeds_One_Collision()
    {
        // ROADMAP Success Criterion #4 — two guests race on linking the same Steam id.
        // Exactly one must win with LinkResultKind.Linked; the other must get
        // AlreadyLinkedToOtherPlayer with a SHA-256 hash (no raw id leaked per T-02-10).
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        var steamId = $"7656119800000{Random.Shared.Next(10000, 99999):D5}";

        Guid guestA, guestB;
        await using (var scope = tc.CreateAsyncScope())
        {
            var guestProvider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "guest");
            guestA = (await guestProvider.CompleteLoginAsync(string.Empty, null, null, "dev-1")).PlayerId!.Value;
            guestB = (await guestProvider.CompleteLoginAsync(string.Empty, null, null, "dev-2")).PlayerId!.Value;
        }

        // Barrier-coordinated concurrent LinkAsync calls for the same (steam, externalId)
        // but different player ids — forces D-14 race against UNIQUE(provider, external_id).
        var barrier = new Barrier(2);

        async Task<LinkResult> Attempt(Guid pid)
        {
            await using var s = tc.CreateAsyncScope();
            var linker = s.ServiceProvider.GetRequiredService<IIdentityLinker>();
            barrier.SignalAndWait();
            return await linker.LinkAsync(pid, "steam", steamId);
        }

        var t1 = Task.Run(() => Attempt(guestA));
        var t2 = Task.Run(() => Attempt(guestB));
        var results = await Task.WhenAll(t1, t2);

        var linked = results.Count(r => r.Kind == LinkResultKind.Linked);
        var collided = results.Count(r => r.Kind == LinkResultKind.AlreadyLinkedToOtherPlayer);
        Assert.Equal(1, linked);
        Assert.Equal(1, collided);

        var collision = results.First(r => r.Kind == LinkResultKind.AlreadyLinkedToOtherPlayer);
        Assert.False(string.IsNullOrEmpty(collision.ExternalIdHash));
        // T-02-10: raw external id must NOT appear in the hash response.
        Assert.DoesNotContain(steamId, collision.ExternalIdHash!);

        // Exactly one row exists in player_identities for this (steam, externalId) — no silent merge.
        await using var verify = tc.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        Assert.Equal(1, await ctx.Set<PlayerIdentity>()
            .CountAsync(i => i.Provider == "steam" && i.ExternalId == steamId));
    }

    [Fact]
    public async Task ConcurrentUsernameRegister_Same_Username_One_Wins_One_Throws_UsernameTaken()
    {
        // RESEARCH §15 open question #3 — two guests race on the same username.
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        var contested = $"race-{Guid.NewGuid():N}"[..16];

        Guid guestA, guestB;
        await using (var scope = tc.CreateAsyncScope())
        {
            var guestProvider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "guest");
            guestA = (await guestProvider.CompleteLoginAsync(string.Empty, null, null, "dev-1")).PlayerId!.Value;
            guestB = (await guestProvider.CompleteLoginAsync(string.Empty, null, null, "dev-2")).PlayerId!.Value;
        }

        var barrier = new Barrier(2);

        async Task<Exception?> Attempt(Guid pid)
        {
            await using var s = tc.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IGuestUpgradeService>();
            barrier.SignalAndWait();
            try
            {
                await svc.UpgradeToPasswordAsync(pid, contested, "strong-pw-12ch", "dev-x");
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        var t1 = Task.Run(() => Attempt(guestA));
        var t2 = Task.Run(() => Attempt(guestB));
        var outcomes = await Task.WhenAll(t1, t2);

        var successes = outcomes.Count(r => r is null);
        var collisions = outcomes.Count(r => r is UsernameAlreadyTakenException);
        Assert.Equal(1, successes);
        Assert.Equal(1, collisions);
    }
}
