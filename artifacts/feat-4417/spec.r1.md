# Specification: GetInvoiceDetails error propagation

## Summary
`GetInvoiceDetailsHandler` currently swallows every exception and returns `Found = false`, which the controller maps to HTTP 404. Infrastructure failures (Flexi outage, timeout, deserialization) must instead propagate so the global exception handler returns HTTP 500, keeping "invoice does not exist" (404) distinct from "could not check" (5xx).

## Background
Issue #4417 (arch-review of InvoiceClassification). `IReceivedInvoicesClient.GetInvoiceByIdAsync` already returns `ReceivedInvoice?` and `FlexiReceivedInvoicesClient` returns null when Flexi reports not found, so the handler's try/catch is not needed for the not-found case. The API pipeline uses `app.UseExceptionHandler()` with `AddExceptionHandler<T>()` handlers (Validation, Argument, UnauthorizedAccess); unhandled exceptions fall through to a 500.

## Functional Requirements

### FR-1: Remove blanket exception swallowing in the handler
Remove the try/catch in `GetInvoiceDetailsHandler.Handle`. Exceptions from the client or mapper propagate.
**Acceptance criteria:**
- When `GetInvoiceByIdAsync` throws, `Handle` throws the same exception.
- When it returns null, response is `Invoice = null`, `Found = false`.
- When it returns an invoice, response is mapped DTO with `Found = true`.

### FR-2: HTTP semantics
**Acceptance criteria:**
- `GET /api/.../invoice/{id}` returns 404 only for a genuinely missing invoice.
- Infrastructure errors yield HTTP 500 (via global handler), not 404.
- Controller logic is unchanged.

### FR-3: Logging retained
The informational "Getting details" log stays; error logging is left to the global exception handler (no duplicate catch-and-rethrow logging).

## Non-Functional Requirements
### NFR-1: Performance
No change.
### NFR-2: Security
500 responses must not leak internals beyond what the existing global handler already returns.

## Data Model
None.

## API / Interface Design
No contract change. `GetInvoiceDetailsResponse` and endpoint signature unchanged; OpenAPI client regeneration not required.

## Dependencies
`IReceivedInvoicesClient`, global `UseExceptionHandler` pipeline. Frontend `useInvoiceDetails` (react-query) will now see an error state for outages rather than 404; no change required, but behaviour is noted.

## Out of Scope
- Introducing `InvoiceNotFoundException` (client already returns null).
- Changing `ClassifyInvoicesHandler` error handling.
- Frontend UI changes.

## Open Questions
None.

## Status: COMPLETE
