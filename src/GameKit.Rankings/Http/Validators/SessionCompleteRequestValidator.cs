// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Linq;
using FluentValidation;
using GameKit.Core.Entities;
using GameKit.Core.Http.Contracts;

namespace GameKit.Rankings.Http.Validators;

/// <summary>
/// FluentValidation validator for <see cref="SessionCompleteRequest"/> (D-09).
/// Resolved from DI by <c>ValidationEndpointFilter&lt;SessionCompleteRequest&gt;</c> on the
/// <c>POST /api/sessions/{id}/complete</c> endpoint.
/// </summary>
public sealed class SessionCompleteRequestValidator : AbstractValidator<SessionCompleteRequest>
{
    /// <summary>Constructs the validator with all rules wired.</summary>
    public SessionCompleteRequestValidator()
    {
        RuleFor(x => x.Participants)
            .NotEmpty().WithMessage("Participants list must not be empty.")
            .Must(p => p is { Count: >= 1 }).WithMessage("At least one participant is required.")
            .Must(p => p is { Count: <= 32 }).WithMessage("At most 32 participants are allowed.")
            // WR-01: reject duplicate PlayerIds. Without this guard, RunCompletionAsync would
            // ExecuteUpdateAsync twice against the same (SessionId, PlayerId) row and the
            // PendingRatingUpdatesAdapter would enqueue duplicate pending_rating_updates,
            // making the ticker count the player as having played the session twice.
            .Must(p => p is null || p.Select(x => x.PlayerId).Distinct().Count() == p.Count)
            .WithMessage("Each participant PlayerId must appear at most once.");

        RuleForEach(x => x.Participants).ChildRules(participant =>
        {
            participant.RuleFor(p => p.PlayerId)
                .NotEqual(Guid.Empty).WithMessage("PlayerId must not be empty.");

            participant.RuleFor(p => p.Team)
                .GreaterThanOrEqualTo(0).WithMessage("Team must be >= 0.");

            participant.RuleFor(p => p.Result)
                .IsInEnum().WithMessage($"Result must be one of: {string.Join(", ", Enum.GetNames<SessionResult>())}.");

            participant.When(p => p.Score.HasValue, () =>
            {
                participant.RuleFor(p => p.Score!.Value)
                    .GreaterThanOrEqualTo(0).WithMessage("Score must be >= 0 when provided.");
            });
        });
    }
}
