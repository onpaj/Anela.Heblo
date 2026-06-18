# PR Context

- **PR**: #3199 — feat-3080: add unit tests for DisassembleGiftPackageHandler
- **URL**: https://github.com/onpaj/Anela.Heblo/pull/3199
- **Branch**: `feature/feat-3080` → `main`
- **State**: OPEN
- **Author**: onpaj
- **Changes**: +105 / -0 across 1 file
- **Absorbed**: no backmerge needed (already up-to-date with main); CI fix pushed

## Description

`DisassembleGiftPackageHandler` had 10.8% line coverage (threshold: 60%). Both exception-routing catch blocks were completely untested — an `InvalidOperationException` maps to `ErrorCodes.InvalidOperation` and an `ArgumentException` maps to `ErrorCodes.InvalidValue`. Because both catch blocks are structurally identical, a copy-paste regression would be silent without tests.

Closes #3080

## What the routine fixed

The only failing CI check was **Claude Code Review**, which was failing with:
> API Error: 404 — model: claude-sonnet-4-20250514 (not found)

The pinned `claude-code-action` at `de8e0b9c` uses a retired model ID. Fix: added `model: claude-sonnet-4-6` to `.github/workflows/claude-review.yml` to override the default. All functional checks (Backend Tests, Frontend Tests, Docker Build) were already passing.
