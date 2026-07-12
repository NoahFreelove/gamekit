// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using GameKit.Core.Data;
using GameKit.Lobby.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace GameKit.Lobby.Data;

/// <summary>
/// Sibling-package <see cref="IModelBuilderExtension"/> that contributes the two Lobby
/// entities to the shared <c>GameKitDbContext</c> model at runtime. Registered via
/// <c>TryAddEnumerable</c> in <c>LobbyBuilderExtensions.AddLobby</c>.
/// </summary>
internal sealed class LobbyModelBuilderExtension : IModelBuilderExtension
{
    /// <inheritdoc />
    public void ApplyTo(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new LobbyConfiguration());
        modelBuilder.ApplyConfiguration(new LobbyMemberConfiguration());
    }
}
