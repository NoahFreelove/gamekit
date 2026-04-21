// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Http.Validators;

/// <summary>
/// Validator for <see cref="LoginRequest"/>. Presence + length ceiling only — the
/// <see cref="Services.IAdminAuthService"/> owns the actual username/password verification
/// (BCrypt + dummy-hash timing parity per T-03-06-03). The length cap defends against
/// oversized-body DoS before the BCrypt hash cost is paid.
/// </summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    /// <summary>Constructs the validator.</summary>
    public LoginRequestValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}
