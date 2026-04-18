// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Auth.Http.Contracts;

namespace GameKit.Auth.Http.Validators;

/// <summary>Validator for <see cref="RefreshRequest"/>. Presence + length ceiling only.</summary>
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    /// <summary>Constructs the validator.</summary>
    public RefreshRequestValidator() =>
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
}
