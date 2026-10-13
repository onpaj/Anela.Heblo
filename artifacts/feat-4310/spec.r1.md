# Specification: FileStorage DownloadFromUrlResponse nullability fix

## Summary
`DownloadFromUrlResponse` declares `BlobUrl`, `BlobName`, and `ContainerName` as non-nullable `string` (initialised with the null-forgiving `= null!`), but `DownloadFromUrlHandler.Failure()` never populates them on any of its three failure paths. Any consumer that reads those properties off a failed response gets a `NullReferenceException` at runtime, and the OpenAPI-generated TypeScript client currently advertises a false, non-nullable contract. This is a minimal, surgical bug fix: make the three properties nullable so the type system reflects actual runtime behavior.

## Background
`DownloadFromUrlHandler.Handle()` (`backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`) has three `catch` blocks — timeout (`OperationCanceledException`, not caller-cancelled), HTTP error (`HttpRequestException`), and any other `Exception` — that all return `Failure(...)`. `Failure()` (lines 135–153) builds a `DownloadFromUrlResponse` setting only `Success = false`, `ErrorCode`, and `Params`; it never sets `BlobUrl`, `BlobName`, or `ContainerName`. Because `DownloadFromUrlResponse.cs` (lines 5–13) declares those three as non-nullable `string` with `= null!`, the compiler's nullable-reference-type warning is suppressed, hiding the defect at compile time. `BaseResponse.Success` is the documented way for callers to gate on outcome, but nothing prevents a consumer (current or future — logging, an orchestrator, a UI reading the OpenAPI-generated client) from reading these fields unconditionally and hitting a `NullReferenceException`.

## Functional Requirements

### FR-1: Make failure-path properties nullable
`DownloadFromUrlResponse.BlobUrl`, `BlobName`, and `ContainerName` must be declared as nullable `string?` instead of non-nullable `string` with `= null!`.

**Acceptance criteria:**
- `DownloadFromUrlResponse.cs` declares `BlobUrl`, `BlobName`, and `ContainerName` as `string?` with no `= null!` initializer.
- `FileSizeBytes` (`long`) is unchanged.
- The project builds cleanly with nullable reference types enabled (no new CS8618/CS8601-class warnings introduced by this change, and no remaining `= null!` on these three members).

### FR-2: Preserve success-path behavior
On the success path (`Handle()`'s `try` block, lines 72–79), `BlobUrl`, `BlobName`, and `ContainerName` continue to be set to their real (non-null) values exactly as today. No behavioral change on success.

**Acceptance criteria:**
- Existing success-path handler tests continue to pass unmodified.
- `BlobUrl`, `BlobName`, `ContainerName` are non-null in the response whenever `Success == true`.

### FR-3: Failure-path properties are null, not defaulted to placeholder strings
`Failure()` continues to omit `BlobUrl`, `BlobName`, and `ContainerName` (they remain unset / `null`) rather than being backfilled with empty strings or placeholder values. Consumers must gate on `Success` before reading them.

**Acceptance criteria:**
- A handler test that triggers each of the three failure paths (timeout, `HttpRequestException`, generic `Exception`) asserts `Success == false` and that `BlobUrl`, `BlobName`, `ContainerName` are `null`.
- No `NullReferenceException` occurs when a test explicitly reads these properties on a failure response.

### FR-4: Regenerate the TypeScript API client
The OpenAPI-generated TypeScript client (`frontend/src/api/generated/api-client.ts`) must be regenerated so the corresponding fields are typed as optional/nullable (`string | undefined`), matching the corrected backend contract, per `docs/development/api-client-generation.md`.

**Acceptance criteria:**
- `api-client.ts`'s `DownloadFromUrlResponse` (or equivalent generated interface) types `blobUrl`, `blobName`, `containerName` as optional/nullable string.
- No hand-editing of the generated file — it is produced by the standard generation step.

## Non-Functional Requirements

### NFR-1: No behavior change beyond nullability
This is a type-correctness fix only. It must not alter control flow, error codes, logging, retry behavior, or the shape of `Params` in `Failure()`.

### NFR-2: Backward compatibility
Existing callers that already gate on `response.Success` before reading `BlobUrl`/`BlobName`/`ContainerName` are unaffected. Any caller that does not gate on `Success` and dereferences these fields directly (e.g. via `.Length`, string interpolation without a null check) must be identified and either guarded or explicitly accept nullable access (see Dependencies).

## Data Model
No new entities. `DownloadFromUrlResponse : BaseResponse` — three fields change from `string` (`= null!`) to `string?`:
- `BlobUrl: string?`
- `BlobName: string?`
- `ContainerName: string?`
- `FileSizeBytes: long` (unchanged)

## API / Interface Design
No endpoint, route, or request/response shape change beyond the nullability of the three fields on `DownloadFromUrlResponse`, returned by `POST` `FileStorageController.DownloadFromUrl` (`backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs`). JSON serialization already omits/nulls unset reference-type properties on failure today; only the compile-time contract (C# type, and the OpenAPI/TypeScript client it generates) changes to state that truthfully.

## Dependencies
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs` — the type to change.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs` — confirms `Failure()` need not change (it already omits these fields); the success path already sets non-null values.
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/DownloadFromUrlHandlerTests.cs` — existing test coverage to extend for FR-3.
- `backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs` — verified it only returns the response object; it does not dereference the three properties itself, so no controller code change is required.
- Frontend/OpenAPI TypeScript client generation (`docs/development/api-client-generation.md`) — regeneration step for FR-4.

## Out of Scope
- Any redesign of `DownloadFromUrlHandler`'s error handling, retry/resilience logic, or `ErrorCodes`.
- Changes to `BaseResponse` or other response types beyond `DownloadFromUrlResponse`.
- Auditing every consumer across the codebase for unrelated nullability issues; only `DownloadFromUrlResponse` consumers reachable from this handler are in scope.

## Open Questions
None.

## Status: COMPLETE
