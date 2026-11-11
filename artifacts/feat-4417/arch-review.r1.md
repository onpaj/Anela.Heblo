# Architecture Review: GetInvoiceDetails error propagation

## Skip Design: true
Backend-only change; no UI.

## Architectural Fit Assessment
Fits existing conventions: handlers are thin, domain contract `IReceivedInvoicesClient.GetInvoiceByIdAsync` returns `ReceivedInvoice?` (null = not found), and the API centralises exceptions through `UseExceptionHandler()` with `IExceptionHandler` implementations in `Anela.Heblo.API/Infrastructure/ExceptionHandling`. Removing the handler-level catch aligns the slice with that model. Verified: `FlexiReceivedInvoicesClient.GetInvoiceByIdAsync` returns null for missing; controller maps `Found=false` to 404.

## Proposed Architecture

### Component Overview
Controller -> MediatR -> `GetInvoiceDetailsHandler` -> `IReceivedInvoicesClient` (Flexi adapter). Exceptions bubble Handler -> Controller -> ExceptionHandler middleware -> 500.

### Key Design Decisions

#### Decision 1: No custom NotFound exception
**Options considered:** (a) remove try/catch, rely on null contract; (b) add `InvoiceNotFoundException`.
**Chosen approach:** (a).
**Rationale:** Null contract already exists and is tested; (b) adds surface with no caller.

## Implementation Guidance

### Directory / Module Structure
Modify only `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetInvoiceDetails/GetInvoiceDetailsHandler.cs` and `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetInvoiceDetailsHandlerTests.cs`. Keep the `ILogger` dependency (still used for the info log) so the constructor and DI are unchanged.

### Interfaces and Contracts
No interface changes.

### Data Flow
Found: client returns entity -> mapped -> 200. Missing: null -> Found=false -> 404. Failure: exception -> global handler -> 500.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Frontend previously saw 404 on outage; now sees 500 | Low | Intended; react-query error state handles it |
| Mapper exception now surfaces as 500 | Low | Correct behaviour; covered by test |
| Process doc staleness CI check | Low | Review `docs/processes/module-invoice-classification.md` / `feed-invoice-classification.md`; update or bump `verified_at` in same PR and run `python3 scripts/process-docs/check.py index` |

## Specification Amendments
None.

## Prerequisites
None.
