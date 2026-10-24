# Implementation: controller-status-mapping

## What was implemented
`ExpeditionListArchiveController.Download` failure branch now uses `HandleResponse(response).Result!` (BlobNotFound -> 404, InvalidBlobPath -> 400 via ErrorCode HTTP attribute). `Success` with null stream still returns `BadRequest(response)`.

## Files
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs`
- `backend/test/Anela.Heblo.Tests/Controllers/ExpeditionListArchiveControllerTests.cs` (new, 4 tests)

## Tests
`dotnet test --filter FullyQualifiedName~ExpeditionListArchive`: 43 passed. dotnet format --verify-no-changes clean on touched files.
Frontend: download URL is built by `getExpeditionListDownloadUrl` and used as a plain link; no FE error-handling depends on 400, no FE change.

## Status
DONE
