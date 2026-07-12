// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 GameKit contributors

using System;
using System.Threading;
using System.Threading.Tasks;
using GameKit.Admin.UI.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace GameKit.Admin.UI.Services;

/// <summary>
/// Background relay service that subscribes to the Redis Pub/Sub channel
/// <c>gamekit:admin:events</c> and delivers each message to all connected admin sessions
/// via <see cref="IHubContext{AdminEventHub}"/> (ADMIN-13).
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="IConnectionMultiplexer"/> is <see langword="null"/> (single-instance
/// deployments without Redis), <see cref="ExecuteAsync"/> returns immediately — the service
/// is a no-op and does not throw (Pitfall 4 / T-12-04-SC). Registration in
/// <c>AddGameKitAdmin()</c> is therefore unconditional and safe.
/// </para>
/// <para>
/// Per-message relay errors are swallowed so that one malformed payload cannot kill the
/// service loop (T-12-04-DOS mitigation). The Redis channel name is the fixed literal
/// <c>gamekit:admin:events</c> — no user input enters the channel name (T-12-04-TAM
/// mitigation).
/// </para>
/// <para>
/// Future publishers to <c>gamekit:admin:events</c> must scope payloads to what the
/// admin role is authorised to see — only admins receive messages because
/// <see cref="AdminEventHub"/> is gated by the <c>GameKitAdmin</c> cookie scheme
/// (T-12-04-INF mitigation).
/// </para>
/// </remarks>
internal sealed class AdminLiveBroadcastService : BackgroundService
{
    private const string Channel = "gamekit:admin:events";
    private readonly IConnectionMultiplexer? _mux;
    private readonly IHubContext<AdminEventHub> _hub;

    /// <summary>
    /// Constructs the broadcast relay service.
    /// </summary>
    /// <param name="hub">Hub context for broadcasting to all connected admin sessions.</param>
    /// <param name="mux">
    /// Redis multiplexer. When <see langword="null"/> (single-instance deployment without Redis),
    /// <see cref="ExecuteAsync"/> short-circuits immediately — no subscription is created and no
    /// exception is thrown.
    /// </param>
    public AdminLiveBroadcastService(IHubContext<AdminEventHub> hub,
        IConnectionMultiplexer? mux = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
        _mux = mux;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Pitfall 4 short-circuit: single-instance installs without Redis start cleanly.
        if (_mux is null) return;

        var sub = _mux.GetSubscriber();
        var queue = await sub.SubscribeAsync(RedisChannel.Literal(Channel))
            .ConfigureAwait(false);

        try
        {
            await foreach (var message in queue.WithCancellation(stoppingToken))
            {
                try
                {
                    await _hub.Clients.All
                        .SendAsync("ReceiveAdminEvent", message.Message.ToString(), stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch
                {
                    // Swallow — individual relay failure must not kill the service (T-12-04-DOS).
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            // Unsubscribe asynchronously after the foreach exits (WR-01 fix):
            // avoids a blocking network call on the shutdown thread and eliminates
            // the discarded CT.Register handle (IN-01 fix).
            await queue.UnsubscribeAsync().ConfigureAwait(false);
        }
    }
}
