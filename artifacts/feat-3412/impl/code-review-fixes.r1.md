# Implementation: code-review-fixes r1

## What was implemented

Fixed both blocking findings from `code-review.r1.md` in `.github/workflows/ci-main-branch.yml` and `.github/workflows/ci-feature-branch.yml`.

## Files modified

- `.github/workflows/ci-main-branch.yml` — reordered existence check before `sed`; replaced `dotnet tool install || true` with `dotnet tool update` + PATH export
- `.github/workflows/ci-feature-branch.yml` — same fixes

## Changes made

**Blocking 1 fix** — moved `if [ ! -f coverage/merged/Cobertura.xml ]; then exit 1; fi` to run before `sed -i`, so a missing merged file produces the intended error message rather than a confusing `sed: can't read ... No such file or directory`.

**Blocking 2 fix** — replaced:
```yaml
dotnet tool install --global dotnet-reportgenerator-globaltool --version 5.4.3 || true
```
with:
```yaml
dotnet tool update --global dotnet-reportgenerator-globaltool --version 5.4.3
export PATH="$HOME/.dotnet/tools:$PATH"
```
`dotnet tool update` is idempotent (installs if absent, upgrades if lower version, no-ops if same version) and exits non-zero on real failures. The PATH export ensures the tool binary is reachable immediately in the same step without requiring a new shell.

## Status
DONE
