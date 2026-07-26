
## Goal
Remove `AzureBlobStorageService`'s compile-time dependency on the Application-layer `FileStorageModule` by introducing a Domain-layer constant, while preserving full backward compatibility for existing Application-layer consumers and tests.

## Steps

1. **Add the Domain constant.** Create `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs`:
   ```csharp
   namespace Anela.Heblo.Domain.Features.FileStorage;

   public static class FileStorageConstants
   {
       public const string FileDownloadClientName = "FileDownload";
   }
   ```

2. **Update the adapter.** In `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`:
   - Remove `using Anela.Heblo.Application.Features.FileStorage;`.
   - Change the `_httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName)` call to `_httpClientFactory.CreateClient(FileStorageConstants.FileDownloadClientName)`, relying on the file's existing `using Anela.Heblo.Domain.Features.FileStorage;`.
   - Verify with `git grep -n "Anela.Heblo.Application" backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` — must return no matches.

3. **Make `FileStorageModule.FileDownloadClientName` a forwarding const.** In `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs`, change:
   ```csharp
   public const string FileDownloadClientName = "FileDownload";
   ```
   to:
   ```csharp
   public const string FileDownloadClientName = FileStorageConstants.FileDownloadClientName;
   ```
   (Add `using Anela.Heblo.Domain.Features.FileStorage;` if not already present in this file.) `services.AddHttpClient(FileDownloadClientName)...` registration body stays unchanged.

4. **Update the adapter's unit tests.** In `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`:
   - Replace `using Anela.Heblo.Application.Features.FileStorage;` with `using Anela.Heblo.Domain.Features.FileStorage;` (if not already present).
   - Replace all ~11 occurrences of `FileStorageModule.FileDownloadClientName` with `FileStorageConstants.FileDownloadClientName`.

5. **Leave untouched:** `AzureAdapterModule.cs`, `IBlobStorageService.cs`, `DownloadFromUrlHandler.cs`, `FileStorageModuleTests.cs`, `DownloadFromUrlHandlerTests.cs`, `SimpleFileStorageTest.cs`, and all `.csproj` files.

## Acceptance criteria
- `git grep -n "Anela.Heblo.Application" backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` returns empty.
- `dotnet build` succeeds with zero new errors/warnings.
- `dotnet format` produces no diff.
- All tests in `backend/test/Anela.Heblo.Tests/Features/FileStorage/` pass (`AzureBlobStorageServiceTests.cs`, `FileStorageModuleTests.cs`, `DownloadFromUrlHandlerTests.cs`, `SimpleFileStorageTest.cs`), assertions unchanged.
- `FileStorageModuleTests.cs` line asserting `Assert.Equal("FileDownload", FileStorageModule.FileDownloadClientName)` passes unmodified.
- No diff in `AzureAdapterModule.cs`, `IBlobStorageService.cs`, `DownloadFromUrlHandler.cs`, or any `.csproj`.

## Status: COMPLETE
