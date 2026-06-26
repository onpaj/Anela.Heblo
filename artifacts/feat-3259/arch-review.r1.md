# Architecture Review: IEshopOrderClient Dead-Method Cleanup (ShoptetOrders)

## Skip Design: true

## Architectural Fit Assessment

This is a pure dead-code removal on the Application-layer port `IEshopOrderClient` and its Adapter-layer implementation `ShoptetOrderClient`. The change aligns exactly with Vertical Slice Architecture and the YAGNI rule stated in the development guidelines. No module boundary is crossed, no new abstraction is introduced, and no production request path is touched.

Integration points are minimal:
- `IEshopOrderClient` (Application layer, `Anela.Heblo.Application/Features/ShoptetOrders/`) — the interface being trimmed.
- `ShoptetOrderClient` (Adapter layer, `Anela.Heblo.Adapters.ShoptetApi/Orders/`) — the concrete implementation being trimmed.
- `EshopOrderInfo.cs` (Application layer, same folder) — DTO to be deleted.
- `BlockOrderProcessingHandlerTests.cs` (unit test project) — contains the sole `Times.Never` assertion on `SetInternalNoteAsync` that must be removed.

Verified: a codebase-wide search of `backend/src/` finds zero callers of all three methods and zero references to `EshopOrderInfo` outside its own definition file and `ShoptetOrderClient.cs`.

## Proposed Architecture

### Component Overview

```
Application layer
  IEshopOrderClient          (interface — 3 dead methods removed, 10 remain)
  EshopOrderInfo.cs          (deleted — sole purpose was typing GetRecentOrdersByEmailAsync)

Adapter layer
  ShoptetOrderClient         (implementation — 3 method bodies removed)
    SetInternalNoteAsync     → deleted (lines 163-177)
    GetRecentOrdersByEmailAsync → deleted (lines 203-215), including MapToOrderInfo helper (lines 301-310)
    GetOrderStatusNamesAsync → deleted (lines 217-226)

Adapter layer / Model
  ShoptetEshopResponse.cs    (file deleted — exclusively used by GetOrderStatusNamesAsync)
  UpdateNotesRequest.cs      (CreateOrderRemarkRequest + CreateOrderRemarkData deleted — exclusively
                              used by SetInternalNoteAsync)

Test project
  BlockOrderProcessingHandlerTests.cs
    Lines 182-184            → the Times.Never verification on SetInternalNoteAsync is removed
```

The ten remaining interface members are untouched. The four test-only methods (`CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`, `GetRecentOrdersAsync`) stay on the interface per the explicit out-of-scope decision in the spec.

### Key Design Decisions

#### Decision 1: Delete ShoptetEshopResponse.cs entirely
**Options considered:**
- Keep the file in case `GET /api/eshop?include=orderStatuses` is needed later.
- Delete it because its sole caller is the dead `GetOrderStatusNamesAsync`.

**Chosen approach:** Delete the file.

**Rationale:** `ShoptetEshopResponse`, `ShoptetEshopData`, `ShoptetEshopDetail`, and `ShoptetOrderStatus` are referenced only from `GetOrderStatusNamesAsync` (confirmed by codebase-wide grep). There are no other callers in src or test. Retaining dead model classes for speculative future use violates YAGNI and is inconsistent with the motivation for this entire task. If `GetOrderStatusNamesAsync` is ever needed again, the model classes are trivial to recreate from the Shoptet API contract.

#### Decision 2: Delete UpdateNotesRequest.cs content (CreateOrderRemarkRequest + CreateOrderRemarkData)
**Options considered:**
- Keep the types in `UpdateNotesRequest.cs` because the filename suggests broader HTTP body coverage.
- Delete them because `CreateOrderRemarkRequest` / `CreateOrderRemarkData` are exclusively used by `SetInternalNoteAsync`.

**Chosen approach:** Delete both types from `UpdateNotesRequest.cs`. If the file becomes empty, delete the file.

**Rationale:** After `SetInternalNoteAsync` is removed, `CreateOrderRemarkRequest` and `CreateOrderRemarkData` have zero callers anywhere in the codebase. The misleading filename `UpdateNotesRequest.cs` is an artifact of the old `POST /api/orders/{code}/history` approach. The current note/remark mechanism uses `UpdateEshopRemarkRequest` (in `UpdateEshopRemarkRequest.cs`) and `UpdateAdditionalFieldRequest` (in `UpdateAdditionalFieldRequest.cs`), both of which have live callers and are unaffected.

#### Decision 3: Remove the Times.Never test assertion, do not replace it
**Options considered:**
- Remove the assertion and add a comment explaining why it was removed.
- Simply delete lines 182-184 of `BlockOrderProcessingHandlerTests.cs`.

**Chosen approach:** Delete the three lines (the `clientMock.Verify` block checking `SetInternalNoteAsync` was never called), no replacement.

**Rationale:** The assertion verified a negative — that a method which no longer exists on the interface is not called. Once the method is gone, the assertion cannot compile, and the intent (ensure the old history-entry approach is not invoked) is structurally enforced by the interface itself. A comment would add noise. The test's meaningful assertions (`UpdateStatusAsync` once, `UpdateEshopRemarkAsync` once with the concatenated string) remain intact.

## Implementation Guidance

### Directory / Module Structure

Files to delete entirely:
```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/EshopOrderInfo.cs
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/ShoptetEshopResponse.cs
```

Files to delete types from (if the file becomes empty after deletion, delete the file too):
```
backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/Model/UpdateNotesRequest.cs
  — remove: CreateOrderRemarkRequest class, CreateOrderRemarkData class
  — file will be empty → delete it
```

Files to edit (method removal only, no structural changes):
```
backend/src/Anela.Heblo.Application/Features/ShoptetOrders/IEshopOrderClient.cs
  — remove: Task SetInternalNoteAsync(...) declaration (line 8)
  — remove: Task<List<EshopOrderInfo>> GetRecentOrdersByEmailAsync(...) declaration + XML doc (lines 29-33)
  — remove: Task<Dictionary<int, string>> GetOrderStatusNamesAsync(...) declaration + XML doc (lines 34-38)

backend/src/Adapters/Anela.Heblo.Adapters.ShoptetApi/Orders/ShoptetOrderClient.cs
  — remove: SetInternalNoteAsync method body (lines 163-177)
  — remove: GetRecentOrdersByEmailAsync method body (lines 203-215) and the NOTE comment above it (line 201-202)
  — remove: GetOrderStatusNamesAsync method body (lines 217-226)
  — remove: MapToOrderInfo private helper (lines 301-310)
  — remove: the `using` for the now-deleted model types if they become unused
            (ShoptetEshopResponse is in Anela.Heblo.Adapters.ShoptetApi.Orders.Model namespace
             which is already bulk-included; check whether any explicit usings become dangling)

backend/test/Anela.Heblo.Tests/Application/ShoptetOrders/BlockOrderProcessingHandlerTests.cs
  — remove: clientMock.Verify block for SetInternalNoteAsync (lines 182-184)
```

### Interfaces and Contracts

After the change, `IEshopOrderClient` declares exactly 10 methods:

```
CreateOrderAsync            (test-only caller — out of scope to remove)
GetOrderStatusIdAsync       (active: BlockOrderProcessingHandler, ScanPackingOrderHandler)
UpdateStatusAsync           (active: BlockOrderProcessingHandler, CompletePackingOrderHandler)
GetEshopRemarkAsync         (active: BlockOrderProcessingHandler)
UpdateEshopRemarkAsync      (active: BlockOrderProcessingHandler)
DeleteOrderAsync            (test-only caller — out of scope to remove)
GetRecentOrdersAsync        (test-only caller — out of scope to remove)
ListByExternalCodePrefixAsync (test-only caller — out of scope to remove)
MarkAsPackedAsync           (active: CompletePackingOrderHandler)
```

No new types or interfaces are introduced. No contracts change.

### Data Flow

No data flow changes. This task removes unreachable code paths; all surviving paths are unchanged.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `UpdateNotesRequest.cs` deletion breaks a non-obvious reference | Low | Confirmed by grep: `CreateOrderRemarkRequest` and `CreateOrderRemarkData` appear only in `ShoptetOrderClient.cs` (the caller being deleted) and in `UpdateNotesRequest.cs` itself. No test, no other adapter, no integration test references them. |
| `ShoptetEshopResponse` types used by a future `GetOrderStatusNamesAsync` re-addition | Low / acceptable | This is YAGNI by definition. The types are easily regenerated from the Shoptet API contract documented in `docs/integrations/shoptet-api.md`. |
| `MapToOrderInfo` private helper used elsewhere in `ShoptetOrderClient` | None | Confirmed by read: it is called only from `GetRecentOrdersByEmailAsync` (line 213), which is itself deleted. |
| Moq `Times.Never` removal causes a test gap | Low | The interface no longer declares the method; calling it would be a compile error. The structural guarantee is stronger than the runtime assertion. |
| Integration tests break because they call test-only methods on `IEshopOrderClient` | None | `CreateOrderAsync`, `DeleteOrderAsync`, `ListByExternalCodePrefixAsync`, and `GetRecentOrdersAsync` are explicitly out of scope and remain on the interface. Integration tests confirmed to call only those four methods plus `GetRecentOrdersAsync`. |

## Specification Amendments

The spec is accurate and complete. One addition based on code inspection:

**Amendment A — Delete `UpdateNotesRequest.cs` entirely.** The spec states only that `CreateOrderRemarkRequest` and `CreateOrderRemarkData` should be removed if they become unused (implied by FR-1). Code inspection confirms these are the only two types in `UpdateNotesRequest.cs`, so the file becomes empty and should be deleted rather than left as a stub.

**Amendment B — Verify dangling `using` directives.** After removing `SetInternalNoteAsync` and its body, the `CreateOrderRemarkRequest` instantiation in `ShoptetOrderClient.cs` is gone. The `Anela.Heblo.Adapters.ShoptetApi.Orders.Model` namespace is used by surviving code (e.g. `OrderListResponse`, `UpdateStatusRequest`, `UpdateEshopRemarkRequest`), so no namespace-level `using` will become dangling. However, `dotnet format` should be run to catch any remaining implicit references. This is already required by NFR-2 but is worth stating explicitly at the file level.

## Prerequisites

None. This is a pure code deletion. No migrations, no infrastructure changes, no config additions, no feature flags, no external dependencies.

Recommended execution order for a clean build at each step:
1. Delete `EshopOrderInfo.cs` → will break `IEshopOrderClient.cs` and `ShoptetOrderClient.cs` (compile-time signal).
2. Remove the three method declarations from `IEshopOrderClient.cs` (resolves the interface break, exposes implementation break).
3. Remove the three method bodies and `MapToOrderInfo` from `ShoptetOrderClient.cs`.
4. Delete `ShoptetEshopResponse.cs` and `UpdateNotesRequest.cs`.
5. Remove the `Times.Never` assertion from `BlockOrderProcessingHandlerTests.cs`.
6. Run `dotnet build` — must pass with zero errors and zero warnings attributable to this change.
7. Run `dotnet format` — must produce no diff.
8. Run `dotnet test` — all tests must pass.
