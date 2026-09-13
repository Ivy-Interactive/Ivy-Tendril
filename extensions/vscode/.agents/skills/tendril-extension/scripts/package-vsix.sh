#!/usr/bin/env bash
set -euo pipefail

EXTENSION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
echo "==> Packaging Ivy Tendril VSIX archive in $EXTENSION_DIR..."
cd "$EXTENSION_DIR"
pnpm install
pnpm run build
npx @vscode/vsce package --no-dependencies
echo "==> VSIX Package created successfully: $(ls -t "$EXTENSION_DIR"/*.vsix | head -n 1)"
