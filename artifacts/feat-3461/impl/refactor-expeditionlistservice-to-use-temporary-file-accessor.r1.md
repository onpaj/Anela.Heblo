# Implementation: refactor-expeditionlistservice-to-use-temporary-file-accessor

## What was implemented
Injected `ITemporaryFileAccessor` into `ExpeditionListService` and replaced the bodies of `Cleanup` and `SendEmailCopy` to delegate to it instead of calling `System.IO.File` directly. `SendEmailCopy` now accepts and forwards a `CancellationToken` into `ReadAllBytesAsync`. Updated both existing test files to mock `ITemporaryFileAccessor`, rewrote `PrintPickingListAsync_CleanupRunsAfterSuccess` to assert against the mock instead of the real filesystem, and added a new test asserting email attachments are built from the accessor's returned bytes.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` — new `ITemporaryFileAccessor` constructor dependency; `Cleanup`/`SendEmailCopy` delegate to it instead of `File.*`; `SendEmailCopy` gained a `CancellationToken` parameter forwarded from `PrintPickingListAsync`'s batch callback.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` — added `Mock<ITemporaryFileAccessor>` field, threaded into `CreateService()`.
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs` — added `Mock<ITemporaryFileAccessor>` field; rewrote `PrintPickingListAsync_CleanupRunsAfterSuccess` to use a fake path + `Verify(DeleteIfExists(...), Times.Once)` instead of `Path.GetTempFileName()`/`File.Exists`; added new test `PrintPickingListAsync_EmailAttachments_BuiltFromAccessorBytes`.

## Tests
- `ExpeditionListServicePrintSinkTests` (2 tests, assertions unchanged): pass.
- `ExpeditionListServiceOrderStateTests` (6 tests: original 5 + new `PrintPickingListAsync_EmailAttachments_BuiltFromAccessorBytes`): pass.
- Full `ExpeditionList` test slice (`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionList`): 165 passed, 0 failed (up from 164 in the previous task — one net new test).
- `grep -n "File\." backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` returns zero matches for `File.Exists`/`File.Delete`/`File.ReadAllBytesAsync` (`Path.GetFileName` remains, correctly — it's not I/O).

## How to verify
```
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~ExpeditionList
git diff --stat  # only the 5 files across both tasks are touched (plus the unrelated pre-existing fix in task 1)
```

## Notes
No deviations from the task-context plan. `IExpeditionListService.cs` (public interface) was not modified, as required.

## PR Summary
Completes the ExpeditionList I/O-placement fix: `ExpeditionListService` no longer calls `System.IO.File` directly, delegating all temp-file read/delete to the `ITemporaryFileAccessor` abstraction added in the prior task. Existing print-sink and order-state test suites are updated to mock the new dependency, and a new test pins that email attachments are built from the bytes the accessor returns.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionList/Services/ExpeditionListService.cs` — refactored to use `ITemporaryFileAccessor`
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServicePrintSinkTests.cs` — mock wiring
- `backend/test/Anela.Heblo.Tests/Features/ExpeditionList/ExpeditionListServiceOrderStateTests.cs` — mock wiring, rewritten cleanup test, new attachment test

## Status
DONE
