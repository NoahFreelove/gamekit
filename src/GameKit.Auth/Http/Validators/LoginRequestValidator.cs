// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Auth.Http.Contracts;

namespace GameKit.Auth.Http.Validators;

/// <summary>
/// Validator for <see cref="LoginRequest"/>. Performs presence checks only — the endpoint
/// enforces provider-specific shape (e.g. password provider requires username + password;
/// guest provider ignores both). Length ceilings defend against oversized-body DoS.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    /// <summary>Constructs the validator.</summary>
    public LoginRequestValidator()
    {
        When(x => x.Username is not null, () =>
        {
            RuleFor(x => x.Username).NotEmpty().MinimumLength(1).MaximumLength(256);
        });
        When(x => x.Password is not null, () =>
        {
            RuleFor(x => x.Password).NotEmpty().MinimumLength(1).MaximumLength(256);
        });
    }
}
