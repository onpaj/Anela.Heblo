### task: handler-not-found
Files: `src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`, `.../DownloadExpeditionListHandler.cs`, `test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs`.
- [ ] Write failing test `Handle_BlobNotFound_ReturnsBlobNotFoundFailure`: setup `DownloadAsync` to throw `new FileNotFoundException()`; assert `Success` false, `ErrorCode == ErrorCodes.BlobNotFound`, `Stream` null.
- [ ] Write test `Handle_UnexpectedBlobException_Propagates`: store throws `IOException("boom")` (plain IOException, not FileNotFound); `await Assert.ThrowsAsync<IOException>`.
- [ ] Run `dotnet test --filter DownloadExpeditionListHandlerTests`; expect the first to fail.
- [ ] Add `public static DownloadExpeditionListResponse NotFound() => new() { Success = false, ErrorCode = ErrorCodes.BlobNotFound };`
- [ ] In handler wrap the `DownloadAsync` call: `Stream stream; try { stream = await ... } catch (FileNotFoundException) { return DownloadExpeditionListResponse.NotFound(); }`.
- [ ] Run the tests; all pass; commit.

