// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Auth.Http.Contracts;

namespace GameKit.Auth.Http.Validators;

/// <summary>Validator for <see cref="LogoutRequest"/>. Presence + length ceiling only.</summary>
public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    /// <summary>Constructs the validator.</summary>
    public LogoutRequestValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
}
