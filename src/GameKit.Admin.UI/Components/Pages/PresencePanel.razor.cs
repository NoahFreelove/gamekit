// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (c) 2026 GameKit contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Core.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace GameKit.Admin.UI.Components.Pages;

/// <summary>
/// Code-behind for <c>PresencePanel.razor</c>. Resolves <see cref="IPresenceProvider"/> at first
/// render; if absent (consumer omitted <c>GameKit.Presence</c>), the page short-circuits and
/// renders <c>MissingPackageAlert</c>. Otherwise starts a <see cref="Timer"/> that polls
/// <see cref="IPresenceProvider.GetOnlinePlayerIdsAsync"/> at
/// <c>GameKitAdminOptions.Panel.RefreshInterval</c> (default 10 s) and refreshes the rendered
/// table via <see cref="ComponentBase.StateHasChanged"/>.
/// </summary>
/// <remarks>
/// <para>Polling pattern: <see cref="System.Threading.Timer"/> + <see cref="CancellationTokenSource"/>
/// per UI-SPEC §10 interaction contract (Plan 03 D-10 — same pattern used by Phase 3 panels).
/// The timer is disposed in <see cref="Dispose"/>; the CTS is cancelled to prevent in-flight Redis
/// reads from outliving the component.</para>
/// <para>Display name resolution is intentionally deferred to v2 — Plan 06-07 ships with a
/// truncated player-id fallback so the panel satisfies PRES-06 even before a name resolver is
/// plumbed in. The fallback is documented in UI-SPEC §8 ("DisplayName placeholder for v1").</para>
/// </remarks>
public partial class PresencePanel : ComponentBase, IDisposable
{
    private IPresenceProvider? _presence;
    private List<PresenceRow>? _rows;
    private Timer? _timer;
    private CancellationTokenSource? _cts;
    private Exception? _lastError;
    private bool _refreshing;

    /// <summary>
    /// Resolves the optional <see cref="IPresenceProvider"/>. When the provider is absent the
    /// component renders <c>MissingPackageAlert</c> and returns without starting the polling
    /// timer (no point polling a missing service). When present, the first refresh is kicked
    /// off immediately and the polling timer is armed.
    /// </summary>
    protected override async Task OnInitializedAsync()
    {
        _presence = Sp.GetService<IPresenceProvider>();
        if (_presence is null)
        {
            // UI-SPEC §9 graceful-degrade path. No timer; component renders MissingPackageAlert.
            return;
        }

        // Initial fetch — populate _rows for the first render before arming the timer so the
        // operator does not stare at the "Loading presence…" state for the full 10 s interval.
        await RefreshAsync().ConfigureAwait(false);

        var interval = AdminOpts.Value.Panel.RefreshInterval;
        // dueTime=interval so the timer's first tick happens AFTER the initial fetch above.
        _timer = new Timer(_ => _ = InvokeAsync(RefreshAsync), state: null, dueTime: interval, period: interval);
    }

    /// <summary>
    /// Fetches the Top-25 online player ids and replaces <c>_rows</c>. Cancels any in-flight
    /// fetch via <see cref="CancellationTokenSource"/> so a manual Refresh click overrides a
    /// pending poll. Surfaces errors into <c>_lastError</c> which the Razor template renders as
    /// the error state alert.
    /// </summary>
    private async Task RefreshAsync()
    {
        if (_presence is null) return;

        // Cancel any in-flight fetch; create a fresh CTS for this refresh.
        var previous = _cts;
        _cts = new CancellationTokenSource();
        if (previous is not null)
        {
            try { previous.Cancel(); } catch { /* best-effort */ }
            previous.Dispose();
        }

        _refreshing = true;
        StateHasChanged();
        try
        {
            var ct = _cts.Token;
            var ids = await _presence.GetOnlinePlayerIdsAsync(25, ct).ConfigureAwait(false);
            // Plan-time decision (UI-SPEC §5): the panel surfaces "Online" for every Top-25 row.
            // Per-row status differentiation (Online vs InMatch) is a v2 enhancement — the
            // Offline transient state happens implicitly when GetOnlinePlayerIdsAsync filters a
            // player out on the next refresh tick.
            var now = DateTimeOffset.UtcNow;
            _rows = ids.Select(id => new PresenceRow(id, TruncatePlayerId(id), PresenceStatus.Online, now)).ToList();
            _lastError = null;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected when a manual Refresh supersedes a pending poll;
            // do not surface as an error.
        }
        catch (Exception ex)
        {
            _lastError = ex;
        }
        finally
        {
            _refreshing = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Renders a relative-time string per UI-SPEC §6 ladder
    /// (<c>just now</c> &lt; 5 s; <c>{n}s ago</c> &lt; 60 s; <c>{n}m ago</c> &lt; 60 min;
    /// <c>{n}h ago</c> &lt; 24 h; <c>{n}d ago</c> ≥ 24 h). Uses the invariant culture so the
    /// admin operator sees consistent rendering across locales.
    /// </summary>
    /// <param name="utcLastSeen">Timestamp the player was last seen (UTC).</param>
    /// <returns>Human-readable relative-time string.</returns>
    private static string RelativeTime(DateTimeOffset utcLastSeen)
    {
        var delta = DateTimeOffset.UtcNow - utcLastSeen;
        if (delta.TotalSeconds < 5) return "just now";
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s ago";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m ago";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h ago";
        return $"{(int)delta.TotalDays}d ago";
    }

    /// <summary>
    /// Truncates a player <see cref="Guid"/> to the first 8 hex characters followed by a
    /// horizontal ellipsis (<c>…</c>, U+2026). Matches the UI-SPEC §8 mono-cell layout shown
    /// in the example row (<c>a3f9c1d2…</c>).
    /// </summary>
    /// <param name="playerId">Player identifier.</param>
    /// <returns>Truncated display string (8 hex chars + ellipsis).</returns>
    private static string TruncatePlayerId(Guid playerId)
    {
        var hex = playerId.ToString("N");
        return string.Concat(hex.AsSpan(0, 8), "…");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        if (_cts is not null)
        {
            try { _cts.Cancel(); } catch { /* best-effort */ }
            _cts.Dispose();
            _cts = null;
        }
    }

    /// <summary>One row in the presence table — kept local because it has no consumers outside this page.</summary>
    /// <param name="PlayerId">Player identifier.</param>
    /// <param name="DisplayName">Display name (v1 ships the truncated player-id as the fallback).</param>
    /// <param name="Status">Resolved presence status.</param>
    /// <param name="LastSeen">Timestamp the player was last seen (UTC).</param>
    private sealed record PresenceRow(Guid PlayerId, string DisplayName, PresenceStatus Status, DateTimeOffset LastSeen);
}
