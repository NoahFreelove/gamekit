// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Rankings.Http.Contracts;

namespace GameKit.Rankings.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="EndSeasonRequest"/> (D-11).
/// Resolved from DI by <c>ValidationEndpointFilter&lt;EndSeasonRequest&gt;</c> on the
/// <c>POST /admin/api/ladders/{id}/end-season</c> endpoint.
/// </summary>
public sealed class EndSeasonRequestValidator : AbstractValidator<EndSeasonRequest>
{
    /// <summary>Constructs the validator with all rules wired.</summary>
    public EndSeasonRequestValidator()
    {
        RuleFor(x => x.ConfirmLadderName)
            .NotEmpty().WithMessage("ConfirmLadderName must not be empty.")
            .MaximumLength(256).WithMessage("ConfirmLadderName must be at most 256 characters.");
    }
}
