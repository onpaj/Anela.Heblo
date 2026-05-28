I have enough context now. Producing the architecture review.

```markdown
# Architecture Review: Dedicated lightweight query path for `GET /api/purchase-orders/{id}/history`

## Skip Design: true

## Architectural Fit Assessment

The feature is a textbook fit for the existing Vertical Slice + MediatR + Repository pattern used across the Purchase module. The codebase already has the building blocks:

- `IPurchaseOrderRepository` (`backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs`) is the well-defined seam for adding focused queries. It already mixes broad reads (`GetByIdWithDetailsAsync`) with lightweight reads (`OrderNumberExistsAsync`), so adding a history-only read is consistent.
- The Application layer already organises use cases as one folder per request under `UseCases/` (e.g. `GetPurchaseOrderById/`), each containing exactly `Request.cs`, `Response.cs`, `Handler.cs`. The spec's proposed `GetPurchaseOrderHistory/` folder matches this exactly.
- `ListResponse<T>` (`backend/src/Anela.Heblo.Application/Shared/ListResponse.cs`) inherits from `BaseResponse` and supports the `(ErrorCodes, params)` constructor — perfect as the handler's response type, removing the controller's manual remapping.
- `ApplicationDbContext.PurchaseOrderHistory` is already exposed as a top-level `DbSet`, so the repository can query it directly without aggregate traversal.
- `ErrorCodes.PurchaseOrderNotFound = 1101` already exists and is the convention for missing aggregates.

**Integration points:** Domain repository interface, Persistence implementation, one new MediatR use case, controller wiring. No DI changes (the existing `AddScoped<IPurchaseOrderRepository, PurchaseOrderRepository>` registration covers it), no DB migration, no contract diff.

**One deviation from existing convention to call out:** `GetPurchaseOrderByIdRequest` is declared as a `record`. Project rules in CLAUDE.md state DTOs must be classes — but they also clarify "Internal domain types may still be records". Since MediatR `IRequest` types are not serialized over OpenAPI (they're consumed in-process), the existing `record` pattern is correct. New request should follow it.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────┐
│ PurchaseOrdersController.GetPurchaseOrderHistory(id)         │
│   - dispatches GetPurchaseOrderHistoryRequest(id)            │
│   - returns response directly (no manual remap)              │
└──────────────────────────┬───────────────────────────────────┘
                           │ IMediator.Send
                           ▼
┌──────────────────────────────────────────────────────────────┐
│ GetPurchaseOrderHistoryHandler                               │
│   - depends on: IPurchaseOrderRepository, ILogger            │
│   - 1) repo.ExistsAsync(id)   → 404 path if missing          │
│   - 2) repo.GetHistoryAsync(id)                              │
│   - 3) projects to PurchaseOrderHistoryDto                   │
└──────────────────────────┬───────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│ IPurchaseOrderRepository (Domain)                            │
│   + ExistsAsync(int id, ct)                                  │
│   + GetHistoryAsync(int orderId, ct)                         │
└──────────────────────────┬───────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────┐
│ PurchaseOrderRepository (Persistence)                        │
│   ExistsAsync   → DbSet.AnyAsync(o => o.Id == id, ct)        │
│   GetHistoryAsync → Context.PurchaseOrderHistory             │
│                       .AsNoTracking()                        │
│                       .Where(h => h.PurchaseOrderId == id)   │
│                       .OrderByDescending(h => h.ChangedAt)   │
│                       .ToListAsync(ct)                       │
└──────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Two repository methods (existence + history) versus one fused method

**Options considered:**
- (A) Single method `GetHistoryAsync(int id)` that returns `null` when the order doesn't exist and `IReadOnlyList<…>` otherwise.
- (B) Two methods: `ExistsAsync(int id)` and `GetHistoryAsync(int id)`, with the handler coordinating.
- (C) Inline the existence check inside `GetPurchaseOrderHistoryHandler` via `DbSet.AnyAsync` — but that leaks EF Core into the Application layer.

**Chosen approach:** (B). Add a new `ExistsAsync(int id, CancellationToken)` to `IPurchaseOrderRepository` and use it alongside `GetHistoryAsync`.

**Rationale:** Each repository method has one clear responsibility. `GetHistoryAsync` returning `IReadOnlyList<PurchaseOrderHistory>` cleanly expresses "history rows for an order" — using `null` to encode "order not found" overloads the return type and forces every future caller to know the convention. The existence check is one round-trip and stays on the hot path only when the history is empty. The existing `OrderNumberExistsAsync` in the same repository file demonstrates the project already uses dedicated existence methods. (C) violates the existing layering: handlers never touch `DbSet` directly anywhere in the codebase — they go through `IPurchaseOrderRepository`.

#### Decision 2: Handler returns `ListResponse<PurchaseOrderHistoryDto>` directly

**Options considered:**
- (A) Define a bespoke `GetPurchaseOrderHistoryResponse : BaseResponse` with `Items`/`TotalCount` properties (mirrors `GetPurchaseOrderByIdResponse` pattern).
- (B) Type the handler as `IRequestHandler<GetPurchaseOrderHistoryRequest, ListResponse<PurchaseOrderHistoryDto>>` and reuse the shared envelope.

**Chosen approach:** (B). The handler returns `ListResponse<PurchaseOrderHistoryDto>` directly.

**Rationale:** The endpoint's HTTP contract is `ListResponse<PurchaseOrderHistoryDto>` and must remain so (NFR-3). A bespoke response class would add a type that the controller would have to remap to `ListResponse<…>` anyway — re-creating exactly the remapping the spec is trying to remove. `ListResponse<T>` is the existing project convention for list-shaped responses and already supports the `(ErrorCodes, params)` constructor used for the not-found path. **Note on spec amendment:** FR-3 calls for a dedicated `GetPurchaseOrderHistoryResponse` type. I recommend amending it (see Specification Amendments) — the rest of the spec already implies `ListResponse<PurchaseOrderHistoryDto>` is the wire shape.

#### Decision 3: Ordering applied in the repository (not the handler)

**Options considered:**
- (A) Order by `ChangedAt` descending in the repository's EF query (executes server-side).
- (B) Return unordered rows and let the handler apply `OrderByDescending` in memory (matches current `GetPurchaseOrderByIdHandler.cs:88`).

**Chosen approach:** (A), as the spec already requires in FR-1.

**Rationale:** Server-side ordering is one less array allocation, deterministic, and the `ChangedAt` column on `PurchaseOrderHistory` is the natural sort. The handler stays a pure projection. This intentionally diverges from `GetPurchaseOrderByIdHandler` which sorts in memory — that handler can't easily push the sort down because it loads via `Include`. The new path has no such constraint.

#### Decision 4: `AsNoTracking()` on the history query

**Chosen approach:** Use `AsNoTracking()` on the history query.

**Rationale:** The endpoint is read-only; entities never need to be tracked. The project already uses `AsNoTracking()` in read-side repositories (`BankStatementImportRepository.cs:25`, `LeafletDocumentRepository.cs:56,63`). Skipping the change tracker is a clean win on a list query.

## Implementation Guidance

### Directory / Module Structure

New files:

```
backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/
  ├─ GetPurchaseOrderHistoryRequest.cs
  └─ GetPurchaseOrderHistoryHandler.cs

backend/test/Anela.Heblo.Tests/Features/Purchase/
  └─ GetPurchaseOrderHistoryHandlerTests.cs
```

Modified files:

```
backend/src/Anela.Heblo.Domain/Features/Purchase/IPurchaseOrderRepository.cs
  + ExistsAsync(int id, CancellationToken)
  + GetHistoryAsync(int orderId, CancellationToken)

backend/src/Anela.Heblo.Persistence/Purchase/PurchaseOrders/PurchaseOrderRepository.cs
  + ExistsAsync implementation
  + GetHistoryAsync implementation

backend/src/Anela.Heblo.API/Controllers/PurchaseOrdersController.cs
  - lines 130–149: dispatch GetPurchaseOrderHistoryRequest, return response directly

backend/test/Anela.Heblo.Tests/...
  - Repository integration test for emitted SQL (no JOIN to PurchaseOrders/Lines)
```

No new project references, no DI changes, no migration.

### Interfaces and Contracts

**Domain — `IPurchaseOrderRepository` (additions):**

```csharp
Task<bool> ExistsAsync(int id, CancellationToken cancellationToken = default);

Task<IReadOnlyList<PurchaseOrderHistory>> GetHistoryAsync(
    int orderId,
    CancellationToken cancellationToken = default);
```

**Application — `GetPurchaseOrderHistoryRequest`:**

```csharp
public record GetPurchaseOrderHistoryRequest(int Id)
    : IRequest<ListResponse<PurchaseOrderHistoryDto>>;
```
(Internal MediatR type — `record` matches `GetPurchaseOrderByIdRequest` precedent.)

**Application — `GetPurchaseOrderHistoryHandler` (signature):**

```csharp
public class GetPurchaseOrderHistoryHandler
    : IRequestHandler<GetPurchaseOrderHistoryRequest, ListResponse<PurchaseOrderHistoryDto>>
{
    public GetPurchaseOrderHistoryHandler(
        ILogger<GetPurchaseOrderHistoryHandler> logger,
        IPurchaseOrderRepository repository) { … }
}
```

**Controller (replacement body for lines 130–149):**

```csharp
[HttpGet("{id:int}/history")]
public async Task<ActionResult<ListResponse<PurchaseOrderHistoryDto>>> GetPurchaseOrderHistory(
    [FromRoute] int id,
    CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetPurchaseOrderHistoryRequest(id), cancellationToken);
    return HandleResponse(response);
}
```

### Data Flow

**Existing order with history:**
1. Controller dispatches `GetPurchaseOrderHistoryRequest(id)`.
2. Handler logs entry, calls `ExistsAsync(id)` → `true`.
3. Handler calls `GetHistoryAsync(id)` → ordered list of `PurchaseOrderHistory`.
4. Handler projects each row into `PurchaseOrderHistoryDto`.
5. Handler returns `new ListResponse<PurchaseOrderHistoryDto> { Items = …, TotalCount = … }`.
6. Controller's `HandleResponse` returns HTTP 200 with the envelope.

**Existing order with no history:**
1–2. Same as above.
3. `GetHistoryAsync` returns empty list.
4. Handler returns `ListResponse` with `Items = []`, `TotalCount = 0`. HTTP 200.

**Non-existent order:**
1. Controller dispatches request.
2. `ExistsAsync` returns `false`.
3. Handler logs warning and returns `new ListResponse<PurchaseOrderHistoryDto>(ErrorCodes.PurchaseOrderNotFound, new Dictionary<string,string>{{"Id", id.ToString()}})`.
4. Controller's `HandleResponse` returns the structured not-found response (same shape and code as today).

**SQL emitted:** worst case two statements — `SELECT EXISTS … FROM PurchaseOrders WHERE Id = @id` and `SELECT … FROM PurchaseOrderHistory WHERE PurchaseOrderId = @id ORDER BY ChangedAt DESC`. No JOIN to `PurchaseOrders`, no touch on `PurchaseOrderLines`, no supplier read, no material-catalog read.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| EF Core query unexpectedly includes a JOIN to `PurchaseOrders` (e.g. due to a misconfigured navigation or shadow property) — would defeat the entire optimisation. | Medium | Integration test that captures the generated SQL (via `IDbCommandInterceptor` or `EF.CompileQuery` + `ToQueryString()`) and asserts neither `PurchaseOrders` nor `PurchaseOrderLines` table names appear. The spec already requires this in FR-1's last bullet — keep it. |
| Behavioural drift: ordering changes for existing clients. | Low | Repository orders by `ChangedAt DESC` — same direction the current handler applies in memory. Add an explicit handler unit test asserting newest-first. |
| Existence-check race: order is deleted between `ExistsAsync` and `GetHistoryAsync`. | Low | History rows are FK-constrained to the order; if the order is deleted, history rows are deleted too. Worst case the user sees an empty list instead of 404 — acceptable for a non-transactional read endpoint. No action needed. |
| Two SQL statements instead of one slightly raises latency on the happy path. | Low | Each statement is a primary-key lookup / indexed FK scan. Combined cost is dominated by the removed `PurchaseOrderLine` materialisation, so net latency drops substantially. No mitigation needed; mention in PR description. |
| `OldValue` / `NewValue` may contain sensitive content; the DTO already exposes them today. | Low | Out of scope for this refactor (no contract change). NFR-2 already forbids logging these. |
| Future contributors copy `GetPurchaseOrderByIdHandler`'s structure (which loads everything) instead of this thin handler. | Low | Keep the new handler small (<40 lines) and put a one-line comment in `GetPurchaseOrderByIdHandler` (or PR description) noting that history-only callers must use `GetPurchaseOrderHistory`. *(Optional — don't add the comment if it would be the only comment in the file.)* |

## Specification Amendments

1. **FR-3 — response type:** The spec proposes a dedicated `GetPurchaseOrderHistoryResponse` class. Replace it with the existing `ListResponse<PurchaseOrderHistoryDto>` envelope: the handler's `TResponse` is `ListResponse<PurchaseOrderHistoryDto>`, no bespoke response class is created. This removes a redundant DTO and lets the controller stop manually remapping — which is half the point of the refactor. The HTTP wire shape is unchanged.

2. **FR-2 — existence check location:** The spec mentions "lightweight query — e.g. `DbSet.AnyAsync(o => o.Id == orderId, ct)`". Move this from a handler-inline call to a new `IPurchaseOrderRepository.ExistsAsync(int id, CancellationToken)` method. The handler must not depend on EF Core (`DbSet` is not visible at the Application layer in this codebase). Existing precedent: `OrderNumberExistsAsync` on the same repository.

3. **FR-1 — `AsNoTracking()`:** Spec mentions this in "API / Interface Design" but not in FR-1's acceptance criteria. Add: "`GetHistoryAsync` calls `AsNoTracking()` on the query."

4. **Controller wiring (FR-4):** The replacement body should be ~3 lines using the inherited `HandleResponse(response)` helper — no manual `if (!response.Success)` branch. Current code's manual remap exists only because `GetPurchaseOrderByIdResponse` ≠ `ListResponse<…>`. After the change, the response type already matches the action's return type.

5. **Test scope:** Add explicit test cases beyond NFR coverage:
   - Handler test: order exists, history empty → HTTP-200-equivalent `ListResponse` with `Items.Count == 0`.
   - Handler test: order missing → `ErrorCode == PurchaseOrderNotFound`, `Params["Id"]` populated.
   - Handler test: history returned in `ChangedAt DESC` order (uses the repository ordering — does **not** re-sort in the handler).
   - Repository test: `ExistsAsync` returns `false` for unknown id, `true` for known id.
   - Repository SQL-shape test (per FR-1 last bullet): emitted SQL string for `GetHistoryAsync` contains `PurchaseOrderHistory` and contains neither `"PurchaseOrders"` nor `"PurchaseOrderLines"` (case-insensitive).

## Prerequisites

None. No migration, no config change, no new package, no DI change required — `IPurchaseOrderRepository` is already registered as `Scoped` in `PersistenceModule.cs:171` and the augmented interface is picked up automatically. Implementation can start immediately.
```