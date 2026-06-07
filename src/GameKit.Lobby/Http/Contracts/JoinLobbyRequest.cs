// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using FluentValidation;

namespace GameKit.Lobby.Http.Contracts;

/// <summary>
/// Request body for joining a lobby. The lobby id is taken from the route parameter
/// rather than the body; this record is retained as a placeholder for future join-options
/// (e.g. password).
/// </summary>
/// <param name="LobbyId">The lobby to join.</param>
public sealed record JoinLobbyRequest(Guid LobbyId);

/// <summary>FluentValidation validator for <see cref="JoinLobbyRequest"/>.</summary>
public sealed class JoinLobbyRequestValidator : AbstractValidator<JoinLobbyRequest>
{
    /// <summary>Initializes validation rules.</summary>
    public JoinLobbyRequestValidator()
    {
        RuleFor(r => r.LobbyId)
            .NotEmpty()
            .WithMessage("LobbyId must not be empty.");
    }
}
