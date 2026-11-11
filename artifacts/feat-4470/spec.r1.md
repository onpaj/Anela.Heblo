# Specification: Type CreatePurchaseOrderRequest date fields as DateTime

## Summary
`CreatePurchaseOrderRequest` exposes `OrderDate` and `ExpectedDeliveryDate` as `string`, while `UpdatePurchaseOrderRequest` uses `DateTime?`. Change the Create request to `DateTime OrderDate` and `DateTime? ExpectedDeliveryDate` so invalid dates are rejected by model binding / validation (HTTP 400) rather than throwing in the handler (HTTP 500), and the generated TypeScript client is consistent across both endpoints.

## Background
Arch-review finding (issue #4470, Purchase module). `CreatePurchaseOrderHandler` (lines ~54-55) calls `request.OrderDate.ToUtcDateTime()` / `ExpectedDeliveryDate.ToUtcDateTimeOrNull()` (string extensions in `Common/Extensions/DateExtensions.cs`), which use `DateTime.Parse` and can throw. `CreatePurchaseOrderRequestValidator` re-parses the raw strings with `DateTime.TryParse`. `PurchaseOrder` constructor already normalizes Kind (Unspecified -> Utc, otherwise `ToUniversalTime()`). The frontend (`PurchaseOrderHelpers.tsx`) currently sends `YYYY-MM-DD` strings; the generated `api-client.ts` types the fields as `string`.

## Functional Requirements

### FR-1: Typed request DTO
`CreatePurchaseOrderRequest.OrderDate` is `DateTime` (required) and `ExpectedDeliveryDate` is `DateTime?`. DTO remains a class.
**Acceptance criteria:**
- Property types match the above; `[Required]` on OrderDate removed or harmless (non-nullable value type).
- A body with `"orderDate": "not-a-date"` yields 400, not 500.
- A body omitting `orderDate` is rejected (400) rather than silently binding to `0001-01-01` (see FR-3).

### FR-2: Handler uses typed dates
`CreatePurchaseOrderHandler` no longer calls `ToUtcDateTime()` / `ToUtcDateTimeOrNull()`. Dates are passed to `PurchaseOrder` (and `GenerateCandidate`) with UTC normalization equivalent to current behaviour: the date part only, `DateTimeKind.Utc` (`DateTime.SpecifyKind(x.Date, Utc)`), consistent with the previous `date.Date` truncation.
**Acceptance criteria:**
- Existing handler tests pass after being updated to supply `DateTime` values.
- Persisted OrderDate/ExpectedDeliveryDate have Kind Utc and no time component for date-only input.

### FR-3: Validator uses typed comparisons
`CreatePurchaseOrderRequestValidator` validates: OrderDate not default (`!= default`), OrderDate not more than 30 days in future, ExpectedDeliveryDate >= OrderDate (date part) when present. All string-parsing helpers are removed.
**Acceptance criteria:**
- Validator tests updated to typed inputs; same error messages retained where applicable ("Order date is required", "Order date cannot be more than 30 days in the future", "Expected delivery date must be on or after the order date"). "must be a valid date" messages are dropped (handled by binding).

### FR-4: Frontend and generated client
Regenerate the OpenAPI TS client; `CreatePurchaseOrderRequest.orderDate` becomes `Date`. Update `PurchaseOrderHelpers.tsx` create-request mapping to pass `Date` objects, avoiding off-by-one-day shifts (construct from the `YYYY-MM-DD` value in a way that serializes to the same calendar date).
**Acceptance criteria:**
- `npm run build` and `npm run lint` pass; relevant FE tests pass.
- Creating an order for date D results in stored order date D.

## Non-Functional Requirements

### NFR-1: Performance
No change.

### NFR-2: Security
Stricter input validation only; no auth changes.

### NFR-3: Compatibility
This is a wire-contract change in type annotation only; JSON ISO-8601 date strings (including `YYYY-MM-DD`) remain accepted by System.Text.Json `DateTime` binding.

## Data Model
No DB or entity change. `PurchaseOrder.OrderDate` / `ExpectedDeliveryDate` unchanged.

## API / Interface Design
`POST /api/purchase-orders` body: `orderDate: date-time` (required), `expectedDeliveryDate?: date-time`. OpenAPI schema changes from `string` to `string(date-time)`.

## Dependencies
FluentValidation, OpenAPI client generation (`docs/development/api-client-generation.md`), frontend purchase-orders form.

## Out of Scope
- Changing UpdatePurchaseOrderRequest.
- Removing `DateExtensions` string helpers if used elsewhere (only remove if left unused; verify by grep).
- Changing the PurchaseOrder domain entity.

## Open Questions
None.

## Status: COMPLETE
