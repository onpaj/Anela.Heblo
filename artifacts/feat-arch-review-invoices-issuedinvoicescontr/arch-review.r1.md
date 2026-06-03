# Architecture Review: Consolidate Issued Invoice HTTP Surface onto a Single Controller

## Skip Design: true

Pure refactor: backend controller deletion, frontend hook URL repointing, and OpenAPI client regeneration. No new UI components, no visual changes, no UX decisions.

## Architectural Fit Assessment

The change aligns directly with three documented standards:

- **Vertical Slice + thin controller** (`docs/architecture/development_guidelines.md`, "Forbidden Practices" table → "Business logic in Controller class"). `IssuedInvoicesController` violates the rule by embedding input validation, error-code-to-HTTP-status mapping, and try/catch wrappers. `InvoicesController` already conforms.
- **Single global exception handler** (`backend/src/Anela.Heblo.API/Extensions/ApplicationBuilderExtensions.cs:91` → `app.UseExceptionHandler()`, composed via `AddExceptionHandler<T>` in DI). Controller-level `try/catch` blocks bypass this cross-cutting middleware.
- **MediatR envelope as single error channel** (`Anela.Heblo.Application.Shared.BaseResponse` with `Success`/`ErrorCode`/`Params`). `GetIssuedInvoiceDetailHandler` already returns `ErrorCode = ValidationError` for empty IDs and `ResourceNotFound` for misses — duplicating that logic in the controller is dead weight.

Main integration points: three MediatR handlers (unchanged), the NSwag-generated TypeScript client, and three React Query hooks. No persistence or domain changes.

**Auth-posture check (per NFR):** neither `InvoicesController` nor `IssuedInvoicesController` declares `[Authorize]`, and `AuthenticationExtensions.ConfigureAuthorizationPolicies` does not install a `FallbackPolicy`. Auth posture is therefore **equivalent on both controllers** — the deletion does not weaken security. (The fact that both surfaces are anonymous is a pre-existing concern outside this refactor's scope; flagged in Risks.)

## Proposed Architecture

### Component Overview

```
Frontend (React)                       Backend (.NET 8 API)
─────────────────                      ─────────────────────
useIssuedInvoicesList ─────────┐
useIssuedInvoiceDetail ────────┼──►   InvoicesController        ──► MediatR ──► GetIssuedInvoicesListHandler
useIssuedInvoiceSyncStats ─────┘      (/api/invoices/*)                     ──► GetIssuedInvoiceDetailHandler
                                                                            ──► GetIssuedInvoiceSyncStatsHandler
api-client.ts (regenerated)            ▲                                        │
  · invoices_GetInvoicesList           │                                        ▼
  · invoices_GetInvoiceDetail          │                              IIssuedInvoiceRepository
  · invoices_GetSyncStats              │
  · issuedInvoices_*   ← DELETED       │
                                       │
                                  Global exception handler
                                  (UseExceptionHandler + IExceptionHandler chain)
                                       │
                                       ▼
                                  Single error path → BaseResponse envelope
```

### Key Design Decisions

#### Decision 1: Delete legacy controller in one shot, no redirect shim
**Options considered:**
A. Delete `IssuedInvoicesController` outright, repoint frontend hooks in the same PR.
B. Keep the legacy controller but make it a thin pass-through that internally calls the new route.
C. Add a redirect/rewrite middleware from `/api/IssuedInvoices*` → `/api/invoices/*` for a deprecation window.

**Chosen approach:** A. Hard cutover, no compatibility layer.
**Rationale:** Internal app with a single frontend deployed in lockstep with the backend (`docs/architecture/infrastructure.md` — single Docker image, single Azure Web App). Spec explicitly forbids a deprecation window. No external API consumers (auth posture is internal, not third-party-facing). A redirect shim would re-introduce a second HTTP surface in the OpenAPI spec, defeating the purpose of consolidation.

#### Decision 2: Preserve `WithDetails=true` semantics at the call site
**Options considered:**
A. Frontend hook explicitly passes `withDetails=true` when calling `/api/invoices/{id}`.
B. Change `InvoicesController.GetInvoiceDetail` default to `withDetails=true`.
C. Add a dedicated `GET /api/invoices/{id}/with-history` route.

**Chosen approach:** A.
**Rationale:** `IssuedInvoicesController.GetDetail` hardcodes `WithDetails = true` (line 105). `InvoicesController.GetInvoiceDetail` defaults `withDetails = false`. The detail modal (`frontend/src/components/customer/IssuedInvoiceDetailModal.tsx`) iterates `data.invoice.syncHistory`, so it needs the loaded history. The behavior is a frontend concern (which call needs history, which doesn't), not a controller concern. Changing the controller default would silently affect any future caller; a dedicated route would proliferate endpoints. The spec's "byte-identical" claim about the three methods is technically inaccurate here — this is the single most important architectural correction to the spec.

#### Decision 3: Keep hook public surface unchanged; do **not** migrate to typed `apiClient.invoices_*` methods in this PR
**Options considered:**
A. Minimal change — keep the `(apiClient as any).http.fetch` reach-around pattern, just swap the URL string.
B. Rewrite hooks to use the typed `apiClient.invoices_GetInvoicesList(...)` etc. and remove the anti-pattern flagged in `docs/development/api-client-generation.md`.

**Chosen approach:** A, with a **follow-up issue** filed for B.
**Rationale:** The spec is explicit ("no surgical improvements", "frontend hook public surface unchanged"). The `(apiClient as any)` pattern is a documented anti-pattern, but cleaning it up is an orthogonal refactor that would expand the diff and risk introducing serializer/Date-handling regressions. Stay surgical; file a follow-up.

## Implementation Guidance

### Directory / Module Structure

**Deleted:**
- `backend/src/Anela.Heblo.API/Controllers/IssuedInvoicesController.cs`

**Modified:**
- `frontend/src/api/hooks/useIssuedInvoices.ts` — two URL string literals + add `?withDetails=true` to the detail call
- `frontend/src/api/hooks/useIssuedInvoiceSyncStats.ts` — one URL string literal, path segment `/sync-stats` → `/stats`
- `frontend/src/api/generated/api-client.ts` — **regenerated**, not hand-edited (NSwag postbuild target)

**Added:**
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceDetailHandlerTests.cs` — new test class. The Invoices test folder currently has no handler-level test for `GetIssuedInvoiceDetailHandler` (verified via `ls`), so create one with at least the empty-id validation case (FR-3).

**Unchanged:**
- `InvoicesController.cs` — three relevant action methods stay byte-identical
- All MediatR requests, responses, handlers, repository, mapping profile

### Interfaces and Contracts

| Operation | Route after change | MediatR request | Auth |
|-----------|--------------------|-----------------|------|
| List | `GET /api/invoices` | `GetIssuedInvoicesListRequest` | Anonymous (unchanged) |
| Detail | `GET /api/invoices/{id}?withDetails={bool}` | `GetIssuedInvoiceDetailRequest` | Anonymous (unchanged) |
| Stats | `GET /api/invoices/stats` | `GetIssuedInvoiceSyncStatsRequest` | Anonymous (unchanged) |

Response envelope (`BaseResponse` → `Success`/`ErrorCode`/`Params`) and hook return types are unchanged. The TypeScript hook response interfaces (`IssuedInvoicesListResponse`, `IssuedInvoiceDetailResponse`, `IssuedInvoiceSyncStats`) stay identical.

### Data Flow

**Detail (the case that requires care):**
```
IssuedInvoiceDetailModal.tsx
  → useIssuedInvoiceDetail(invoiceId)
    → fetch GET /api/invoices/{id}?withDetails=true       [NEW: explicit ?withDetails=true]
      → InvoicesController.GetInvoiceDetail(id, withDetails=true)
        → MediatR.Send(GetIssuedInvoiceDetailRequest{InvoiceId, WithDetails=true})
          → GetIssuedInvoiceDetailHandler:
              · empty-id?  → BaseResponse{Success=false, ErrorCode=ValidationError}
              · not found? → BaseResponse{Success=false, ErrorCode=ResourceNotFound}
              · ok         → BaseResponse{Success=true, Invoice=...}
          → Repository.GetByIdWithSyncHistoryAsync(...)
        ← Response (handler-level try/catch already wraps unexpected exceptions into BaseResponse{Exception})
      ← Controller returns Ok(response) — no HTTP-status translation
    ← Hook returns body verbatim to React Query
  ← Modal renders syncHistory section
```

**Error path:** validation/not-found are returned as HTTP 200 with `Success=false` envelope (existing project pattern — controllers do not translate `ErrorCode → 400/404`). Unexpected exceptions are caught by the global `UseExceptionHandler` middleware → `ProblemDetails`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Detail modal loses sync history because new route defaults `withDetails=false` | **HIGH** | Frontend hook must append `?withDetails=true`. Add this to the spec acceptance criteria. Manually open the detail modal after the change and confirm the "Sync History" section renders. |
| Sync-stats path segment renamed (`/sync-stats` → `/stats`) — easy to forget | MEDIUM | Single string change in `useIssuedInvoiceSyncStats.ts`. Verify the stats card on `IssuedInvoicesPage.tsx` loads after the change. Grep `frontend/src` for `sync-stats` post-change. |
| `(apiClient as any).http.fetch` / `(apiClient as any).baseUrl` reach-around persists — anti-pattern stays in code | LOW | Out of scope per spec. File a follow-up issue. |
| OpenAPI client regeneration runs on `npm run build`/`npm start` postbuild — stale `api-client.ts` could be committed if dev forgets | LOW | Run `dotnet msbuild backend/src/Anela.Heblo.API -t:GenerateFrontendClientManual` explicitly, then verify `grep -n issuedInvoices_ frontend/src/api/generated/api-client.ts` returns nothing. Commit the regenerated file. |
| Both controllers are anonymous — deletion doesn't fix the pre-existing lack of `[Authorize]` | MEDIUM (pre-existing, not introduced) | **Out of scope.** Flag in PR description and consider a separate issue: "Add `[Authorize]` to `InvoicesController` to match other controllers in the API project." Do not bundle into this refactor. |
| Test FR-6 says "migrate, don't drop" tests that hit `/api/IssuedInvoices*` — search confirms zero such tests exist | NONE | Acceptance is trivially satisfied. Mention "no controller-level tests targeted the legacy routes; FR-6 is a no-op" in the PR. |

## Specification Amendments

1. **FR-4 must explicitly require `?withDetails=true` on the detail call.** Current spec says "`/api/IssuedInvoices` → `/api/invoices` (list and detail)" but omits the `withDetails` query parameter. Without it, `IssuedInvoiceDetailModal` will silently lose its sync history. Update FR-4 to:
   > `useIssuedInvoices.ts`: list call URL changes to `/api/invoices`; detail call URL changes to `/api/invoices/{id}?withDetails=true`.

2. **FR-2 "byte-identical" claim needs softening.** The three methods are *semantically* equivalent given proper query-string usage, but `GetDetail` in the legacy controller hardcodes `WithDetails=true` and the surviving controller takes it as a defaulted query parameter. Reword as "no controller-level changes; behavioral parity preserved by the frontend explicitly passing `withDetails=true`."

3. **FR-6 should note that no controller-level HTTP tests target the legacy routes.** `grep IssuedInvoicesController|api/IssuedInvoices backend/test` returns nothing. The acceptance criterion `dotnet test green` holds trivially.

4. **Add a follow-up note (not blocking).** The two hooks use `(apiClient as any).http.fetch` and `(apiClient as any).baseUrl`, which `docs/development/api-client-generation.md` explicitly bans. Migrating these to typed `apiClient.invoices_*` calls is a clean, isolated follow-up. Keep this PR surgical.

## Prerequisites

- **NSwag tool restored**: `dotnet tool restore` at repo root (one-time per workspace, already typical).
- **Backend builds clean before regeneration**: `dotnet build backend/src/Anela.Heblo.API` must succeed after the controller is deleted; otherwise the regenerated `api-client.ts` will be incomplete or stale.
- **Frontend dev server restarted after regeneration**: `npm start` triggers the `prebuild` → `generate-client` chain. A still-running dev server may serve a stale bundled `api-client.ts`.
- **No database, migration, config, or infrastructure changes required.** Nothing to deploy beyond the standard Docker image build.