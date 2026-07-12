// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using FluentValidation;
using GameKit.Admin.UI.Http.Contracts;

namespace GameKit.Admin.UI.Http.Validators;

/// <summary>
/// Validator for <see cref="MergePlayersRequest"/>. Enforces: both GUIDs are non-empty,
/// and source != target (merging a player into themselves is meaningless). These checks
/// short-circuit before the merge SERIALIZABLE transaction opens (T-10-04-05).
/// </summary>
public sealed class MergePlayersRequestValidator : AbstractValidator<MergePlayersRequest>
{
    /// <summary>Constructs the validator.</summary>
    public MergePlayersRequestValidator()
    {
        RuleFor(x => x.SourcePlayerId)
            .NotEmpty().WithMessage("SourcePlayerId is required.");

        RuleFor(x => x.TargetPlayerId)
            .NotEmpty().WithMessage("TargetPlayerId is required.");

        RuleFor(x => x)
            .Must(r => r.SourcePlayerId != r.TargetPlayerId)
            .WithMessage("Source and target player must be different.");
    }
}
