# Implementation Plan: Download missing-blob handling

Goal: missing archived PDF yields `BlobNotFound` structured failure and HTTP 404 instead of 500.
Base path: `backend/`. Follow TDD; run `dotnet build` and `dotnet format` at the end.

### task: azure-404-translation
Files: `src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`, `src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs`, `src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs`.
- [ ] In `DownloadAsync`, add `catch (RequestFailedException ex) when (ex.Status == 404)` before the generic catch: log Warning, `throw new FileNotFoundException($"Blob '{blobName}' not found in container '{containerName}'.", blobName, ex);`. Generic catch unchanged.
- [ ] Add `<exception cref="FileNotFoundException">` XML docs to `IBlobStorageService.DownloadAsync` and a `<summary>` note on `IExpeditionListArchiveBlobStore.DownloadAsync`.
- [ ] If an Azure service test project/harness exists (grep tests for `AzureBlobStorageService`), add a test with a mocked `BlobServiceClient` throwing `RequestFailedException(404, ...)`; otherwise skip and note it.
- [ ] `dotnet build backend/Anela.Heblo.sln`; commit.

### task: handler-not-found
Files: `src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`, `.../DownloadExpeditionListHandler.cs`, `test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs`.
- [ ] Write failing test `Handle_BlobNotFound_ReturnsBlobNotFoundFailure`: setup `DownloadAsync` to throw `new FileNotFoundException()`; assert `Success` false, `ErrorCode == ErrorCodes.BlobNotFound`, `Stream` null.
- [ ] Write test `Handle_UnexpectedBlobException_Propagates`: store throws `IOException("boom")` (plain IOException, not FileNotFound); `await Assert.ThrowsAsync<IOException>`.
- [ ] Run `dotnet test --filter DownloadExpeditionListHandlerTests`; expect the first to fail.
- [ ] Add `public static DownloadExpeditionListResponse NotFound() => new() { Success = false, ErrorCode = ErrorCodes.BlobNotFound };`
- [ ] In handler wrap the `DownloadAsync` call: `Stream stream; try { stream = await ... } catch (FileNotFoundException) { return DownloadExpeditionListResponse.NotFound(); }`.
- [ ] Run the tests; all pass; commit.

### task: controller-status-mapping
Files: `src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs`, tests under `test/Anela.Heblo.Tests/Controllers` or `ExpeditionListArchive` (inspect for existing controller tests of this controller first).
- [ ] Change the failure branch of `Download` from `return BadRequest(response);` to `return HandleResponse(response).Result!;` (404 for BlobNotFound, 400 for InvalidBlobPath, via the ErrorCode HTTP attribute). Keep the `response.Stream == null` guard consistent: if `Success` true but stream null, keep `BadRequest(response)`.
- [ ] Add controller tests (mock `IMediator`): BlobNotFound -> `NotFoundObjectResult`; InvalidBlobPath -> `BadRequestObjectResult`; success -> `FileStreamResult`. Mirror setup used by existing controller tests (e.g. `PurchaseOrdersControllerErrorTests`).
- [ ] Check frontend usage of the download URL (`grep -rn "expedition-list-archive/download" frontend/src`) to confirm a 404 does not break handling; no FE change expected.
- [ ] Run tests; commit.

### task: docs-and-validation
Files: `docs/processes/module-expedition-list-archive.md`.
- [ ] Add a Known quirks/runtime-facts line: missing blob on download returns 404 `BlobNotFound` (1805); Reprint still propagates blob exceptions. Bump `verified_at`.
- [ ] Run `python3 scripts/process-docs/check.py index`.
- [ ] Run `dotnet build`, `dotnet format`, and `dotnet test` for `ExpeditionListArchive` and controller tests; all green.
- [ ] Commit.
