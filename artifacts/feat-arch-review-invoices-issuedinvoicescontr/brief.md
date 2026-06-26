## Module
Invoices

## Finding
Two controllers expose the same three operations using the identical MediatR requests:

- `InvoicesController` (`backend/src/Anela.Heblo.API/Controllers/InvoicesController.cs`) — `/api/invoices` — clean, thin, correct
- `IssuedInvoicesController` (`backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs`) — `/api/IssuedInvoices` — bloated, incorrect

`IssuedInvoicesController` re-exposes the exact same three MediatR requests (`GetIssuedInvoicesListRequest`, `GetIssuedInvoiceDetailRequest`, `GetIssuedInvoiceSyncStatsRequest`) but adds forbidden business logic directly in the controller:

1. **Input validation in the controller** (lines 97–100): `if (string.IsNullOrWhiteSpace(id)) return BadRequest(...)`
2. **Error routing in the controller** (lines 109–115): `if (!response.Success) { if (response.ErrorCode == ErrorCodes.ResourceNotFound) return NotFound(...) }`
3. **Exception handling in the controller** (lines 55, 93, 138): `try/catch` blocks returning `StatusCode(500, ...)`

The side-effect in the generated client is 6 methods where 3 should exist (`invoices_GetInvoicesList` + `issuedInvoices_GetList`, etc.), causing confusion about which endpoint to call.

## Why it matters
- Directly violates the project rule: *"Business logic in Controller class: Business logic should be in MediatR handlers"*
- Creates two HTTP surfaces for the same data, causing ambiguity for the frontend (the `useIssuedInvoices.ts` hooks call `/api/IssuedInvoices`, not `/api/invoices`)
- The `IssuedInvoicesController` swallows exceptions inside the controller rather than letting the MediatR pipeline or a middleware handle them, bypassing any cross-cutting error handling

## Suggested fix
Delete `IssuedInvoicesController.cs` entirely. The input validation (`string.IsNullOrWhiteSpace`) and not-found logic belong in the handler (already handled in `GetIssuedInvoiceDetailHandler` via `Success = false` / `ErrorCode`). Update the frontend hooks to call `/api/invoices` instead of `/api/IssuedInvoices`.

---
_Filed by daily arch-review routine on 2026-05-29._