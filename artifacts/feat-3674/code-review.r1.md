## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- None

## Notes
The diff is a minimal, mechanical constant relocation: adds `backend/src/Anela.Heblo.Domain/Features/FileStorage/FileStorageConstants.cs`, repoints `AzureBlobStorageService.cs` at it (dropping its `using Anela.Heblo.Application.Features.FileStorage;`), turns `FileStorageModule.FileDownloadClientName` into a forwarding const, and updates `AzureBlobStorageServiceTests.cs` to reference the new Domain constant. The string value (`"FileDownload"`), `HttpClient` registration, and all runtime behavior are unchanged — verified by 116 passing FileStorage-scoped tests, a clean `dotnet build`, and a no-diff `dotnet format`. No scope creep: `AzureAdapterModule.cs`, `IBlobStorageService.cs`, `DownloadFromUrlHandler.cs`, and all `.csproj` files are untouched, matching the spec's explicit out-of-scope list.
