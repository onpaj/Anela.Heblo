# Implementation: update-filestorage-tests

## What was implemented
Updated the two test files affected by the relocation of `AzureBlobStorageService` from
`Anela.Heblo.Application` to `Anela.Heblo.Adapters.Azure` (task `relocate-service-and-rewire-di`),
and added a new test file covering the relocated DI registration.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` — swapped the
  `using Anela.Heblo.Application.Features.FileStorage.Services;` import for
  `using Anela.Heblo.Adapters.Azure.Features.FileStorage;` so the tests compile against the relocated
  type. No behavioral change to the tests themselves.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/FileStorageModuleTests.cs` — removed the three
  tests that asserted `BlobServiceClient` / `IBlobStorageService` registration
  (`AddFileStorageModule_RegistersBlobStorageService_AsSingleton`,
  `AddFileStorageModule_ResolvingBlobStorageServiceTwice_ReturnsSameInstance`,
  `AddFileStorageModule_DevelopmentEnvironmentWithMissingKey_FallsBackAndLogsWarning`) since
  `FileStorageModule` no longer owns that registration. Also dropped the now-unused
  `Anela.Heblo.Application.Features.FileStorage.Services` and `Azure.Storage.Blobs` usings.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureAdapterModuleTests.cs` (new) — the three
  removed tests, relocated and renamed to target `AzureAdapterModule.AddAzureBlobStorageService`
  instead of `FileStorageModule.AddFileStorageModule`. Each test calls both `AddFileStorageModule`
  (for `FileStorageOptions` binding/validation) and `AddAzureBlobStorageService` (for the
  `BlobServiceClient` factory + `IBlobStorageService` binding), matching the split responsibility
  established by task 1.

## Tests
- `AzureBlobStorageServiceTests` (10 tests, existing) — compiles against the relocated type, behavior
  unchanged.
- `FileStorageModuleTests` (existing, reduced by 3) — covers the options binding/validation and the
  named `FileDownload` HTTP client; no longer asserts on Azure SDK types.
- `AzureAdapterModuleTests` (new, 3 tests) — Singleton lifetime, same-instance resolution, and the
  Development-environment fallback + warning log for `AddAzureBlobStorageService`.

Full run: `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.FileStorage"` from `backend/` →
**Passed! Failed: 0, Passed: 115, Skipped: 0, Total: 115**.

`dotnet format Anela.Heblo.sln --verify-no-changes --include <the three files>` from the repo root → clean (exit 0, no output).

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.FileStorage"
```
Expect `Passed! Failed: 0, Passed: 115, Skipped: 0, Total: 115`.

## Notes
- The `dotnet build`/`dotnet run` step that regenerates `access-matrix.generated.json` throws an
  unrelated `JsonException` in this environment (pre-existing tool/environment issue with the
  `Anela.Heblo.AccessMatrixGen` tool, unrelated to this change — it reads a malformed/missing JSON
  file). It only emits an MSBuild warning (exit code 134 from that sub-process) and does not fail the
  build or the test run; not addressed here as it is out of scope for this task.
- No production code was touched in this task — only test files.

## Status
DONE
