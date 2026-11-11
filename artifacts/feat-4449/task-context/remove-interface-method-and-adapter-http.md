### task: remove-interface-method-and-adapter-http
- `IBlobStorageService.cs`: delete `DownloadFromUrlAsync` and its summary.
- `AzureBlobStorageService.cs`: delete `DownloadFromUrlAsync`, `_httpClientFactory` field, ctor parameter, `GetExtensionFromContentType`, `GetContentTypeFromExtension`; ctor becomes `(BlobServiceClient, ILogger<AzureBlobStorageService>)`. No change needed in `AzureAdapterModule` (DI-resolved).
- `AzureBlobStorageServiceTests.cs`: delete all `DownloadFromUrlAsync_*` tests and the `_httpClientFactory` plumbing/related helpers; update the ~9 `new AzureBlobStorageService(...)` call sites to the two-arg ctor; keep all remaining tests.
- `MockBlobStorageService.cs` and `SimpleFileStorageTest.cs`: remove the `DownloadFromUrlAsync` member/usage (delete `SimpleFileStorageTest` test if it only exercises that method).
Verify: `grep -rn DownloadFromUrlAsync backend/` returns nothing; `dotnet build backend/Anela.Heblo.sln` succeeds.

