## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs:25` — None blocking. The 404 -> FileNotFoundException translation in AzureBlobStorageService, handler NotFound() mapping, and BlobNotFound (HttpStatusCode.NotFound attribute) via HandleResponse form a consistent chain; ReprintExpeditionListHandler behaviour is unchanged. Verified: build 0 errors, 93 related tests pass.
