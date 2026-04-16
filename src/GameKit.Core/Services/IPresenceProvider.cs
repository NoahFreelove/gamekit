// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameKit.Core.Services;

/// <summary>Player presence states reported by <see cref="IPresenceProvider"/>.</summary>
public enum PresenceStatus
{
    /// <summary>Player is not currently active.</summary>
    Offline = 0,

    /// <summary>Player is active (heartbeat within TTL).</summary>
    Online = 1,

    /// <summary>Player is currently in a game session (set by game-server <c>POST /api/sessions/{id}/start</c>).</summary>
    InMatch = 2,
}

/// <summary>
/// Optional presence provider. Implemented by <c>GameKit.Presence</c> (Phase 6) using Redis TTL-keyed heartbeats.
/// Core defines the interface so <c>GameKit.Admin.UI</c> (Phase 3) can light up presence panels when the sibling
/// package is installed and degrade gracefully when it is absent.
/// </summary>
public interface IPresenceProvider
{
    /// <summary>Returns the current presence status for the given player.</summary>
    ValueTask<PresenceStatus> GetStatusAsync(Guid playerId, CancellationToken cancellationToken = default);

    /// <summary>Returns up to <paramref name="take"/> ids of players currently <see cref="PresenceStatus.Online"/>.</summary>
    ValueTask<IReadOnlyList<Guid>> GetOnlinePlayerIdsAsync(int take, CancellationToken cancellationToken = default);
}
