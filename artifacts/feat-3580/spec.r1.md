# Specification: Deduplicate PurchaseOrderHistory → PurchaseOrderHistoryDto Mapping

## Summary
The mapping from the `PurchaseOrderHistory` domain entity to `PurchaseOrderHistoryDto` is copy-pasted verbatim across three MediatR handlers in the Purchase module. This spec introduces a single static factory method, `PurchaseOrderHistoryDto.FromDomain`, and updates all three call sites to use it, following the existing `PurchaseOrderLineDto.FromLine` pattern already established in the same module.

## Background
`PurchaseOrderLineDto` already solves the identical problem with a static factory method (`FromLine`). `PurchaseOrderHistoryDto` has no equivalent, so each handler that needs to map history entries re-implements the same six-field assignment inline. If `PurchaseOrderHistory` gains, loses, or renames a field, all three sites must be updated in lockstep; a missed site silently produces incorrect API responses. This is a pure mechanical refactor with no behavior change — it centralizes the mapping logic to a single source of truth.

## Functional Requirements

### FR-1: Add `PurchaseOrderHistoryDto.FromDomain` static factory
Add a static factory method to `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/PurchaseOrderHistoryDto.cs` that maps a `PurchaseOrderHistory` domain entity to a `PurchaseOrderHistoryDto`, mirroring the style of `PurchaseOrderLineDto.FromLine`:

```csharp
public static PurchaseOrderHistoryDto FromDomain(PurchaseOrderHistory h) => new()
{
    Id = h.Id,
    Action = h.Action,
    OldValue = h.OldValue,
    NewValue = h.NewValue,
    ChangedAt = h.ChangedAt,
    ChangedBy = h.ChangedBy
};
```

This requires adding a `using Anela.Heblo.Domain.Features.Purchase;` import to the contract file (the same namespace `PurchaseOrderLineDto.cs` already imports for `PurchaseOrderLine`).

**Acceptance criteria:**
- `PurchaseOrderHistoryDto.FromDomain(PurchaseOrderHistory h)` exists as a `public static` method returning a new `PurchaseOrderHistoryDto`.
- All six properties (`Id`, `Action`, `OldValue`, `NewValue`, `ChangedAt`, `ChangedBy`) are mapped 1:1 with no transformation, exactly matching the three existing inline blocks.
- Signature and body style match `PurchaseOrderLineDto.FromLine` (single-expression static factory using object initializer).

### FR-2: Replace the three duplicated mapping sites with calls to the factory
Replace each inline `Select(h => new PurchaseOrderHistoryDto { ... })` block with `Select(PurchaseOrderHistoryDto.FromDomain)`, preserving all surrounding logic (ordering, assignment target) exactly as-is:

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderHandler.cs` (lines 114–122, inside `MapToResponse`): `purchaseOrder.History.Select(PurchaseOrderHistoryDto.FromDomain).ToList()`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdHandler.cs` (lines 71–79): `purchaseOrder.History.Select(PurchaseOrderHistoryDto.FromDomain).OrderByDescending(h => h.ChangedAt).ToList()`
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryHandler.cs` (lines 37–47): `history.Select(PurchaseOrderHistoryDto.FromDomain).ToList()`

**Acceptance criteria:**
- No handler in the Purchase module constructs a `PurchaseOrderHistoryDto` via an inline object initializer (`new PurchaseOrderHistoryDto { ... }`) anymore — a search for `new PurchaseOrderHistoryDto` under `Features/Purchase/UseCases/` returns no matches.
- All three handlers compile and their existing unit tests (`CreatePurchaseOrderHandlerTests.cs`, `GetPurchaseOrderHistoryHandlerTests.cs`, and any covering `GetPurchaseOrderByIdHandler`) pass unmodified, since output shape and values are identical to before.
- `GetPurchaseOrderByIdHandler`'s `OrderByDescending(h => h.ChangedAt)` ordering step is preserved exactly (applied after the `Select`, on the DTO's `ChangedAt`, not moved or altered).
- No other change to method signatures, logging, response construction, or unrelated code in the three handler files.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected; this is a like-for-like replacement of an inline lambda with a static method reference in a `Select`. No new allocations, loops, or I/O introduced.

### NFR-2: Security
None. No change to data exposed, authorization, or input handling — the DTO shape and field values are unchanged.

## Data Model
No changes to `PurchaseOrderHistory` (domain entity) or `PurchaseOrderHistoryDto` (contract) fields. `PurchaseOrderHistoryDto` gains one static method; its data shape is unchanged:

| Field | Type | Source |
|---|---|---|
| Id | int | `PurchaseOrderHistory.Id` |
| Action | string | `PurchaseOrderHistory.Action` |
| OldValue | string? | `PurchaseOrderHistory.OldValue` |
| NewValue | string? | `PurchaseOrderHistory.NewValue` |
| ChangedAt | DateTime | `PurchaseOrderHistory.ChangedAt` |
| ChangedBy | string | `PurchaseOrderHistory.ChangedBy` |

## API / Interface Design
No API contract changes. This is an internal mapping refactor; JSON shape returned by `CreatePurchaseOrder`, `GetPurchaseOrderById`, and `GetPurchaseOrderHistory` endpoints is byte-for-byte identical before and after.

New internal interface:
```csharp
public static PurchaseOrderHistoryDto PurchaseOrderHistoryDto.FromDomain(PurchaseOrderHistory h)
```

## Dependencies
- `PurchaseOrderHistory` domain entity (`backend/src/Anela.Heblo.Domain/Features/Purchase/PurchaseOrderHistory.cs`) — read-only dependency, no changes needed.
- Existing `PurchaseOrderLineDto.FromLine` pattern as the style reference (no code dependency, just precedent).

## Out of Scope
- Any change to `PurchaseOrderHistory`, `PurchaseOrderLineDto`, or other Purchase contracts/entities.
- Introducing a general-purpose mapping library (e.g., AutoMapper) for this or other DTOs.
- Any change to the three handlers' business logic, logging, error handling, or response structure beyond the mapping call site.
- Adding new unit tests specifically for `FromDomain` (existing handler tests already exercise it indirectly); adding a small dedicated test is left to implementer discretion but is not required by this spec.

## Open Questions
None.

## Status: COMPLETE
