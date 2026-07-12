#!/usr/bin/env bash
# SPDX-License-Identifier: Apache-2.0
# Generates a throwaway RSA 2048 key pair for the TicTacToeDuel sample.
# Output: samples/TicTacToeDuel/keys/dev-priv.pem + dev-pub.pem (mode 0600/0644).
# DO NOT use the resulting keys in production.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
KEY_DIR="$REPO_ROOT/samples/TicTacToeDuel/keys"
mkdir -p "$KEY_DIR"

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$KEY_DIR/dev-priv.pem"
openssl rsa -in "$KEY_DIR/dev-priv.pem" -pubout -out "$KEY_DIR/dev-pub.pem"

chmod 0600 "$KEY_DIR/dev-priv.pem"
chmod 0644 "$KEY_DIR/dev-pub.pem"

echo "Generated throwaway RSA key pair in $KEY_DIR"
echo "    dev-priv.pem (0600) && dev-pub.pem (0644)"
echo "These keys are for LOCAL DEVELOPMENT ONLY. Regenerate per deployment."
