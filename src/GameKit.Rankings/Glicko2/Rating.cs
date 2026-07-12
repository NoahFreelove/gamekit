// SPDX-License-Identifier: BSD-3-Clause AND Apache-2.0
// Original work Copyright (c) 2015, Maarten Staa (BSD-3-Clause)
// https://github.com/MaartenStaa/glicko2-csharp commit 59033eec
// Modified work Copyright (c) 2026 GameKit contributors (Apache-2.0)

// Differences from upstream:
//   - Namespace changed from Glicko2 to GameKit.Rankings.Glicko2
//   - Class visibility narrowed from public to internal sealed
//   - Target framework: net10.0 (upstream targeted .NET 4.5 / PCL)

namespace GameKit.Rankings.Glicko2;

/// <summary>
/// Holds an individual's Glicko-2 rating.
///
/// Glicko-2 ratings are an average skill value, a standard deviation and a volatility
/// (how consistent the player is). Prof Glickman's paper on the algorithm allows scaling
/// of these values to be more directly comparable with existing rating systems such as
/// Elo or USCF's derivation thereof. This implementation outputs ratings at this larger scale.
/// </summary>
internal sealed class Rating
{
    private readonly RatingCalculator _ratingSystem;

    private double _rating;
    private double _ratingDeviation;
    private double _volatility;

    /// <summary>The number of results from which the rating has been calculated.</summary>
    private int _numberOfResults;

    // the following variables are used to hold values temporarily whilst running calculations
    private double _workingRating;
    private double _workingRatingDeviation;
    private double _workingVolatility;

    /// <summary>
    /// Constructor. Takes the rating, deviation, and volatility default values
    /// from the rating system.
    /// </summary>
    /// <param name="ratingSystem">The rating calculator that owns this rating.</param>
    internal Rating(RatingCalculator ratingSystem)
    {
        _ratingSystem = ratingSystem;
        _rating = _ratingSystem.GetDefaultRating();
        _ratingDeviation = _ratingSystem.GetDefaultRatingDeviation();
        _volatility = ratingSystem.GetDefaultVolatility();
    }

    /// <summary>
    /// Constructor. Allows you to pass in the rating, deviation, and volatility.
    /// </summary>
    /// <param name="ratingSystem">The rating calculator that owns this rating.</param>
    /// <param name="initRating">Initial rating value on the Glicko scale.</param>
    /// <param name="initRatingDeviation">Initial rating deviation on the Glicko scale.</param>
    /// <param name="initVolatility">Initial volatility.</param>
    internal Rating(RatingCalculator ratingSystem, double initRating, double initRatingDeviation,
        double initVolatility)
    {
        _ratingSystem = ratingSystem;
        _rating = initRating;
        _ratingDeviation = initRatingDeviation;
        _volatility = initVolatility;
    }

    /// <summary>Return the average skill value of the player.</summary>
    internal double GetRating() => _rating;

    internal void SetRating(double rating) => _rating = rating;

    /// <summary>
    /// Return the average skill value of the player scaled down
    /// to the scale used by the algorithm's internal workings.
    /// </summary>
    internal double GetGlicko2Rating() => _ratingSystem.ConvertRatingToGlicko2Scale(_rating);

    /// <summary>Set the average skill value, taking in a value in Glicko2 scale.</summary>
    internal void SetGlicko2Rating(double rating) =>
        _rating = _ratingSystem.ConvertRatingToOriginalGlickoScale(rating);

    internal double GetVolatility() => _volatility;

    internal void SetVolatility(double volatility) => _volatility = volatility;

    internal double GetRatingDeviation() => _ratingDeviation;

    internal void SetRatingDeviation(double ratingDeviation) => _ratingDeviation = ratingDeviation;

    /// <summary>
    /// Return the rating deviation of the player scaled down
    /// to the scale used by the algorithm's internal workings.
    /// </summary>
    internal double GetGlicko2RatingDeviation() =>
        _ratingSystem.ConvertRatingDeviationToGlicko2Scale(_ratingDeviation);

    /// <summary>Set the rating deviation, taking in a value in Glicko2 scale.</summary>
    internal void SetGlicko2RatingDeviation(double ratingDeviation) =>
        _ratingDeviation = _ratingSystem.ConvertRatingDeviationToOriginalGlickoScale(ratingDeviation);

    /// <summary>
    /// Used by the calculation engine, to move interim calculations into their "proper" places.
    /// </summary>
    internal void FinaliseRating()
    {
        SetGlicko2Rating(_workingRating);
        SetGlicko2RatingDeviation(_workingRatingDeviation);
        SetVolatility(_workingVolatility);

        SetWorkingRatingDeviation(0);
        SetWorkingRating(0);
        SetWorkingVolatility(0);
    }

    internal int GetNumberOfResults() => _numberOfResults;

    internal void IncrementNumberOfResults(int increment) => _numberOfResults += increment;

    internal void SetWorkingVolatility(double workingVolatility) => _workingVolatility = workingVolatility;

    internal void SetWorkingRating(double workingRating) => _workingRating = workingRating;

    internal void SetWorkingRatingDeviation(double workingRatingDeviation) =>
        _workingRatingDeviation = workingRatingDeviation;
}
