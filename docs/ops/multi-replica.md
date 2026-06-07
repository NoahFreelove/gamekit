<!-- SPDX-License-Identifier: GPL-3.0-or-later -->
<!-- Copyright (c) 2026 GameKit contributors -->

# Multi-replica deployment

This page covers what you must configure when running **more than one instance** of
your GameKit-consuming application behind a load balancer.

GameKit supports multi-replica deployments through three mandatory mechanisms:

| Mechanism | Why required |
|-----------|--------------|
| Shared Data Protection key ring | Admin cookies encrypted on replica A are rejected by replica B without a shared key. |
| SignalR Redis backplane | Blazor Server circuits (`/_blazor`) and `AdminEventHub` (`/admin/hubs/events`) events must be routable to the correct replica; the backplane relays them through shared Redis. |
| Sticky sessions | Strongly recommended even with the backplane because a Blazor circuit reconnecting to a different replica loses all component state. |

> **Zero-cloud constraint (CLAUDE.md):** GameKit uses the Redis backplane **only**.
> Azure SignalR Service is never used — it is a proprietary cloud dependency incompatible
> with GameKit's GPL license and self-hosted design. Do not configure
> `AddAzureSignalR()` in a GameKit-consuming application.

---

## Requirements summary

Before running multiple replicas you must have:

1. Redis accessible to all replicas (already required for matchmaking + presence).
2. A shared Data Protection key ring (see below).
3. Sticky sessions configured on your load balancer or ingress (see below).

The SignalR Redis backplane is **automatically provided** by `AddGameKitAdmin()` (via
`AdminBackplanePostConfigure`) and optionally by `AddLobby()` (via
`LobbyRedisBackplanePostConfigure`). You do not call `AddStackExchangeRedis` yourself —
GameKit registers it internally.

---

## 1. Data Protection key sharing

**This is critical.** ASP.NET Core Data Protection encrypts the `gk_admin_session`
cookie. Without a shared key ring every admin login token encrypted by replica A is
rejected with a 403 by replica B, making the admin console unusable under load balancing.

Data Protection keys must be persisted to a location reachable by all replicas
**before** the application starts. The three supported backends are:

### Option A — Redis (recommended when Redis is already in use)

```csharp
builder.Services
    .AddDataProtection()
    .PersistKeysToStackExchangeRedis(
        ConnectionMultiplexer.Connect(redisConnectionString),
        "gamekit:data-protection:keys")
    .SetApplicationName("gamekit");
```

The key `gamekit:data-protection:keys` is an arbitrary Redis key; all replicas must
use the same key name and `SetApplicationName` value. Redis is already a required
dependency for multi-replica GameKit deployments, so this option adds no new
infrastructure.

### Option B — Shared file system (NFS / mounted network volume)

```csharp
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/mnt/shared/dp-keys"))
    .SetApplicationName("gamekit");
```

Requires a network-accessible mount (NFS, Azure Files, GlusterFS, etc.). Ensure the
directory is writable by the application process and readable by all replicas.

### Option C — Entity Framework Core (Postgres, via `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`)

```csharp
builder.Services
    .AddDataProtection()
    .PersistKeysToDbContext<YourDbContext>()
    .SetApplicationName("gamekit");
```

Requires adding the `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` NuGet
package and implementing `IDataProtectionKeyContext` on your `DbContext`. Keys are
stored in a `DataProtectionKeys` table in Postgres — consistent with the
"Postgres + Redis on hardware you control" deployment model.

### Key lifetime and rotation

The default key lifetime is 90 days. ASP.NET Core auto-rotates keys; old keys are
retained (not deleted) so cookies issued before rotation remain valid for their
original expiry period. Never delete keys from the shared store while any of those
cookies may still be in use.

---

## 2. SignalR Redis backplane

GameKit registers the SignalR Redis backplane automatically. When you call
`AddGameKitAdmin()`, the `AdminBackplanePostConfigure` post-configurator wires the
existing `IConnectionMultiplexer` (your Redis connection) into the SignalR
`RedisOptions.ConnectionFactory`. No additional call to `AddStackExchangeRedis` is
needed in your application.

**Channel prefix:** GameKit uses the Redis channel prefix `"GameKit"` for all
SignalR backplane channels. This isolates GameKit's SignalR traffic from any other
use of Redis Pub/Sub in your application.

### What the backplane covers

| Component | Transport path | Backplane channel |
|-----------|---------------|------------------|
| Blazor Server circuit (`/_blazor`) | Long-poll / WebSocket | `GameKit:*` prefix |
| `AdminEventHub` (`/admin/hubs/events`) | WebSocket | `GameKit:*` prefix |
| `LobbyHub` (`/hubs/lobby`, when `AddLobby()` is called) | WebSocket | `GameKit:*` prefix |

### `AdminLiveBroadcastService` and the event channel

`AdminLiveBroadcastService` is a `BackgroundService` registered by `AddGameKitAdmin()`.
It subscribes to the Redis Pub/Sub channel `gamekit:admin:events` and relays each
message to all connected admin sessions via `IHubContext<AdminEventHub>`.

To broadcast an admin event from your application code, publish to the channel:

```csharp
await mux.GetSubscriber().PublishAsync(
    RedisChannel.Literal("gamekit:admin:events"),
    payload);
```

Every admin client connected to any replica will receive the `ReceiveAdminEvent`
SignalR message with the payload string.

### Single-instance deployments (no Redis)

`AdminLiveBroadcastService` short-circuits `ExecuteAsync` when `IConnectionMultiplexer`
is not registered — it returns immediately and does not throw. You can run a single-
instance GameKit deployment without Redis for the admin backplane (though Redis is still
required for matchmaking and presence if those modules are enabled).

---

## 3. Sticky sessions (strongly recommended)

Even with the Redis backplane in place, sticky sessions (also called session affinity)
are **strongly recommended** for multi-replica deployments.

**Why:** The backplane routes SignalR hub method calls and broadcast messages across
replicas, but it cannot preserve Blazor Server component state. When a Blazor circuit
reconnects to a different replica (e.g. after a replica restart or a non-sticky
load-balancer decision) all in-memory component state is lost and the admin page renders
from scratch. Sticky sessions prevent this by ensuring the same client always routes to
the same replica for the lifetime of the browser session.

### nginx configuration example

```nginx
upstream gamekit_backend {
    ip_hash;  # sticky — routes each client IP to the same upstream
    server 10.0.0.1:8080;
    server 10.0.0.2:8080;
}

server {
    listen 443 ssl;
    # ... TLS config ...

    location / {
        proxy_pass http://gamekit_backend;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
    }
}
```

For cookie-based affinity (more robust than IP hash under NAT / proxies):

```nginx
upstream gamekit_backend {
    server 10.0.0.1:8080;
    server 10.0.0.2:8080;
    sticky cookie gamekit_srv expires=1h path=/;
}
```

The `sticky` directive requires the `nginx-extras` package (Debian) or the
[nginx-upstream-fair](https://github.com/nicholasgasior/nginx_upstream_fair) module.

### Kubernetes / Ingress-NGINX

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  annotations:
    nginx.ingress.kubernetes.io/affinity: "cookie"
    nginx.ingress.kubernetes.io/affinity-mode: "balanced"
    nginx.ingress.kubernetes.io/session-cookie-name: "gamekit_route"
    nginx.ingress.kubernetes.io/session-cookie-expires: "3600"
    nginx.ingress.kubernetes.io/session-cookie-max-age: "3600"
spec:
  # ... your ingress rules ...
```

---

## 4. Checklist before going live with multiple replicas

- [ ] Shared Data Protection key ring configured (`PersistKeysToStackExchangeRedis` or
      `PersistKeysToDbContext` or `PersistKeysToFileSystem`).
- [ ] All replicas use the same `SetApplicationName("gamekit")` value.
- [ ] Redis is reachable from all replicas (same `RedisConnectionString` in
      `GameKitOptions`).
- [ ] Load balancer sticky sessions enabled (cookie affinity preferred over IP hash).
- [ ] `AddGameKitAdmin()` called — the backplane is registered automatically.
- [ ] Verified admin login works end-to-end on both replicas (cookie issued by A is
      honoured by B).

---

## See also

- [`container.md`](container.md) — Docker Compose scale-out recipe.
- [`bare-metal.md`](bare-metal.md) — systemd unit + nginx configuration for bare-metal hosts.
- [`redis-aof.md`](redis-aof.md) — Redis persistence requirements (AOF + noeviction).
