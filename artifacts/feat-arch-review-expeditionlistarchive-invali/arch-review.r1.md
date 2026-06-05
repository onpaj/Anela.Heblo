# Architecture Review: Consistent Invalid-Input Handling in `GetExpeditionListsByDateHandler`

## Skip Design: true

Backend-only change. No new UI components, screens, or visual decisions. The frontend already consumes `BaseResponse.Success` semantics; surfaced HTTP `400` rendering is governed by existing global error UX, not by this feature.

## Architectural Fit Assessment

The fix is conceptually trivial but sits on top of a non-trivial architectural tension that the spec does not surface. The codebase has **two competing error-response patterns**:

| Pattern | Where it lives | Count |
|---|---|---|
| **Canonical (`ErrorCode` + `Params` + `HandleResponse<T>`)** | `BaseResponse` (shared) → routed through `BaseApiController.HandleResponse<T>()`, which maps `ErrorCodes` to HTTP status via `[HttpStatusCode]` attributes | 35+ handlers across `Catalog`, `Manufacture`, `Logistics`, `Purchase`, `Invoices`, etc. |
| **Local outlier (`ErrorMessage` string + `Fail(string)` factory + hand-rolled `BadRequest`)** | Added ad-hoc to the two sibling responses in `ExpeditionListArchive` | 2 handlers — **both inside the module this fix targets** |

`BaseResponse` has **no `ErrorMessage` property** (`backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs:1-21`); the sibling responses added their own local `ErrorMessage` string and a manual `Fail(string)` factory. The `ExpeditionListArchiveController` does not call `HandleResponse<T>` — it hard-codes `BadRequest(response.ErrorMessage)` for Download and `BadRequest(response)` for Reprint (`ExpeditionListArchiveController.cs:42-66`).

So the spec's brief is *locally* correct ("make handler #3 consistent with siblings #1 and #2"), but *globally* it propagates an outlier pattern further. The architecturally correct fix is to align `GetExpeditionListsByDate` with the **canonical** pattern using the existing `ErrorCodes.InvalidFormat` enum value (already declared in `ErrorCodes.cs:18-19` with `[HttpStatusCode(HttpStatusCode.BadRequest)]`), and route through `HandleResponse<T>`. This costs zero new types and zero new properties — everything required already exists.

The brief's "mirror the siblings" suggestion is functionally fine, but it doubles down on a divergence that the next arch-review pass will flag.

## Proposed Architecture

### Component Overview

```
[Client]
   │  GET /api/expedition-list-archive/{date}
   ▼
[ExpeditionListArchiveController.GetByDate]
   │  (return HandleResponse(response) instead of Ok(response))
   ▼
[MediatR pipeline]
   ▼
[GetExpeditionListsByDateHandler]
   ├─ Invalid date? → response.Success=false, ErrorCode=InvalidFormat, Params={ "Date": <token> }
   └─ Valid date  → IBlobStorageService.ListBlobsAsync → map → Items
   ▼
[BaseApiController.HandleResponse<T>]
   └─ Reads [HttpStatusCode] from ErrorCodes.InvalidFormat → 400 BadRequest
```

### Key Design Decisions

#### Decision 1: Adopt the canonical `ErrorCode`-based pattern, not the local `Fail(string)` pattern
**Options considered:**
- **A. Mirror siblings.** Add `Fail(string)` to `GetExpeditionListsByDateResponse` plus a local `ErrorMessage` property; controller becomes `if (!response.Success) BadRequest(response); else Ok(response);`. Matches the brief literally.
- **B. Use the canonical pattern.** Set `response.Success = false`, `response.ErrorCode = ErrorCodes.InvalidFormat`, optionally `Params`; controller becomes `return HandleResponse(response);`. Aligns with 35+ other handlers; requires no new properties or factories.
- **C. Mirror siblings now and file a follow-up to migrate all three to canonical.** Two-step.

**Chosen approach:** **B** — go straight to the canonical pattern for the new handler.

**Rationale:** The "missing" piece the spec wants to introduce (`ErrorMessage` + `Fail` factory) is already an outlier in this codebase. Option A locks in three outliers instead of two. Option B reuses what exists, gives `HandleResponse<T>` automatic HTTP mapping via the `[HttpStatusCode]` attribute on `ErrorCodes.InvalidFormat`, and produces a response shape (`Success`, `ErrorCode`, `Params`) that downstream frontend code already understands for every other endpoint. The sibling migration to canonical can be a follow-up (out of this spec's scope, per the brief), but we should not extend the divergence.

If the user explicitly rejects this and wants strict mirror-the-siblings consistency, fall back to A — the change is small either way, and the spec's wording supports A.

#### Decision 2: Do **not** add an `ErrorMessage` string to `GetExpeditionListsByDateResponse`
**Rationale:** `BaseResponse` already encodes failure via `Success`/`ErrorCode`/`Params`. Adding another `ErrorMessage` field would duplicate state that the canonical pattern already covers and would conflict with the `FullError()` helper on `BaseResponse`. Localization is an open project concern (see spec §"Out of scope: localizing the error message"); `Params` is the existing slot for it. Keep the contract one-way.

#### Decision 3: Switch `GetByDate` controller action to `HandleResponse<T>`
**Rationale:** `Ok(response)` unconditionally returns 200, which is exactly the bug. `HandleResponse(response)` is one line, already implemented (`BaseApiController.cs:29-60`), and used by every canonical handler. Do not introduce ad-hoc `if (!response.Success) BadRequest(...)` here — that would replicate the sibling outliers.

#### Decision 4: Do not echo `request.Date` into `Params`
**Rationale:** NFR-2 in the spec says don't echo raw input. Use a constant identifier (e.g. `Params = new() { { "Field", "Date" }, { "ExpectedFormat", "yyyy-MM-dd" } }`) or omit `Params` entirely. The frontend's existing error renderer can fall back to a generic message keyed on `ErrorCode = InvalidFormat`.

## Implementation Guidance

### Directory / Module Structure
No new files. Touch:

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — change the early return.
- `backend/src/Anela.Heblo.API/Controllers/ExpeditionListArchiveController.cs:33-39` — change `Ok(response)` to `HandleResponse(response)`.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` — add invalid-date test asserting `Success == false`, `ErrorCode == ErrorCodes.InvalidFormat`, and `_blobStorageServiceMock.Verify(... Times.Never)`.

Do **not** touch `GetExpeditionListsByDateResponse.cs`. Do **not** touch `BaseResponse`.

### Interfaces and Contracts

**Handler invalid-date branch:**
```csharp
if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
{
    return new GetExpeditionListsByDateResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.InvalidFormat,
        Params = new Dictionary<string, string> { { "Field", "Date" }, { "ExpectedFormat", "yyyy-MM-dd" } }
    };
}
```

**Controller:**
```csharp
[HttpGet("{date}")]
public async Task<ActionResult<GetExpeditionListsByDateResponse>> GetByDate(string date)
{
    var request = new GetExpeditionListsByDateRequest { Date = date };
    var response = await _mediator.Send(request);
    return HandleResponse(response);
}
```

**Response JSON on invalid date (HTTP 400):**
```json
{
  "success": false,
  "errorCode": "InvalidFormat",
  "params": { "Field": "Date", "ExpectedFormat": "yyyy-MM-dd" },
  "items": []
}
```

Note: the spec's literal payload (`errorMessage: "Invalid date format. Expected yyyy-MM-dd."`) is **not** produced by this design. See Specification Amendments below.

### Data Flow

1. Controller receives `GET /api/expedition-list-archive/{date}`.
2. MediatR dispatches `GetExpeditionListsByDateRequest`.
3. Handler runs `DateOnly.TryParseExact`.
   - **Fail:** populates `Success=false`, `ErrorCode=InvalidFormat`, `Params`. Returns. **No blob call.**
   - **Pass:** calls `IBlobStorageService.ListBlobsAsync`, maps results to `Items`, returns success.
4. Controller calls `HandleResponse(response)`. `[HttpStatusCode(BadRequest)]` on `InvalidFormat` → HTTP 400 with body. Success path → HTTP 200.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Spec says "mirror siblings" but this review proposes canonical — implementer may proceed against this guidance without reading | Medium | Call this out explicitly in PR description; reference `ErrorCodes.cs:18-19` and `HandleResponse<T>` in `BaseApiController.cs:29-60` |
| Frontend consumers that previously got `200 OK + empty items` may not handle the new `400` gracefully | Medium | Spec NFR-3 already calls this out as an intentional behavior change. Smoke-check the `ExpeditionListArchive` frontend page for an invalid date in the URL after deploy. |
| TypeScript OpenAPI client regen needed when response shape changes | Low | Triggered automatically on build per `docs/development/api-client-generation.md`. The shape is technically unchanged (only `Success`/`ErrorCode`/`Params` are populated differently), so no client breakage expected. |
| Siblings still use outlier `Fail(string)` pattern after this fix | Low | Out of scope per spec. Optional follow-up issue to migrate `Download`/`Reprint` to canonical. |
| Test for invalid date asserts on a literal error message string (spec FR-2) that this design does not produce | Medium | Amend tests to assert on `ErrorCode == ErrorCodes.InvalidFormat` instead. See Specification Amendments. |

## Specification Amendments

The spec was written assuming the siblings' pattern is canonical. After verifying the codebase, the following amendments are needed:

1. **FR-1 (Fail factory):** Replace with: "Do not add a `Fail` factory to `GetExpeditionListsByDateResponse`. Use the canonical `Success` / `ErrorCode` / `Params` fields on `BaseResponse` instead. No DTO changes are required."
2. **FR-2 (Handler returns failed response):** Replace the error message constant with `ErrorCode = ErrorCodes.InvalidFormat` and `Params = { "Field": "Date", "ExpectedFormat": "yyyy-MM-dd" }`. The acceptance criterion "the message `Invalid date format. Expected yyyy-MM-dd.`" should be removed; the equivalent assertion is `response.ErrorCode == ErrorCodes.InvalidFormat`.
3. **FR-3 (Controller surfaces failure as 400):** Tighten to: "Change `ExpeditionListArchiveController.GetByDate` from `return Ok(response);` to `return HandleResponse(response);`. The existing `[HttpStatusCode(BadRequest)]` on `ErrorCodes.InvalidFormat` provides the 400 mapping automatically."
4. **FR-4 (Tests):** Update the unit-test acceptance criterion to assert `response.ErrorCode == ErrorCodes.InvalidFormat` and that `_blobStorageServiceMock` was never invoked. Drop the literal-message assertion.
5. **API/Interface Design section:** Replace the example invalid-date JSON body with the `errorCode` / `params` form shown above.
6. **Open Questions:** Add one — "Should the existing sibling handlers `DownloadExpeditionListHandler` and `ReprintExpeditionListHandler` also be migrated from their local `Fail(string)` / `ErrorMessage` pattern to the canonical `ErrorCode` pattern?" Recommendation: yes, but as a separate spec; out of scope here.

If the user prefers strict spec adherence (mirror siblings), revert FR-1..FR-5 amendments and implement exactly as the spec describes. The architectural concern remains, but the change becomes a one-property + one-factory addition rather than a controller refactor.

## Prerequisites

None. All required types exist:

- `ErrorCodes.InvalidFormat` — `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:18-19` with `[HttpStatusCode(BadRequest)]`.
- `BaseApiController.HandleResponse<T>` — `backend/src/Anela.Heblo.API/Controllers/BaseApiController.cs:29-60`.
- `BaseResponse.Success` / `ErrorCode` / `Params` — `backend/src/Anela.Heblo.Application/Shared/BaseResponse.cs:6-21`.

No migrations, no config, no DI changes, no new NuGet packages. Implementation is a single-PR change: handler, controller, test.