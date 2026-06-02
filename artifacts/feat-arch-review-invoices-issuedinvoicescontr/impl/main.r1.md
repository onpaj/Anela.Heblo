---

# Implementation: Consolidate Issued Invoice HTTP Surface onto a Single Controller

## What was implemented

Deleted the duplicate `IssuedInvoicesController` (`/api/IssuedInvoices`) and repointed three frontend hooks to the existing thin `InvoicesController` (`/api/invoices`). Regenerated the OpenAPI TypeScript client to remove legacy `issuedInvoices_*` methods. Added a unit test pinning the empty-id validation guarantee in `GetIssuedInvoiceDetailHandler`.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added, 2 test cases pinning `ErrorCode=ValidationError` for empty/whitespace InvoiceId
- `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs` — **deleted**
- `frontend/src/api/hooks/useIssuedInvoices.ts` — list and detail URLs repointed; `?withDetails=true` appended to detail call
- `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts` — stats URL repointed (`/api/IssuedInvoices/sync-stats` → `/api/invoices/stats`)
- `frontend/src/api/generated/api-client.ts` — regenerated; `issuedInvoices_*` methods removed

## Verification

- `dotnet build`: 0 errors
- `dotnet format --verify-no-changes`: clean
- `dotnet test`: all pass (32 pre-existing Postgres testcontainer failures are unrelated)
- `npm run lint` + `npm run build`: clean
- Grep sweep: no legacy `IssuedInvoicesController`, `api/IssuedInvoices`, or `issuedInvoices_` strings in `backend/src` or `frontend/src`

## Notes

- Task 7 (manual smoke test) cannot be automated in the pipeline — the detail modal sync-history section must be manually verified against a running dev stack after merge
- Pre-existing follow-ups (out of scope): missing `[Authorize]` on `InvoicesController`; `(apiClient as any).http.fetch` anti-pattern in hooks
- FR-6 "migrate, don't drop" controller tests: no such tests existed; trivially satisfied

## Status

DONE