# Implementation: handler-not-found

## What was implemented
`DownloadExpeditionListResponse.NotFound()` (ErrorCode BlobNotFound) and a `catch (FileNotFoundException)` around `DownloadAsync` in `DownloadExpeditionListHandler`. Other exceptions still propagate.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs` - not-found and unexpected-exception tests

## Tests
`dotnet test backend/test/Anela.Heblo.Tests --filter FullyQualifiedName~DownloadExpeditionListHandlerTests`: 8 passed. Solution build 0 errors. Test-first red run not performed separately; `dotnet format` not run (no .sln at backend root; invocation errored).

## Notes
None.

## PR Summary
Download handler returns a structured BlobNotFound failure when the blob is missing instead of propagating.

### Changes
- DownloadExpeditionListResponse.NotFound, handler catch of FileNotFoundException, two tests

## Status
DONE
