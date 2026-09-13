#!/usr/bin/env bash
set -euo pipefail

if [ -d "$(dirname "${BASH_SOURCE[0]}")/../../../extensions/vscode" ]; then
    EXTENSION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../extensions/vscode" && pwd)"
else
    EXTENSION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
fi
echo "==> Packaging Ivy Tendril VSIX archive in $EXTENSION_DIR..."
cd "$EXTENSION_DIR"
pnpm install
pnpm run build
npx @vscode/vsce package --no-dependencies
echo "==> VSIX Package created successfully: $(ls -t "$EXTENSION_DIR"/*.vsix | head -n 1)"
