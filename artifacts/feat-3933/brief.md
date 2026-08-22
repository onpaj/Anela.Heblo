## Module / File
`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetIssuedInvoiceDetail/GetIssuedInvoiceDetailHandler.cs`

## Coverage
Line coverage: 40% (filter threshold: 60%)

## What's not tested
The handler has three distinct branches that are not covered:

1. **Null/empty InvoiceId guard** — when `request.InvoiceId` is null or whitespace, the handler returns `ErrorCodes.ValidationError`. This path is never asserted, so any regression (e.g. guard removed or wrong error code) would go undetected.
2. **WithDetails toggle** — the handler calls a different repository method (`GetByIdWithSyncHistoryAsync` vs `GetByIdAsync`) depending on `request.WithDetails`. Neither call path has a test verifying the correct method is dispatched.
3. **Invoice not found** — the null-check after repository fetch returns `ErrorCodes.ResourceNotFound`. This path is uncovered, so a regression that crashes instead of returning a structured response would not be caught.
4. **Exception path** — the outer `catch (Exception)` returns `ErrorCodes.Exception`. No test verifies that an unexpected repository throw results in a structured error response rather than an unhandled exception bubbling up.

## Why it matters
This handler is the detail endpoint for issued invoices. All four error shapes (validation, not-found, wrong-method, exception) are part of the public contract. If any of them regresses, API callers receive an unexpected error or HTTP 500 instead of a structured response, with no test catching it.

## Suggested approach
Unit test with a mocked `IIssuedInvoiceRepository`:
- Case: null InvoiceId → response.Success == false, ErrorCode == ValidationError
- Case: WithDetails == true → `GetByIdWithSyncHistoryAsync` called; WithDetails == false → `GetByIdAsync` called
- Case: repository returns null → ErrorCode == ResourceNotFound
- Case: repository throws → ErrorCode == Exception, no rethrow
~1 h effort.

---
_Filed by weekly coverage-gap routine on 2026-08-17. Based on CI run #31804633307 (6f781d410eb84616c8decb088d6d18cd1de01fb8)._
