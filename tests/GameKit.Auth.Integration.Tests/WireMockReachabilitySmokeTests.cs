// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Net.Http;
using System.Threading.Tasks;
using GameKit.TestFixtures;
using Xunit;

namespace GameKit.Auth.Integration.Tests;

/// <summary>Wave-0 smoke: confirms the AuthCollection fixture starts cleanly and WireMock default stubs answer.</summary>
[Collection("Auth")]
[Trait("Category", "Smoke")]
public sealed class WireMockReachabilitySmokeTests
{
    private readonly PostgresFixture _pg;
    private readonly RedisFixture _redis;
    private readonly WireMockFixture _wm;

    public WireMockReachabilitySmokeTests(PostgresFixture pg, RedisFixture redis, WireMockFixture wm)
    {
        _pg = pg; _redis = redis; _wm = wm;
    }

    [Fact]
    public async Task Steam_Default_Stub_Returns_IsValidTrue_Body()
    {
        using var http = new HttpClient();
        using var resp = await http.PostAsync(
            _wm.SteamOpenIdLoginUrl,
            new FormUrlEncodedContent(System.Array.Empty<System.Collections.Generic.KeyValuePair<string, string>>()));
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("is_valid:true", body);
    }

    [Fact]
    public async Task Discord_Default_UserInfo_Stub_Returns_Identify_Payload()
    {
        using var http = new HttpClient();
        using var resp = await http.GetAsync(_wm.DiscordUserInfoUrl);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains(WireMockDiscordStubs.DefaultDiscordId, body);
        Assert.Contains(WireMockDiscordStubs.DefaultUsername, body);
    }

    [Fact]
    public void Postgres_Fixture_Has_All_Three_Role_Connection_Strings()
    {
        Assert.False(string.IsNullOrEmpty(_pg.OwnerConnectionString));
        Assert.False(string.IsNullOrEmpty(_pg.AppConnectionString));
        Assert.False(string.IsNullOrEmpty(_pg.ReaderConnectionString));
    }

    [Fact]
    public void Redis_Fixture_Is_Up()
    {
        Assert.NotNull(_redis);
    }
}
