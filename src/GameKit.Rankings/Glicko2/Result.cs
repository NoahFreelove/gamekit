// SPDX-License-Identifier: BSD-3-Clause AND Apache-2.0
// Original work Copyright (c) 2015, Maarten Staa (BSD-3-Clause)
// https://github.com/MaartenStaa/glicko2-csharp commit 59033eec
// Modified work Copyright (c) 2026 GameKit contributors (Apache-2.0)

// Differences from upstream:
//   - Namespace changed from Glicko2 to GameKit.Rankings.Glicko2
//   - Class visibility narrowed from public to internal sealed
//   - Target framework: net10.0 (upstream targeted .NET 4.5 / PCL)

using System;

namespace GameKit.Rankings.Glicko2;

/// <summary>
/// Represents the result of a match between two players.
/// </summary>
internal sealed class Result
{
    private const double PointsForWin = 1.0;
    private const double PointsForLoss = 0.0;
    private const double PointsForDraw = 0.5;

    private readonly bool _isDraw;
    private readonly Rating _winner;
    private readonly Rating _loser;

    /// <summary>
    /// Record a new result from a match between two players.
    /// </summary>
    /// <param name="winner">The winning player.</param>
    /// <param name="loser">The losing player.</param>
    /// <param name="isDraw">Whether the match was a draw.</param>
    internal Result(Rating winner, Rating loser, bool isDraw = false)
    {
        if (!ValidPlayers(winner, loser))
        {
            throw new ArgumentException("Players winner and loser are the same player");
        }

        _winner = winner;
        _loser = loser;
        _isDraw = isDraw;
    }

    /// <summary>
    /// Check that we're not doing anything silly like recording a match with only one player.
    /// </summary>
    private static bool ValidPlayers(Rating player1, Rating player2) => player1 != player2;

    /// <summary>
    /// Test whether a particular player participated in the match represented by this result.
    /// </summary>
    /// <param name="player">The player to check.</param>
    internal bool Participated(Rating player) => player == _winner || player == _loser;

    /// <summary>
    /// Returns the "score" for a match.
    /// </summary>
    /// <param name="player">The player to get the score for.</param>
    internal double GetScore(Rating player)
    {
        double score;

        if (_winner == player)
        {
            score = PointsForWin;
        }
        else if (_loser == player)
        {
            score = PointsForLoss;
        }
        else
        {
            throw new ArgumentException("Player did not participate in match", nameof(player));
        }

        if (_isDraw)
        {
            score = PointsForDraw;
        }

        return score;
    }

    /// <summary>
    /// Given a particular player, returns the opponent.
    /// </summary>
    /// <param name="player">The player whose opponent to retrieve.</param>
    internal Rating GetOpponent(Rating player)
    {
        if (_winner == player)
        {
            return _loser;
        }
        else if (_loser == player)
        {
            return _winner;
        }
        else
        {
            throw new ArgumentException("Player did not participate in match", nameof(player));
        }
    }

    internal Rating GetWinner() => _winner;

    internal Rating GetLoser() => _loser;
}
