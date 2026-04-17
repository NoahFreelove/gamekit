// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace TicTacToeDuel.Http;

/// <summary>Request body for <c>POST /demo/players/register</c>.</summary>
public sealed record RegisterPlayerRequest(string DisplayName);

/// <summary>Response body for <c>POST /demo/players/register</c>.</summary>
public sealed record RegisterPlayerResponse(Guid Id, string DisplayName);

/// <summary>Request body for <c>POST /demo/games</c>.</summary>
public sealed record CreateGameRequest(Guid PlayerXId, Guid PlayerOId);

/// <summary>Request body for <c>POST /demo/games/{id}/moves</c>.</summary>
public sealed record MoveRequest(Guid PlayerId, int Row, int Col);

/// <summary>One participant's view within <see cref="GameStateResponse"/>.</summary>
public sealed record ParticipantView(Guid? PlayerId, int Team, string DisplayName, string? Result);

/// <summary>Full game state shape returned by the create/move/get endpoints.</summary>
public sealed record GameStateResponse(
    Guid Id,
    string State,
    int[][] Cells,
    string WhoseTurn,
    string Outcome,
    ParticipantView[] Participants);
