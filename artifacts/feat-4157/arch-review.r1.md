# Architecture Review: Eliminate transient 404 on PUT /api/purchase-orders/{id}

## Skip Design: true
Backend-only fix (handler ordering, Flexi adapter caching, logging, process doc). No UI.

## Architectural Fit Assessment
Fits existing Clean Architecture and vertical-slice layout. Changes are confined to:
- `Anela.Heblo.Adapters.Flexi/Purchase/FlexiSupplierRepository.cs` (adapter, singleton, `IMemoryCache`)
- `Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderHandler.cs`
- Tests in `test/Anela.Heblo.Tests/Features/Purchase/` and `test/Anela.Heblo.Adapters.Flexi.Tests/Purchase/`
- `docs/processes/flow-purchase-order.md`

Verified in code: `GetByIdWithDetailsAsync` is a plain tracked PK query (no AsNoTracking, no filter, no cache), so the issue's stale-read hypothesis is unsupported. `SupplierNotFound` (1102) maps to 404 exactly like `PurchaseOrderNotFound` (1101) via `HttpStatusCodeAttribute`, and `FlexiSupplierRepository` is `AddSingleton` with a list cache that is not validated against the requested id.

## Proposed Architecture

### Component Overview
Controller -> MediatR `UpdatePurchaseOrderHandler` -> (1) `IPurchaseOrderRepository.GetByIdWithDetailsAsync`, (2) `ISupplierRepository.GetByIdAsync` -> `FlexiSupplierRepository` -> `IMemoryCache` / `IContactListClient` (Flexi).

### Key Design Decisions

#### Decision 1: Fix in the adapter, not the handler
**Options considered:** (a) retry in handler; (b) frontend retry; (c) make the repository robust (refresh on miss, never trust empty).
**Chosen approach:** (c).
**Rationale:** the cause is cache/upstream semantics owned by the adapter; it benefits Create, GetById and SearchSuppliers too and keeps the handler free of infrastructure concerns.

#### Decision 2: Refresh-on-miss, single-flight and throttled
**Chosen approach:** in `GetByIdAsync`, if the id is not in the cached list, force one reload (bypass cache) guarded by a `SemaphoreSlim` and a minimum interval (30s, via a stored timestamp) so unknown ids cannot stampede Flexi. After the refresh, return the match or null.
**Rationale:** the singleton repository can hold the lock and timestamp as instance fields. Empty fetch results are never cached (existing) and are logged at Warning.

#### Decision 3: Exceptions propagate
Do not catch Flexi exceptions in the repository. Existing global exception handling returns 500 (`ErrorCodes.Exception`), which the client can legitimately retry and which telemetry can separate from real 404s.

#### Decision 4: Validate before mutate in the handler
Move the `_supplierRepository.GetByIdAsync` call (and its not-found return) above `UpdateOrderNumber`. Keep the existing error codes. Add order Id to the supplier warning log.

## Implementation Guidance

### Directory / Module Structure
No new files required besides tests. Add `FlexiSupplierRepositoryTests.cs` under `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Purchase/` if not present (check first), and extend `UpdatePurchaseOrderHandlerTests.cs`.

### Interfaces and Contracts
`ISupplierRepository` unchanged. No DTO/OpenAPI change. If a bool is needed for refresh internals, keep it private.

### Data Flow
Request -> load order (404 only if the row is truly absent) -> resolve supplier (cache hit; on miss, one throttled refresh; upstream error -> exception -> 500) -> mutate -> SaveChanges.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Root cause is not the supplier lookup | Medium | FR-1 logging makes the next occurrence attributable; the order read path is left untouched and the handler logs both exits distinctly |
| Refresh-on-miss floods Flexi for invalid ids | Medium | Single-flight lock plus 30s minimum refresh interval |
| Behaviour change 404 -> 500 for upstream failure | Low | Intentional; document in process doc |
| Mock-based tests mask real Flexi behaviour | Low | Tests use `IContactListClient` mock with sequenced returns |

## Specification Amendments
Spec FR-2 wording on "dedicated exception" is simplified: let the Flexi client exception propagate; no new exception type or ErrorCode. Empty result on first fetch is treated as "not cached, then logged"; the refresh-on-miss path covers the retry.

## Prerequisites
None (no migrations, no config). Process doc `docs/processes/flow-purchase-order.md` must be updated in the same PR and `python3 scripts/process-docs/check.py index` run.
