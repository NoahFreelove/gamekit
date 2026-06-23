// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Platformer3D.GameServer;

/// <summary>
/// Authoritative run-summary for a single player's platformer run.
/// Collected by <see cref="WebSocketGameSession"/> from the client WS frames and validated
/// by <see cref="RunSummaryValidator"/> before submission to the session-complete endpoint (D-01/D-03).
/// </summary>
/// <param name="SessionId">The session the run belongs to.</param>
/// <param name="StartMs">
/// Epoch-millisecond timestamp when the run started (from client <c>run_start</c> frame).
/// </param>
/// <param name="CheckpointTimesMs">
/// Ordered list of epoch-millisecond timestamps, one per checkpoint trigger in index order (from
/// <c>checkpoint</c> frames). Must be strictly ascending and within the [StartMs, FinishMs] range.
/// </param>
/// <param name="FinishMs">
/// Epoch-millisecond timestamp when the run finished (from client <c>run_finish</c> frame).
/// </param>
public sealed record RunSummary(
    Guid SessionId,
    long StartMs,
    IReadOnlyList<long> CheckpointTimesMs,
    long FinishMs);

// ─── Inbound WS message DTOs (client → server) ──────────────────────────────
// These are independent records (no shared base) because C# records are sealed
// by default and cannot be subclassed. The type discriminator is parsed manually
// by the WebSocketGameSession frame dispatcher.

/// <summary>
/// <c>run_start</c> frame — sent by the client to open a run summary session (D-02).
/// </summary>
/// <param name="MatchId">The match id from the URL segment, echoed back in this frame.</param>
/// <param name="StartMs">Epoch-millisecond timestamp at which the player started their run.</param>
public sealed record WsRunStart(
    [property: JsonPropertyName("matchId")] Guid MatchId,
    [property: JsonPropertyName("startMs")] long StartMs);

/// <summary>
/// <c>checkpoint</c> frame — sent once per checkpoint trigger in index order (D-02).
/// </summary>
/// <param name="Index">Zero-based checkpoint index. Must arrive in strictly ascending order.</param>
/// <param name="TimestampMs">Epoch-millisecond timestamp when the checkpoint was triggered.</param>
public sealed record WsCheckpoint(
    [property: JsonPropertyName("index")] int Index,
    [property: JsonPropertyName("timestampMs")] long TimestampMs);

/// <summary>
/// <c>run_finish</c> frame — sent when the player reaches the finish trigger (D-02).
/// Exactly one per WebSocket session; subsequent duplicates are rejected.
/// </summary>
/// <param name="FinishMs">Epoch-millisecond timestamp when the finish was triggered.</param>
public sealed record WsRunFinish(
    [property: JsonPropertyName("finishMs")] long FinishMs);

/// <summary>
/// <c>pong</c> frame — client response to server <c>ping</c> (D-04 liveness).
/// </summary>
public sealed record WsPong();

// ─── Outbound WS message DTOs (server → client) ──────────────────────────────

/// <summary>
/// <c>validated</c> frame — sent when the run-summary passed D-03 validation and the
/// authoritative completion was posted successfully.
/// </summary>
/// <param name="CompletionMs">Completion time in milliseconds (FinishMs - StartMs).</param>
/// <param name="SessionId">The session id the completion was recorded against.</param>
public sealed record WsValidated(
    [property: JsonPropertyName("completionMs")] long CompletionMs,
    [property: JsonPropertyName("sessionId")] Guid SessionId)
{
    /// <summary>Discriminator field for client-side type switch.</summary>
    [JsonPropertyName("type")]
    public string Type => "validated";
}

/// <summary>
/// <c>rejected</c> frame — sent when the run-summary failed D-03 validation.
/// </summary>
/// <param name="Reason">Machine-readable reason string (e.g. <c>"non_monotonic_checkpoints"</c>).</param>
public sealed record WsRejected(
    [property: JsonPropertyName("reason")] string Reason)
{
    /// <summary>Discriminator field for client-side type switch.</summary>
    [JsonPropertyName("type")]
    public string Type => "rejected";
}

/// <summary>
/// <c>ping</c> frame — sent by the server periodically for D-04 liveness detection.
/// The client must respond with <see cref="WsPong"/> within the keep-alive window.
/// </summary>
public sealed class WsPing
{
    /// <summary>Discriminator field for client-side type switch.</summary>
    [JsonPropertyName("type")]
    public string Type => "ping";
}
