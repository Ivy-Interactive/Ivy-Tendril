#!/bin/bash
# Plan 00613: Remove Stale REBASE_HEAD File
#
# This script removes the stale REBASE_HEAD file from the main Ivy-Tendril repository.
# The file is a leftover from a previous rebase operation that didn't clean up properly,
# causing Tendril to incorrectly report "rebase in progress" during preflight checks.

REPO_PATH="/home/joel/.tendril/Repos/Ivy-Tendril"
REBASE_HEAD_FILE="$REPO_PATH/.git/REBASE_HEAD"

echo "Checking for stale REBASE_HEAD file..."

if [ -f "$REBASE_HEAD_FILE" ]; then
    echo "Found REBASE_HEAD file:"
    cat "$REBASE_HEAD_FILE"
    echo ""
    echo "Removing stale file..."
    rm "$REBASE_HEAD_FILE"

    if [ ! -f "$REBASE_HEAD_FILE" ]; then
        echo "✓ Successfully removed REBASE_HEAD file"
        echo ""
        echo "Verifying git status..."
        cd "$REPO_PATH"
        git status
    else
        echo "✗ Failed to remove REBASE_HEAD file"
        exit 1
    fi
else
    echo "REBASE_HEAD file not found (already removed or never existed)"
fi
