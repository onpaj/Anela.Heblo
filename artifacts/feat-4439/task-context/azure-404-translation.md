### task: azure-404-translation
Files: `src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`, `src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`, `src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs`.
- [ ] In `DownloadAsync`, add `catch (RequestFailedException ex) when (ex.Status == 404)` before the generic catch: log Warning, `throw new FileNotFoundException($"Blob '{blobName}' not found in container '{containerName}'.", blobName, ex);`. Generic catch unchanged.
- [ ] Add `<exception cref="FileNotFoundException">` XML docs to `IBlobStorageService.DownloadAsync` and a `<summary>` note on `IExpeditionListArchiveBlobStore.DownloadAsync`.
- [ ] If an Azure service test project/harness exists (grep tests for `AzureBlobStorageService`), add a test with a mocked `BlobServiceClient` throwing `RequestFailedException(404, ...)`; otherwise skip and note it.
- [ ] `dotnet build backend/Anela.Heblo.sln`; commit.

