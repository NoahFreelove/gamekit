// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace GameKit.Lobby;

/// <summary>
/// Fail-fast validator for <see cref="GameKitLobbyOptions"/>. Throws
/// <see cref="OptionsValidationException"/> at host startup when any required invariant is
/// violated.
/// </summary>
public sealed class LobbyOptionsValidator : IValidateOptions<GameKitLobbyOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, GameKitLobbyOptions options)
    {
        var problems = new List<string>();

        if (options.DefaultMaxMembers <= 0)
            problems.Add($"{nameof(GameKitLobbyOptions.DefaultMaxMembers)} must be > 0 (got {options.DefaultMaxMembers}).");

        if (options.MaxChatMessageLength <= 0)
            problems.Add($"{nameof(GameKitLobbyOptions.MaxChatMessageLength)} must be > 0 (got {options.MaxChatMessageLength}).");

        return problems.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(problems);
    }
}
