# Architecture Review: Remove dead `supplierId` parameter from `IPurchaseOrderRepository.GetPaginatedAsync`

## Skip Design: true

No UI work. The frontend `PurchaseOrderList.tsx` never wired up a supplier filter, so there is no visible component to remove or redesign. The only frontend changes are an internal hook interface and a generated client.

## Architectural Fit Assessment

The spec is well-aligned with the existing architecture. Verified against the codebase:

- **Vertical slice + clean layering is preserved.** The change removes a parameter that crosses Domain (`IPurchaseOrderRepository`), Persistence (`PurchaseOrderRepository`), Application (`InMemoryPurchaseOrderRepository`, `GetPurchaseOrdersHandler`, `GetPurchaseOrdersRequest`), and API (auto-bound query string). No new boundaries, no new abstractions — strictly a removal across the existing slice at `backend/src/Anela.Heblo.{Domain,Persistence,Application}/Features/Purchase/...`.
- **Repository pattern remains intact.** `IPurchaseOrderRepository : IRepository<PurchaseOrder, int>` (Domain layer) keeps the canonical Domain interface and both implementations (EF + in-memory test/fallback) keep parity.
- **MediatR + MVC binding is untouched.** `PurchaseOrdersController.GetPurchaseOrders([FromQuery] GetPurchaseOrdersRequest …)` continues to bind from the query string. ASP.NET Core ignores unknown query parameters by default, so the FR-7 contract is preserved without any extra code.
- **Frontend convention is respected.** `usePurchaseOrders.ts` is a hand-written wrapper around the generated client (it builds its own `URLSearchParams` because the generated overload exists but is bypassed). The hand-written builder must be edited in lockstep with regeneration — spec FR-6 captures this.

One inconsistency the spec does **not** flag and that I would *not* fix here (separate dead-code finding, out of scope): `GetPurchaseOrdersHandler` sets `SupplierId = 0` on every `PurchaseOrderSummaryDto` with the comment "No longer using SupplierId" (`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersHandler.cs:45`). The DTO field is also typed `int` while the domain is `long`. Surgical-change discipline says leave it. See **Specification Amendments** below — I want this noted, not silently expanded into.

## Proposed Architecture

### Component Overview

```
                  GET /api/purchase-orders?searchTerm=&status=&fromDate=…&activeOrdersOnly=
                                          (SupplierId silently ignored by model binder)
                                                    │
                                                    ▼
                       ┌──────────────────────────────────────────────────────┐
                       │  PurchaseOrdersController.GetPurchaseOrders          │
                       │  [FromQuery] GetPurchaseOrdersRequest                │  (DTO, class — no SupplierId)
                       └──────────────────────────┬───────────────────────────┘
                                                  │  IMediator.Send
                                                  ▼
                       ┌──────────────────────────────────────────────────────┐
                       │  GetPurchaseOrdersHandler                            │
                       │   → IPurchaseOrderRepository.GetPaginatedAsync(…)    │  (no supplierId arg)
                       └──────────────────────────┬───────────────────────────┘
                                                  │
                       ┌──────────────────────────┴───────────────────────────┐
                       ▼                                                      ▼
       PurchaseOrderRepository (EF Core)                         InMemoryPurchaseOrderRepository
       — no supplierId param, no dead if-block                   — same signature, same removal
```

Topology is unchanged. Only the parameter list of `GetPaginatedAsync` shrinks and the `GetPurchaseOrdersRequest` DTO loses one property.

### Key Design Decisions

#### Decision 1: Remove rather than implement the filter
**Options considered:**
- (A) Remove the parameter end-to-end (this spec).
- (B) Implement real `SupplierName` LIKE filtering and add UI.
- (C) Leave as-is with a TODO.

**Chosen approach:** (A). The spec already settled this; I am confirming it is the right architectural call.

**Rationale:** No product driver, no UI surface, no test asserts filtering, and the type mismatch (`int?` parameter vs. `long SupplierId` domain field) signals that the parameter was never finished. YAGNI plus "silent no-op at the data layer" outweighs theoretical optionality. (B) is reintroducible later as a real feature with a real spec and `long?` typing. (C) preserves the bug.

#### Decision 2: Keep HTTP contract permissive (no 400 on legacy `SupplierId=…`)
**Options considered:**
- (A) Let ASP.NET Core's default model binder silently ignore unknown query params (spec FR-7).
- (B) Add explicit validation that rejects `SupplierId` with HTTP 400.

**Chosen approach:** (A).

**Rationale:** Old clients in the browser will continue to send `SupplierId` during the rolling deploy window. Silent acceptance keeps them green; explicit rejection trades a real backward-compatibility break for an aesthetic gain. The integration test in FR-7/FR-8 pins this so a future model-binding change cannot regress it.

#### Decision 3: Update the generated client via regeneration, edit the hand-written wrapper manually
**Options considered:**
- (A) Touch only the hand-written wrapper; let the generated client regenerate on next backend build.
- (B) Commit both files together in the same PR.

**Chosen approach:** (B), per `docs/development/api-client-generation.md` convention — regeneration happens as part of `npm run build`, and the regenerated `api-client.ts` is committed alongside the wrapper edit so reviewers see the full diff and the build is reproducible from the committed sources.

**Rationale:** The frontend hook bypasses the generated overload (it constructs its own `URLSearchParams`), so the regeneration is technically not load-bearing for runtime behavior — but a stale generated client would still advertise `supplierId` to any new consumer who decided to use the generated method directly. Commit both for a coherent contract.

## Implementation Guidance

### Directory / Module Structure

All files exist. No new files are required for the change itself; the only new file is the test added under FR-8.

**Files to modify:**
- `backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs` — interface signature.
- `backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs` — EF implementation; delete lines 18 and 50-54.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/InMemoryPurchaseOrderRepository.cs` — in-memory implementation; delete lines 38 and 70-74.
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersHandler.cs` — drop `request.SupplierId` from the call site (line 31).
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrders/GetPurchaseOrdersRequest.cs` — delete `SupplierId` property (line 11).
- `frontend/src/api/hooks/usePurchaseOrders.ts` — delete `supplierId?: number` (line 24) and the `params.append("SupplierId", …)` branch (lines 65-66).
- `frontend/src/api/generated/api-client.ts` — let `npm run build` regenerate; commit the regenerated file.

**File to add:**
- `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseOrdersHandlerTests.cs` (folder already exists — see spec amendment below).

### Interfaces and Contracts

The contracts in the spec's "API / Interface Design" section are correct as written. One reinforcement:

- **Parameter order for `GetPaginatedAsync` is preserved minus `supplierId`.** Do **not** reorder remaining parameters even if alphabetical or grouping feels nicer — call sites elsewhere may rely on positional order (none currently do other than the handler, but reordering invites churn and review noise).
- **`GetPurchaseOrdersRequest` remains a `class`** with public mutable setters, per the project rule about DTOs and OpenAPI client generation (`docs/architecture/development_guidelines.md`). Do not migrate to `record` while you are in the file.

### Data Flow

For `GET /api/purchase-orders?searchTerm=foo&status=Draft&activeOrdersOnly=true`:

1. ASP.NET Core model binder constructs `GetPurchaseOrdersRequest`, ignoring any unknown query keys (including a legacy `SupplierId=…`).
2. Controller forwards to `IMediator.Send`.
3. `GetPurchaseOrdersHandler.Handle` calls `_repository.GetPaginatedAsync(searchTerm, status, fromDate, toDate, activeOrdersOnly, pageNumber, pageSize, sortBy, sortDescending, cancellationToken)`.
4. EF repo builds the `IQueryable`, applies five filter branches (search term, status, fromDate, toDate, activeOrdersOnly), counts, sorts, paginates, returns.
5. Handler maps `PurchaseOrder` → `PurchaseOrderSummaryDto` (still hardcodes `SupplierId = 0` — pre-existing, intentionally out of scope).
6. Response serialized to JSON. Frontend hook deserializes via `getPurchaseOrdersClient().http.fetch(...)`.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| A legacy mobile/browser client sends `?SupplierId=…` immediately after deploy and breaks | Low | ASP.NET Core ignores unknown query params by default; FR-7 integration test pins this. No code change needed. |
| A hidden consumer (script, MCP tool, external integration) relies on the parameter being in the OpenAPI schema | Low | Repo grep shows only the FE wrapper, generated client, and BE flow. MCP tools (`docs/integrations/mcp-server.md`) are 15 enumerated tools; none use this endpoint. If an external consumer exists outside the repo, the parameter was a no-op anyway — removing it cannot change observed behavior. |
| OpenAPI client regeneration introduces unrelated drift in `api-client.ts` and bloats the diff | Medium | Run `npm run build` on a clean tree first, commit only the `supplierId`-related regeneration hunks. If unrelated drift appears, file it separately rather than smuggling it into this PR. |
| `dotnet build` surfaces new nullable/warning noise from unrelated files when the touched projects are recompiled | Low | NFR-4 already requires `dotnet build` clean for *this change*. Don't fix unrelated warnings — note them if encountered. |
| Tests added under FR-8 drift from the existing pattern and become brittle | Medium | Reuse `PurchaseOrdersTestFactory : HebloWebApplicationFactory` (already established in `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs`) for the FR-7 integration test. For the FR-8 unit test, instantiate `GetPurchaseOrdersHandler` with `InMemoryPurchaseOrderRepository` directly — no factory needed. |

## Specification Amendments

1. **FR-8 wording:** "create folder if missing" is incorrect — `backend/test/Anela.Heblo.Tests/Features/Purchase/` already exists and contains five test files (`CreatePurchaseOrderHandlerTests.cs`, `UpdatePurchaseOrderStatusHandlerTests.cs`, etc.). Drop the "create folder if missing" qualifier; place the new file alongside its siblings.

2. **FR-8, integration test placement:** Put the FR-7 contract-pin test in `backend/test/Anela.Heblo.Tests/Controllers/PurchaseOrdersControllerTests.cs` (next to the existing `GetPurchaseOrders_*` tests). It reuses `PurchaseOrdersTestFactory` and `HttpClient`, matching the convention. Adding a separate file just for one assertion fragments the suite.

3. **Out-of-scope reinforcement (not a change, an explicit non-action):** Do **not** alter `PurchaseOrderSummaryDto.SupplierId = 0` in `GetPurchaseOrdersHandler.cs:45` or the `int` vs. `long` mismatch on `PurchaseOrderSummaryDto`. This is a separate dead-data finding worth its own arch-review entry — mention it in the PR description so the daily arch-review routine can pick it up tomorrow, but do not include it in this PR's diff.

4. **NFR-1 verification:** The "manual EF logging spot-check" is acceptable but optional. The dead `if (supplierId.HasValue) { }` block compiles to a no-op even before this change — there is provably no SQL difference. A unit-test assertion that `TotalCount` is identical with/without a `SupplierId` query param (already implied by FR-7/FR-8) is sufficient evidence; an EF log capture is overkill.

5. **FR-6 lint posture:** After deleting the `if (request.supplierId) …` branch in `usePurchaseOrders.ts`, verify no resulting `no-unused-vars` warnings appear (none expected — `supplierId` is destructured from `request` only at the use site). Run `npm run lint` once after the edit.

## Prerequisites

None — no migration, no config, no infrastructure, no new package, no new module registration.

Standard validation gates from `CLAUDE.md` apply: `dotnet build` clean, `dotnet format` clean, `npm run build` clean (which also regenerates the TS client), `npm run lint` clean, all Purchase tests pass.