# Architecture Review: Download missing-blob handling

## Skip Design: true
Backend-only change; no UI.

## Architectural Fit Assessment
Fits the existing Clean Architecture split: Application (handler, error codes) has no Azure SDK dependency; the Azure adapter project (`Anela.Heblo.Adapters.Azure`) owns SDK exception knowledge. Verified: Application/Domain csproj contain no Azure reference; the only caller of `IBlobStorageService.DownloadAsync` is `ExpeditionListArchiveBlobStoreAdapter` (pure delegation).

## Proposed Architecture

### Component Overview
Controller -> `DownloadExpeditionListHandler` -> `IExpeditionListArchiveBlobStore` -> `ExpeditionListArchiveBlobStoreAdapter` -> `IBlobStorageService` -> `AzureBlobStorageService` (translates 404 to `FileNotFoundException`).

### Key Design Decisions

#### Decision 1: Where to translate the 404
**Options:** (a) handler catches `RequestFailedException` (breaks layering, needs SDK ref); (b) add `ExistsAsync` pre-check (extra call, race, widens the "narrow" contract); (c) Azure service throws provider-neutral `FileNotFoundException`.
**Chosen:** (c). **Rationale:** keeps layering, no contract widening, `FileNotFoundException` is an `IOException` so Reprint's existing `ThrowsAsync<IOException>` semantic (propagate) is untouched.

#### Decision 2: Error code
Reuse existing `ErrorCodes.BlobNotFound` (1805, 404). No new code.

#### Decision 3: Controller mapping
Issue says no controller change needed, but `BadRequest(response)` would return 400 despite the 404-annotated code. Make the failure branch status-aware: use the existing `BaseApiController` status mapping (`GetStatusCodeForError`) so BlobNotFound -> 404 and InvalidBlobPath -> 400. If `HandleResponse` cannot be used because the success case returns a file, `GetStatusCodeForError` is private, so on failure use `return HandleResponse(response).Result!;` (the failure branch of `HandleResponse` always yields an `ObjectResult`). Minimal fallback: `response.ErrorCode == BlobNotFound ? NotFound(response) : BadRequest(response)`.

## Implementation Guidance

### Directory / Module Structure
No new files other than tests. Edits:
- `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs` (DownloadAsync catch `RequestFailedException { Status: 404 }` -> throw `FileNotFoundException`)
- `backend/src/Anela.Heblo.Domain/Features/FileStorage/IBlobStorageService.cs` (XML doc: throws FileNotFoundException)
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs` (XML doc on DownloadAsync)
- `.../UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs` (add `NotFound()` factory)
- `.../DownloadExpeditionListHandler.cs` (catch `FileNotFoundException`)
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs` (status-aware failure)
- Tests: `DownloadExpeditionListHandlerTests.cs`, controller test if one exists, Azure service test if a test harness exists.
- `docs/processes/module-expedition-list-archive.md` (Known quirks + bump `verified_at`), then `python3 scripts/process-docs/check.py index`.

### Interfaces and Contracts
`DownloadAsync` contract: throws `FileNotFoundException` when blob absent; any other exception propagates.

### Data Flow
Missing blob: Azure 404 -> `FileNotFoundException` -> handler catch -> `Fail(BlobNotFound)` -> controller 404 with body.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Other future callers of `IBlobStorageService.DownloadAsync` unaware of new exception type | Low | Document on interface; grep shows single caller |
| Handler catch too broad | Low | Catch only `FileNotFoundException` |
| Frontend relied on 400 for any failure | Low | Generic error handling; check FE usage of the download URL during implementation |

## Specification Amendments
None.

## Prerequisites
None.
