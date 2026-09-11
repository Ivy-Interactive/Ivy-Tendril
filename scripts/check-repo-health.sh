#!/bin/sh
# Checks that the top-level repository layout has not drifted.
# Prints "OK <path>" or "MISSING <path>" for each required directory and
# exits 1 if any check failed.
set -eu

script_dir=$(dirname "$0")
repo_root=${1:-$(cd "$script_dir/.." && pwd)}

required_dirs=".config docs scripts src .github"

failed=0

for dir in $required_dirs; do
    path="$repo_root/$dir"
    if [ -d "$path" ]; then
        echo "OK $dir"
    else
        echo "MISSING $dir"
        failed=1
    fi
done

exit "$failed"
