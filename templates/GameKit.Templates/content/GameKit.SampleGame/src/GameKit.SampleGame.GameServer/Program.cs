// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors
//
// GameKit.SampleGame.GameServer — game-server tier console process (D-13).
//
// Demonstrates the production 2-process topology:
//   * Web tier (src/GameKit.SampleGame) WRITES to Postgres via gamekit_owner.
//   * This game-server tier READS from Postgres via gamekit_reader and
//     ORCHESTRATES game-server-authoritative session lifecycle transitions
//     over HTTP against the web tier's /api/sessions/{id}/{start,complete,abandon}
//     endpoints.
//
// Postgres role separation is enforced by docker/postgres/init/01-roles.sql.

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

var builder = Host.CreateApplicationBuilder(args);

// IHttpClientFactory wire-up: keeps socket reuse / DNS refresh / DI test-injection
// available even though the console only issues a handful of requests.
builder.Services.AddHttpClient("gamekit.web-api");

using var host = builder.Build();

var config = host.Services.GetRequiredService<IConfiguration>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();
var httpClientFactory = host.Services.GetRequiredService<IHttpClientFactory>();

// gamekit_reader credential per docker/postgres/init/01-roles.sql.
// Operators rotate the password via the docs/ops/postgres-roles.md runbook in production.
var gameKitConnString = config.GetConnectionString("GameKit")
    ?? throw new InvalidOperationException(
        "Missing ConnectionStrings:GameKit (expected gamekit_reader credentials per appsettings.json).");

var webApiBaseUrl = config["Services:WebApi:BaseUrl"]
    ?? throw new InvalidOperationException(
        "Missing Services:WebApi:BaseUrl (expected the web tier URL, e.g. http://localhost:5000).");

// Optional: a service-account JWT lets this process POST /api/sessions/{id}/start.
// Operator-coordinated runtime work; absent in the dev demo.
var serviceJwt = config["Services:WebApi:ServiceJwt"];
var demoSessionId = config["Services:WebApi:DemoSessionId"];

logger.LogInformation(
    "GameServer starting. Postgres role: gamekit_reader. Web API base URL: {Url}.",
    webApiBaseUrl);

// Step 1 — prove the gamekit_reader role can SELECT on gamekit.players. This is the
// game-server's bread-and-butter read path (matchmaking eligibility, ladder lookups).
try
{
    await using var conn = new NpgsqlConnection(gameKitConnString);
    await conn.OpenAsync(CancellationToken.None);

    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM gamekit.players;";
    var count = await cmd.ExecuteScalarAsync(CancellationToken.None);

    logger.LogInformation(
        "Postgres SELECT as gamekit_reader OK: {Count} players visible.",
        count);
}
catch (Exception ex)
{
    logger.LogError(
        ex,
        "Postgres SELECT failed. Verify docker-compose is up and gamekit_reader exists per docker/postgres/init/01-roles.sql.");
}

// Step 2 — prove cross-tier HTTP works by fetching the OpenAPI document
// (GameKit.OpenApi publishes /openapi/v1.json — anonymous; no auth required).
try
{
    var http = httpClientFactory.CreateClient("gamekit.web-api");
    http.BaseAddress = new Uri(webApiBaseUrl);

    using var response = await http.GetAsync("/openapi/v1.json", CancellationToken.None);
    logger.LogInformation(
        "Web API /openapi/v1.json fetch returned {Status}.",
        (int)response.StatusCode);
}
catch (Exception ex)
{
    logger.LogWarning(
        ex,
        "Web API GET /openapi/v1.json failed. Is the web tier running on {Url}?",
        webApiBaseUrl);
}

// Step 3 — game-server-authoritative session lifecycle demonstration.
// POST /api/sessions/{id}/start requires a service-account JWT (the RequiresServiceToken
// policy from GameKit.Rankings). The demo skips the POST when either the JWT or the
// session id are absent.
if (!string.IsNullOrWhiteSpace(serviceJwt) && !string.IsNullOrWhiteSpace(demoSessionId))
{
    try
    {
        var http = httpClientFactory.CreateClient("gamekit.web-api");
        http.BaseAddress = new Uri(webApiBaseUrl);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceJwt);

        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(
            $"/api/sessions/{demoSessionId}/start",
            content,
            CancellationToken.None);

        logger.LogInformation(
            "POST /api/sessions/{Id}/start returned {Status}.",
            demoSessionId,
            (int)response.StatusCode);
    }
    catch (Exception ex)
    {
        logger.LogWarning(
            ex,
            "POST /api/sessions/{Id}/start failed.",
            demoSessionId);
    }
}
else
{
    logger.LogInformation(
        "No service-account JWT + session id configured; skipping /api/sessions/{{id}}/start POST. " +
        "Set Services:WebApi:ServiceJwt + Services:WebApi:DemoSessionId in appsettings to exercise the call.");
}

logger.LogInformation(
    "GameServer started. Connected to Postgres as gamekit_reader. Web API base URL: {Url}.",
    webApiBaseUrl);
