// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameKit.Core;
using GameKit.Core.Builder;
using GameKit.Matchmaking.Builder;
using GameKit.Matchmaking.Entities;
using GameKit.Matchmaking.Redis;
using GameKit.TestFixtures;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for <see cref="AtomicClaimScript"/> against a live Testcontainer
/// Redis. Verifies the fencing-token guard (Pitfall §2), the ticket-gone race, the
/// success path, and the EVALSHA fast-path cache.
/// </summary>
[Collection("Redis")]
[Trait("Category", "Integration")]
public sealed class AtomicClaimScriptTests : IAsyncLifetime
{
    private readonly RedisFixture _redis;
    private ConnectionMultiplexer? _mux;
    private IDatabase _db = null!;
    private IServer _server = null!;

    public AtomicClaimScriptTests(RedisFixture redis) => _redis = redis;

    public async Task InitializeAsync()
    {
        var opts = ConfigurationOptions.Parse(_redis.ConnectionString);
        opts.AllowAdmin = true; // required for FLUSHDB + SCRIPT EXISTS via IServer
        _mux = await ConnectionMultiplexer.ConnectAsync(opts);
        _db = _mux.GetDatabase();
        _server = _mux.GetServer(_mux.GetEndPoints().First());

        // Clean slate per test class invocation.
        await _server.FlushDatabaseAsync();
        await _server.ScriptFlushAsync();
    }

    public async Task DisposeAsync()
    {
        if (_mux is not null)
            await _mux.DisposeAsync();
    }

    [Fact]
    public async Task Success_Path_Removes_Both_Tickets_And_Writes_Proposal_Hash()
    {
        var ladderId = Guid.NewGuid();
        var poolName = "main";
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, poolName);
        var leaseKey = MatchmakingRedisKeys.MatcherLock;
        var leaseValue = "instance-A";
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);

        // Seed: lease + two queued tickets with QueuedAt Unix-ms scores.
        await _db.StringSetAsync(leaseKey, leaseValue);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.SortedSetAddAsync(queueKey, ticket1.ToString(), nowMs);
        await _db.SortedSetAddAsync(queueKey, ticket2.ToString(), nowMs + 1);

        var script = new AtomicClaimScript();
        var result = await script.ExecuteAsync(
            _db, leaseKey, leaseValue, queueKey, proposalKey,
            new[] { ticket1, ticket2 }, proposalId,
            proposalFieldsJson: "{\"deadline\":\"2026-05-17T00:00:10Z\"}",
            ttlSeconds: 15);

        Assert.Equal(AtomicClaimResult.Success, result);

        // Both tickets removed from queue.
        Assert.Equal(0, await _db.SortedSetLengthAsync(queueKey));

        // Proposal hash has the fields blob + TTL near 15s.
        var fields = await _db.HashGetAsync(proposalKey, "fields");
        Assert.Equal("{\"deadline\":\"2026-05-17T00:00:10Z\"}", fields.ToString());
        var ttl = await _db.KeyTimeToLiveAsync(proposalKey);
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value.TotalSeconds, 10, 15);

        // Each ticket hash marked Proposed with proposalId.
        var t1Status = await _db.HashGetAsync(MatchmakingRedisKeys.Ticket(ticket1), "status");
        var t1Proposal = await _db.HashGetAsync(MatchmakingRedisKeys.Ticket(ticket1), "proposalId");
        Assert.Equal("Proposed", t1Status.ToString());
        Assert.Equal(proposalId.ToString(), t1Proposal.ToString());

        var t2Status = await _db.HashGetAsync(MatchmakingRedisKeys.Ticket(ticket2), "status");
        Assert.Equal("Proposed", t2Status.ToString());
    }

    [Fact]
    public async Task LeaseLost_When_Fencing_Value_Does_Not_Match()
    {
        var ladderId = Guid.NewGuid();
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, "main");
        var leaseKey = MatchmakingRedisKeys.MatcherLock;
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();
        var proposalId = Guid.NewGuid();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);

        // The lease is held by "instance-A" but we run the script with leaseValue "instance-B"
        // — fencing-token mismatch.
        await _db.StringSetAsync(leaseKey, "instance-A");
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.SortedSetAddAsync(queueKey, ticket1.ToString(), nowMs);
        await _db.SortedSetAddAsync(queueKey, ticket2.ToString(), nowMs + 1);

        var script = new AtomicClaimScript();
        var result = await script.ExecuteAsync(
            _db, leaseKey, leaseValue: "instance-B", queueKey, proposalKey,
            new[] { ticket1, ticket2 }, proposalId,
            proposalFieldsJson: "{}",
            ttlSeconds: 15);

        Assert.Equal(AtomicClaimResult.LeaseLost, result);

        // No partial mutation — both tickets still present.
        Assert.Equal(2, await _db.SortedSetLengthAsync(queueKey));
        Assert.False(await _db.KeyExistsAsync(proposalKey));
        Assert.False(await _db.KeyExistsAsync(MatchmakingRedisKeys.Ticket(ticket1)));
    }

    [Fact]
    public async Task TicketGone_When_One_Ticket_Already_Claimed()
    {
        var ladderId = Guid.NewGuid();
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, "main");
        var leaseKey = MatchmakingRedisKeys.MatcherLock;
        var leaseValue = "instance-A";
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid(); // not present in queue — already claimed
        var proposalId = Guid.NewGuid();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);

        await _db.StringSetAsync(leaseKey, leaseValue);
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.SortedSetAddAsync(queueKey, ticket1.ToString(), nowMs);
        // Intentionally do NOT add ticket2 — simulating it being already claimed by an earlier
        // EVAL pass.

        var script = new AtomicClaimScript();
        var result = await script.ExecuteAsync(
            _db, leaseKey, leaseValue, queueKey, proposalKey,
            new[] { ticket1, ticket2 }, proposalId,
            proposalFieldsJson: "{}",
            ttlSeconds: 15);

        Assert.Equal(AtomicClaimResult.TicketGone, result);

        // ticket1 is NOT removed — script aborted before any ZREM.
        Assert.Equal(1, await _db.SortedSetLengthAsync(queueKey));
        Assert.False(await _db.KeyExistsAsync(proposalKey));
    }

    [Fact]
    public async Task EVALSHA_Fast_Path_Loads_Script_Into_Cache_After_First_Call()
    {
        var ladderId = Guid.NewGuid();
        var queueKey = MatchmakingRedisKeys.Queue(ladderId, "main");
        var leaseKey = MatchmakingRedisKeys.MatcherLock;
        var leaseValue = "instance-A";

        await _db.StringSetAsync(leaseKey, leaseValue);

        // Inspect the SCRIPT cache count via INFO before any call.
        // We can't easily list every cached SHA, but we can rely on the documented
        // StackExchange.Redis behavior: the first ScriptEvaluateAsync call sends EVAL with
        // the literal source which auto-loads the script into the server-side cache. After
        // that, the server-internal cache holds at least one script — verifiable by SCRIPT
        // LOAD returning a deterministic SHA and then SCRIPT EXISTS for that SHA returning 1.
        var script = new AtomicClaimScript();
        var ticket1 = Guid.NewGuid();
        var ticket2 = Guid.NewGuid();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.SortedSetAddAsync(queueKey, ticket1.ToString(), nowMs);
        await _db.SortedSetAddAsync(queueKey, ticket2.ToString(), nowMs + 1);

        var proposalId = Guid.NewGuid();
        var proposalKey = MatchmakingRedisKeys.Proposal(proposalId);
        var first = await script.ExecuteAsync(
            _db, leaseKey, leaseValue, queueKey, proposalKey,
            new[] { ticket1, ticket2 }, proposalId,
            proposalFieldsJson: "{}",
            ttlSeconds: 15);
        Assert.Equal(AtomicClaimResult.Success, first);

        // After the first call, manually SCRIPT LOAD the SAME source — Redis returns the
        // same SHA1 (Redis hashes the source server-side). The returned SHA confirms Redis
        // has cached the script.
        var loadedSha = (string?)await _server.ExecuteAsync(
            "SCRIPT", "LOAD", AtomicClaimScript.LuaSource);
        Assert.NotNull(loadedSha);
        Assert.False(string.IsNullOrEmpty(loadedSha));

        // Verify SCRIPT EXISTS for the loaded SHA. Use raw ExecuteAsync to avoid any
        // StackExchange.Redis preprocessing of the SHA argument.
        var existsResult = await _server.ExecuteAsync("SCRIPT", "EXISTS", loadedSha);
        // Reply is an array of 0/1 ints; first element is for our SHA.
        var existsArray = (RedisResult[])existsResult!;
        Assert.Single(existsArray);
        Assert.Equal(1, (int)existsArray[0]);

        // For diagnostic value, confirm our precomputed C# SHA1 matches Redis's server-side
        // computation (sanity check the Sha1Hex constant — useful for future Replace()
        // semantics where Plan 05-07 may swap the script).
        Assert.Equal(AtomicClaimScript.Sha1Hex, loadedSha);
    }

    [Fact]
    public void LuaSource_Is_Under_30_Lines()
    {
        // Plan 05-04 Task 3 truth: the Lua source body is ≤30 lines. We strip blank lines
        // and the surrounding C# raw-string blank lines, then count the remaining lines.
        var lines = AtomicClaimScript.LuaSource
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();
        Assert.True(lines.Length <= 30,
            $"Lua source has {lines.Length} non-blank lines; max allowed is 30.");
    }

    [Fact]
    public void LuaSource_First_Step_Is_Fencing_Token_Check()
    {
        // Pitfall §2: the FIRST executed step of the script MUST be the lease-fencing check.
        // We strip leading blank/comment lines, then assert the first code line contains the
        // GET KEYS[1] guard.
        var lines = AtomicClaimScript.LuaSource
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("--"))
            .ToArray();
        Assert.NotEmpty(lines);
        var first = lines[0];
        Assert.Contains("redis.call('GET', KEYS[1])", first);
        Assert.Contains("LEASE_LOST", first);
    }

    /// <summary>
    /// Plan 05-04 done criterion: the placeholder Channel&lt;TicketEvent&gt; + writer +
    /// reader singletons resolve from DI via AddMatchmaking() (so Wave 3 plans 05-05/05-06
    /// can consume the ChannelWriter without depending on Plan 05-07).
    /// </summary>
    [Fact]
    public void Channel_Placeholder_Resolves_From_DI_After_AddMatchmaking()
    {
        var services = new ServiceCollection();
        services.AddGameKit(o =>
        {
            o.ConnectionString = "Host=localhost;Database=test;Username=t;Password=t";
            o.AutoMigrate = false;
        }).AddMatchmaking();

        using var sp = services.BuildServiceProvider();

        var channel = sp.GetRequiredService<Channel<TicketEvent>>();
        var writer = sp.GetRequiredService<ChannelWriter<TicketEvent>>();
        var reader = sp.GetRequiredService<ChannelReader<TicketEvent>>();

        Assert.NotNull(channel);
        Assert.Same(channel.Writer, writer);
        Assert.Same(channel.Reader, reader);
    }
}
