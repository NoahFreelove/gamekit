// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.IO;
using System.Text.Json;

namespace TicTacToeDuel.Game;

/// <summary>
/// (De)serializes <see cref="TicTacToeBoard"/> to/from the compact JSON shape that lives
/// in <c>GameSession.Metadata</c>:
/// <code>
/// { "v": 1, "cells": [[0,0,0],[0,0,0],[0,0,0]], "moveCount": 0, "outcome": "InProgress" }
/// </code>
/// </summary>
public static class TicTacToeBoardSerializer
{
    private const int Version = 1;

    /// <summary>Serializes the board into a fresh <see cref="JsonDocument"/> suitable for EF's jsonb column.</summary>
    public static JsonDocument ToJsonDocument(TicTacToeBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);

        var cells = new int[3][];
        for (var r = 0; r < 3; r++)
        {
            cells[r] = new int[3];
            for (var c = 0; c < 3; c++)
                cells[r][c] = (int)board.Cells[r, c];
        }

        var payload = new
        {
            v = Version,
            cells,
            moveCount = board.MoveCount,
            outcome = board.Outcome.ToString(),
        };

        return JsonSerializer.SerializeToDocument(payload);
    }

    /// <summary>Rehydrates a board from a <see cref="JsonDocument"/>. Throws <see cref="InvalidDataException"/> on shape mismatch.</summary>
    public static TicTacToeBoard FromJsonDocument(JsonDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("board document root must be an object");

        if (!root.TryGetProperty("v", out var versionEl) || versionEl.ValueKind != JsonValueKind.Number ||
            versionEl.GetInt32() != Version)
            throw new InvalidDataException($"unsupported board schema version; expected {Version}");

        if (!root.TryGetProperty("cells", out var cellsEl) || cellsEl.ValueKind != JsonValueKind.Array ||
            cellsEl.GetArrayLength() != 3)
            throw new InvalidDataException("cells must be a 3-element array");

        var cells = new Mark[3, 3];
        for (var r = 0; r < 3; r++)
        {
            var rowEl = cellsEl[r];
            if (rowEl.ValueKind != JsonValueKind.Array || rowEl.GetArrayLength() != 3)
                throw new InvalidDataException($"cells[{r}] must be a 3-element array");
            for (var c = 0; c < 3; c++)
            {
                var cellEl = rowEl[c];
                if (cellEl.ValueKind != JsonValueKind.Number)
                    throw new InvalidDataException($"cells[{r}][{c}] must be a number");
                var raw = cellEl.GetInt32();
                if (raw is < 0 or > 2)
                    throw new InvalidDataException($"cells[{r}][{c}] must be 0..2 (got {raw})");
                cells[r, c] = (Mark)raw;
            }
        }

        if (!root.TryGetProperty("moveCount", out var mcEl) || mcEl.ValueKind != JsonValueKind.Number)
            throw new InvalidDataException("moveCount must be a number");
        var moveCount = mcEl.GetInt32();
        if (moveCount is < 0 or > 9)
            throw new InvalidDataException($"moveCount must be 0..9 (got {moveCount})");

        if (!root.TryGetProperty("outcome", out var outcomeEl) || outcomeEl.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("outcome must be a string");
        if (!Enum.TryParse<BoardOutcome>(outcomeEl.GetString(), ignoreCase: false, out var outcome))
            throw new InvalidDataException($"outcome is not a valid BoardOutcome (got '{outcomeEl.GetString()}')");

        return TicTacToeBoard.FromState(cells, moveCount, outcome);
    }
}
