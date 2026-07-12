// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Auth.Http.Contracts;

namespace GameKit.Auth.Http.Validators;

/// <summary>
/// Validator for <see cref="LinkRequest"/>. <see cref="LinkRequest.ExternalId"/> is optional
/// (Steam may verify from query); when present we enforce a generous length cap.
/// </summary>
public sealed class LinkRequestValidator : AbstractValidator<LinkRequest>
{
    /// <summary>Constructs the validator.</summary>
    public LinkRequestValidator()
    {
        When(x => x.ExternalId is not null, () =>
        {
            RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(64);
        });
    }
}
