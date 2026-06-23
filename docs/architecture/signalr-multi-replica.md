<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# SignalR multi-replica deployment

This document describes how the `LobbyHub` and `AdminEventHub` SignalR hubs
behave in a multi-replica deployment, what the Redis backplane does and does not
handle, and what operators must configure on their load balancer to ensure correct
behaviour.

---

## How the backplane works

Both `LobbyHub` and `AdminEventHub` are registered under a single
`AddSignalR().AddStackExchangeRedis(...)` call with the channel prefix `"GameKit"`.
The shared Redis backplane ensures that a `IHubContext<T>.Clients.*` broadcast on
**any** replica is published to Redis and forwarded to all connected clients on
**every** other replica.

### Admin-specific relay path

`AdminEventHub` is receive-only — clients cannot invoke methods on it. Cross-replica
admin event delivery flows through a second path:

1. Any publisher calls `IConnectionMultiplexer.GetSubscriber().PublishAsync(RedisChannel.Literal("gamekit:admin:events"), payload)`.
2. The `AdminLiveBroadcastService` (a `BackgroundService`) on **every** replica
   subscribes to `gamekit:admin:events` and relays each message to that replica's
   locally-connected admin clients via `IHubContext<AdminEventHub>.Clients.All.SendAsync("ReceiveAdminEvent", payload)`.
3. The SignalR backplane then fans the relay call out to any admin clients that may
   be connected to sibling replicas of that relay-calling replica.

In practice, because every replica runs its own `AdminLiveBroadcastService`, a
single Redis Pub/Sub publish reaches all admin clients on all replicas.

---

## Sticky sessions (session affinity) — required

The Redis backplane handles **outbound fan-out** only: it routes hub context
broadcasts from a hub instance to connected clients across all replicas.

The backplane does **not** route an incoming WebSocket frame (a client hub method
invocation) to an arbitrary replica. When a client calls `connA.InvokeAsync(...)`,
that frame is sent over the existing WebSocket connection — which is tied to a
specific replica's Kestrel process. If the load balancer routes the client's TCP
connection to a **different** replica on reconnect, the client loses its hub
connection and must re-negotiate.

**Operators MUST configure sticky sessions (session affinity) on the load balancer
for the Lobby and Admin WebSocket endpoints.** The backplane makes broadcast
fan-out correct across replicas; sticky sessions make hub invocations reachable.

### Recommended LB configurations

| LB / Ingress | Configuration |
|---|---|
| nginx | `upstream { ip_hash; ... }` or `sticky cookie` module |
| HAProxy | `balance source` or `cookie` persistence |
| Kubernetes nginx-ingress | `nginx.ingress.kubernetes.io/affinity: "cookie"` + `nginx.ingress.kubernetes.io/session-cookie-name: "GKLBSESSION"` |
| AWS ALB | Sticky sessions (target group `LbCookieStickinessPolicy`) |
| Traefik | `loadBalancer.sticky.cookie.name: "gklbsession"` |

Apply affinity to the WebSocket upgrade path. For Kubernetes ingress:

```yaml
metadata:
  annotations:
    nginx.ingress.kubernetes.io/affinity: "cookie"
    nginx.ingress.kubernetes.io/session-cookie-name: "GKLBSESSION"
    nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "3600"
```

The `LobbyHub` is mounted at `/hubs/lobby`; the `AdminEventHub` is mounted at
`{MountPath}/hubs/events` (default `/admin/hubs/events`).

---

## Reconnect behaviour and message loss

`StackExchange.Redis` reconnects automatically after a Redis outage. Both the
SignalR backplane subscription and the `AdminLiveBroadcastService` relay subscription
are restored on reconnect — no manual re-registration is required.

However, **messages published during the outage window are not buffered**. At-most-once
delivery applies for the brief outage window between disconnection and reconnect. This
is the standard Pub/Sub delivery guarantee for Redis.

**Sticky sessions scope the loss.** Clients connected to the same replica as the
affected hub instance are likely to see the gap; clients on unaffected replicas
continue receiving events normally.

Operators should:

1. Configure Redis with AOF persistence (see `docs/ops/redis-aof.md`) and an HA
   setup (Redis Sentinel or Cluster) to minimise outage windows.
2. Implement client-side reconnect with exponential back-off so clients that drop
   their WebSocket connection due to a replica restart automatically re-establish.
3. Document to end-users that real-time events (lobby chat, admin notifications)
   are best-effort during a Redis or replica restart — events in flight are lost,
   but delivery resumes once connectivity is restored.

---

## Replica restart behaviour (rolling deploy)

When Replica A is stopped (SIGTERM) and a fresh Replica B starts:

1. Clients connected to the stopped replica lose their WebSocket connections. The
   SignalR client library retries automatically if `.WithAutomaticReconnect()` is
   configured.
2. The Redis backplane subscription on the stopped replica is released during
   shutdown (StackExchange.Redis `ISubscriber.UnsubscribeAllAsync()`).
3. Clients that successfully reconnect to a running replica re-negotiate the hub
   connection and can resume publishing and receiving events.
4. **Clients on surviving replicas are unaffected.** Broadcasts from a freshly
   started replica are delivered to surviving-replica clients immediately via the
   shared Redis backplane.

The integration test `SignalRReplicaTests.HubEvents_AfterReplicaRestart_AreDeliveredToClientOnOtherReplica`
in `tests/GameKit.Lobby.Integration.Tests/SignalRReplicaTests.cs` verifies fact 4
using two in-process `TestServer` instances sharing one Testcontainers Redis node.

Similarly, `AdminSignalRReplicaTests.AdminEvents_AfterPublishingReplicaRestart_AreDeliveredToClientOnOtherReplica`
in `tests/GameKit.Admin.Integration.Tests/AdminSignalRReplicaTests.cs` verifies
that the `AdminLiveBroadcastService` relay on the surviving replica continues
delivering cross-replica admin events after the publishing replica restarts.

---

## Summary

| Concern | Handled by | Operator responsibility |
|---|---|---|
| Broadcast fan-out across replicas | Redis backplane (`ChannelPrefix "GameKit"`) | None — wired automatically |
| Admin event cross-replica delivery | `AdminLiveBroadcastService` + Redis backplane | None — wired automatically |
| Hub method invocation routing | WebSocket connection (tied to one replica) | **Configure sticky sessions on LB** |
| Redis outage message loss | At-most-once — not buffered | Redis HA + client reconnect |
| Replica restart client reconnect | SignalR client auto-reconnect | Configure `.WithAutomaticReconnect()` in game client |
