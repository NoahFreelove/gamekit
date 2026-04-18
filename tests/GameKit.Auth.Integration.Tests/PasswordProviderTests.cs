// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using GameKit.Auth.Providers;
using GameKit.Auth.Providers.Password;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>
/// Postgres integration test for <see cref="PasswordOAuthProvider"/> (AUTH-09): covers register,
/// successful login, wrong-password, and unknown-username paths. The unknown-username case
/// exercises the T-02-16 timing-attack-mitigation dummy BCrypt verify (correctness of the hash
/// response is proven functionally here; the timing parity itself is enforced by code inspection
/// + grep count on the production file).
/// </summary>
[Collection("Postgres")]
[Trait("Category", "Integration")]
public sealed class PasswordProviderTests
{
    private readonly PostgresFixture _pg;

    /// <summary>xUnit-injected fixture.</summary>
    public PasswordProviderTests(PostgresFixture pg) => _pg = pg;

    [Fact]
    public async Task Register_Then_Login_Returns_TokenPair()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        // Ensure unique usernames across parallel test runs against the shared PG fixture.
        var username = $"alice-{Guid.NewGuid():N}"[..16];

        Guid registeredId;
        await using (var scope = tc.CreateAsyncScope())
        {
            var provider = (PasswordOAuthProvider)scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "password");
            var reg = await provider.RegisterAsync(username, "correct-horse-battery-staple", "Alice", "dev-1");
            Assert.True(reg.Success);
            registeredId = reg.PlayerId!.Value;
        }

        await using (var scope = tc.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "password");
            var login = await provider.CompleteLoginAsync(username, "correct-horse-battery-staple", null, "dev-1");
            Assert.True(login.Success);
            Assert.Equal(registeredId, login.PlayerId);
        }
    }

    [Fact]
    public async Task Wrong_Password_Returns_Invalid_Credentials()
    {
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        var username = $"bob-{Guid.NewGuid():N}"[..16];

        await using (var scope = tc.CreateAsyncScope())
        {
            var provider = (PasswordOAuthProvider)scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "password");
            var reg = await provider.RegisterAsync(username, "good-password-12", null, null);
            Assert.True(reg.Success);
        }

        await using (var scope = tc.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
                .First(p => p.Provider == "password");
            var login = await provider.CompleteLoginAsync(username, "WRONG-PASSWORD", null, null);
            Assert.False(login.Success);
            Assert.Equal("invalid_credentials", login.ErrorCode);
        }
    }

    [Fact]
    public async Task Unknown_Username_Returns_Invalid_Credentials()
    {
        // T-02-16 mitigation path: user-not-found still runs BCrypt.Verify against DummyHash
        // to equalize wall-clock cost (asserted structurally by grep on PasswordOAuthProvider.cs;
        // here we assert the functional contract — no leak of existence via the ErrorCode).
        await TestHelpers.ApplyMigrations(_pg.OwnerConnectionString);
        await using var tc = TestHelpers.BuildProvider(_pg.OwnerConnectionString);

        await using var scope = tc.CreateAsyncScope();
        var provider = scope.ServiceProvider.GetServices<IOAuthProvider>()
            .First(p => p.Provider == "password");
        var login = await provider.CompleteLoginAsync(
            $"nonexistent-{Guid.NewGuid():N}"[..16],
            "xxxxxxxxxxxx",
            null,
            null);
        Assert.False(login.Success);
        Assert.Equal("invalid_credentials", login.ErrorCode);
    }
}
