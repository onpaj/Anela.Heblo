# Code Review: feat-3412 (r2)

## Review Result: CLEAN

## Blocking
- None

## Advisory

- **Self-inclusion fragility: no comment guards against a future rename of the merged output file.** The `reportgenerator -reports:"coverage/**/*.cobertura.xml"` glob does not self-include `coverage/merged/Cobertura.xml` today only because the output filename ends in `.xml`, not `.cobertura.xml`. If someone later changes `-reporttypes:Cobertura` output naming or moves the target directory under a path whose files happen to match the source glob, the merge step will ingest its own previous output. This was flagged as advisory in r1 and the code is still unchanged. A one-line comment above the `-reports:` argument (e.g. `# NOTE: targetdir must not produce *.cobertura.xml or the glob will self-include`) would eliminate the future debugging trap. No functional change needed.

- **`export PATH` in the merge step does not carry forward to the "Process coverage files" step, but this is not a problem in practice.** Each workflow step runs in its own shell. The `export PATH="$HOME/.dotnet/tools:$PATH"` line is correctly placed in the same step as the `reportgenerator` invocation, so it is effective where it matters. The subsequent "Process coverage files" step calls only `sed` and `grep` and does not need the tools path. Confirmed safe in both `ci-feature-branch.yml` (lines 105–130) and `ci-main-branch.yml` (lines 124–142).

- **`ci-main-branch.yml` now has a redundant `export PATH` in the merge step.** `actions/setup-dotnet@v4` (line 98 of `ci-main-branch.yml`) already appends `~/.dotnet/tools` to `PATH` for all subsequent steps in the job. The explicit `export PATH="$HOME/.dotnet/tools:$PATH"` in the merge step is therefore harmless but unnecessary. Keeping it in sync with the feature-branch workflow (which genuinely needs it) is reasonable for consistency; no change is required.

- **`dotnet tool update --global` is idempotent on .NET 6+ and exits non-zero only on genuine failures.** Both workflows correctly use `dotnet tool update` rather than `dotnet tool install || true`. On .NET 6+, `update` installs the tool when absent and upgrades it when present, making it safe as the sole install command. Confirmed correct.
