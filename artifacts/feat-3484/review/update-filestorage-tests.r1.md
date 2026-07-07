# Code Review: update-filestorage-tests (feat-3484)

## Summary
The developer updated `AzureBlobStorageServiceTests.cs` to reference the relocated type's new
namespace, removed the three registration tests from `FileStorageModuleTests.cs` that no longer
apply now that `AzureBlobStorageService` lives in the Azure adapter, and created
`AzureAdapterModuleTests.cs` with the three relocated tests targeting `AddAzureBlobStorageService`.
Independent verification confirms the implementation matches the task context exactly, the full
FileStorage test suite passes, and no stray references to the old namespace or type location remain
anywhere in the repo.

## Review Result: PASS

### task: update-filestorage-tests
**Status:** PASS

**Verification performed:**
- Diffed all three files against the task-context spec — content matches verbatim (imports,
  removed tests, new `AzureAdapterModuleTests.cs` class with its three tests and doc comment).
- Ran `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.FileStorage"`
  from `backend/` in the worktree: build succeeded (only pre-existing, unrelated nullable warnings
  in other feature test files), and the run reported
  `Passed! - Failed: 0, Passed: 115, Skipped: 0, Total: 115`.
- `grep -rn "FileStorage.Services" backend/ --include=*.cs` — no matches; the old namespace is gone
  repo-wide.
- `grep -rn "AzureBlobStorageService" backend/ --include=*.cs` — all matches point to the new
  adapter location (`Adapters/Anela.Heblo.Adapters.Azure/...`), `Program.cs` wiring, the unrelated
  doc-comment mention in `AzureBlobConflictTelemetryFilter.cs`, and the three test files under
  review. No lingering references to the old `Application` location.

## Docs to Update
None.

## Overall Notes
Clean, surgical test-only change. Test split correctly reflects the DI ownership split established
in task 1 (`FileStorageModule` keeps options/HTTP/resilience; `AzureAdapterModule` owns
`BlobServiceClient`/`IBlobStorageService`), and the relocated tests are exact behavioral analogues of
the originals, just re-targeted at the new registration extension.
