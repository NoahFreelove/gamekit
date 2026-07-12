// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using GameKit.Matchmaking.Http.Contracts;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Matchmaking.Integration.Tests;

/// <summary>
/// Integration tests for the four /api/parties/* routes — MATCH-03 / CONTEXT D-01..D-05.
/// Verifies the happy path, the citext case-insensitive join (Pitfall §9 end-to-end), the
/// single-active-party guard (409), and the owner-only dissolve.
/// </summary>
[Collection("Matchmaking")]
[Trait("Category", "Integration")]
public sealed class PartyEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private MatchmakingTestApp? _app;

    public PartyEndpointTests(PostgresFixture pg, RedisFixture redis)
    {
        _pg = pg;
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        _app = new MatchmakingTestApp();
        await _app.StartAsync(_pg, _redis);
    }

    public async Task DisposeAsync()
    {
        if (_app is not null) await _app.DisposeAsync();
    }

    [Fact]
    public async Task Create_GeneratesCode_AndReturns201()
    {
        var player = Guid.NewGuid();
        using var client = _app!.CreateClient(player);

        var resp = await client.PostAsJsonAsync("/api/parties", new CreatePartyRequest());
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<PartyResponse>();
        Assert.NotNull(body);
        Assert.Equal(player, body!.OwnerPlayerId);
        Assert.Equal("open", body.State);
        Assert.NotEmpty(body.PartyCode);
        Assert.InRange(body.PartyCode.Length, 6, 8);
        Assert.Single(body.MemberPlayerIds);
        Assert.Equal(player, body.MemberPlayerIds[0]);
    }

    [Fact]
    public async Task Join_IsCaseInsensitive_PerPitfall9()
    {
        var owner = Guid.NewGuid();
        var joiner = Guid.NewGuid();
        using var ownerClient = _app!.CreateClient(owner);
        using var joinerClient = _app!.CreateClient(joiner);

        var createResp = await ownerClient.PostAsJsonAsync("/api/parties", new CreatePartyRequest());
        var party = await createResp.Content.ReadFromJsonAsync<PartyResponse>();

        // Join with the OPPOSITE case from what was issued — citext case-folds at SQL level.
        var altCase = party!.PartyCode.ToLowerInvariant() == party.PartyCode
            ? party.PartyCode.ToUpperInvariant()
            : party.PartyCode.ToLowerInvariant();

        var joinResp = await joinerClient.PostAsJsonAsync("/api/parties/join", new JoinPartyRequest(altCase));
        Assert.Equal(HttpStatusCode.OK, joinResp.StatusCode);

        var joined = await joinResp.Content.ReadFromJsonAsync<PartyResponse>();
        Assert.Equal(party.PartyId, joined!.PartyId);
        Assert.Equal(2, joined.MemberPlayerIds.Count);
    }

    [Fact]
    public async Task Join_RejectsWhenPlayerAlreadyInActiveParty()
    {
        var p1Owner = Guid.NewGuid();
        var p2Owner = Guid.NewGuid();
        using var c1 = _app!.CreateClient(p1Owner);
        using var c2 = _app!.CreateClient(p2Owner);

        var party1Resp = await c1.PostAsJsonAsync("/api/parties", new CreatePartyRequest());
        var party2Resp = await c2.PostAsJsonAsync("/api/parties", new CreatePartyRequest());
        var party1 = await party1Resp.Content.ReadFromJsonAsync<PartyResponse>();
        var party2 = await party2Resp.Content.ReadFromJsonAsync<PartyResponse>();

        // p2Owner is already the owner of party2 (active). Joining party1 must be rejected.
        var joinResp = await c2.PostAsJsonAsync("/api/parties/join", new JoinPartyRequest(party1!.PartyCode));
        Assert.Equal(HttpStatusCode.Conflict, joinResp.StatusCode);
    }

    [Fact]
    public async Task Dissolve_RequiresOwner()
    {
        var owner = Guid.NewGuid();
        var joiner = Guid.NewGuid();
        using var ownerClient = _app!.CreateClient(owner);
        using var joinerClient = _app!.CreateClient(joiner);

        var createResp = await ownerClient.PostAsJsonAsync("/api/parties", new CreatePartyRequest());
        var party = await createResp.Content.ReadFromJsonAsync<PartyResponse>();
        await joinerClient.PostAsJsonAsync("/api/parties/join", new JoinPartyRequest(party!.PartyCode));

        // Non-owner attempts dissolve → 403.
        var dissolveResp = await joinerClient.PostAsync($"/api/parties/{party.PartyId}/dissolve", null);
        Assert.Equal(HttpStatusCode.Forbidden, dissolveResp.StatusCode);
    }

    [Fact]
    public async Task Dissolve_TransitionsState_AndCleansMembers()
    {
        var owner = Guid.NewGuid();
        using var client = _app!.CreateClient(owner);

        var createResp = await client.PostAsJsonAsync("/api/parties", new CreatePartyRequest());
        var party = await createResp.Content.ReadFromJsonAsync<PartyResponse>();

        var dissolveResp = await client.PostAsync($"/api/parties/{party!.PartyId}/dissolve", null);
        Assert.Equal(HttpStatusCode.NoContent, dissolveResp.StatusCode);

        // Verify via GET — state is dissolved.
        var getResp = await client.GetAsync($"/api/parties/{party.PartyId}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var afterDissolve = await getResp.Content.ReadFromJsonAsync<PartyResponse>();
        Assert.Equal("dissolved", afterDissolve!.State);
        // PartyMember rows preserved for audit (CONTEXT D-04).
        Assert.NotEmpty(afterDissolve.MemberPlayerIds);

        // The owner can now create a new party — slot freed.
        var party2Resp = await client.PostAsJsonAsync("/api/parties", new CreatePartyRequest());
        Assert.Equal(HttpStatusCode.Created, party2Resp.StatusCode);
    }
}
