// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

namespace GameKit.Matchmaking.Services;

/// <summary>
/// Generates short, human-friendly party codes for the join-by-code flow (CONTEXT D-02).
/// Default implementation: <see cref="PartyCodeGenerator"/> — Crockford base32 alphabet
/// (no <c>I/L/O/0/1</c>), 6 chars by default, CSPRNG-sourced.
/// </summary>
/// <remarks>
/// Codes are case-insensitive in storage (Postgres <c>citext</c> column on
/// <c>parties.party_code</c>), so the generator does not need to bias toward a single
/// case. The default implementation emits uppercase for readability.
/// </remarks>
public interface IPartyCodeGenerator
{
    /// <summary>
    /// Generate a single random code.
    /// </summary>
    /// <param name="length">Code length. Default 6; CONTEXT D-02 permits 6–8.</param>
    /// <returns>A freshly generated party code.</returns>
    string GenerateCode(int length = 6);
}
