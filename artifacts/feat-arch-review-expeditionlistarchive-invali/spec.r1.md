# Specification: Consistent Invalid-Input Handling in `GetExpeditionListsByDateHandler`

## Summary
Fix `GetExpeditionListsByDateHandler` so that an invalid `Date` input returns a failed response with a descriptive error message, matching the pattern already used by the two sibling handlers in the `ExpeditionListArchive` module. Today the handler silently returns `200 OK` with an empty list, which is indistinguishable from a valid but empty day.

## Background
The `ExpeditionListArchive` module exposes three MediatR handlers that operate on blob-archived expedition lists:

- `GetExpeditionListsByDateHandler` — list archived expedition lists for a given date.
- `DownloadExpeditionListHandler` — download a specific archived list.
- `ReprintExpeditionListHandler` — reprint a specific archived list.

All three perform input validation. Two of them (`Download`, `Reprint`) return a failed response via a `Fail(message)` factory on the response DTO, so the controller / frontend can distinguish "bad input" from "valid input, no data". The third (`GetExpeditionListsByDate`) is inconsistent: on a malformed date string it returns an empty success response instead of failing.

Concretely (current state at `GetExpeditionListsByDateHandler.cs:21-24`):

```csharp
if (!DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _))
{
    return new GetExpeditionListsByDateResponse { Items = new List<ExpeditionListItemDto>() };
}
```

Because `GetExpeditionListsByDateResponse` inherits `Success = true` from `BaseResponse`, this leaks "invalid date" upstream as "valid empty day". The fix is to add a `Fail` factory mirroring the sibling response types and call it from the handler, so the controller can surface a `400 Bad Request` instead of a misleading `200 OK`.

This is a small consistency / correctness fix flagged by the daily arch-review routine on 2026-06-04.

## Functional Requirements

### FR-1: Add `Fail` factory to `GetExpeditionListsByDateResponse`
Introduce a static `Fail(string message)` factory on `GetExpeditionListsByDateResponse` that constructs a response with `Success = false` and `ErrorMessage = message`. The shape and signature must match the analogous factories on `DownloadExpeditionListResponse` and `ReprintExpeditionListResponse`.

**Acceptance criteria:**
- `GetExpeditionListsByDateResponse.Fail("…")` returns an instance with `Success == false`.
- The returned instance has `ErrorMessage` equal to the supplied message.
- The returned instance has `Items` left at its default value (no items populated on failure).
- Signature mirrors the sibling response classes (static method, single `string message` parameter, returns the response type).

### FR-2: Handler returns a failed response on invalid `Date`
`GetExpeditionListsByDateHandler` must return `GetExpeditionListsByDateResponse.Fail("Invalid date format. Expected yyyy-MM-dd.")` when `DateOnly.TryParseExact(request.Date, "yyyy-MM-dd", out _)` returns `false`. No blob storage call, logging beyond the existing pattern, or other side effects should occur in the invalid-input path.

**Acceptance criteria:**
- Given `request.Date = "not-a-date"`, the handler returns a response with `Success == false` and the message `"Invalid date format. Expected yyyy-MM-dd."`.
- Given `request.Date = "2026-06-04"`, behavior is unchanged from today (valid path executes, items are returned per existing logic).
- Given `request.Date = null` or empty string, the handler returns a failed response with the same message (these inputs already fail `TryParseExact`, so no extra branch is needed; just verify behavior).
- No call is made to the underlying blob storage / archive client when the date is invalid.

### FR-3: Controller surfaces failure as HTTP 400
The MVC controller that wraps this handler must map a response with `Success == false` to `400 Bad Request`, mirroring how `Download` and `Reprint` already behave. If the controller already inspects `response.Success` generically (e.g. via a shared base), no controller change is needed; otherwise the controller must be updated for parity.

**Acceptance criteria:**
- A request with an invalid date receives HTTP `400 Bad Request` with a body containing `Success: false` and the error message.
- A request with a valid date but no archived files receives HTTP `200 OK` with `Success: true` and `Items: []` (unchanged).
- The controller behavior for `GetExpeditionListsByDate` is consistent with `DownloadExpeditionList` and `ReprintExpeditionList` for the failed-response case.

### FR-4: Tests cover both branches
Unit tests for the handler must cover both the invalid-date branch and the valid-date branch. If integration / controller tests already exist for the sibling handlers, add equivalent coverage for `GetExpeditionListsByDate` so the HTTP-level behavior is also verified.

**Acceptance criteria:**
- A unit test asserts that an invalid date string produces `Success == false` and the expected error message, and does not invoke the storage dependency (verified via mock).
- A unit test asserts that a valid date string produces `Success == true` and exercises the storage call as before.
- Existing tests continue to pass after the change.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact. The change shortcuts an already-cheap branch and avoids one unnecessary storage call on invalid input.

### NFR-2: Security
No new attack surface. Returning a clear "invalid date format" message to the caller is acceptable — the format requirement is public and disclosed in the OpenAPI contract. Do not echo the raw user input back in the error message (the constant string above is sufficient).

### NFR-3: Backwards compatibility
The response shape (`Success`, `ErrorMessage`, `Items`) is unchanged. Existing successful responses are byte-identical. The only observable behavior change is:
- Previously: invalid date → `200 OK` with `{ success: true, items: [] }`.
- After: invalid date → `400 Bad Request` with `{ success: false, errorMessage: "Invalid date format. Expected yyyy-MM-dd." }`.

Any frontend consumer that was relying on the silent-empty behavior must be updated, but per the brief the controller already propagates the response as-is, so this is in line with the sibling endpoints and considered a bug fix rather than a breaking contract change.

### NFR-4: Consistency
The three handlers in `ExpeditionListArchive` must follow the same invalid-input pattern after this change: validate → return `Response.Fail("…")` on bad input.

## Data Model
No data model changes. Only an added static factory method on the existing `GetExpeditionListsByDateResponse` DTO. Per the project rule, response DTOs remain classes (not records), so the factory simply constructs a new instance with `Success`, `ErrorMessage`, and default `Items`.

## API / Interface Design

**Endpoint (unchanged path):**
- `GET /api/expedition-list-archive/by-date?date={yyyy-MM-dd}` (or whatever the existing route is — no path change).

**Response on success (unchanged):**
```json
{
  "success": true,
  "errorMessage": null,
  "items": [ /* … */ ]
}
```

**Response on invalid date (new behavior, HTTP 400):**
```json
{
  "success": false,
  "errorMessage": "Invalid date format. Expected yyyy-MM-dd.",
  "items": []
}
```

**MediatR contract (unchanged shapes, new factory):**
```csharp
public class GetExpeditionListsByDateResponse : BaseResponse
{
    public List<ExpeditionListItemDto> Items { get; set; } = new();

    public static GetExpeditionListsByDateResponse Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
```

## Dependencies
- Existing `BaseResponse` (provides `Success` / `ErrorMessage`).
- Existing MediatR handler infrastructure and DI registration — no new bindings.
- Existing `ExpeditionListArchive` controller — possibly minor adjustment per FR-3 if it does not already map `Success == false` to `400`.
- No new NuGet packages, no new external services.

## Out of Scope
- Refactoring `BaseResponse` or introducing a shared `Result<T>` abstraction across the codebase.
- Changing the date format accepted by the endpoint (still `yyyy-MM-dd`).
- Localizing the error message.
- Frontend UX changes beyond what the new `400` response surfaces by default (e.g. dedicated inline validation messaging in the UI).
- Reviewing other modules for similar silent-success-on-invalid-input patterns.

## Open Questions
None.

## Status: COMPLETE