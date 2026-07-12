// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;

namespace GameKit.Presence.Services;

/// <summary>
/// Adapter that bridges <see cref="ISessionLifecycleObserver"/> (Core port) into
/// <see cref="IPresenceWriter"/> (Presence-internal write port). Registered via
/// <c>TryAddEnumerable</c> in <c>AddPresence(...)</c> so it coexists with any other
/// <see cref="ISessionLifecycleObserver"/> implementations a sibling package may add
/// (CONTEXT D-21).
/// </summary>
/// <remarks>
/// <para>
/// LIFETIME NOTE (intentional, NOT a bug): this observer is registered <c>Scoped</c>
/// (it participates in the per-request ambient transaction owned by the session
/// services) while <see cref="IPresenceWriter"/> is registered <c>Singleton</c>
/// (a Redis-multiplexer-backed instance shared across requests). A Scoped service
/// consuming a Singleton dependency is the canonical ASP.NET Core DI pattern —
/// the problematic captive-dependency direction is a Singleton holding a Scoped
/// reference (longer-lived holding shorter-lived). The reverse, used here, is safe.
/// </para>
/// <para>
/// Each method iterates the supplied participant list and fans the corresponding
/// write out to the Redis-backed provider. The observer is idempotent — replays
/// produce identical Redis state (last-write-wins per CONTEXT D-04).
/// </para>
/// </remarks>
internal sealed class PresenceSessionObserver : ISessionLifecycleObserver
{
    private readonly IPresenceWriter _writer;

    /// <summary>
    /// Constructs the observer.
    /// </summary>
    /// <param name="writer">The Presence write-side port (resolves to the Singleton
    /// <c>RedisPresenceProvider</c>).</param>
    public PresenceSessionObserver(IPresenceWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
    }

    /// <inheritdoc />
    public async Task OnSessionStartedAsync(
        Guid sessionId,
        IReadOnlyList<Guid> participants,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(participants);
        foreach (var p in participants)
        {
            await _writer.WriteInMatchAsync(p, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task OnSessionCompletedAsync(
        Guid sessionId,
        IReadOnlyList<Guid> participants,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(participants);
        foreach (var p in participants)
        {
            await _writer.WriteOnlineAsync(p, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task OnSessionAbandonedAsync(
        Guid sessionId,
        IReadOnlyList<Guid> participants,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(participants);
        foreach (var p in participants)
        {
            await _writer.ClearInMatchAsync(p, ct).ConfigureAwait(false);
        }
    }
}
