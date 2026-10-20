# Implementation: migrate-reprint-handler

## What was implemented

Migrated `ReprintExpeditionListHandler` and its manual DI factory in `ExpeditionListArchiveModule.cs`
off the shared `Anela.Heblo.Domain.Features.FileStorage.IBlobStorageService` abstraction and onto the
module-local `IExpeditionListArchiveBlobStore` adapter. The field/parameter was renamed
(`_blobStorageService` → `_blobStore`), the DI factory's local variable renamed (`blobStorage` →
`blobStore`), and the `using Anela.Heblo.Domain.Features.FileStorage;` import removed from all three
files. No other logic changed — validation via `BlobPathValidator.IsValid`, the try/finally temp-file
cleanup, the keyed `"cups"` `IPrintQueueSink` fallback resolution, and the response shape are all
byte-for-byte unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — constructor now takes `IExpeditionListArchiveBlobStore`; call site updated to `_blobStore.DownloadAsync(...)`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — manual DI factory resolves `IExpeditionListArchiveBlobStore` instead of `IBlobStorageService`.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — mock type switched from `Mock<IBlobStorageService>` to `Mock<IExpeditionListArchiveBlobStore>` (all 5 test methods), `using Anela.Heblo.Domain.Features.FileStorage;` removed.

## Tests

- `ReprintExpeditionListHandlerTests.Handle_ValidBlobPath_DownloadsAndSendsToCupsSink` — verifies a successful download is sent to the CUPS sink.
- `ReprintExpeditionListHandlerTests.Handle_SuccessfulSend_DeletesTempFile` — verifies temp file cleanup after a successful send.
- `ReprintExpeditionListHandlerTests.Handle_SendAsyncThrows_StillDeletesTempFileAndPropagates` — verifies cleanup-on-exception and exception propagation.
- `ReprintExpeditionListHandlerTests.Handle_BlobDownloadFails_CreatesNothing` — verifies no temp file / print call when the blob download itself fails.
- `ReprintExpeditionListHandlerTests.Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob` — verifies path-traversal validation short-circuits before touching the blob store.

## How to verify

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ReprintExpeditionListHandlerTests"
```

Result: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.

Also verified: `dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — 0 errors (135 pre-existing warnings unrelated to this change).

## Notes

Followed the task-context file verbatim — full-file replacements as specified, no deviations. This
resolves correctly at runtime because the `adapter-and-di` task already registered
`IExpeditionListArchiveBlobStore` in `FileStorageModule`, and both `FileStorageModule.AddFileStorageModule`
and `ExpeditionListArchiveModule.AddExpeditionListArchiveModule` are called from `ApplicationModule.cs`
before the app starts serving requests.

## PR Summary
Migrated `ReprintExpeditionListHandler` (handler + its manual DI factory in `ExpeditionListArchiveModule`)
from the shared `IBlobStorageService` to the module-scoped `IExpeditionListArchiveBlobStore` adapter,
continuing the ExpeditionListArchive module's move away from the shared FileStorage domain abstraction.
Behavior is unchanged; only the storage dependency and its test double were swapped.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListHandler.cs` — use `IExpeditionListArchiveBlobStore` instead of `IBlobStorageService`
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/ExpeditionListArchiveModule.cs` — DI factory resolves `IExpeditionListArchiveBlobStore`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/ReprintExpeditionListHandlerTests.cs` — mock updated to `IExpeditionListArchiveBlobStore`

## Status
DONE
