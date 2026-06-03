# Specification: Consolidate Issued Invoice HTTP Surface onto a Single Controller

## Summary
Delete the duplicate `IssuedInvoicesController` (`/api/IssuedInvoices`) and keep only the clean `InvoicesController` (`/api/invoices`) as the single HTTP surface for issued invoice list, detail, and sync-stats endpoints. Update the three frontend hooks that still call the legacy route, and regenerate the TypeScript API client so the duplicated `issuedInvoices_*` methods disappear. This removes a project-rule violation (business logic in controllers), eliminates ambiguity for frontend callers, and restores a single MediatR-driven error path.

## Background
The Invoices module currently exposes the same three MediatR requests (`GetIssuedInvoicesListRequest`, `GetIssuedInvoiceDetailRequest`, `GetIssuedInvoiceSyncStatsRequest`) through two different controllers:

- `backend/src/Anela.Heblo.API/Controllers/InvoicesController.cs` — route `/api/invoices`, thin, conforms to project rules.
- `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs` — route `/api/IssuedInvoices`, bloated with controller-level validation, error routing, and try/catch blocks that the architecture rules forbid.

## Functional Requirements

### FR-1: Delete `IssuedInvoicesController`
Remove the file. After deletion, `/api/IssuedInvoices*` routes no longer exist.
**Acceptance criteria:** file gone, `dotnet build` green, no grep hits, Swagger no longer lists those endpoints.

### FR-2: Keep `InvoicesController` as the canonical surface
Three relevant methods stay byte-identical: `GET /api/invoices`, `GET /api/invoices/{id}?withDetails={bool}`, `GET /api/invoices/stats`. Import endpoints untouched.
**Acceptance criteria:** no surgical "improvements"; one route per operation in Swagger.

### FR-3: Verify validation parity in the handler
`GetIssuedInvoiceDetailHandler` already validates empty `InvoiceId` and returns `ErrorCode=ValidationError`. Add no new code; verify with a unit test.
**Acceptance criteria:** unit test covers the empty-id case; no new validation in controller.

### FR-4: Repoint frontend hooks to `/api/invoices`
- `useIssuedInvoices.ts`: `/api/IssuedInvoices` → `/api/invoices` (list and detail).
- `useIssuedInvoiceSyncStats.ts`: `/api/IssuedInvoices/sync-stats` → `/api/invoices/stats` (path segment also changes to match `InvoicesController`).
**Acceptance criteria:** no `/api/IssuedInvoices` strings in `frontend/src`; `npm run build` and `npm run lint` pass; list/detail/stats screens load.

### FR-5: Regenerate the OpenAPI TypeScript client
Regenerate `api-client.ts` so `issuedInvoices_*` methods are removed.
**Acceptance criteria:** no `issuedInvoices_*` methods; `tsc` green.

### FR-6: No behavior change for backend tests
Migrate (don't drop) any test that hit `/api/IssuedInvoices*`.
**Acceptance criteria:** `dotnet test` green; no tests deleted.

## Non-Functional Requirements
- **Performance:** no expected change.
- **Security:** confirm auth posture is equivalent on both controllers before deletion; confirm a global exception handler exists so we don't reintroduce controller-level `try/catch`.
- **Maintainability:** one HTTP route per operation; single error channel via MediatR envelope.
- **Backwards compatibility:** internal app, frontend updated in lockstep, no deprecation window.

## Data Model
Unchanged. MediatR requests/responses, `IIssuedInvoiceRepository`, DTOs, and AutoMapper profile stay as is.

## API / Interface Design
Surviving endpoints listed in a table in the spec. Removed: `GET /api/IssuedInvoices`, `GET /api/IssuedInvoices/{id}`, `GET /api/IssuedInvoices/sync-stats`. Response envelope (`Success`, `ErrorCode`, `Params`) and frontend hook public surface unchanged.

## Dependencies
Existing MediatR handlers, repository, AutoMapper, React Query, OpenAPI generator. No new dependencies.

## Out of Scope
Refactoring import endpoints, renaming `InvoicesController`, changing the response envelope, translating Czech messages, adding new endpoints, redirect from old route to new, other invoice-adjacent surfaces.

## Open Questions
None.

## Status: COMPLETE

Spec written to `artifacts/feat-arch-review-invoices-issuedinvoicescontr/spec.md`.