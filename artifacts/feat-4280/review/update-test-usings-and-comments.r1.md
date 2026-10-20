# Code Review: update-test-usings-and-comments

## Summary
The implementation matches the task spec exactly: the new
`Infrastructure.Exceptions` using was added (with `Contracts` retained where
still needed) in all three test files, and the two stale doc-comment/message
references in `ModuleBoundariesTests.cs` were updated to the new namespace
with no other lines touched. Full solution build is clean (0 errors) and the
filtered test run (96 tests, including `ModuleBoundariesTests`) passes.

## Review Result: PASS

### task: update-test-usings-and-comments
**Status:** PASS

## Docs to Update
(none — this is a mechanical using/comment fix, no public behaviour changed)

## Overall Notes
Verified each of the 4 files diff-for-diff against the task context's exact
before/after snippets — they match verbatim. Verified the `SdkExceptionAllowlist`
entries and reflection logic in `ModuleBoundariesTests.cs` were left untouched,
as required. Build and test commands were re-run independently for this review:
`dotnet build Anela.Heblo.sln` (0 errors, pre-existing warnings only) and
`dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter
"FullyQualifiedName~UserManagement|FullyQualifiedName~ModuleBoundariesTests"`
(96 passed, 0 failed, 0 skipped).

**Status:** PASS
