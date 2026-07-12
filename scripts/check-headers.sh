#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 GameKit contributors
# REUSE-IgnoreStart

# Fast, zero-dep SPDX header check for local pre-commit use.
# The authoritative CI check is fsfe/reuse-action@v6 (.github/workflows/license-check.yml);
# this script is the contributor convenience fallback.

set -euo pipefail

missing=0
for f in $(find src tests samples -name '*.cs' 2>/dev/null | grep -v -E '/(obj|bin)/' | grep -v 'Migrations/' ); do
    if ! head -n 1 "$f" | grep -q 'SPDX-License-Identifier: Apache-2.0'; then
        echo "Missing SPDX header: $f"
        missing=$((missing+1))
    fi
done

if [ "$missing" -gt 0 ]; then
    echo ""
    echo "ERROR: $missing file(s) missing SPDX headers."
    echo "Add this as the first two lines of each file (no blank line between):"
    echo "  // SPDX-License-Identifier: Apache-2.0"
    echo "  // Copyright (c) 2026 GameKit contributors"
    exit 1
fi

echo "OK — all .cs files have SPDX headers."
# REUSE-IgnoreEnd
