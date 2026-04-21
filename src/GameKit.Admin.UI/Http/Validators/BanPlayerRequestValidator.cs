// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Http.Validators;

/// <summary>
/// Validator for <see cref="BanPlayerRequest"/>. Enforces D-09: ban reason is mandatory, at
/// least 3 characters, and at most 512 characters. The literal error messages are load-bearing
/// (ROADMAP success-criteria anchors rely on them as integration-test assertion strings).
/// </summary>
public sealed class BanPlayerRequestValidator : AbstractValidator<BanPlayerRequest>
{
    /// <summary>Constructs the validator.</summary>
    public BanPlayerRequestValidator()
    {
        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("A reason is required.")
            .MinimumLength(3).WithMessage("Reason must be at least 3 characters.")
            .MaximumLength(512).WithMessage("Reason is too long (max 512 characters).");
    }
}
