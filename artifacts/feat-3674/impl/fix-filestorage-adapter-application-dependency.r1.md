# Implementation: fix-filestorage-adapter-application-dependency

## What was implemented
Removed `AzureBlobStorageService`'s compile-time dependency on the Application-layer `FileStorageModule` by introducing a Domain-layer `FileStorageConstants` class holding the `FileDownloadClientName` string. `FileStorageModule.FileDownloadClientName` now forwards to the Domain constant so existing Application-layer consumers (`DownloadFromUrlHandler.cs`, `FileStorageModuleTests.cs`) need no changes.

## Files created/modified
- `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs` — new file, `public static class` with `public const string FileDownloadClientName = "FileDownload"`.
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` — removed `using Anela.Heblo.Application.Features.FileStorage;`, switched to `FileStorageConstants.FileDownloadClientName`.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — `FileDownloadClientName` is now a forwarding const referencing `FileStorageConstants.FileDownloadClientName`.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` — swapped `using Anela.Heblo.Application.Features.FileStorage;` for `using Anela.Heblo.Domain.Features.FileStorage;`; replaced all `FileStorageModule.FileDownloadClientName` references with `FileStorageConstants.FileDownloadClientName`.

## Tests
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage"` — 116 passed, 0 failed (covers `AzureBlobStorageServiceTests`, `FileStorageModuleTests`, `DownloadFromUrlHandlerTests`, `SimpleFileStorageTest`).

## How to verify
1. `git grep -n "Anela.Heblo.Application" backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` → no matches.
2. `dotnet build Anela.Heblo.sln` → succeeds, 0 errors.
3. `dotnet format Anela.Heblo.sln --verify-no-changes` → no diff.
4. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage"` → all pass.

## Notes
- `dotnet build` emits a pre-existing warning (MSB3073, exit code 134) from the `Anela.Heblo.AccessMatrixGen` post-build tool failing to parse a JSON file — this is unrelated to this change (reproduces on a clean checkout of the same commit before this diff) and does not fail the build.
- No diff in `AzureAdapterModule.cs`, `IBlobStorageService.cs`, `DownloadFromUrlHandler.cs`, or any `.csproj`, matching the spec's scope constraints.

## PR Summary
`AzureBlobStorageService` (an infrastructure adapter) imported the Application-layer `FileStorageModule` purely to read a string constant naming its `HttpClient`, violating Clean Architecture's dependency rule. This change adds a `FileStorageConstants` class in the Domain layer holding that constant, points the adapter at it instead, and turns `FileStorageModule.FileDownloadClientName` into a forwarding const so every existing Application-layer caller and test keeps compiling unchanged. Pure compile-time refactor — the `HttpClient` name, its registration, and all runtime behavior are unchanged.

### Changes
- `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs` — new Domain-layer constant holder
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` — drop Application dependency, use Domain constant
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — forwarding const
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` — updated to reference the Domain constant

## Status
DONE
