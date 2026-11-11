# Code Review: update-tests-for-contracts-namespace

## Summary

The task's goal — repoint the five test files' `using` directives at the
new `FileStorage.Contracts` namespace so the test project builds again —
is achieved, and independently verified (build succeeds, all 54 tests in
the five affected classes pass). The implementation correctly deviated
from the task's literal per-file instructions where following them
verbatim would have broken the build, and documented why.

## Review Result: PASS

### task: update-tests-for-contracts-namespace
**Status:** PASS

Verification performed independently:
- `grep -rn "FileStorage.UseCases.DownloadFromUrl" backend/test/` — confirmed empty (exit 1), matching the acceptance criterion in step 6.
- `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — confirmed `Build succeeded.`, `0 Error(s)`.
- `dotnet test ... --filter "FullyQualifiedName~DownloadFromUrlHandlerTests|FullyQualifiedName~FileStorageControllerTests|FullyQualifiedName~FileStorageValidationPipelineTests|FullyQualifiedName~DownloadFromUrlRequestValidatorTests|FullyQualifiedName~ProductExportDownloadJobTests"` — confirmed `Passed! - Failed: 0, Passed: 54, Skipped: 0, Total: 54`.
- Diff reviewed for all five files: only `using` lines changed, no test method, assertion, or test-data changes — matches the task's stated scope ("no test assertion, test data, or test behavior changes").
- The deviation (keeping `using ...UseCases.DownloadFromUrl;` in `DownloadFromUrlHandlerTests.cs` and `FileStorageValidationPipelineTests.cs` instead of dropping it) is correct: both files reference `DownloadFromUrlHandler` (a type not moved to `Contracts`), confirmed by inspecting `DownloadFromUrlHandler.cs`, which still declares `namespace Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;`. Following the task's literal instruction here would have caused `CS0246` build errors, which is exactly what happened on the first attempt before the fix.
- The other three files (`FileStorageControllerTests.cs`, `DownloadFromUrlRequestValidatorTests.cs`, `ProductExportDownloadJobTests.cs`) only reference `DownloadFromUrlRequest`, so the straight replacement specified by the task is correct for them.
- Commit message accurately describes the change and the deviation.

## Docs to Update

None — this is an internal test-only namespace fix with no public behavior, API, or documented-process change.

## Overall Notes

No cross-cutting concerns. This confirms `create-filestorage-contracts-folder` (the prior task) only moved `DownloadFromUrlRequest`/`DownloadFromUrlResponse`, not `DownloadFromUrlHandler`, into `Contracts` — worth keeping in mind for the remaining `verify-build-format-and-openapi-client` task, which should not assume the handler itself moved.
