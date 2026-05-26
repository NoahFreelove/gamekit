#!/usr/bin/env bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Generates a throwaway RSA 2048 key pair for the generated GameKit sample.
#
# Lives at <project-root>/scripts/gen-test-rsa-pem.sh after the template renders.
# Discovers the web-tier keys/ directory by scanning src/*/keys/ — works regardless
# of the project name the consumer passed to `dotnet new gamekit -n <name>`.
#
# DO NOT use the resulting keys in production — see docs/ops/jwt-keys.md in the
# GameKit repo for production key rotation guidance.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Find the web-tier keys/ directory. The template ships one src/<name>/keys/ for the
# web tier (the GameServer tier has no keys). Pick the first match; if a consumer
# adds more keys/ dirs we don't auto-discover them.
KEY_DIR=""
for candidate in "$PROJECT_ROOT"/src/*/keys; do
    if [ -d "$candidate" ]; then
        KEY_DIR="$candidate"
        break
    fi
done

if [ -z "$KEY_DIR" ]; then
    echo "ERROR: could not locate a src/<name>/keys/ directory under $PROJECT_ROOT." >&2
    echo "       Expected a single web-tier project under src/ with a keys/ subdir." >&2
    exit 1
fi

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$KEY_DIR/dev-priv.pem"
openssl rsa -in "$KEY_DIR/dev-priv.pem" -pubout -out "$KEY_DIR/dev-pub.pem"

chmod 0600 "$KEY_DIR/dev-priv.pem"
chmod 0644 "$KEY_DIR/dev-pub.pem"

echo "Generated throwaway RSA key pair in $KEY_DIR"
echo "    dev-priv.pem (0600) && dev-pub.pem (0644)"
echo "These keys are for LOCAL DEVELOPMENT ONLY. Regenerate per deployment."
