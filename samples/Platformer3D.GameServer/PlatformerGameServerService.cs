// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Entities;
using GameKit.Core.Http.Contracts;
using GameKit.Rankings.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platformer3D.GameServer;

/// <summary>
/// Embedded <see cref="IHostedService"/> that acts as the authoritative game server for the
/// Platformer3D demo (D-13). Responsibilities:
/// <list type="bullet">
/// <item>
///   <description>
///     At <see cref="StartAsync"/>: issues an in-process service token via a scoped
///     <see cref="IServiceTokenService"/> (revoke-then-issue pattern, A5 / Pitfall 2).
///     The raw token is held in a private field and NEVER logged, returned to clients,
///     or persisted by sample code (D-13 / T-21-14).
///   </description>
/// </item>
/// <item>
///   <description>
///     Exposes <see cref="HandleConnectionAsync"/> for the <c>/ws/game/{matchId}</c> endpoint.
///     Each connection runs a <see cref="WebSocketGameSession"/> state machine.
///   </description>
/// </item>
/// <item>
///   <description>
///     When both players in a 1v1 match have submitted validated run summaries, posts
///     <c>POST /api/sessions/{sessionId}/complete</c> with a deterministic
///     <c>Idempotency-Key</c> and Bearer service token so the session is completed
///     authoritatively and idempotently (R7 / D-05).
///   </description>
/// </item>
/// </list>
/// </summary>
public sealed class PlatformerGameServerService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformerGameServerService> _logger;

    // Raw service token held in process memory — NEVER logged/returned/persisted (T-21-14).
    private string? _serviceTokenRaw;

    // Track which sessions have been activated (Pending → Active) so only the first
    // player connection triggers POST /api/sessions/{id}/start.
    private readonly ConcurrentDictionary<Guid, bool> _activatedSessions = new();

    // Track completed run times per matchId (matchId → list of (playerId, completionMs)).
    // Two entries triggers the authoritative completion POST.
    private readonly ConcurrentDictionary<Guid, List<(Guid PlayerId, long CompletionMs)>> _runResults = new();
    private readonly SemaphoreSlim _runResultsLock = new(1, 1);

    // Track which players have connected to each match (matchId → set of playerIds). Used by the
    // DNF timeout to identify the opponent when only one player submits a run.
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, byte>> _connectedPlayers = new();

    // DNF (did-not-finish) timeout: once the FIRST player finishes a 1v1, the opponent has this
    // long to also finish. If they don't (rage-quit / AFK / closed tab), the match is completed
    // with the finisher as the winner so it always resolves instead of hanging forever. The
    // session-complete POST is idempotent, so a late real completion that races this is a no-op.
    private static readonly TimeSpan DnfTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Constructs the embedded game server service.</summary>
    public PlatformerGameServerService(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<PlatformerGameServerService> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    // ─── IHostedService ──────────────────────────────────────────────────────

    /// <summary>
    /// Issues the in-process service token at startup. Uses revoke-then-issue (A5 / Pitfall 2):
    /// <see cref="IServiceTokenService.RevokeAsync"/> returns <see langword="false"/> when the
    /// name is absent (never throws), so it is called unconditionally before
    /// <see cref="IServiceTokenService.IssueAsync"/> to handle container restarts cleanly.
    /// <see cref="IServiceTokenService"/> is registered SCOPED — must be resolved via a scope
    /// (not injected into the constructor).
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var baseName = _configuration["Platformer:ServiceTokenName"]
            ?? "platformer-gameserver-embedded";

        using var scope = _scopeFactory.CreateScope();
        var tokenSvc = scope.ServiceProvider.GetRequiredService<IServiceTokenService>();

        // Revoke any legacy fixed-name token from older builds (false-on-missing, no throw).
        await tokenSvc.RevokeAsync(baseName, cancellationToken);

        // Issue a fresh token under a UNIQUE per-start name. ServiceTokenService enforces a
        // unique index on Name, and RevokeAsync only marks (keeps) the row — so reusing a fixed
        // name across restarts throws ServiceTokenNameAlreadyExistsException once the row is
        // persisted (e.g. `docker compose restart` / `up` without `down -v`). A unique name per
        // start makes the embedded server restart-safe; the name is only a label — auth validates
        // the token by its hash (TokenHash unique index), never by name. (D-15: GameServer-only.)
        var tokenName = $"{baseName}-{Guid.NewGuid():N}";
        var (raw, _) = await tokenSvc.IssueAsync(tokenName, expiresAt: null, cancellationToken);
        _serviceTokenRaw = raw;

        _logger.LogInformation(
            "In-process service token '{Name}' issued. Raw token held in memory only (D-13).",
            tokenName);
    }

    /// <summary>No-op — token cleanup is handled by <see cref="StartAsync"/> on the next start.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ─── WebSocket connection handler ────────────────────────────────────────

    /// <summary>
    /// Handles an accepted WebSocket connection for the <c>/ws/game/{matchId}</c> endpoint (D-01).
    /// Creates a <see cref="WebSocketGameSession"/> and runs its receive loop.
    /// </summary>
    /// <param name="ws">The accepted WebSocket.</param>
    /// <param name="matchId">The match id extracted from the URL route.</param>
    /// <param name="user">The authenticated player principal (guaranteed by auth middleware, D-01).</param>
    /// <param name="ct">Cancellation token tied to the HTTP connection lifetime.</param>
    public async Task HandleConnectionAsync(
        WebSocket ws,
        Guid matchId,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "WebSocket connected for match {MatchId}, player {Player}.",
            matchId,
            user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "(unknown)");

        // Remember which players have connected to this match so the DNF timeout can identify
        // the opponent if only one player ends up submitting a run.
        var connectedIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        if (Guid.TryParse(connectedIdStr, out var connectedPid))
            _connectedPlayers.GetOrAdd(matchId, _ => new ConcurrentDictionary<Guid, byte>())[connectedPid] = 1;

        // Transition the session from Pending → Active on the first player connection.
        // Idempotent: only the first caller per matchId sends the start request (D-13 / PRES-05).
        if (_activatedSessions.TryAdd(matchId, true))
        {
            await StartSessionAsync(matchId, ct);
        }

        var session = new WebSocketGameSession(ws, matchId, user, this, _logger);
        await session.RunAsync(ct);

        _logger.LogInformation("WebSocket closed for match {MatchId}.", matchId);
    }

    // ─── Run result coordination ─────────────────────────────────────────────

    /// <summary>
    /// Records a validated player run result. When both players in the 1v1 match have posted,
    /// triggers the authoritative session-complete POST.
    /// </summary>
    internal async Task RecordPlayerRunAsync(
        Guid matchId,
        ClaimsPrincipal user,
        long completionMs,
        CancellationToken ct)
    {
        var playerIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        if (!Guid.TryParse(playerIdStr, out var playerId))
        {
            _logger.LogWarning("Cannot extract player id from user principal for match {MatchId}.", matchId);
            return;
        }

        List<(Guid PlayerId, long CompletionMs)> results;
        bool shouldComplete;
        bool isFirstFinisher;

        await _runResultsLock.WaitAsync(ct);
        try
        {
            results = _runResults.GetOrAdd(matchId, _ => new List<(Guid, long)>());
            results.Add((playerId, completionMs));
            shouldComplete = results.Count >= 2;
            isFirstFinisher = results.Count == 1;
        }
        finally
        {
            _runResultsLock.Release();
        }

        if (shouldComplete)
        {
            // Take the first two results — extra entries (from reconnects) are ignored.
            await PostCompleteAsync(matchId, results[0].PlayerId, results[0].CompletionMs,
                                    results[1].PlayerId, results[1].CompletionMs, ct);

            // Clean up the in-memory tracking.
            _runResults.TryRemove(matchId, out _);
            _connectedPlayers.TryRemove(matchId, out _);
        }
        else if (isFirstFinisher)
        {
            // Arm the DNF timeout. Fire-and-forget with NO request cancellation token — the
            // finisher's WebSocket may close (they move to the results screen) before the
            // opponent's grace period elapses, and we still want the match to resolve.
            _ = DnfTimeoutAsync(matchId, playerId, completionMs);
        }
    }

    /// <summary>
    /// After the first player finishes, waits <see cref="DnfTimeout"/> for the opponent. If the
    /// opponent has still not submitted a run, completes the session with the finisher as the
    /// winner (opponent = did-not-finish loss) so the match never hangs. Idempotent vs. a late
    /// normal completion via the deterministic Idempotency-Key.
    /// </summary>
    private async Task DnfTimeoutAsync(Guid matchId, Guid finisherId, long finisherMs)
    {
        try { await Task.Delay(DnfTimeout); }
        catch (OperationCanceledException) { return; }

        bool stillPending;
        await _runResultsLock.WaitAsync();
        try
        {
            stillPending = _runResults.TryGetValue(matchId, out var r) && r.Count == 1;
        }
        finally
        {
            _runResultsLock.Release();
        }
        if (!stillPending)
            return; // the opponent finished in time (or the match already completed)

        var opponentId = _connectedPlayers.TryGetValue(matchId, out var players)
            ? players.Keys.FirstOrDefault(p => p != finisherId)
            : Guid.Empty;
        if (opponentId == Guid.Empty)
            opponentId = await LookupOpponentFromDbAsync(matchId, finisherId) ?? Guid.Empty;

        if (opponentId == Guid.Empty)
        {
            _logger.LogWarning(
                "DNF timeout for match {MatchId}: could not determine the opponent — leaving the session incomplete.",
                matchId);
            return;
        }

        _runResults.TryRemove(matchId, out _);
        _connectedPlayers.TryRemove(matchId, out _);

        _logger.LogInformation(
            "DNF timeout for match {MatchId}: {Finisher} finished in {Ms}ms; opponent {Opponent} did not finish — completing with finisher as winner.",
            matchId, finisherId, finisherMs, opponentId);

        // Finisher's real time vs the opponent's sentinel (long.MaxValue) → finisher Win, opponent Loss.
        await PostCompleteAsync(matchId, finisherId, finisherMs, opponentId, long.MaxValue, CancellationToken.None);
    }

    /// <summary>
    /// Looks up the opponent's player id from the session's participants (fallback for the DNF
    /// path when the opponent never opened a WebSocket on this server instance).
    /// </summary>
    private async Task<Guid?> LookupOpponentFromDbAsync(Guid sessionId, Guid finisherId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<GameKit.Core.Data.GameKitDbContext>();
            var ids = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
                db.Set<SessionParticipant>()
                  .Where(p => p.SessionId == sessionId && p.PlayerId != null)
                  .Select(p => p.PlayerId!.Value));
            var opponent = ids.FirstOrDefault(id => id != finisherId);
            return opponent == Guid.Empty ? null : opponent;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNF opponent DB lookup failed for session {SessionId}.", sessionId);
            return null;
        }
    }

    // ─── Authoritative session-complete POST ─────────────────────────────────

    /// <summary>
    /// Posts the authoritative session completion to <c>POST /api/sessions/{sessionId}/complete</c>
    /// with Bearer service-token auth and a deterministic <c>Idempotency-Key</c> (R7 / D-05).
    /// </summary>
    private async Task PostCompleteAsync(
        Guid sessionId,
        Guid p1Id, long p1Ms,
        Guid p2Id, long p2Ms,
        CancellationToken ct)
    {
        if (_serviceTokenRaw is null)
        {
            _logger.LogError("Service token not available — cannot post session-complete for {SessionId}.", sessionId);
            return;
        }

        var baseUrl = _configuration["Platformer:WebApiBaseUrl"]
            ?? "http://localhost:8080";

        var request = BuildCompleteRequest(p1Id, p1Ms, p2Id, p2Ms);
        var idempotencyKey = IdempotencyKeyFor(sessionId);

        try
        {
            var http = _httpClientFactory.CreateClient("platformer.web-api");
            http.BaseAddress = new Uri(baseUrl);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _serviceTokenRaw);
            http.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);

            var response = await http.PostAsJsonAsync(
                $"/api/sessions/{sessionId}/complete",
                request,
                ct);

            _logger.LogInformation(
                "POST /api/sessions/{SessionId}/complete → {StatusCode} (Idempotency-Key: {Key}).",
                sessionId, (int)response.StatusCode, idempotencyKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post session-complete for {SessionId}.", sessionId);
        }
    }

    // ─── Session lifecycle helpers ───────────────────────────────────────────

    /// <summary>
    /// Activates the session (Pending → Active) via <c>POST /api/sessions/{sessionId}/start</c>.
    /// Called once per match on the first player WebSocket connection (D-13 / PRES-05).
    /// A non-200 response is logged but not fatal — the complete POST will still attempt later
    /// and will return 409 InvalidState if start didn't go through.
    /// </summary>
    private async Task StartSessionAsync(Guid sessionId, CancellationToken ct)
    {
        if (_serviceTokenRaw is null)
        {
            _logger.LogWarning("Service token not available — cannot start session {SessionId}.", sessionId);
            return;
        }

        try
        {
            var baseUrl = _configuration["Platformer:WebApiBaseUrl"] ?? "http://localhost:8080";
            var http = _httpClientFactory.CreateClient("platformer.web-api");
            http.BaseAddress = new Uri(baseUrl);
            http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _serviceTokenRaw);

            // SessionStartRequest is an empty record — POST with an empty JSON body.
            var response = await http.PostAsJsonAsync(
                $"/api/sessions/{sessionId}/start",
                new { }, // Empty body matching SessionStartRequest()
                ct);
            _logger.LogInformation(
                "POST /api/sessions/{SessionId}/start → {StatusCode}.",
                sessionId, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start session {SessionId}.", sessionId);
        }
    }

    // ─── Static helpers (unit-testable without HTTP) ─────────────────────────

    /// <summary>
    /// Builds the <see cref="SessionCompleteRequest"/> for a 1v1 match.
    /// <list type="bullet">
    ///   <item><description>Faster integer-ms time → <see cref="SessionResult.Win"/> (opponent <see cref="SessionResult.Loss"/>).</description></item>
    ///   <item><description>Exact integer-ms tie → both <see cref="SessionResult.Draw"/> (D-10), equal <see cref="SessionCompleteParticipant.Score"/>.</description></item>
    ///   <item><description>Integer-ms completion time stored in <see cref="SessionCompleteParticipant.Score"/>.</description></item>
    /// </list>
    /// </summary>
    public static SessionCompleteRequest BuildCompleteRequest(
        Guid p1Id, long p1Ms,
        Guid p2Id, long p2Ms)
    {
        SessionResult r1, r2;
        if (p1Ms == p2Ms)
        {
            // D-10: exact integer-ms tie → symmetric Draw, no rating change
            r1 = SessionResult.Draw;
            r2 = SessionResult.Draw;
        }
        else if (p1Ms < p2Ms)
        {
            // p1 faster → Win
            r1 = SessionResult.Win;
            r2 = SessionResult.Loss;
        }
        else
        {
            // p2 faster → p2 Win
            r1 = SessionResult.Loss;
            r2 = SessionResult.Win;
        }

        var participants = new List<SessionCompleteParticipant>
        {
            new SessionCompleteParticipant(
                PlayerId: p1Id,
                Team: 0,
                Result: r1,
                Score: (int)Math.Min(p1Ms, int.MaxValue)),  // integer-ms completion time
            new SessionCompleteParticipant(
                PlayerId: p2Id,
                Team: 1,
                Result: r2,
                Score: (int)Math.Min(p2Ms, int.MaxValue)),
        };

        return new SessionCompleteRequest(participants);
    }

    /// <summary>
    /// Returns the deterministic <c>Idempotency-Key</c> for a session (R7 / D-05).
    /// Two calls with the same <paramref name="sessionId"/> always return byte-equal strings.
    /// </summary>
    public static string IdempotencyKeyFor(Guid sessionId) =>
        $"platformer-session-{sessionId}";
}
