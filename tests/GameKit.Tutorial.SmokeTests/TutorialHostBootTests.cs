// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Net;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Tutorial.SmokeTests;

/// <summary>
/// BOOT GATE for the tutorial smoke-test plan (DOCS-02, Task 1).
///
/// This test class proves that <see cref="TutorialSmokeTestApp"/> actually constructs and
/// initialises the full GameKit in-process host — not merely compiles. The single
/// <see cref="TutorialHostBoots_WithRunningMatchmakingTicker"/> fact MUST FAIL if the host
/// cannot boot.
/// </summary>
/// <remarks>
/// By verifying the host boots here (before any match-forming test in
/// <see cref="TutorialSmokeTests"/>), we provide a clean fail-fast signal: if
/// migrations, DI, or the ticker break, this test fails with a diagnostic error rather
/// than the smoke test hanging on the status-poll deadline.
/// </remarks>
[Collection("TutorialSmoke")]
[Trait("Category", "Integration")]
public sealed class TutorialHostBootTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;

    public TutorialHostBootTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    [Fact(DisplayName = "DOCS-02: tutorial host boots with a running matchmaking ticker")]
    public async Task TutorialHostBoots_WithRunningMatchmakingTicker()
    {
        // Construct + initialise the full in-process host (ephemeral RSA PEM, fresh DB,
        // Core + Auth + Rankings + Matchmaking + Presence + Admin chain, in-process ticker).
        // If this throws, the await-using dispose still fires cleanly.
        await using var app = await TutorialSmokeTestApp.StartAsync(_pg, _redis);

        // Assert 1: the tictactoe ladder Guid resolved non-empty (StartupLadderUpserter ran).
        Assert.NotEqual(System.Guid.Empty, app.TicTacToeLadderId);

        // Assert 2: the host is reachable — GET /health/ready returns 200 (or 503 on degraded,
        // which is still "reachable"). We accept 200 only: migrations ran, Postgres + Redis are up.
        using var client = app.CreateClient("boot-test-device");
        var resp = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
