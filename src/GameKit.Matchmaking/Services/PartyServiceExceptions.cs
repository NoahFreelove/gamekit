// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Raised by <see cref="IPartyService"/> when the requested operation conflicts with
/// existing state — most commonly when a player attempts to create or join a second
/// active party while already a member of one (CONTEXT D-02 single-active-party).
/// Middleware maps this to HTTP 409.
/// </summary>
public sealed class PartyConflictException : Exception
{
    /// <summary>Stable error code (e.g. <c>player_already_in_party</c>, <c>party_code_exhausted</c>).</summary>
    public string Code { get; }

    /// <summary>Constructs the exception.</summary>
    /// <param name="code">A stable machine-readable error code.</param>
    /// <param name="message">Human-readable message for logs.</param>
    public PartyConflictException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>
/// Raised by <see cref="IPartyService"/> when an operation targets a party whose state
/// does not permit it (e.g. joining a <c>Dissolved</c> party, dissolving a party already
/// <c>InMatch</c>). Middleware maps this to HTTP 400 / 409 depending on the case.
/// </summary>
public sealed class PartyInvalidStateException : Exception
{
    /// <summary>Stable error code (e.g. <c>party_not_open</c>, <c>party_already_dissolved</c>).</summary>
    public string Code { get; }

    /// <summary>Constructs the exception.</summary>
    /// <param name="code">A stable machine-readable error code.</param>
    /// <param name="message">Human-readable message for logs.</param>
    public PartyInvalidStateException(string code, string message) : base(message)
    {
        Code = code;
    }
}

/// <summary>
/// Raised by <see cref="IPartyService.DissolveAsync"/> when the actor is not the party
/// owner. Middleware maps this to HTTP 403. Mirrors <c>GameKit.Auth.Services.UnauthorizedException</c>
/// in shape but lives in the Matchmaking namespace to avoid an Auth runtime dependency.
/// </summary>
public sealed class PartyAuthorizationException : Exception
{
    /// <summary>Stable error code (e.g. <c>not_party_owner</c>).</summary>
    public string Code { get; }

    /// <summary>Constructs the exception.</summary>
    /// <param name="code">A stable machine-readable error code.</param>
    /// <param name="message">Human-readable message for logs.</param>
    public PartyAuthorizationException(string code, string message) : base(message)
    {
        Code = code;
    }
}
