# Repository health check

`scripts/check-repo-health.sh` verifies that the top-level repository layout has not drifted. It checks that each of the following exists and is a directory:

- `.config`
- `docs`
- `scripts`
- `src`
- `.github`

For each one it prints `OK <path>` or `MISSING <path>`, and exits `1` if any check failed, `0` otherwise. It is dependency-free POSIX shell, so it runs on a bare runner with no `jq` or `node` required.

The check runs in CI via `.github/workflows/repo-health.yml` on every push and pull request targeting `development`.

## Running locally

```sh
bash scripts/check-repo-health.sh
```

To exercise both the pass and fail branches of the checker:

```sh
bash scripts/test-check-repo-health.sh
```

## Adding a required directory

Edit the `required_dirs` list near the top of `scripts/check-repo-health.sh` and add the new directory name. If the addition changes what the test script's temporary-directory fixture reports, update the assertions in `scripts/test-check-repo-health.sh` to match.
