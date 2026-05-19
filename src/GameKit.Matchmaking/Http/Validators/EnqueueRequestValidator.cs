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
            .MaximumLength(64).WithMessage("PoolName must be at most 64 characters.");

        // Optional but if provided must be non-empty.
        When(x => x.PartyId.HasValue, () =>
        {
            RuleFor(x => x.PartyId!.Value)
                .NotEmpty().WithMessage("PartyId must be a non-empty Guid when supplied.");
        });
    }
}
