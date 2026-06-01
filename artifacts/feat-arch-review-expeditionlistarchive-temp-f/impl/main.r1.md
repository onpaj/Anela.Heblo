Final reviewer says **READY TO MERGE**. All three tasks are complete. Let me write the output artifact.

---

# Implementation: Fix Temp File Leak in ReprintExpeditionListHandler

## What was implemented

Eliminated the orphaned `.tmp` file that `ReprintExpeditionListHandler` leaked into the system temp directory on every reprint. `Path.GetTempFileName()` creates a zero-byte file on disk as a side effect; appending `.pdf` produced a different path, so the original `.tmp` was never cleaned up. Replaced with a pure in-memory GUID-based path that has no filesystem side effect.

Two regression tests were written first (TDD RED) and then the production fix was applied (GREEN).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — single-line change at line 29
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — two new regression tests added

## Tests

- `Handle_SuccessfulReprint_LeavesNoTempFileBehind` — captures path via Moq Callback, asserts file exists during sink call, is gone after handler returns, and no handler-shaped files leaked in temp dir
- `Handle_BlobDownloadFails_LeavesNoTempFileBehind` — configures blob download to throw IOException, asserts exception propagates, no leaked files, sink never called
- Both new tests were RED before the fix and GREEN after
- Full suite: 3985 tests pass, 3 skipped

## How to verify

```bash
# Run the 4 handler tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests" --nologo

# Full suite
dotnet test backend/Anela.Heblo.sln --nologo

# Build + format gates
dotnet build backend/Anela.Heblo.sln --nologo
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

## Notes

Commits (in order):
- `b7493ec4` — test: add regression test for ReprintExpeditionListHandler temp file leak
- `2eb92837` — test: add regression test for ReprintExpeditionListHandler temp file leak on failure
- `e3f0f7f4` — fix: prevent temp file leak in ReprintExpeditionListHandler

`PlaudCliClient.cs` has the same bug pattern but is explicitly out of scope per spec (different adapter slice, different lifecycle). Mentioned for the next sweep.

## PR Summary

Fixes a silent resource leak in `ReprintExpeditionListHandler` where every reprint left an orphaned zero-byte `.tmp` file in the system temp directory. Over time this fills `/tmp` with junk and degrades server health.

Root cause: `Path.GetTempFileName()` atomically creates a file on disk and returns its path; appending `.pdf` produces a different path referencing a non-existent file. The handler wrote and deleted the `.pdf` path, never touching the underlying `.tmp` file.

Fix: replace with `Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf")` — a pure string operation with no disk side effect, preserving the `.pdf` extension the CUPS sink depends on. One fewer syscall per reprint as a bonus.

### Changes
- `ReprintExpeditionListHandler.cs:29` — replaced path construction (single line)
- `ReprintExpeditionListHandlerTests.cs` — added two regression tests (success path and failure path), written RED-first before the fix

## Status
DONE