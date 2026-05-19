// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using GameKit.Matchmaking;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameKit.Matchmaking.Tests.Services;

/// <summary>
/// Unit tests for <see cref="DeclineCooldownService"/> — the escalating decline-cooldown
/// ladder defined by CONTEXT D-08 (3 / 15 / 30 min steps within a 60-min rolling window).
/// </summary>
/// <remarks>
/// Uses a fake <see cref="IDeclineHistoryReader"/> seam so the test exercises the cooldown
/// arithmetic without spinning up Postgres. The integration-level cooldown enforcement test
/// lives in <c>tests/GameKit.Matchmaking.Integration.Tests/CooldownEnforcementTests.cs</c>
/// and exercises the same logic against a real <c>decline_history</c> table.
/// </remarks>
public sealed class DeclineCooldownEscalationTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ZeroDeclines_NotLocked()
    {
        var svc = BuildService(new List<DeclineHistory>());

        var status = await svc.GetCurrentCooldownAsync(Guid.NewGuid(), Now, CancellationToken.None);

        Assert.False(status.IsLocked);
        Assert.Null(status.RetryAfter);
    }

    [Fact]
    public async Task OneDecline_LocksFor_Step1Minutes()
    {
        var playerId = Guid.NewGuid();
        var declines = new List<DeclineHistory>
        {
            // 1 minute ago — Step1 = 3 min → 2 minutes of cooldown remain.
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-1), ProposalId = Guid.NewGuid() },
        };
        var svc = BuildService(declines);

        var status = await svc.GetCurrentCooldownAsync(playerId, Now, CancellationToken.None);

        Assert.True(status.IsLocked);
        Assert.NotNull(status.RetryAfter);
        // 3-min step minus 1 minute elapsed ≈ 2 minutes.
        Assert.InRange(status.RetryAfter!.Value.TotalMinutes, 1.9, 2.1);
    }

    [Fact]
    public async Task TwoDeclines_LocksFor_Step2Minutes()
    {
        var playerId = Guid.NewGuid();
        var declines = new List<DeclineHistory>
        {
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-10), ProposalId = Guid.NewGuid() },
            // Most recent 5 minutes ago — Step2 = 15 min → 10 minutes remain.
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-5), ProposalId = Guid.NewGuid() },
        };
        var svc = BuildService(declines);

        var status = await svc.GetCurrentCooldownAsync(playerId, Now, CancellationToken.None);

        Assert.True(status.IsLocked);
        Assert.NotNull(status.RetryAfter);
        Assert.InRange(status.RetryAfter!.Value.TotalMinutes, 9.9, 10.1);
    }

    [Fact]
    public async Task ThreeDeclines_LocksFor_Step3Minutes()
    {
        var playerId = Guid.NewGuid();
        var declines = new List<DeclineHistory>
        {
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-30), ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-20), ProposalId = Guid.NewGuid() },
            // Most recent 1 minute ago — Step3 = 30 min → 29 minutes remain.
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-1), ProposalId = Guid.NewGuid() },
        };
        var svc = BuildService(declines);

        var status = await svc.GetCurrentCooldownAsync(playerId, Now, CancellationToken.None);

        Assert.True(status.IsLocked);
        Assert.NotNull(status.RetryAfter);
        Assert.InRange(status.RetryAfter!.Value.TotalMinutes, 28.9, 29.1);
    }

    [Fact]
    public async Task FourDeclines_StillStep3()
    {
        // 4 declines within the window should still saturate at Step3 (30 min). The cap is
        // documented in D-08 ("Third: 30 min") — the ladder does not escalate beyond Step3.
        var playerId = Guid.NewGuid();
        var declines = new List<DeclineHistory>
        {
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-50), ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-40), ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-20), ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-2), ProposalId = Guid.NewGuid() },
        };
        var svc = BuildService(declines);

        var status = await svc.GetCurrentCooldownAsync(playerId, Now, CancellationToken.None);

        Assert.True(status.IsLocked);
        // Step3 = 30 min; latest decline 2 min ago → 28 minutes remain.
        Assert.InRange(status.RetryAfter!.Value.TotalMinutes, 27.9, 28.1);
    }

    [Fact]
    public async Task DeclinesOlderThanWindow_Ignored()
    {
        // Three declines exist BUT two are outside the 60-min window (only the most recent
        // counts). Effective count = 1 → Step1 (3 min) cooldown.
        var playerId = Guid.NewGuid();
        var declines = new List<DeclineHistory>
        {
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-120), ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-90),  ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-1),   ProposalId = Guid.NewGuid() },
        };
        var svc = BuildService(declines);

        var status = await svc.GetCurrentCooldownAsync(playerId, Now, CancellationToken.None);

        Assert.True(status.IsLocked);
        // Effective count = 1; Step1 = 3 min; 1 min elapsed → ~2 min remain.
        Assert.InRange(status.RetryAfter!.Value.TotalMinutes, 1.9, 2.1);
    }

    [Fact]
    public async Task LatestPlusStepExpired_NotLocked()
    {
        // 3 declines within the window BUT the latest one was 31 min ago. Step3 = 30 min;
        // 30 < 31 → the cooldown window has elapsed. The player is no longer locked.
        var playerId = Guid.NewGuid();
        var declines = new List<DeclineHistory>
        {
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-50), ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-45), ProposalId = Guid.NewGuid() },
            new() { Id = Guid.NewGuid(), PlayerId = playerId, DeclinedAt = Now.AddMinutes(-31), ProposalId = Guid.NewGuid() },
        };
        var svc = BuildService(declines);

        var status = await svc.GetCurrentCooldownAsync(playerId, Now, CancellationToken.None);

        Assert.False(status.IsLocked);
        Assert.Null(status.RetryAfter);
    }

    private static DeclineCooldownService BuildService(IList<DeclineHistory> declines)
    {
        var opts = Options.Create(new GameKitMatchmakingOptions());
        var reader = new FakeDeclineHistoryReader(declines);
        return new DeclineCooldownService(reader, opts);
    }

    /// <summary>
    /// Test-only in-memory implementation of <see cref="IDeclineHistoryReader"/> used to
    /// drive <see cref="DeclineCooldownService"/> escalation logic without standing up
    /// Postgres. The real reader (registered by <c>AddProposalServices</c>) queries the
    /// scoped <c>GameKitDbContext</c>.
    /// </summary>
    private sealed class FakeDeclineHistoryReader : IDeclineHistoryReader
    {
        private readonly IList<DeclineHistory> _rows;
        public FakeDeclineHistoryReader(IList<DeclineHistory> rows) => _rows = rows;

        public Task<IReadOnlyList<DeclineHistory>> GetRecentDeclinesAsync(
            Guid playerId, DateTimeOffset since, int take, CancellationToken ct)
        {
            var matches = _rows
                .Where(d => d.PlayerId == playerId && d.DeclinedAt > since)
                .OrderByDescending(d => d.DeclinedAt)
                .Take(take)
                .ToList();
            return Task.FromResult<IReadOnlyList<DeclineHistory>>(matches);
        }

        public Task RecordDeclineAsync(Guid playerId, Guid proposalId, DateTimeOffset declinedAt, CancellationToken ct)
        {
            _rows.Add(new DeclineHistory
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                DeclinedAt = declinedAt,
                ProposalId = proposalId,
            });
            return Task.CompletedTask;
        }
    }
}
