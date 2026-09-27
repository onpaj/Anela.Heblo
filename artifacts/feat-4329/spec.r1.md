# Specification: GetExpeditionListsByDateResponse.Fail() factory method

## Summary
`GetExpeditionListsByDateHandler` currently builds its "invalid date" error response inline, duplicating knowledge (error code + params shape) that belongs on the response type. The two sibling response types in the same module (`DownloadExpeditionListResponse`, `ReprintExpeditionListResponse`) already encapsulate their failure construction behind a static `Fail()`-style factory. This change adds an equivalent static factory to `GetExpeditionListsByDateResponse` and updates the handler to call it, bringing the module back to a single consistent pattern.

## Background
`GetExpeditionListsByDateHandler.Handle` validates that `request.Date` parses as `yyyy-MM-dd`. On failure it constructs a `GetExpeditionListsByDateResponse` directly, setting `Success = false`, `ErrorCode = ErrorCodes.InvalidFormat`, and a `Params` dictionary with `Field`/`ExpectedFormat` keys. This is a mild SRP violation: the handler owns both the validation decision and the shape of the resulting error response. The other two response types in `Features/ExpeditionListArchive/UseCases/*` already expose a static factory (`DownloadExpeditionListResponse.Fail()`, `ReprintExpeditionListResponse.Fail()`) that encapsulates their own failure shape, so `GetExpeditionListsByDateResponse` is the outlier. Fixing this is purely a refactor — no behavior, API contract, or response payload changes.

## Functional Requirements

### FR-1: Add a static failure factory to `GetExpeditionListsByDateResponse`
Add a static method on `GetExpeditionListsByDateResponse` (file: `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateResponse.cs`) that returns a fully-populated failure response for the "invalid date format" case, matching the exact shape currently built inline in the handler:
- `Success = false`
- `ErrorCode = ErrorCodes.InvalidFormat`
- `Params = new Dictionary<string, string> { { "Field", "Date" }, { "ExpectedFormat", "yyyy-MM-dd" } }`

Method name: `InvalidDate()`, as proposed in the issue (this is a single-purpose factory named after the specific failure condition, not a generic `Fail()`, since this response type currently has exactly one failure case and its shape is condition-specific — this mirrors the sibling factories' spirit of "one static method encapsulates one failure shape" without inventing parameters that aren't needed yet).

**Acceptance criteria:**
- `GetExpeditionListsByDateResponse.InvalidDate()` is a `public static` method returning `GetExpeditionListsByDateResponse`.
- Calling it produces a response with `Success == false`, `ErrorCode == ErrorCodes.InvalidFormat`, `Items` empty (default), and `Params` containing exactly `{"Field": "Date", "ExpectedFormat": "yyyy-MM-dd"}`.
- No other public members of `GetExpeditionListsByDateResponse` change.

### FR-2: Update the handler to use the new factory
In `GetExpeditionListsByDateHandler.Handle` (same module, `GetExpeditionListsByDateHandler.cs` lines 21–33), replace the inline `return new GetExpeditionListsByDateResponse { ... }` block with `return GetExpeditionListsByDateResponse.InvalidDate();`.

**Acceptance criteria:**
- The handler no longer references `ErrorCodes.InvalidFormat` or constructs a `Params` dictionary directly; it delegates entirely to the factory.
- The `using Anela.Heblo.Application.Shared;` directive in the handler is removed if it becomes unused after the change (verify via build; keep it if still needed elsewhere in the file).
- Existing behavior is unchanged: for any date string that fails `DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", ...)`, the handler still returns a response with the same `Success`, `ErrorCode`, and `Params` values as before.
- The blob store (`_blobStore.ListBlobsAsync`) is still never called when the date is invalid.

## Non-Functional Requirements

### NFR-1: Performance
Not applicable — this is a same-process, same-allocation-shape refactor (one object construction moves from the handler to a static factory). No measurable performance impact.

### NFR-2: Security
Not applicable — no change to authorization, input handling, or data exposure. The validation logic (`DateOnly.TryParseExact`) and its trigger condition are untouched; only where the resulting object is *constructed* changes.

## Data Model
No data model changes. `GetExpeditionListsByDateResponse` keeps its existing shape (`Items: List<ExpeditionListItemDto>`, inherited `Success`, `ErrorCode`, `Params` from `BaseResponse`). No new fields are introduced.

## API / Interface Design
No public API contract change. This is an internal (MediatR handler + response DTO) refactor within the `ExpeditionListArchive` module. The HTTP-facing shape of `GetExpeditionListsByDateResponse` (as serialized to clients, and as reflected in the generated OpenAPI/TypeScript client) is unchanged — same properties, same values for the same inputs.

New surface added: `public static GetExpeditionListsByDateResponse GetExpeditionListsByDateResponse.InvalidDate()` — an internal C# API, not exposed over HTTP or through the OpenAPI-generated client (DTO methods are not serialized).

## Dependencies
- No new external dependencies.
- Depends on existing types: `ErrorCodes.InvalidFormat` (`Anela.Heblo.Application.Shared`), `BaseResponse.Params`/`ErrorCode`/`Success`.
- Existing test file `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` already asserts on `result.Success`, `result.ErrorCode`, and `result.Params` values (not on construction mechanics), so it should continue to pass unmodified once the handler is updated. No test changes are required by this spec, but the architect/designer should confirm during review.

## Out of Scope
- Renaming or restructuring `DownloadExpeditionListResponse.Fail()` or `ReprintExpeditionListResponse.Fail()` — they are already correct and are not touched.
- Introducing a shared base "Fail factory" abstraction (e.g. a generic interface or base class method) across all three response types. The issue and this spec address only the one outlier; broader consolidation is a separate concern not requested here.
- Any change to the `yyyy-MM-dd` validation rule itself, the error code value, or the params dictionary keys/values — the shape is preserved exactly as-is, only its ownership moves.
- Any change to `GetExpeditionListsByDateRequest` or the blob-listing / PDF-filtering logic later in the handler.

## Open Questions
None.

## Status: COMPLETE
