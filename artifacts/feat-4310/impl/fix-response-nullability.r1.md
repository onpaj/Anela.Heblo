# Implementation: fix-response-nullability

## What was implemented

`DownloadFromUrlResponse.BlobUrl`, `.BlobName`, and `.ContainerName` were declared
as non-nullable `string` with the null-forgiving `= null!` initializer, but the
handler's `Failure()` factory never sets them, so they are actually `null` at
runtime on every failure path (timeout, HTTP error, unexpected exception). This
suppressed the compiler's nullable-reference warning and created a false API
contract (including in the generated TypeScript client). Changed all three
properties to nullable `string?` to match actual runtime behavior.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs` — `BlobUrl`, `BlobName`, `ContainerName` changed from `string` (`= null!`) to `string?`.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` — added `Assert.Null(result.BlobUrl/BlobName/ContainerName)` assertions to the four existing failure-path tests (`Handle_RetryExhausted_ReturnsFailure_With_Cause_RetryExhausted`, `Handle_HardHttpStatus_ReturnsFailure_With_Cause_HttpStatus`, `Handle_InnerTimeout_ReturnsFailure_With_Cause_Timeout`, `Handle_UnexpectedException_ReturnsFileDownloadFailed`), covering all three `catch` blocks in `DownloadFromUrlHandler.Handle()`.

## Tests

- `DownloadFromUrlHandlerTests` (14 tests) — all pass, including the 4 tests newly extended with null-property assertions on the failure paths.
- Full `FileStorage`-scoped test suite (123 tests: `DownloadFromUrlHandlerTests`, `FileStorageControllerTests`, `AzureBlobStorageServiceTests`, `MockBlobStorageServiceTests`, `FileStorageValidationPipelineTests`, `DownloadFromUrlRequestValidatorTests`) — all pass.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~FileStorage"
dotnet build
```

Both succeed with 0 errors. No new nullable-reference warnings were introduced
anywhere in the solution — the only other consumer of these properties
(`ProductExportDownloadJob.cs`) already null-coalesces `BlobUrl` (`?? string.Empty`)
and only uses `BlobName`/`BlobUrl` as structured-logging arguments, both of which
are unaffected by the nullability change.

## Notes

`dotnet format` produced no additional diff beyond the hand-written edits — the
change was already format-clean. No deviations from the task context; implemented
exactly per the provided steps.

## PR Summary

Fixed a latent `NullReferenceException` risk in `DownloadFromUrlResponse`: the
three blob-related properties (`BlobUrl`, `BlobName`, `ContainerName`) were typed
as non-nullable `string` via the null-forgiving `= null!` initializer, but the
handler's failure path never sets them, so any caller reading them after a failed
download without first checking `Success` would crash. This also caused the
OpenAPI-generated TypeScript client to advertise a false non-nullable contract.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs` — `BlobUrl`, `BlobName`, `ContainerName` changed to `string?`
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` — added null-property assertions to the four failure-path tests

## Status
DONE
