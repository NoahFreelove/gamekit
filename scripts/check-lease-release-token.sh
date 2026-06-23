#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 GameKit contributors

# SCALE-02 static gate: assert that no .cs file under src/ contains a finally-path
# lease release that passes the stopping token (ct). The stopping token is already
# cancelled when the finally block runs on SIGTERM, causing StackExchange.Redis to
# cancel the release command before it is sent and leaving the lock held until TTL.
#
# All finally-path ReleaseLeaseAsync calls must pass CancellationToken.None instead.
#
# Usage: bash scripts/check-lease-release-token.sh
# Exit 0: SCALE-02 OK — no stopping-token lease release found.
# Exit 1: SCALE-02 violation — one or more stopping-token lease releases found.

set -euo pipefail

# Collect violations: .cs files under src/ containing the stopping-token release,
# excluding comment lines (lines starting with optional whitespace + //) so the grep
# string inside this script body does not self-invalidate the gate.
# Use a temp variable so set -e does not abort on grep exit 1 (no matches).
violations=$(grep -rn --include='*.cs' 'ReleaseLeaseAsync(ct)' src/ 2>/dev/null \
    | grep -vE '^\s*//' || true)
count=$(printf '%s' "$violations" | grep -c . || true)

if [ "$count" -ne 0 ]; then
    echo "SCALE-02 VIOLATION: stopping-token lease release found in src/:"
    printf '%s\n' "$violations"
    echo ""
    echo "Fix: change ReleaseLeaseAsync(ct) to ReleaseLeaseAsync(CancellationToken.None)"
    echo "     in every finally block so the release survives SIGTERM (SCALE-02)."
    exit 1
fi

echo "SCALE-02 OK: no stopping-token lease release in src/"
