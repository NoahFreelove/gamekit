// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Http.Validators;

/// <summary>
/// Validator for <see cref="PlayerSearchRequest"/>. Presence + length ceiling on the query
/// (256 chars is ample for any UUID / provider:external_id / display-name prefix) plus the
/// page-size clamp to [1, 50] matching <see cref="Services.PlayerSearchService"/> defense-in-depth.
/// </summary>
public sealed class PlayerSearchRequestValidator : AbstractValidator<PlayerSearchRequest>
{
    /// <summary>Constructs the validator.</summary>
    public PlayerSearchRequestValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(256);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}
