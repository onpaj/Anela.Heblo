# Specification: PackingMaterials daily-consumption date validation via FluentValidation

## Summary
`GET /api/packing-materials/consumption` validates and parses its `date` query parameter inside `PackingMaterialsController`. Move validation into a FluentValidation validator run by the MediatR `ValidationBehavior`, and parsing into the handler, matching the sibling `GetConsumptionHistory` use case.

## Background
`development_guidelines.md` requires business logic in MediatR handlers, not controllers. `PackingMaterialsController.GetDailyConsumptionBreakdown` (lines ~157-162) calls `DateOnly.TryParseExact(date, "yyyy-MM-dd", ...)` and returns `BadRequest(new { error = ... })`. `GetConsumptionHistoryRequest` (string dates) + `GetConsumptionHistoryRequestValidator` + `ValidationBehavior` registration in `PackingMaterialsModule` is the established pattern. Validation is currently invisible to handler-level tests and non-HTTP callers.

## Functional Requirements

### FR-1: Request carries date as string
`GetDailyConsumptionBreakdownRequest.Date` changes from `DateOnly` to `string?`; `GroupBy` unchanged.
**Acceptance criteria:**
- Request class compiles with `string? Date`; remains a class (not a record).

### FR-2: Validator
Add `GetDailyConsumptionBreakdownRequestValidator : AbstractValidator<GetDailyConsumptionBreakdownRequest>` in the use-case folder. The date is required (null/empty/whitespace invalid, preserving current 400 behaviour) and must match `yyyy-MM-dd` via `DateOnly.TryParseExact` with `CultureInfo.InvariantCulture`.
**Acceptance criteria:**
- `"2025-06-15"` valid; `null`, `""`, `"  "`, `"15.06.2025"`, `"2025-6-5"`, `"2025-02-30"`, `"abc"` invalid with an error on `Date`.
- Error message states "yyyy-MM-dd".

### FR-3: Pipeline registration
In `PackingMaterialsModule`, register `IValidator<GetDailyConsumptionBreakdownRequest>` and `IPipelineBehavior<GetDailyConsumptionBreakdownRequest, GetDailyConsumptionBreakdownResponse>` -> `ValidationBehavior<,>`, same style as `GetConsumptionHistoryRequest`.
**Acceptance criteria:**
- Sending an invalid request through the MediatR pipeline throws `FluentValidation.ValidationException`, mapped to HTTP 400 ProblemDetails by the existing `ValidationExceptionHandler`.

### FR-4: Handler parses
The handler parses `request.Date` with `DateOnly.ParseExact`/`TryParseExact` ("yyyy-MM-dd", invariant) and uses the resulting `DateOnly` for repository lookup and `Response.Date`. Response `Date` stays `DateOnly`. If parsing fails (validator bypassed), return `Success = false` with an error rather than throwing an unhandled parse error.
**Acceptance criteria:**
- Existing handler tests pass after updating requests to string dates (`"2025-06-15"`).
- Handler given an unparseable date returns a failure response (does not call repository).

### FR-5: Controller thin
Controller action binds `[FromQuery] GetDailyConsumptionBreakdownRequest request`, removes manual parsing and unused `using`s (only if now unused), and returns `response.Success ? Ok(response) : BadRequest(new { error = response.Error })`.
**Acceptance criteria:**
- No `DateOnly.TryParseExact` in the controller.
- Query contract (`date`, `groupBy`) unchanged, so the generated OpenAPI/TS client signature `packingMaterials_GetDailyConsumptionBreakdown(date, groupBy)` is unchanged.

## Non-Functional Requirements
### NFR-1: Performance
No measurable change.
### NFR-2: Security
Stricter, centralized input validation; no new surface.
### NFR-3: Compatibility
Valid requests behave identically. Invalid-date 400 body changes from `{ "error": "..." }` to the standard ProblemDetails (`title: "Validation Failed"`, `errors[]`), consistent with other validated endpoints. Status code stays 400.

## Data Model
No persistence changes.

## API / Interface Design
`GET /api/packing-materials/consumption?date=yyyy-MM-dd&groupBy=Material|Product|Order`. 200 `GetDailyConsumptionBreakdownResponse`; 400 on invalid/missing date.

## Dependencies
FluentValidation, existing `ValidationBehavior`, `ValidationExceptionHandler`.

## Out of Scope
Other endpoints, frontend changes, changing response `Date` type, regenerating beyond what build does automatically.

## Open Questions
None.

## Status: COMPLETE
