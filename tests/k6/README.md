# GameKit k6 Load Scenarios

This directory contains [k6](https://grafana.com/docs/k6/latest/) load-test scenario scripts for
GameKit performance validation (PERF-03, PERF-04). k6 is invoked exclusively as an external
Docker process — see [AGPLv3 licensing posture](#licensing-posture-agplv3) below.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Docker Invocation](#docker-invocation)
3. [Host Networking: Linux vs. macOS/Windows](#host-networking-linux-vs-macoswindows)
4. [Environment Variables](#environment-variables)
5. [Available Scripts](#available-scripts)
6. [Credential Hygiene](#credential-hygiene)
7. [Licensing Posture (AGPLv3)](#licensing-posture-agplv3)
8. [Running Against a Local Stack](#running-against-a-local-stack)

---

## Prerequisites

- **Docker** — `docker run --rm grafana/k6:latest version` should print `k6 v2.0.0` or later.
- A **running local GameKit host** — the Testcontainers-backed sample or `dotnet run` of a host
  that maps the required endpoints. **Never run these scenarios against production.**
- A **short-lived player JWT** minted against the LOCAL stack (see
  [Credential Hygiene](#credential-hygiene)).

---

## Docker Invocation

### Standard (stdin) invocation — Linux

```bash
docker run --rm -i \
  --network host \
  -e BASE_URL=http://localhost:5000 \
  -e WS_URL=ws://localhost:5000 \
  -e JWT=<player_jwt> \
  -e LADDER_ID=<ladder_guid> \
  -e LOBBY_ID=<lobby_guid> \
  grafana/k6:latest run - < tests/k6/matchmaking-burst.js
```

### Volume-mount invocation (required when the script imports local helpers)

Scripts that `import` local helper modules (e.g. `spike-signalr.js` imports
`./helpers/signalr.js`) cannot be piped via stdin because Docker cannot resolve relative
imports from the pipe. Mount the scripts directory instead:

```bash
docker run --rm \
  --network host \
  -v "$(pwd)/tests/k6:/scripts" \
  -e BASE_URL=http://localhost:5000 \
  -e WS_URL=ws://localhost:5000 \
  -e JWT=<player_jwt> \
  -e LOBBY_ID=<lobby_guid> \
  grafana/k6:latest run /scripts/spike-signalr.js
```

### macOS / Windows — use `host.docker.internal` instead of `--network host`

```bash
docker run --rm \
  -v "$(pwd)/tests/k6:/scripts" \
  -e BASE_URL=http://host.docker.internal:5000 \
  -e WS_URL=ws://host.docker.internal:5000 \
  -e JWT=<player_jwt> \
  -e LOBBY_ID=<lobby_guid> \
  grafana/k6:latest run /scripts/spike-signalr.js
```

---

## Host Networking: Linux vs. macOS/Windows

| Platform | Recommended flag | Target URL |
|----------|-----------------|------------|
| Linux | `--network host` | `http://localhost:<port>` |
| macOS (Docker Desktop) | _(omit `--network host`)_ | `http://host.docker.internal:<port>` |
| Windows (Docker Desktop) | _(omit `--network host`)_ | `http://host.docker.internal:<port>` |

**Why the difference:** On Linux, `--network host` maps the container directly onto the host
network stack, so `localhost` inside the container resolves to the host machine. On macOS and
Windows, Docker Desktop runs inside a Linux VM — `--network host` maps to the VM's loopback, not
the host's. Use `host.docker.internal` (a special DNS name that Docker Desktop resolves to the
host gateway) on non-Linux platforms.

CI always runs on `ubuntu-24.04` (Linux), so `--network host` is correct in CI.

---

## Environment Variables

All sensitive values are passed via `-e` at invocation time. **Nothing is hardcoded in the
committed scripts.**

| Variable | Required | Description |
|----------|----------|-------------|
| `BASE_URL` | Yes | HTTP base URL of the local GameKit host. E.g. `http://localhost:5000`. |
| `WS_URL` | Yes (SignalR scripts) | WebSocket base URL. E.g. `ws://localhost:5000`. |
| `JWT` | Yes | Short-lived player JWT minted against the LOCAL stack. |
| `LADDER_ID` | Matchmaking scripts | A valid ladder GUID seeded in the local test database. |
| `LOBBY_ID` | SignalR scripts | A valid lobby GUID seeded in the local test database. |

---

## Available Scripts

### `spike-signalr.js` — SignalR handshake GO/NO-GO spike (PERF-04a)

Proves that stock `grafana/k6` v2.0.0 can complete the full SignalR JSON protocol handshake
against the real Lobby hub at `/hubs/lobby`. This is the Open Q2 gate: run this spike before
committing the full fan-out scenario.

**Six-step sequence:**
1. HTTP POST to `/hubs/lobby/negotiate?negotiateVersion=1`
2. Open WebSocket to `/hubs/lobby?id=<connectionToken>`
3. Send `{"protocol":"json","version":1}\x1e` handshake frame
4. Assert `{}` handshake-ack is received
5. Invoke `JoinLobbyAsync(<LOBBY_ID>)`
6. Assert a response (result or SignalR error frame) arrives within 3 s

```bash
docker run --rm \
  --network host \
  -v "$(pwd)/tests/k6:/scripts" \
  -e BASE_URL=http://localhost:5000 \
  -e WS_URL=ws://localhost:5000 \
  -e JWT=<player_jwt> \
  -e LOBBY_ID=<lobby_guid> \
  grafana/k6:latest run /scripts/spike-signalr.js
```

**GO:** All checks pass — fan-out scenario proceeds with stock k6.
**NO-GO:** Checks fail — document the failure and escalate before writing the fan-out scenario.

### `matchmaking-burst.js` — 500 VU matchmaking burst + auth throughput (PERF-03)

_(Added in plan 19-03.)_

### `lobby-signalr-fanout.js` — Lobby SignalR fan-out delivery distribution (PERF-04b)

_(Added in plan 19-05, gated on the GO/NO-GO spike above.)_

---

## Credential Hygiene

- **JWTs used in k6 scripts MUST be short-lived tokens minted against the LOCAL Testcontainers
  stack — NEVER against production.**
- JWTs are passed via `-e JWT=<value>` at invocation time. They are **never** committed to the
  repository or hardcoded in any script.
- Use `grep -rniE "eyJ[A-Za-z0-9_-]{10}" tests/k6/` to verify no JWT has accidentally been
  committed. This check is also enforced in the plan verification gate.
- The local stack generates ephemeral RSA keypairs per test run; tokens issued against it are
  worthless outside that process.

---

## Licensing Posture (AGPLv3)

**k6 is AGPLv3-licensed.** This creates the following constraint:

- k6 is invoked **exclusively as an external Docker CLI process**. It is **never** referenced as
  a NuGet dependency, **never** linked into any build artifact, and **never** shipped inside any
  GameKit package.
- The `.js` scripts in this directory are GameKit-owned test scripts that merely invoke the
  external k6 binary. They are licensed under the same Apache-2.0 license as the rest of
  the GameKit repository.
- AGPLv3 copyleft applies to distributed software that links or incorporates the library. Test
  scripts that invoke the binary as a subprocess (via Docker) are not considered derivative works
  for the purposes of copyleft propagation — this preserves GameKit's GPL self-hosted posture.

**These scenarios are NEVER run in CI against production.** They target a local Testcontainers
stack only. The CI benchmark job (if added) runs only on `ubuntu-24.04` against an ephemeral
local Testcontainers host spun up in the same CI job.

---

## Running Against a Local Stack

### Option A: Sample app (`TicTacToeDuel`)

```bash
# Start the sample on a non-conflicting port (host Postgres on :5432, gamekit on :5433):
cd samples/TicTacToeDuel
ASPNETCORE_URLS=http://localhost:5000 dotnet run -c Release
```

Then mint a JWT via the `/api/auth/login` endpoint and seed a lobby via `/api/lobbies`.

### Option B: Testcontainers-backed integration test host

The existing `tests/GameKit.Lobby.Integration.Tests/LobbyTestApp.cs` provides a full in-process
test host, but it is not addressable from Docker networking. For k6 scenarios that need a
network-accessible host, use Option A or build a standalone host app that calls
`app.AddGameKit().AddAuth().AddLobby()` and `app.MapLobby()`.

---

## See Also

- `tests/k6/helpers/signalr.js` — Reusable SignalR negotiate + handshake + invoke helpers.
- `.planning/phases/19-load-performance-testing/19-RESEARCH.md` — Full phase research including
  SignalR protocol reference, pitfalls, and stack decisions.
