// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading.Tasks;
using GameKit.Admin.UI.Services;
using GameKit.Auth;
using GameKit.Auth.Services;
using GameKit.Core.Data;
using GameKit.Core.Services;
using Moq;
using Xunit;

namespace GameKit.Admin.Tests;

public class AdminAuthServiceTests
{
    private static GameKitDbContext MakeCtx() =>
        TestDbContextFactory.Create($"admin-auth-{Guid.NewGuid()}");

    private static IPasswordHasher MakeRealHasher()
    {
        // Real BCryptPasswordHasher needs a GameKitAuthOptions with Password.BCryptWorkFactor.
        var opts = new GameKitAuthOptions();
        return new BCryptPasswordHasher(opts);
    }

    private static IAdminAuditWriter MakeAuditStub()
    {
        var m = new Mock<IAdminAuditWriter>();
        m.Setup(a => a.WriteAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid>(),
                It.IsAny<object?>(),
                It.IsAny<object?>(),
                It.IsAny<string?>(),
                It.IsAny<System.Threading.CancellationToken>()))
            .Returns(Task.CompletedTask);
        return m.Object;
    }

    private static IClock MakeClock(DateTimeOffset? now = null)
    {
        var m = new Mock<IClock>();
        m.SetupGet(c => c.UtcNow).Returns(now ?? new DateTimeOffset(2026, 4, 19, 12, 0, 0, TimeSpan.Zero));
        return m.Object;
    }

    [Fact]
    public async Task LoginAsync_UnknownUsername_DoesNotThrow_ReturnsNull()
    {
        // W2 spec: construct AdminAuthService with a fresh GameKitDbContext containing zero AdminUser rows
        // + a real BCryptPasswordHasher, call VerifyPasswordAsync("no-such-admin", "anything", default),
        // assert result is null and no exception is thrown. Proves the DummyHash literal is BCrypt-parseable
        // and Verify does not throw on it — timing parity mitigation for T-03-06-03.
        await using var ctx = MakeCtx();
        var sut = new AdminAuthService(ctx, MakeRealHasher(), MakeAuditStub(), MakeClock());

        var result = await sut.VerifyPasswordAsync("no-such-admin", "anything", default);

        Assert.Null(result);
    }
}
