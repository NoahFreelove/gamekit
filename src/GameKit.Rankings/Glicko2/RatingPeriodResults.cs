// SPDX-License-Identifier: BSD-3-Clause AND Apache-2.0
// Original work Copyright (c) 2015, Maarten Staa (BSD-3-Clause)
// https://github.com/MaartenStaa/glicko2-csharp commit 59033eec
// Modified work Copyright (c) 2026 GameKit contributors (Apache-2.0)

// Differences from upstream:
//   - Namespace changed from Glicko2 to GameKit.Rankings.Glicko2
//   - Class visibility narrowed from public to internal sealed
//   - Target framework: net10.0 (upstream targeted .NET 4.5 / PCL)

using System.Collections.Generic;

namespace GameKit.Rankings.Glicko2;

/// <summary>
/// This class holds the results accumulated over a rating period.
/// </summary>
internal sealed class RatingPeriodResults
{
    private readonly List<Result> _results = new List<Result>();
    private readonly HashSet<Rating> _participants = new HashSet<Rating>();

    /// <summary>Create an empty result set.</summary>
    internal RatingPeriodResults()
    {
    }

    /// <summary>
    /// Constructor that allows you to initialise the list of participants.
    /// </summary>
    /// <param name="participants">Pre-seeded set of participants.</param>
    internal RatingPeriodResults(HashSet<Rating> participants)
    {
        _participants = participants;
    }

    /// <summary>
    /// Add a result to the set.
    /// </summary>
    /// <param name="winner">The winning player.</param>
    /// <param name="loser">The losing player.</param>
    internal void AddResult(Rating winner, Rating loser)
    {
        var result = new Result(winner, loser);
        _results.Add(result);
    }

    /// <summary>
    /// Record a draw between two players and add to the set.
    /// </summary>
    /// <param name="player1">First player.</param>
    /// <param name="player2">Second player.</param>
    internal void AddDraw(Rating player1, Rating player2)
    {
        var result = new Result(player1, player2, isDraw: true);
        _results.Add(result);
    }

    /// <summary>
    /// Get a list of the results for a given player.
    /// </summary>
    /// <param name="player">The player to get results for.</param>
    internal IList<Result> GetResults(Rating player)
    {
        var filteredResults = new List<Result>();

        foreach (var result in _results)
        {
            if (result.Participated(player))
            {
                filteredResults.Add(result);
            }
        }

        return filteredResults;
    }

    /// <summary>Get all the participants whose results are being tracked.</summary>
    internal IEnumerable<Rating> GetParticipants()
    {
        // Run through the results and make sure all players have been pushed into the participants set.
        foreach (var result in _results)
        {
            _participants.Add(result.GetWinner());
            _participants.Add(result.GetLoser());
        }

        return _participants;
    }

    /// <summary>
    /// Add a participant to the rating period, e.g. so that their rating will
    /// still be calculated even if they don't actually compete.
    /// </summary>
    /// <param name="rating">The rating to add.</param>
    internal void AddParticipant(Rating rating)
    {
        _participants.Add(rating);
    }

    /// <summary>Clear the result set.</summary>
    internal void Clear()
    {
        _results.Clear();
    }
}
