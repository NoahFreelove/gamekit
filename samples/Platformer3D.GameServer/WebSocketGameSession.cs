// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Platformer3D.GameServer;

/// <summary>
/// Per-connection state machine for the <c>/ws/game/{matchId}</c> WebSocket endpoint.
/// Receives <c>run_start</c> / <c>checkpoint</c> / <c>run_finish</c> / <c>pong</c> frames,
/// enforces exactly-one-finish per connection (D-03), runs <see cref="RunSummaryValidator"/>,
/// sends <c>validated</c> or <c>rejected</c>, and calls back the <see cref="PlatformerGameServerService"/>
/// to post the authoritative session-complete request (D-01).
/// </summary>
/// <remarks>
/// Not thread-safe — each instance is created and driven exclusively from a single WebSocket
/// handler invocation. The instance is discarded when the connection closes.
/// </remarks>
public sealed class WebSocketGameSession
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly WebSocket _ws;
    private readonly Guid _matchId;
    private readonly ClaimsPrincipal _user;
    private readonly PlatformerGameServerService _gameServer;
    private readonly ILogger _logger;

    // ─── Per-connection run state ─────────────────────────────────────────────
    private bool _runStarted;
    private bool _runFinished;   // exactly-one-finish guard (D-03)
    private long _startMs;
    private readonly List<long> _checkpointTimesMs = new();

    /// <summary>Constructs the session state machine.</summary>
    public WebSocketGameSession(
        WebSocket ws,
        Guid matchId,
        ClaimsPrincipal user,
        PlatformerGameServerService gameServer,
        ILogger logger)
    {
        _ws = ws;
        _matchId = matchId;
        _user = user;
        _gameServer = gameServer;
        _logger = logger;
    }

    /// <summary>
    /// Runs the receive loop until the WebSocket closes or <paramref name="ct"/> is cancelled.
    /// Dispatches each text frame to the appropriate handler.
    /// Also drives app-level ping/pong liveness (D-04): sends a <c>ping</c> frame every
    /// 15 seconds; the connection is closed if no <c>pong</c> arrives within the TCP keep-alive
    /// window (managed by <c>WebSocketOptions.KeepAliveInterval</c> on the host).
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using var pingTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var pingTask = SendPeriodicPingsAsync(pingTimer, ct);

        try
        {
            var buffer = new byte[8 * 1024];
            while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await _ws.ReceiveAsync(buffer, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (WebSocketException ex)
                {
                    _logger.LogDebug("WebSocket closed unexpectedly: {Message}", ex.Message);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                    break;

                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await HandleFrameAsync(json, ct);
            }
        }
        finally
        {
            pingTimer.Dispose();
            await pingTask;

            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
                }
                catch { /* best-effort */ }
            }
        }
    }

    // ─── Frame dispatcher ─────────────────────────────────────────────────────

    private async Task HandleFrameAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("type", out var typeProp))
            return;

        var type = typeProp.GetString();
        switch (type)
        {
            case "run_start":
                HandleRunStart(doc.RootElement);
                break;
            case "checkpoint":
                HandleCheckpoint(doc.RootElement);
                break;
            case "run_finish":
                await HandleRunFinishAsync(doc.RootElement, ct);
                break;
            case "pong":
                // Received liveness response — no action needed (D-04)
                break;
            default:
                _logger.LogDebug("Unknown WS frame type: {Type}", type);
                break;
        }
    }

    private void HandleRunStart(JsonElement el)
    {
        if (_runStarted)
        {
            _logger.LogWarning("Duplicate run_start frame on match {MatchId} — ignored", _matchId);
            return;
        }

        _startMs = el.TryGetProperty("startMs", out var startProp) ? startProp.GetInt64() : 0L;
        _checkpointTimesMs.Clear();
        _runStarted = true;
        _logger.LogDebug("run_start received for match {MatchId}, startMs={StartMs}", _matchId, _startMs);
    }

    private void HandleCheckpoint(JsonElement el)
    {
        if (!_runStarted || _runFinished)
            return;

        if (el.TryGetProperty("timestampMs", out var tsProp))
        {
            _checkpointTimesMs.Add(tsProp.GetInt64());
        }
    }

    private async Task HandleRunFinishAsync(JsonElement el, CancellationToken ct)
    {
        // Exactly-one-finish per connection (D-03).
        if (_runFinished)
        {
            _logger.LogWarning("Duplicate run_finish on match {MatchId} — rejecting", _matchId);
            await SendRejectedAsync("duplicate_finish", ct);
            return;
        }

        if (!_runStarted)
        {
            await SendRejectedAsync("run_not_started", ct);
            return;
        }

        _runFinished = true;

        var finishMs = el.TryGetProperty("finishMs", out var fp) ? fp.GetInt64() : 0L;

        // Build the run-summary and validate it (D-03).
        var summary = new RunSummary(
            SessionId: _matchId,  // sessionId and matchId are co-located for this demo
            StartMs: _startMs,
            CheckpointTimesMs: _checkpointTimesMs.AsReadOnly(),
            FinishMs: finishMs);

        var validationResult = RunSummaryValidator.Validate(summary);
        if (validationResult != RunSummaryValidationResult.Ok)
        {
            var reason = validationResult switch
            {
                RunSummaryValidationResult.NonMonotonic => "non_monotonic_checkpoints",
                RunSummaryValidationResult.Implausible   => "implausible_duration",
                RunSummaryValidationResult.DuplicateFinish => "duplicate_finish",
                _ => "validation_failed",
            };
            _logger.LogWarning(
                "Run-summary validation failed for match {MatchId}: {Reason}", _matchId, reason);
            await SendRejectedAsync(reason, ct);
            return;
        }

        var completionMs = finishMs - _startMs;

        // Notify the player their run was validated.
        await SendValidatedAsync(completionMs, _matchId, ct);

        // Report result to the game server — it will coordinate with the other player
        // and post the authoritative session-complete once both have finished.
        await _gameServer.RecordPlayerRunAsync(_matchId, _user, completionMs, ct);

        _logger.LogInformation(
            "Run validated for match {MatchId}: completionMs={CompletionMs}", _matchId, completionMs);
    }

    // ─── Outbound helpers ─────────────────────────────────────────────────────

    private Task SendValidatedAsync(long completionMs, Guid sessionId, CancellationToken ct)
    {
        var msg = new WsValidated(completionMs, sessionId);
        var json = JsonSerializer.Serialize(msg, JsonOpts);
        return SendTextAsync(json, ct);
    }

    private Task SendRejectedAsync(string reason, CancellationToken ct)
    {
        var msg = new WsRejected(reason);
        var json = JsonSerializer.Serialize(msg, JsonOpts);
        return SendTextAsync(json, ct);
    }

    private async Task SendPeriodicPingsAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (_ws.State != WebSocketState.Open)
                    break;
                var ping = JsonSerializer.Serialize(new WsPing(), JsonOpts);
                await SendTextAsync(ping, ct);
            }
        }
        catch (OperationCanceledException) { /* expected on shutdown */ }
        catch (Exception ex)
        {
            _logger.LogDebug("Ping timer stopped: {Message}", ex.Message);
        }
    }

    private async Task SendTextAsync(string json, CancellationToken ct)
    {
        if (_ws.State != WebSocketState.Open)
            return;
        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await _ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Send failed: {Message}", ex.Message);
        }
    }
}
