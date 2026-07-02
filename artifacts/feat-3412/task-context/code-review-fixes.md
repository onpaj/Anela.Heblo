## Goal
Fix the code review findings below

## Blocking findings from code-review.r1.md

1. **Logic inversion in the existence guard (both files)** — `sed -i` ran before `if [ ! -f ... ]`, so if `reportgenerator` failed, `sed` would abort with "No such file or directory" rather than the intended error message. The guard was unreachable. Fixed: check existence first, then apply `sed`.

2. **`|| true` masks failed installs** — `dotnet tool install --global ... || true` silently swallowed genuine install failures (network errors, version conflicts), surfacing them only later as opaque "command not found" on the `reportgenerator` line. Fixed: replaced with `dotnet tool update --global` (idempotent — succeeds if already installed or upgrades if a lower version is present) and added `export PATH="$HOME/.dotnet/tools:$PATH"` to ensure the tool is reachable without a shell restart.
