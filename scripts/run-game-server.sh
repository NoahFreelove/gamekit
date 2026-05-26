#!/usr/bin/env bash
# Plan 06-08 Task 1 (D-13): launch the TicTacToeDuel.GameServer console process.
#
# Topology reminder:
#   * samples/TicTacToeDuel/             — WEB tier (gamekit_owner; ./scripts/run-sample.sh)
#   * samples/TicTacToeDuel.GameServer/  — this game-server tier (gamekit_reader)
#
# The two processes run side-by-side: the web tier owns the database + HTTP API; this
# game-server reads via Npgsql + calls /api/sessions/{id}/{start,complete,abandon} over HTTP.
#
# Assumes docker-compose Postgres + Redis are already up (typically launched by
# scripts/run-sample.sh in another terminal).

set -euo pipefail

cd "$(dirname "$0")/.."

# Default to Development environment so appsettings.Development.json layers on top.
export DOTNET_ENVIRONMENT="${DOTNET_ENVIRONMENT:-Development}"

echo
echo "[game-server] starting TicTacToeDuel.GameServer (DOTNET_ENVIRONMENT=$DOTNET_ENVIRONMENT)"
echo "[game-server] connection string uses gamekit_reader (read-only — DIST-02 asserts INSERT denied)"
echo

exec dotnet run --project samples/TicTacToeDuel.GameServer
