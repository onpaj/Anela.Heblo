# Code Review: Remove Azure Adapter's Compile-Time Dependency on the Application Layer (FileStorage)

## Summary
The implementation matches the task plan exactly: a new `FileStorageConstants` class in Domain, the adapter switched to it, a forwarding const left in `FileStorageModule` for backward compatibility, and the adapter's tests updated to the Domain constant. Independently verified against commit `c39db03`.

## Review Result: PASS

### task: fix-filestorage-adapter-application-dependency
**Status:** PASS

Verification performed:
- `git grep -n "Anela.Heblo.Application" backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` returns no matches (exit code 1).
- `git show c39db03 --stat` shows only the 4 expected code files changed plus the impl artifact — no diff in `AzureAdapterModule.cs`, `IBlobStorageService.cs`, `DownloadFromUrlHandler.cs`, `FileStorageModuleTests.cs`, or any `.csproj`.
- `dotnet build Anela.Heblo.sln` succeeds with 0 errors (one pre-existing, unrelated AccessMatrixGen tool warning reproduces independent of this change).
- `dotnet format Anela.Heblo.sln --verify-no-changes` reports no diff.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage"` — 116/116 passed, covering `AzureBlobStorageServiceTests`, `FileStorageModuleTests`, `DownloadFromUrlHandlerTests`, `SimpleFileStorageTest`.
- `FileStorageModule.FileDownloadClientName` is now `= FileStorageConstants.FileDownloadClientName;` — still a compile-time const, still equals `"FileDownload"`, and `FileStorageModuleTests.cs`'s assertion against it is unchanged and passing.

## Overall Notes
Clean, surgical, in-scope refactor. No further action needed.
