#!/usr/bin/env bash
set -euo pipefail

ANTIGRAVITY_EXT_DIR="$HOME/.antigravity-ide/extensions"
SYMLINK_NAME="ivy-interactive.ivy-tendril-0.1.0"
TARGET_LINK="$ANTIGRAVITY_EXT_DIR/$SYMLINK_NAME"

if [ -L "$TARGET_LINK" ] || [ -d "$TARGET_LINK" ]; then
    echo "==> Removing Ivy Tendril extension from Antigravity IDE ($TARGET_LINK)..."
    rm -rf "$TARGET_LINK"
    echo "==> Successfully removed."
else
    echo "==> Extension is not currently linked in $ANTIGRAVITY_EXT_DIR."
fi
