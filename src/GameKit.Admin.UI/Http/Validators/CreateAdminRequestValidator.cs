// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System.Text.RegularExpressions;
using FluentValidation;
using GameKit.Admin.UI.Authorization;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Http.Validators;

/// <summary>
/// Validator for <see cref="CreateAdminRequest"/>. Username must match
/// <c>^[a-z0-9_-]{3,32}$</c> (D-06: lowercase + digits + underscore/hyphen, 3-32 chars);
/// password min-length is 8; role must be one of <see cref="AdminRoles.Admin"/> or
/// <see cref="AdminRoles.Superadmin"/>. Short-circuits BEFORE the BCrypt hash cost is paid.
/// </summary>
public sealed class CreateAdminRequestValidator : AbstractValidator<CreateAdminRequest>
{
    private static readonly Regex UsernameRegex = new(
        "^[a-z0-9_-]{3,32}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Constructs the validator.</summary>
    public CreateAdminRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty()
            .Must(u => u is not null && UsernameRegex.IsMatch(u))
            .WithMessage("Username must be 3-32 chars, lowercase letters, digits, underscore, or hyphen.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(256);

        RuleFor(x => x.Role)
            .Must(r => r == AdminRoles.Admin || r == AdminRoles.Superadmin)
            .WithMessage($"Role must be '{AdminRoles.Admin}' or '{AdminRoles.Superadmin}'.");
    }
}
