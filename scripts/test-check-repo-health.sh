#!/bin/sh
# Exercises both branches of check-repo-health.sh: the pass case against the
# real repo root, and the fail case against an incomplete temporary tree.
set -eu

script_dir=$(dirname "$0")
repo_root=$(cd "$script_dir/.." && pwd)
checker="$script_dir/check-repo-health.sh"

overall=0

echo "== pass branch =="
set +e
bash "$checker" "$repo_root"
pass_status=$?
set -e
if [ "$pass_status" -eq 0 ]; then
    echo "PASS: checker succeeded against repo root"
else
    echo "FAIL: checker exited $pass_status against repo root, expected 0"
    overall=1
fi

echo "== fail branch =="
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

set +e
fail_output=$(bash "$checker" "$tmp" 2>&1)
fail_status=$?
set -e

if [ "$fail_status" -eq 0 ]; then
    echo "FAIL: checker exited 0 against incomplete tree, expected non-zero"
    overall=1
elif ! printf '%s\n' "$fail_output" | grep -q '^MISSING '; then
    echo "FAIL: checker did not print a MISSING line against incomplete tree"
    overall=1
else
    echo "PASS: checker exited $fail_status and reported at least one MISSING line"
fi

exit "$overall"
