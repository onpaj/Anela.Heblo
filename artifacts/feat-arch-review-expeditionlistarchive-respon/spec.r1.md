# Specification: Align ExpeditionListArchive responses with BaseResponse error contract

## Summary
`DownloadExpeditionListResponse` and `ReprintExpeditionListResponse` carry an ad-hoc `string? ErrorMessage` property that bypasses the structured error contract defined on `BaseResponse` (`ErrorCode`, `Params`, `FullError()`). This work removes the parallel error channel, adopts the typed `ErrorCode` path used by every other module, and propagates the change through the controller, the manual frontend hook, and the affected tests.

## Background
All API responses in this codebase inherit from `Anela.Heblo.Application.Shared.BaseResponse`, which exposes:

- `Success` — bool flag
- `ErrorCode` — typed `ErrorCodes` enum (HTTP-status-mapped via `[HttpStatusCode]`)
- `Params` — localisation parameter dictionary
- `FullError()` — combined serialisation helper
- Constructors that accept `(ErrorCodes, Dictionary<string, string>?)` or `(Exception)`

Every module except `ExpeditionListArchive` returns failures through that channel. The two response classes under review (`DownloadExpeditionListResponse`, `ReprintExpeditionListResponse`) declare a freeform `string? ErrorMessage` instead, and their `Fail(string message)` factories produce responses whose `ErrorCode` is `null`. The frontend hook `useExpeditionListArchive.ts` currently reads `errorData?.errorMessage` directly (lines 116-118 and 142-145), and the controller's `Download` action returns `BadRequest(response.ErrorMessage)` instead of `BadRequest(response)`.

Consequences:

- Generic API error handlers that look at `errorCode` see `null` for these endpoints and miss the error.
- The serialised error shape diverges from every other endpoint, polluting the OpenAPI contract.
- Two parallel error mechanisms increase cognitive overhead.

This was filed by the daily arch-review routine on 2026-06-04.

## Functional Requirements

### FR-1: Remove the ad-hoc `ErrorMessage` property from both response classes
The `string? ErrorMessage` property must be removed from both `DownloadExpeditionListResponse` (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListResponse.cs`) and `ReprintExpeditionListResponse` (`backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/ReprintExpeditionList/ReprintExpeditionListResponse.cs`). No replacement string field is added; the inherited `BaseResponse.ErrorCode` and `BaseResponse.Params` carry error information.

**Acceptance criteria:**
- Neither response class declares an `ErrorMessage` property.
- Compilation succeeds with no consumers still reading `response.ErrorMessage`.
- The classes remain `public class` (not records) per project rule on DTOs.

### FR-2: Introduce `ErrorCodes.InvalidBlobPath` and use it in the `Fail` factories
Add a new enum member `InvalidBlobPath` to `Anela.Heblo.Application.Shared.ErrorCodes` annotated with `[HttpStatusCode(HttpStatusCode.BadRequest)]`. Place it inside the FileStorage module range (18XX) — the next free value is `1808`, immediately after `UnsupportedFileType = 1807`. Re-implement both `Fail` factories to set `Success = false` and `ErrorCode = ErrorCodes.InvalidBlobPath`, dropping the `string message` parameter entirely.

**Acceptance criteria:**
- `ErrorCodes.InvalidBlobPath = 1808` is defined with `[HttpStatusCode(HttpStatusCode.BadRequest)]`.
- `DownloadExpeditionListResponse.Fail()` and `ReprintExpeditionListResponse.Fail()` are parameterless and produce responses with `Success = false`, `ErrorCode = ErrorCodes.InvalidBlobPath`, `Params = null`.
- Existing callers (`DownloadExpeditionListHandler` line 22, `ReprintExpeditionListHandler` line 25) compile against the new signature.

### FR-3: Update handlers to call the parameterless `Fail()`
Both handlers currently pass a hard-coded `"Invalid blob path."` string to `Fail`. After FR-2, the handlers call `Fail()` with no arguments. The handler logic itself is otherwise unchanged.

**Acceptance criteria:**
- `DownloadExpeditionListHandler.Handle` returns `DownloadExpeditionListResponse.Fail()` when `BlobPathValidator.IsValid(request.BlobPath)` returns `false`.
- `ReprintExpeditionListHandler.Handle` returns `ReprintExpeditionListResponse.Fail()` in the same condition.
- No human-readable error string is hard-coded anywhere in either handler.

### FR-4: Fix `ExpeditionListArchiveController.Download` to return the response object
The `Download` action (line 49) currently returns `BadRequest(response.ErrorMessage)`. After FR-1, that property no longer exists. Change the action to return `BadRequest(response)` so the structured error object reaches the client (matching the existing pattern used by the `Reprint` action on line 62).

**Acceptance criteria:**
- `Download` returns `BadRequest(response)` on failure (the same shape `Reprint` already uses).
- Response body on failure is a JSON object containing `success: false`, `errorCode: "InvalidBlobPath"` (or numeric `1808` depending on serialisation), and `params: null`.
- HTTP status remains `400 Bad Request`.

### FR-5: Update the frontend hook to use the typed error contract
`frontend/src/api/hooks/useExpeditionListArchive.ts` currently:

- Declares `ReprintExpeditionListResponse` with `errorMessage: string | null` (lines 29-32).
- Reads `errorData?.errorMessage` in both `useReprintExpeditionList` (line 118) and `useRunExpeditionListPrintFix` (line 145).

Update the hook to consume the new structured shape:

- Replace `errorMessage: string | null` with the standard shared shape: `success: boolean`, `errorCode: string | null`, `params: Record<string, string> | null`.
- Replace `errorData?.errorMessage` with a localisation lookup keyed by `errorData?.errorCode`. The hook may surface a generic fallback string (e.g. `"HTTP error! status: ${response.status}"`) when `errorCode` is absent.
- The exact i18n key naming convention should match whatever existing modules use; if no existing convention covers this, fall back to throwing `new Error(errorData?.errorCode ?? "HTTP error...")` and let the caller translate. (See Open Questions.)

**Acceptance criteria:**
- The `ReprintExpeditionListResponse` TypeScript interface no longer contains `errorMessage`.
- Neither `useReprintExpeditionList` nor `useRunExpeditionListPrintFix` references `errorMessage`.
- `npm run build` and `npm run lint` succeed.
- Manual reprint of an invalid blob path surfaces an error to the user (verified by existing UI flow on `ExpeditionListArchivePage`).

### FR-6: Update existing tests to assert the new contract
Tests under `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/` currently assert `result.Success == false` and `result.Stream == null` (see `DownloadExpeditionListHandlerTests.cs` line 60-61, `ReprintExpeditionListHandlerTests.cs` line 58). Extend these assertions to verify the new typed error.

**Acceptance criteria:**
- `Handle_InvalidBlobPath_ReturnsFailure` (Download) additionally asserts `result.ErrorCode == ErrorCodes.InvalidBlobPath`.
- `Handle_InvalidBlobPath_ReturnsFailureWithoutCallingBlob` (Reprint) additionally asserts `result.ErrorCode == ErrorCodes.InvalidBlobPath`.
- No test references `result.ErrorMessage`.
- All tests in the `ExpeditionListArchive` test folder pass via `dotnet test`.

## Non-Functional Requirements

### NFR-1: Backwards-compatibility scope
This change is a breaking API contract change: the response JSON shape loses `errorMessage` and gains `errorCode` + `params` on the affected endpoints. The frontend is updated in lockstep (FR-5). No external consumers depend on this API (solo developer + AI-reviewed PRs, internal-only workspace tool), so a versioning step is not required, but the change must ship as one atomic PR so the backend and frontend never diverge.

### NFR-2: OpenAPI client consistency
After the change, the auto-generated TypeScript client (`docs/development/api-client-generation.md`) for these two response types should match the shape produced for every other module's responses. The build step that regenerates the client must succeed and produce no `errorMessage` field on these types.

### NFR-3: No regression in error-path behaviour
Invalid blob paths must still:
- Be rejected before any blob-storage call is made (currently enforced by `BlobPathValidator.IsValid`).
- Result in HTTP 400 from both endpoints.
- Leave no temp files on disk in the Reprint flow.

### NFR-4: Code style
- Nullable reference types stay enabled.
- DTOs remain classes, not records (project rule).
- `dotnet format` clean on changed files.
- No new analyzer warnings.

## Data Model
No persistence schema changes. The only data-shape changes are on the API response DTOs:

**Before** (`DownloadExpeditionListResponse`):
```
{
  success: bool,
  errorMessage: string?,   // removed
  stream: Stream?,
  contentType: string,
  fileName: string,
  // inherited but unused on failure:
  errorCode: ErrorCodes?,  // always null
  params: Dictionary?      // always null
}
```

**After**:
```
{
  success: bool,
  stream: Stream?,
  contentType: string,
  fileName: string,
  errorCode: ErrorCodes?,  // set to InvalidBlobPath on failure
  params: Dictionary?      // null for this error
}
```

`ReprintExpeditionListResponse` mirrors the same removal/addition pattern, minus the stream/file fields.

`ErrorCodes` gains one new value: `InvalidBlobPath = 1808` (FileStorage module range, BadRequest).

## API / Interface Design

### Backend endpoints (unchanged routes, changed failure body)

- `GET /api/expedition-list-archive/download/{*blobPath}`
  - Success: `200 OK`, returns the PDF file stream as `application/pdf` (no change).
  - Failure (invalid blob path): `400 Bad Request` with JSON body `{ "success": false, "errorCode": 1808, "params": null }` instead of plain text `"Invalid blob path."`.
- `POST /api/expedition-list-archive/reprint`
  - Success: `200 OK`, `{ "success": true, "errorCode": null, "params": null }`.
  - Failure (invalid blob path): `400 Bad Request`, `{ "success": false, "errorCode": 1808, "params": null }`.

### Frontend interface

- `ReprintExpeditionListResponse` TypeScript interface updated as described in FR-5.
- Error display in `ExpeditionListArchivePage` (and the page test) keeps working — the user still sees an error indication when reprint fails, but the source of the displayed string is the i18n layer keyed by `errorCode`, not a server-supplied string. If the current page does not localise error codes, the hook still throws an `Error` (so React-Query's `error` state stays populated) — the message text simply changes from `"Invalid blob path."` to whatever the hook now produces.

### Internal API (handler signatures)

- `DownloadExpeditionListResponse.Fail()` — parameterless.
- `ReprintExpeditionListResponse.Fail()` — parameterless.

## Dependencies
- `Anela.Heblo.Application.Shared.BaseResponse` (already exists, unchanged).
- `Anela.Heblo.Application.Shared.ErrorCodes` enum (adds one value).
- `BlobPathValidator` (existing, unchanged).
- `frontend/src/api/hooks/useExpeditionListArchive.ts` (must be updated in the same PR).
- OpenAPI client regeneration step (`docs/development/api-client-generation.md`) — runs on backend build.

## Out of Scope
- Refactoring `useRunExpeditionListPrintFix` beyond the minimum needed to drop `errorMessage`. Its underlying endpoint (`/api/expedition-list/run-fix`) lives on a different controller and may or may not follow the same pattern; only the hook's error-reading code is touched here.
- Adding i18n entries for the new `InvalidBlobPath` error code (project may already have a convention; if not, the hook falls back to a generic message — see Open Questions).
- Renaming or reorganising the `ErrorCodes` enum module ranges.
- Touching `GetExpeditionDatesResponse` or `GetExpeditionListsByDateResponse` — they already inherit `BaseResponse` cleanly and have no `ErrorMessage` property.
- Changing the controller routes or HTTP verbs.
- Removing or altering the existing `BlobPathValidator` rules.
- Localising the error on the server side.

## Open Questions
None.

## Status: COMPLETE
