// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Lobby.Services;

/// <summary>Thrown when a referenced lobby does not exist in the database.</summary>
public sealed class LobbyNotFoundException : Exception
{
    /// <summary>The lobby id that was not found.</summary>
    public Guid LobbyId { get; }

    /// <summary>Constructs the exception.</summary>
    public LobbyNotFoundException(Guid lobbyId)
        : base($"Lobby {lobbyId} was not found.")
    {
        LobbyId = lobbyId;
    }
}

/// <summary>
/// Thrown when a player attempts to join a lobby that has reached its <c>MaxMembers</c> cap.
/// </summary>
public sealed class LobbyFullException : Exception
{
    /// <summary>The lobby id that is full.</summary>
    public Guid LobbyId { get; }

    /// <summary>The member cap of the lobby.</summary>
    public int MaxMembers { get; }

    /// <summary>Constructs the exception.</summary>
    public LobbyFullException(Guid lobbyId, int maxMembers)
        : base($"Lobby {lobbyId} has reached its maximum member count of {maxMembers}.")
    {
        LobbyId = lobbyId;
        MaxMembers = maxMembers;
    }
}

/// <summary>
/// Thrown when a player attempts to join a lobby they are already a member of.
/// </summary>
public sealed class AlreadyMemberException : Exception
{
    /// <summary>The lobby id.</summary>
    public Guid LobbyId { get; }

    /// <summary>The player id that is already a member.</summary>
    public Guid PlayerId { get; }

    /// <summary>Constructs the exception.</summary>
    public AlreadyMemberException(Guid lobbyId, Guid playerId)
        : base($"Player {playerId} is already a member of lobby {lobbyId}.")
    {
        LobbyId = lobbyId;
        PlayerId = playerId;
    }
}

/// <summary>
/// Thrown when a hub or service operation requires lobby membership and the player is not a member.
/// </summary>
public sealed class NotAMemberException : Exception
{
    /// <summary>The lobby id.</summary>
    public Guid LobbyId { get; }

    /// <summary>The player id that is not a member.</summary>
    public Guid PlayerId { get; }

    /// <summary>Constructs the exception.</summary>
    public NotAMemberException(Guid lobbyId, Guid playerId)
        : base($"Player {playerId} is not a member of lobby {lobbyId}.")
    {
        LobbyId = lobbyId;
        PlayerId = playerId;
    }
}

/// <summary>
/// Thrown when the lobby is not configured correctly to enter matchmaking
/// (e.g. no <c>LadderId</c> is set) or when the matchmaking service rejects the submission.
/// </summary>
public sealed class LobbyMatchmakingException : Exception
{
    /// <summary>The lobby id involved in the failed matchmaking attempt.</summary>
    public Guid LobbyId { get; }

    /// <summary>Constructs the exception.</summary>
    public LobbyMatchmakingException(Guid lobbyId, string reason)
        : base($"Lobby {lobbyId} cannot enter matchmaking: {reason}")
    {
        LobbyId = lobbyId;
    }
}

/// <summary>
/// Thrown when a player attempts an operation they are not authorized to perform
/// (e.g. removing a member without being the owner or the target).
/// </summary>
public sealed class LobbyAuthorizationException : Exception
{
    /// <summary>The lobby id.</summary>
    public Guid LobbyId { get; }

    /// <summary>The actor player id that was denied.</summary>
    public Guid ActorId { get; }

    /// <summary>Constructs the exception.</summary>
    public LobbyAuthorizationException(Guid lobbyId, Guid actorId)
        : base($"Player {actorId} is not authorized to perform this operation on lobby {lobbyId}.")
    {
        LobbyId = lobbyId;
        ActorId = actorId;
    }
}
