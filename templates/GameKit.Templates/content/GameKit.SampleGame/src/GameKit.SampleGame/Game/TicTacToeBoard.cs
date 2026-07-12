// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.SampleGame.Game;

/// <summary>Cell occupant / move marker. 0/1/2 match the on-disk JSON board encoding.</summary>
public enum Mark
{
    /// <summary>Empty cell.</summary>
    None = 0,

    /// <summary>X mark (team 0, moves first).</summary>
    X = 1,

    /// <summary>O mark (team 1, moves second).</summary>
    O = 2,
}

/// <summary>Terminal or in-progress outcome computed after each <see cref="TicTacToeBoard.ApplyMove"/>.</summary>
public enum BoardOutcome
{
    /// <summary>Game still in play.</summary>
    InProgress = 0,

    /// <summary>X has three in a row.</summary>
    XWins = 1,

    /// <summary>O has three in a row.</summary>
    OWins = 2,

    /// <summary>Board full, no winner.</summary>
    Draw = 3,
}

/// <summary>
/// Pure domain model for a 3x3 tic-tac-toe board. No I/O, no EF, no ASP.NET — the board
/// is rehydrated from <c>GameSession.Metadata</c> on every move and re-serialized after
/// applying. Mark &lt;-&gt; Team convention used by callers: Team 0 = X, Team 1 = O.
/// </summary>
public sealed class TicTacToeBoard
{
    private const int Size = 3;

    private TicTacToeBoard(Mark[,] cells, int moveCount, BoardOutcome outcome)
    {
        Cells = cells;
        MoveCount = moveCount;
        Outcome = outcome;
    }

    /// <summary>3x3 cell array. <see cref="Mark.None"/> in any cell means empty.</summary>
    public Mark[,] Cells { get; }

    /// <summary>Number of moves applied so far (0..9).</summary>
    public int MoveCount { get; private set; }

    /// <summary>Whose turn it is. X always moves first (even move count =&gt; X, odd =&gt; O).</summary>
    public Mark WhoseTurn => (MoveCount % 2 == 0) ? Mark.X : Mark.O;

    /// <summary>Terminal-state indicator, recomputed after each <see cref="ApplyMove"/>.</summary>
    public BoardOutcome Outcome { get; private set; }

    /// <summary>Creates a fresh empty board with X to move.</summary>
    public static TicTacToeBoard NewEmpty() => new(new Mark[Size, Size], 0, BoardOutcome.InProgress);

    /// <summary>
    /// Rehydrates a board from cell values (0/1/2), a move count, and a previously computed outcome.
    /// Intended for use by <c>TicTacToeBoardSerializer</c> only.
    /// </summary>
    internal static TicTacToeBoard FromState(Mark[,] cells, int moveCount, BoardOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (cells.GetLength(0) != Size || cells.GetLength(1) != Size)
            throw new ArgumentException($"Cells must be {Size}x{Size}.", nameof(cells));
        return new TicTacToeBoard(cells, moveCount, outcome);
    }

    /// <summary>
    /// Applies a move; validates bounds, cell occupancy, turn ownership, and terminal state.
    /// Updates <see cref="MoveCount"/> and <see cref="Outcome"/>.
    /// </summary>
    /// <param name="row">Row index, 0..2.</param>
    /// <param name="col">Column index, 0..2.</param>
    /// <param name="mark">The moving mark; must equal <see cref="WhoseTurn"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Row or column outside 0..2.</exception>
    /// <exception cref="InvalidOperationException">
    /// Cell is already occupied, it is not the supplied mark's turn, the mark is
    /// <see cref="Mark.None"/>, or the game is already over.
    /// </exception>
    public void ApplyMove(int row, int col, Mark mark)
    {
        if (row < 0 || row >= Size)
            throw new ArgumentOutOfRangeException(nameof(row), row, "row must be 0..2");
        if (col < 0 || col >= Size)
            throw new ArgumentOutOfRangeException(nameof(col), col, "col must be 0..2");
        if (mark == Mark.None)
            throw new InvalidOperationException("mark cannot be None");
        if (Outcome != BoardOutcome.InProgress)
            throw new InvalidOperationException("game over");
        if (mark != WhoseTurn)
            throw new InvalidOperationException("not your turn");
        if (Cells[row, col] != Mark.None)
            throw new InvalidOperationException("cell occupied");

        Cells[row, col] = mark;
        MoveCount++;
        Outcome = ComputeOutcome();
    }

    private BoardOutcome ComputeOutcome()
    {
        // Rows and columns.
        for (var i = 0; i < Size; i++)
        {
            if (Cells[i, 0] != Mark.None && Cells[i, 0] == Cells[i, 1] && Cells[i, 1] == Cells[i, 2])
                return MarkToWin(Cells[i, 0]);
            if (Cells[0, i] != Mark.None && Cells[0, i] == Cells[1, i] && Cells[1, i] == Cells[2, i])
                return MarkToWin(Cells[0, i]);
        }

        // Diagonals.
        if (Cells[0, 0] != Mark.None && Cells[0, 0] == Cells[1, 1] && Cells[1, 1] == Cells[2, 2])
            return MarkToWin(Cells[0, 0]);
        if (Cells[0, 2] != Mark.None && Cells[0, 2] == Cells[1, 1] && Cells[1, 1] == Cells[2, 0])
            return MarkToWin(Cells[0, 2]);

        return MoveCount == Size * Size ? BoardOutcome.Draw : BoardOutcome.InProgress;
    }

    private static BoardOutcome MarkToWin(Mark m) => m switch
    {
        Mark.X => BoardOutcome.XWins,
        Mark.O => BoardOutcome.OWins,
        _ => BoardOutcome.InProgress,
    };
}
