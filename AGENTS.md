# Ivy Tendril — Repository Instructions

## Branching

All development work must target the `development` branch. PRs should use `development` as their base branch. The `main` branch is updated by merging `development` into it for releases.

## Merge Conflict Resolution

When resolving merge conflicts between a feature branch and `development`:

1. **Never delete files or directories that exist on `development` but weren't touched by the feature branch.** If a file exists on `development` and your branch didn't modify it, keep the `development` version.
2. **Never use blanket resolution strategies** like `git checkout --theirs .` or `git checkout --ours .`. Resolve each conflict individually.
3. After resolving conflicts, run `dotnet build src/Ivy.Tendril/Ivy.Tendril.slnx` to verify the merge didn't break anything.
