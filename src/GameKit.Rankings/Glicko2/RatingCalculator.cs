// SPDX-License-Identifier: BSD-3-Clause AND GPL-3.0-or-later
// Original work Copyright (c) 2015, Maarten Staa (BSD-3-Clause)
// https://github.com/MaartenStaa/glicko2-csharp commit 59033eec
// Modified work Copyright (c) 2026 GameKit contributors (GPL-3.0-or-later)

// Differences from upstream:
//   - Namespace changed from Glicko2 to GameKit.Rankings.Glicko2
//   - Class visibility narrowed from public to internal sealed
//   - Target framework: net10.0 (upstream targeted .NET 4.5 / PCL)

using System;
using System.Collections.Generic;

namespace GameKit.Rankings.Glicko2;

/// <summary>
/// Core Glicko-2 rating calculation engine.
///
/// Upstream default tau is 0.75; GameKit's <c>Glicko2Algorithm</c> constructs this calculator
/// with <c>tau: 0.5</c> — Glickman's own example value. Do not change this default here;
/// the caller is responsible for supplying the correct tau.
/// </summary>
internal sealed class RatingCalculator
{
    private const double DefaultRating = 1500.0;
    private const double DefaultDeviation = 350;
    private const double DefaultVolatility = 0.06;
    private const double DefaultTau = 0.75; // upstream default — callers should pass tau explicitly
    private const double Multiplier = 173.7178;
    private const double ConvergenceTolerance = 0.000001;

    private readonly double _tau; // constrains volatility over time
    private readonly double _defaultVolatility;

    /// <summary>
    /// Standard constructor, taking default values for volatility (0.06) and tau (0.75 upstream default).
    /// GameKit's <c>Glicko2Algorithm</c> does NOT use this constructor — it always passes tau: 0.5
    /// explicitly via the two-argument constructor.
    /// </summary>
    internal RatingCalculator()
    {
        _tau = DefaultTau;
        _defaultVolatility = DefaultVolatility;
    }

    /// <summary>
    /// Constructor allowing you to specify values for volatility and tau.
    /// </summary>
    /// <param name="initVolatility">Initial volatility (Glickman's example uses 0.06).</param>
    /// <param name="tau">System constant constraining volatility change over time (Glickman's example uses 0.5).</param>
    internal RatingCalculator(double initVolatility, double tau)
    {
        _defaultVolatility = initVolatility;
        _tau = tau;
    }

    /// <summary>
    /// Run through all players within a resultset and calculate their new ratings.
    ///
    /// Players within the resultset who did not compete during the rating period
    /// will have their deviation increase (in line with Prof Glickman's paper).
    ///
    /// Note that this method will clear the results held in the association result set.
    /// </summary>
    /// <param name="results">The accumulated results for this rating period.</param>
    internal void UpdateRatings(RatingPeriodResults results)
    {
        foreach (var player in results.GetParticipants())
        {
            if (results.GetResults(player).Count > 0)
            {
                CalculateNewRating(player, results.GetResults(player));
            }
            else
            {
                // if a player does not compete during the rating period, then only Step 6 applies.
                // the player's rating and volatility parameters remain the same but deviation increases
                player.SetWorkingRating(player.GetGlicko2Rating());
                player.SetWorkingRatingDeviation(CalculateNewRatingDeviation(player.GetGlicko2RatingDeviation(),
                    player.GetVolatility()));
                player.SetWorkingVolatility(player.GetVolatility());
            }
        }

        // now iterate through the participants and confirm their new ratings
        foreach (var player in results.GetParticipants())
        {
            player.FinaliseRating();
        }

        // lastly, clear the result set down in anticipation of the next rating period
        results.Clear();
    }

    /// <summary>
    /// This is the function processing described in step 5 of Glickman's paper.
    /// </summary>
    private void CalculateNewRating(Rating player, IList<Result> results)
    {
        var phi = player.GetGlicko2RatingDeviation();
        var sigma = player.GetVolatility();
        var a = Math.Log(Math.Pow(sigma, 2));
        var delta = Delta(player, results);
        var v = V(player, results);

        // step 5.2 - set the initial values of the iterative algorithm to come in step 5.4
        var A = a;
        double B;
        if (Math.Pow(delta, 2) > Math.Pow(phi, 2) + v)
        {
            B = Math.Log(Math.Pow(delta, 2) - Math.Pow(phi, 2) - v);
        }
        else
        {
            double k = 1;
            B = a - (k * Math.Abs(_tau));

            while (F(B, delta, phi, v, a, _tau) < 0)
            {
                k++;
                B = a - (k * Math.Abs(_tau));
            }
        }

        // step 5.3
        var fA = F(A, delta, phi, v, a, _tau);
        var fB = F(B, delta, phi, v, a, _tau);

        // step 5.4
        while (Math.Abs(B - A) > ConvergenceTolerance)
        {
            var C = A + (((A - B) * fA) / (fB - fA));
            var fC = F(C, delta, phi, v, a, _tau);

            if (fC * fB < 0)
            {
                A = B;
                fA = fB;
            }
            else
            {
                fA = fA / 2.0;
            }

            B = C;
            fB = fC;
        }

        var newSigma = Math.Exp(A / 2.0);

        player.SetWorkingVolatility(newSigma);

        // Step 6
        var phiStar = CalculateNewRatingDeviation(phi, newSigma);

        // Step 7
        var newPhi = 1.0 / Math.Sqrt((1.0 / Math.Pow(phiStar, 2)) + (1.0 / v));

        // note that the newly calculated rating values are stored in a "working" area in the Rating object
        // this avoids us attempting to calculate subsequent participants' ratings against a moving target
        player.SetWorkingRating(
            player.GetGlicko2Rating()
            + (Math.Pow(newPhi, 2) * OutcomeBasedRating(player, results)));
        player.SetWorkingRatingDeviation(newPhi);
        player.IncrementNumberOfResults(results.Count);
    }

    private static double F(double x, double delta, double phi, double v, double a, double tau)
    {
        return (Math.Exp(x) * (Math.Pow(delta, 2) - Math.Pow(phi, 2) - v - Math.Exp(x)) /
                (2.0 * Math.Pow(Math.Pow(phi, 2) + v + Math.Exp(x), 2))) -
               ((x - a) / Math.Pow(tau, 2));
    }

    /// <summary>This is the first sub-function of step 3 of Glickman's paper.</summary>
    private static double G(double deviation) =>
        1.0 / (Math.Sqrt(1.0 + (3.0 * Math.Pow(deviation, 2) / Math.Pow(Math.PI, 2))));

    /// <summary>This is the second sub-function of step 3 of Glickman's paper.</summary>
    private static double E(double playerRating, double opponentRating, double opponentDeviation) =>
        1.0 / (1.0 + Math.Exp(-1.0 * G(opponentDeviation) * (playerRating - opponentRating)));

    /// <summary>This is the main function in step 3 of Glickman's paper.</summary>
    private static double V(Rating player, IEnumerable<Result> results)
    {
        var v = 0.0;

        foreach (var result in results)
        {
            v = v + (
                (Math.Pow(G(result.GetOpponent(player).GetGlicko2RatingDeviation()), 2))
                * E(player.GetGlicko2Rating(),
                    result.GetOpponent(player).GetGlicko2Rating(),
                    result.GetOpponent(player).GetGlicko2RatingDeviation())
                * (1.0 - E(player.GetGlicko2Rating(),
                    result.GetOpponent(player).GetGlicko2Rating(),
                    result.GetOpponent(player).GetGlicko2RatingDeviation())
                    ));
        }

        return Math.Pow(v, -1);
    }

    /// <summary>This is a formula as per step 4 of Glickman's paper.</summary>
    private double Delta(Rating player, IList<Result> results) =>
        V(player, results) * OutcomeBasedRating(player, results);

    /// <summary>
    /// This is a formula as per step 4 of Glickman's paper.
    /// </summary>
    /// <returns>Expected rating based on outcomes.</returns>
    private static double OutcomeBasedRating(Rating player, IEnumerable<Result> results)
    {
        double outcomeBasedRating = 0;

        foreach (var result in results)
        {
            outcomeBasedRating = outcomeBasedRating
                                 + (G(result.GetOpponent(player).GetGlicko2RatingDeviation())
                                    * (result.GetScore(player) - E(
                                        player.GetGlicko2Rating(),
                                        result.GetOpponent(player).GetGlicko2Rating(),
                                        result.GetOpponent(player).GetGlicko2RatingDeviation()))
                                     );
        }

        return outcomeBasedRating;
    }

    /// <summary>
    /// This is the formula defined in step 6. It is also used for players
    /// who have not competed during the rating period.
    /// </summary>
    /// <returns>New rating deviation.</returns>
    private static double CalculateNewRatingDeviation(double phi, double sigma) =>
        Math.Sqrt(Math.Pow(phi, 2) + Math.Pow(sigma, 2));

    /// <summary>
    /// Converts from the value used within the algorithm to a rating in
    /// the same range as traditional Elo et al.
    /// </summary>
    internal double ConvertRatingToOriginalGlickoScale(double rating) =>
        ((rating * Multiplier) + DefaultRating);

    /// <summary>
    /// Converts from a rating in the same range as traditional Elo
    /// et al to the value used within the algorithm.
    /// </summary>
    internal double ConvertRatingToGlicko2Scale(double rating) =>
        ((rating - DefaultRating) / Multiplier);

    /// <summary>
    /// Converts from the value used within the algorithm to a
    /// rating deviation in the same range as traditional Elo et al.
    /// </summary>
    internal double ConvertRatingDeviationToOriginalGlickoScale(double ratingDeviation) =>
        (ratingDeviation * Multiplier);

    /// <summary>
    /// Converts from a rating deviation in the same range as traditional Elo et al
    /// to the value used within the algorithm.
    /// </summary>
    internal double ConvertRatingDeviationToGlicko2Scale(double ratingDeviation) =>
        (ratingDeviation / Multiplier);

    internal double GetDefaultRating() => DefaultRating;

    internal double GetDefaultVolatility() => _defaultVolatility;

    internal double GetDefaultRatingDeviation() => DefaultDeviation;
}
