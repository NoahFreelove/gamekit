// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Text.RegularExpressions;
using FluentValidation;
using GameKit.Auth.Http.Contracts;

namespace GameKit.Auth.Http.Validators;

/// <summary>
/// Validator for <see cref="RegisterRequest"/>. Enforces <see cref="PasswordOptions.UsernameRegex"/>
/// and <see cref="PasswordOptions.MinPasswordLength"/> so malformed requests short-circuit BEFORE
/// the BCrypt hash cost is paid (T-02-27 mitigation).
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    /// <summary>Constructs the validator. The regex is compiled once per instance.</summary>
    /// <param name="opts">Resolved <see cref="GameKitAuthOptions"/> (singleton) supplying the password policy.</param>
    public RegisterRequestValidator(GameKitAuthOptions opts)
    {
        var usernameRegex = new Regex(opts.Password.UsernameRegex, RegexOptions.Compiled);

        RuleFor(x => x.Username)
            .NotEmpty()
            .MaximumLength(256)
            .Must(u => u is not null && usernameRegex.IsMatch(u))
            .WithMessage($"Username must match {opts.Password.UsernameRegex}.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(opts.Password.MinPasswordLength)
            .MaximumLength(256);

        When(x => x.DisplayName is not null, () =>
        {
            RuleFor(x => x.DisplayName).MaximumLength(64);
        });
    }
}
