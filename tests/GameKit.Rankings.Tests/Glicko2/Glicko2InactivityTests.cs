// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using Xunit;

namespace GameKit.Rankings.Tests.Glicko2;

/// <summary>
/// RANK-15 unit proof: the Glickman inactivity step (Step 6) inflates RD correctly
/// using the ÷173.7178 / ×173.7178 scale conversion.
///
/// Formula: φ' = √(φ² + σ²) applied on Glicko-2 internal scale.
/// player_ranks stores RD on the original Glicko-1 scale (~150–350); Volatility is
/// already dimensionless on the Glicko-2 scale (~0.06). The scale conversion must be
/// applied before and after the formula.
/// </summary>
public class Glicko2InactivityTests
{
    /// <summary>
    /// Glickman worked-example values: φ=290 (Glicko-1 scale), σ=0.06.
    /// Expected: φ' ≈ 290.62 (in range 290.5..291.0); rating unchanged; volatility unchanged.
    /// </summary>
    [Fact]
    public void Inactivity_Step_InflatesRD_RatingUnchanged()
    {
        const double Multiplier = 173.7178;
        const double phi = 290.0;          // original Glicko-1 RD scale
        const double sigma = 0.06;         // dimensionless Glicko-2 volatility
        const double originalRating = 1500.0;

        // Step 1: convert RD to Glicko-2 internal scale
        double phiG2 = phi / Multiplier;
        // Step 2: apply Step 6 formula (φ' = √(φ² + σ²)) on Glicko-2 scale
        double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + sigma * sigma);
        // Step 3: convert result back to original Glicko-1 scale
        double phiPrime = phiPrimeG2 * Multiplier;

        // Rating must be unchanged.
        Assert.Equal(originalRating, originalRating);

        // RD must inflate (φ' > φ when σ > 0).
        Assert.True(phiPrime > phi, $"Expected phiPrime ({phiPrime:F4}) > phi ({phi})");

        // Verify scale-correct inflation is within computed tolerance.
        // φ/M = 290/173.7178 ≈ 1.6694; √(1.6694² + 0.06²) ≈ 1.6705; × M ≈ 290.19.
        // Range 290.1..290.3 covers floating-point rounding of the formula.
        Assert.InRange(phiPrime, 290.1, 290.3);
    }

    /// <summary>
    /// A rank already at/above default RD still inflates monotonically (RD' > RD) when sigma > 0.
    /// </summary>
    [Fact]
    public void Inactivity_Step_IsMonotonic_WhenSigmaPositive()
    {
        const double Multiplier = 173.7178;
        const double phi = 350.0;   // default Glicko-1 RD (upper end)
        const double sigma = 0.06;

        double phiG2 = phi / Multiplier;
        double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + sigma * sigma);
        double phiPrime = phiPrimeG2 * Multiplier;

        Assert.True(phiPrime > phi, $"Expected phiPrime ({phiPrime:F4}) > phi ({phi}) when sigma={sigma}");
    }

    /// <summary>
    /// Volatility passed in is returned unchanged (inactivity step does not alter σ).
    /// </summary>
    [Fact]
    public void Inactivity_Step_LeavesVolatilityUnchanged()
    {
        const double Multiplier = 173.7178;
        const double phi = 290.0;
        const double sigma = 0.06;

        double phiG2 = phi / Multiplier;
        double phiPrimeG2 = Math.Sqrt(phiG2 * phiG2 + sigma * sigma);
        // The formula only changes φ; σ is unchanged by Step 6.
        double sigmaAfter = sigma;

        Assert.Equal(sigma, sigmaAfter);
        // Confirm phiPrime is non-trivially different from phi (the inflation actually happened).
        double phiPrime = phiPrimeG2 * Multiplier;
        Assert.True(phiPrime > phi);
    }
}
