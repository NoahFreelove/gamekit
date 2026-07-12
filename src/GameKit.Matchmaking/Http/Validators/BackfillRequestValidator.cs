// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Matchmaking.Http.Contracts;

namespace GameKit.Matchmaking.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="BackfillRequest"/>. Enforces non-empty
/// <c>LadderId</c> and <c>SessionId</c>, and bounds <c>RegionName</c> length and
/// character class (security: <c>RegionName</c> is used as a Redis sorted-set key component —
/// injection guard mirrors <see cref="EnqueueRequestValidator"/>).
/// </summary>
public sealed class BackfillRequestValidator : AbstractValidator<BackfillRequest>
{
    /// <summary>Constructs the validator.</summary>
    public BackfillRequestValidator()
    {
        RuleFor(x => x.LadderId)
            .NotEmpty().WithMessage("LadderId must be a non-empty Guid.");

        RuleFor(x => x.SessionId)
            .NotEmpty().WithMessage("SessionId must be a non-empty Guid.");

        RuleFor(x => x.RegionName)
            .MaximumLength(64).WithMessage("RegionName must be at most 64 characters.")
            .Matches(@"^[a-zA-Z0-9\-]+$").When(x => x.RegionName is not null)
            .WithMessage("RegionName may only contain alphanumeric characters and hyphens (security: used as Redis key component).");
    }
}
