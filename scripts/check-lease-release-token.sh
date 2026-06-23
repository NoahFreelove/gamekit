#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (c) 2026 GameKit contributors

# SCALE-02 static gate: assert that no .cs file under src/ contains a finally-path
# lease release that passes any token OTHER THAN CancellationToken.None.
#
# The stopping token is already cancelled when the finally block runs on SIGTERM,
# causing StackExchange.Redis to cancel the release command before it is sent and
# leaving the lock held until TTL.
#
# Strategy: negative-match — find every ReleaseLeaseAsync( call, strip the ones
# that CORRECTLY pass CancellationToken.None, and report the rest as violations.
# This catches any token variable name (ct, stoppingToken, token, etc.) rather
# than only the literal "ct" that the original gate matched.
#
# Exclusions (not violations):
#   - Lines starting with optional whitespace + "//" (comment lines)
#   - The interface/abstract declaration: "Task ReleaseLeaseAsync(" (no argument)
#   - Lines that already pass "CancellationToken.None" as the argument
#
# Usage: bash scripts/check-lease-release-token.sh
# Exit 0: SCALE-02 OK — no non-None token lease release found.
# Exit 1: SCALE-02 violation — one or more non-None lease releases found.

set -euo pipefail

# Step 1: find ALL ReleaseLeaseAsync( call-sites in src/ .cs files,
# excluding comment lines.
all_calls=$(grep -rn --include='*.cs' 'ReleaseLeaseAsync(' src/ 2>/dev/null \
    | grep -vE '^\S+:\d+:\s*//' || true)

# Step 2: from those, keep only the lines that DO pass CancellationToken.None
# (the correct pattern). We will subtract these from the total.
correct_calls=$(printf '%s' "$all_calls" \
    | grep 'ReleaseLeaseAsync(CancellationToken\.None)' || true)

# Step 3: also exclude the interface/abstract declaration lines that have no
# argument at all — they are not call-sites.
# Pattern: "Task ReleaseLeaseAsync(" without a closing ")" on the same line
# (interface method signature) OR with an empty arg list like "ReleaseLeaseAsync()".
# More precisely: any line where the token after "(" is NOT an identifier/token
# but we can't easily detect "no argument" with grep alone, so we exclude the
# common interface-declaration shape instead:
#   - "Task ReleaseLeaseAsync(CancellationToken" (method signature with param name)
#   These are declarations, not invocations — they take a parameter TYPE, not a value.
declaration_lines=$(printf '%s' "$all_calls" \
    | grep 'Task ReleaseLeaseAsync(' || true)

# Step 4: violations = all calls MINUS correct calls MINUS declaration lines.
# Use process substitution + comm (both already sorted by grep -n output) — but
# since grep output is not guaranteed sorted, use a simpler approach: filter out
# matching lines via grep -v with fixed strings, iteratively.
violations="$all_calls"

# Remove correctly-passing lines.
if [ -n "$correct_calls" ]; then
    while IFS= read -r line; do
        [ -z "$line" ] && continue
        violations=$(printf '%s\n' "$violations" | grep -vxF "$line" || true)
    done <<< "$correct_calls"
fi

# Remove declaration lines (method signatures, not invocations).
if [ -n "$declaration_lines" ]; then
    while IFS= read -r line; do
        [ -z "$line" ] && continue
        violations=$(printf '%s\n' "$violations" | grep -vxF "$line" || true)
    done <<< "$declaration_lines"
fi

count=$(printf '%s' "$violations" | grep -c . || true)

if [ "$count" -ne 0 ]; then
    echo "SCALE-02 VIOLATION: ReleaseLeaseAsync call(s) not passing CancellationToken.None found in src/:"
    printf '%s\n' "$violations"
    echo ""
    echo "Fix: all finally-path ReleaseLeaseAsync calls MUST pass CancellationToken.None"
    echo "     so the release survives SIGTERM (SCALE-02)."
    echo "     The stopping token is already cancelled when the finally block runs."
    exit 1
fi

echo "SCALE-02 OK: all ReleaseLeaseAsync calls in src/ pass CancellationToken.None"
