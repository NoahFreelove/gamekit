// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using FluentValidation;
using GameKit.Rankings.Http.Contracts;
using Microsoft.Extensions.Options;

namespace GameKit.Rankings.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="RankAdjustRequest"/> (D-19).
/// Resolved from DI by <c>ValidationEndpointFilter&lt;RankAdjustRequest&gt;</c> on the
/// <c>POST /admin/api/players/{id}/rank-adjust</c> endpoint.
/// </summary>
public sealed class RankAdjustRequestValidator : AbstractValidator<RankAdjustRequest>
{
    /// <summary>Constructs the validator with rules read from <see cref="GameKitRankingsOptions"/>.</summary>
    /// <param name="opts">Rankings options supplying MinRating / MaxRating bounds.</param>
    public RankAdjustRequestValidator(IOptions<GameKitRankingsOptions> opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        var minRating = opts.Value.RankAdjust.MinRating;
        var maxRating = opts.Value.RankAdjust.MaxRating;

        RuleFor(x => x.LadderId)
            .NotEqual(Guid.Empty).WithMessage("LadderId must not be empty.");

        RuleFor(x => x.NewRating)
            .Must(r => !double.IsNaN(r) && !double.IsInfinity(r))
                .WithMessage("NewRating must be a finite number.")
            .InclusiveBetween(minRating, maxRating)
                .WithMessage($"NewRating must be between {minRating} and {maxRating}.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Reason must not be empty.")
            .MinimumLength(3).WithMessage("Reason must be at least 3 characters.")
            .MaximumLength(512).WithMessage("Reason must be at most 512 characters.");
    }
}
