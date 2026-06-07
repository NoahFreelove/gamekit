// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using FluentValidation;

namespace GameKit.Lobby.Http.Contracts;

/// <summary>
/// Request body for <c>POST /api/lobbies</c>. The requesting player's id is sourced from
/// the JWT <c>NameIdentifier</c> / <c>sub</c> claim — it is not part of the request body.
/// </summary>
/// <param name="MaxMembers">Optional member cap override. Must be between 1 and 100 when provided.</param>
/// <param name="LadderId">Optional ladder to associate with the lobby.</param>
/// <param name="RegionName">Optional pool-affinity region name for matchmaking.</param>
public sealed record CreateLobbyRequest(
    int? MaxMembers = null,
    Guid? LadderId = null,
    string? RegionName = null);

/// <summary>FluentValidation validator for <see cref="CreateLobbyRequest"/>.</summary>
public sealed class CreateLobbyRequestValidator : AbstractValidator<CreateLobbyRequest>
{
    /// <summary>Initializes validation rules.</summary>
    public CreateLobbyRequestValidator()
    {
        When(r => r.MaxMembers.HasValue, () =>
        {
            RuleFor(r => r.MaxMembers!.Value)
                .InclusiveBetween(1, 100)
                .WithMessage("MaxMembers must be between 1 and 100.");
        });

        When(r => r.RegionName is not null, () =>
        {
            RuleFor(r => r.RegionName!)
                .MaximumLength(64)
                .WithMessage("RegionName must not exceed 64 characters.");
        });
    }
}
