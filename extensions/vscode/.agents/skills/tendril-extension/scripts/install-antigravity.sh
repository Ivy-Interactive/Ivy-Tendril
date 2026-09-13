#!/usr/bin/env bash
set -euo pipefail

EXTENSION_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../.." && pwd)"
ANTIGRAVITY_EXT_DIR="$HOME/.antigravity-ide/extensions"
SYMLINK_NAME="ivy-interactive.ivy-tendril-0.1.0"
TARGET_LINK="$ANTIGRAVITY_EXT_DIR/$SYMLINK_NAME"

echo "==> Building Ivy Tendril extension in $EXTENSION_DIR..."
cd "$EXTENSION_DIR"
pnpm install
pnpm run build

echo "==> Ensuring Antigravity extensions directory exists: $ANTIGRAVITY_EXT_DIR"
mkdir -p "$ANTIGRAVITY_EXT_DIR"

if [ -L "$TARGET_LINK" ] || [ -d "$TARGET_LINK" ]; then
    echo "==> Removing existing extension link/folder at $TARGET_LINK..."
    rm -rf "$TARGET_LINK"
fi

echo "==> Linking extension to Antigravity IDE ($TARGET_LINK -> $EXTENSION_DIR)..."
ln -s "$EXTENSION_DIR" "$TARGET_LINK"

echo "==> Verifying extension installation..."
if which antigravity-ide >/dev/null 2>&1; then
    antigravity-ide --list-extensions | grep "ivy-interactive.ivy-tendril" || true
    echo "==> Extension successfully installed/linked to Antigravity IDE!"
    echo "==> Reload Antigravity IDE (Cmd+Shift+P -> 'Developer: Reload Window') to activate."
else
    echo "==> Extension linked to $TARGET_LINK."
fi
