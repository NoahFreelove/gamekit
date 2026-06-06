// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Matchmaking.Http.Contracts;

namespace GameKit.Matchmaking.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="EnqueueRequest"/>. Enforces a non-empty
/// <c>LadderId</c>, bounds <c>PoolName</c> length, and requires <c>PartyId</c> (when present)
/// to be non-empty.
/// </summary>
public sealed class EnqueueRequestValidator : AbstractValidator<EnqueueRequest>
{
    /// <summary>Constructs the validator.</summary>
    public EnqueueRequestValidator()
    {
        RuleFor(x => x.LadderId)
            .NotEmpty().WithMessage("LadderId must be a non-empty Guid.");

        RuleFor(x => x.PoolName)
            .MaximumLength(64).WithMessage("PoolName must be at most 64 characters.")
            .NotEmpty().When(x => x.PoolName is not null)
            .WithMessage("PoolName must not be empty when supplied; omit the field to route to the default pool.")
            .Matches(@"^[a-zA-Z0-9\-]+$").When(x => !string.IsNullOrEmpty(x.PoolName))
            .WithMessage("PoolName may only contain alphanumeric characters and hyphens (security: used as Redis key component).");

        RuleFor(x => x.RegionName)
            .MaximumLength(64).WithMessage("RegionName must be at most 64 characters.")
            .Matches(@"^[a-zA-Z0-9\-]+$").When(x => x.RegionName is not null)
            .WithMessage("RegionName may only contain alphanumeric characters and hyphens (security: used as Redis key component).");

        // Optional but if provided must be non-empty.
        When(x => x.PartyId.HasValue, () =>
        {
            RuleFor(x => x.PartyId!.Value)
                .NotEmpty().WithMessage("PartyId must be a non-empty Guid when supplied.");
        });
    }
}
