# Implementation: Consolidate Issued Invoice HTTP Surface onto a Single Controller

## What was implemented

Deleted the duplicate `IssuedInvoicesController` (`/api/IssuedInvoices`) and repointed three frontend hooks to the existing clean `InvoicesController` (`/api/invoices`). Regenerated the OpenAPI TypeScript client to remove the legacy `issuedInvoices_*` methods. Added a unit test pinning the empty-id validation guarantee in `GetIssuedInvoiceDetailHandler`.

## Files created/modified

- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — new xUnit test (2 cases: empty string, whitespace) pinning `ErrorCode=ValidationError` for empty InvoiceId
- `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs` — **deleted** (FR-1)
- `frontend/src/api/hooks/useIssuedInvoices.ts` — list URL and detail URL updated; detail now appends `?withDetails=true` (critical: preserves sync-history loading in the modal)
- `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts` — stats URL updated (`/api/IssuedInvoices/sync-stats` → `/api/invoices/stats`)
- `frontend/src/api/generated/api-client.ts` — regenerated; `issuedInvoices_*` methods removed

## Tests

- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs`
  - `Handle_EmptyOrWhitespaceInvoiceId_ReturnsValidationError` — 2 passing cases (empty, whitespace)
  - Note: null case omitted because `GetIssuedInvoiceDetailRequest.InvoiceId` is a non-nullable `string`

## How to verify

```bash
# Backend
dotnet build backend/src/Anela.Heblo.API        # 0 errors
dotnet format backend/Anela.Heblo.sln --verify-no-changes  # no diffs
dotnet test backend/test/Anela.Heblo.Tests       # all pass (32 pre-existing Postgres testcontainer failures unrelated)

# Frontend
cd frontend && npm run lint && npm run build      # clean build

# Grep sweep
grep -rn "IssuedInvoicesController\|api/IssuedInvoices\|issuedInvoices_" backend/src frontend/src || echo "OK: clean"

# Commit history (4 commits expected)
git log --oneline origin/main..HEAD
```

## Notes

- The null `InvoiceId` test case was dropped (non-nullable property with `= string.Empty` default — null cannot arrive at the handler via normal code paths).
- Task 7 (manual smoke test) is not automatable in the pipeline — the list/detail/stats screens and the sync-history section of the detail modal must be manually verified against a running dev stack.
- Pre-existing: both `InvoicesController` and the deleted controller lack `[Authorize]`. Out of scope; file a follow-up issue.
- Pre-existing: frontend hooks still use `(apiClient as any).http.fetch` anti-pattern. Out of scope; file a follow-up issue.
- FR-6 "migrate, don't drop" controller tests: no controller-level HTTP tests targeted the legacy routes; acceptance is trivially satisfied.

## PR Summary

Delete duplicate `IssuedInvoicesController` and consolidate all issued-invoice traffic onto the existing thin `InvoicesController` at `/api/invoices`. The legacy controller violated project rules (business logic in controller, controller-level try/catch, error-code-to-HTTP-status mapping) while `InvoicesController` already exposed the same three operations cleanly via MediatR.

Key correctness move: the detail hook now explicitly passes `?withDetails=true` because the surviving controller defaults the parameter to `false` — without this, `IssuedInvoiceDetailModal` would silently render an empty sync-history section.

### Changes
- `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs` — **deleted**
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — added, pins empty-id `ValidationError` guarantee
- `frontend/src/api/hooks/useIssuedInvoices.ts` — list and detail URLs repointed; `?withDetails=true` added to detail call
- `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts` — stats URL repointed (`/api/IssuedInvoices/sync-stats` → `/api/invoices/stats`)
- `frontend/src/api/generated/api-client.ts` — regenerated; `issuedInvoices_*` methods removed

Out of scope (follow-ups): (1) neither controller carried `[Authorize]` — add it to `InvoicesController`; (2) hooks still use `(apiClient as any).http.fetch` anti-pattern from `docs/development/api-client-generation.md`.

## Status
DONE
