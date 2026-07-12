// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Matchmaking.Http.Contracts;

namespace GameKit.Matchmaking.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="JoinPartyRequest"/>. Enforces the Crockford
/// base32 alphabet (no <c>I/L/O/U/0/1</c>) and the 6–8 character length window the party
/// code generator (Plan 05-04) emits. Case-insensitive match — the citext SQL column does
/// the case-folding.
/// </summary>
public sealed class JoinPartyRequestValidator : AbstractValidator<JoinPartyRequest>
{
    /// <summary>Constructs the validator.</summary>
    public JoinPartyRequestValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Code must not be empty.")
            .Length(6, 8).WithMessage("Code must be 6 to 8 characters.")
            // Crockford base32 alphabet (uppercase or lowercase). Exclude I, L, O, U, 0, 1.
            .Matches("^[A-HJKMNP-TV-Za-hjkmnp-tv-z2-9]+$")
            .WithMessage("Code must contain only Crockford base32 characters (no I/L/O/U/0/1).");
    }
}
