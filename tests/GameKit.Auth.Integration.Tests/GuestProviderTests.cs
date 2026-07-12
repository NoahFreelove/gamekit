// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Auth.Entities;
using GameKit.Auth.Providers;
using GameKit.Core.Data;
using GameKit.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// Postgres integration test for <see cref="Providers.Guest.GuestOAuthProvider"/> (AUTH-08):
/// confirms a guest login mints a <c>Player</c> with no identities or credentials and that the
/// issued JWT carries <c>is_guest=true</c> (D-13 computed-property claim).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class GuestProviderTests
{
    private readonly PostgresFixture _pg;

    /// <summary>xUnit-injected fixture.</summary>
    public GuestProviderTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Guest_Login_Creates_Player_With_No_Identities_Or_Credentials()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        await using var scope = tc.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
            .First(p => p.Provider == "guest");

        var result = await provider.CompleteLoginAsync(
            externalId: string.Empty,
            displayName: null,
            avatarUrl: null,
            fingerprint: "dev-1");

        Assert.True(result.Success);
        Assert.NotNull(result.PlayerId);
        Assert.NotNull(result.Tokens);
        Assert.False(string.IsNullOrEmpty(result.Tokens!.AccessJwt));

        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Tokens!.AccessJwt);
        Assert.Equal("true", token.Claims.First(c => c.Type == "is_guest").Value);
        Assert.Equal("guest", token.Claims.First(c => c.Type == "provider").Value);
        Assert.Equal(result.PlayerId!.Value.ToString(), token.Claims.First(c => c.Type == "sub").Value);

        // Verify the Player row exists and has no identities or credentials.
        await using var verify = tc.CreateAsyncScope();
        var ctx = verify.ServiceProvider.GetRequiredService<GameKitDbContext>();
        Assert.Equal(1, await ctx.Players.CountAsync(p => p.Id == result.PlayerId));
        Assert.Equal(0, await ctx.Set<PlayerIdentity>().CountAsync(i => i.PlayerId == result.PlayerId));
        Assert.Equal(0, await ctx.Set<PlayerCredential>().CountAsync(c => c.PlayerId == result.PlayerId));
    }
}
