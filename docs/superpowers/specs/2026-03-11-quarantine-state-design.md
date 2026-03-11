# Quarantine State Design

**Date:** 2026-03-11
**Status:** Approved
**Topic:** Replace Karantena location with dedicated Quarantine transport box state

---

## Summary

"Karantena" is currently a location option within the Reserve state. This design replaces it with a dedicated `Quarantine` state on transport boxes — no location required — and adds a separate `Quarantine` stock metric surfaced in Catalog and Manufacturing views.

---

## Context

### Current Model

- `TransportBoxState` enum: `New, Opened, InTransit, Received, InSwap, Stocked, Closed, Error, Reserve`
- `TransportBoxLocation` enum: `Kumbal, Relax, SkladSkla, Karantena`
- When a box enters Reserve state, a physical location must be selected (including Karantena as one option)
- Items in Reserve boxes contribute to `StockData.Reserve` metric

### Problem

Karantena is semantically distinct from the other reserve locations (Kumbal, Relax, SkladSkla). It represents a quality/compliance hold, not a storage choice. Modeling it as a location conflates two different concepts and prevents separate stock visibility for quarantined items.

---

## Design

### Domain Layer

**`TransportBoxState` enum** — add `Quarantine` after `Reserve`:
```csharp
Reserve,
Quarantine
```

**`TransportBoxLocation` enum** — remove `Karantena`:
```csharp
public enum TransportBoxLocation { Kumbal, Relax, SkladSkla }
```

**`TransportBox` entity** — the following changes are required:

1. Add `ToQuarantine()` method (no location parameter). Explicitly set `Location = null` inside the method to ensure that any stale location value is cleared, regardless of what the handler may have written:
```csharp
public void ToQuarantine(DateTime date, string userName)
{
    Location = null;
    ChangeState(TransportBoxState.Quarantine, date, userName, TransportBoxState.Opened);
}
```

2. Add `IsInQuarantinePredicate` and `IsInQuarantine` property (parallel to `IsInReservePredicate`/`IsInReserve`):
```csharp
public static Expression<Func<TransportBox, bool>> IsInQuarantinePredicate =
    b => b.State == TransportBoxState.Quarantine;

public bool IsInQuarantine => State == TransportBoxState.Quarantine;
```

3. Update `Receive()` to accept `Quarantine` as a valid source state (alongside `InTransit` and `Reserve`):
```csharp
ChangeState(TransportBoxState.Received, date, userName,
    TransportBoxState.InTransit, TransportBoxState.Reserve, TransportBoxState.Quarantine);
```

4. Update `RevertToOpened()` to allow `Quarantine` as a valid source state (alongside `InTransit` and `Reserve`).

5. Register `quarantineNode` in the static `_transitions` dictionary with outbound transitions:
   - `Quarantine → Received` (quarantine cleared, stock enters warehouse)
   - `Quarantine → Opened` (reverted back for re-packing)

6. Register `Opened → Quarantine` transition on the `openedNode` (no condition, no location required).

**Intentional asymmetries vs. Reserve:**
- `ToQuarantine()` only accepts `Opened` as source — `Error → Quarantine` is out of scope. `ToReserve()` accepts both `Opened` and `Error`; this asymmetry is deliberate. `Error → Quarantine` can be added in a future iteration if needed.
- `InSwap` state has no transitions registered in `_transitions` in the current implementation (pre-existing omission, out of scope for this change).

**`StockData`** — add `Quarantine` property:
```csharp
public decimal Quarantine { get; set; }
```
`Total` updated to: `Available + Reserve + Quarantine`

`EffectiveStock` (`Available + Ordered`) is intentionally **not** updated — quarantined items are on hold and must not factor into purchase planning until cleared.

Note: `StockData` is currently declared as a `record`, which is a pre-existing violation of the project's DTO class rule (CLAUDE.md rule 3). Changing it to a `class` is out of scope for this change — add the `Quarantine` property to the existing record as-is.

### Application Layer

**`ChangeTransportBoxStateHandler`:**
- Add `HandleOpenToQuarantine()` method (no location validation, parallel to `HandleOpenToReserve()`)
- Add `(TransportBoxState.Quarantine, TransportBoxState.Received) => HandleReceived` entry to the `CallBackMap` so stock-up operations are created when a Quarantine box is received
- Location safety: the handler contains a second location assignment block (after the CallBackMap lookup) that silently writes `request.Location` into `box.Location` if it is non-empty. For `Opened → Quarantine`, `ToQuarantine()` explicitly sets `Location = null`, which overrides any stale client value. No additional handler guard is required, but this is the mechanism — do not remove the `Location = null` from `ToQuarantine()`.

**`GetTransportBoxByIdHandler`** — add Czech label:
```csharp
TransportBoxState.Quarantine => "V karanténě"
```

**`TransportBoxDto`** — add `IsInQuarantine` property (backed by `transportBox.IsInQuarantine`, parallel to `IsInReserve`).

**`CatalogRepository`:**
- Add `GetProductsInQuarantine()` using `IsInQuarantinePredicate` (same pattern as `GetProductsInReserve()`)
- Keep `RefreshReserveData()` name — call `GetProductsInQuarantine()` from within it (do not rename the method)
- Add `CachedInQuarantineData` field and `QuarantineLoadDate` property (parallel to `CachedInReserveData` / `ReserveLoadDate`)
- Include `QuarantineLoadDate` in the hardcoded `loadDates` array inside `ChangesPendingForMerge` so stale quarantine data triggers a pending-merge flag. Note: `ManufactureCostLoadDate` is already missing from this array (pre-existing bug, out of scope)
- Apply per product:
  ```csharp
  product.Stock.Quarantine = CachedInQuarantineData.ContainsKey(product.ProductCode)
      ? CachedInQuarantineData[product.ProductCode] : 0;
  ```

**`ICatalogRepository`** — add `QuarantineLoadDate` property to the interface (parallel to `ReserveLoadDate`). The quarantine refresh is called from within `RefreshReserveData()` so no new interface method is needed.

### Infrastructure Layer

**No database migration required:**
- `Quarantine` enum value added at the end — existing int mappings unaffected
- `Location` is stored as `string?`, so removing `Karantena` from the enum has no database impact
- Existing boxes in Reserve state with location "Karantena" remain unchanged

### Frontend Layer

**`LocationSelectionModal.tsx`** — remove `Karantena` from `LOCATIONS` array.

**State transition UI** — add Quarantine as a selectable transition from Opened state. No location modal shown for Quarantine transition.

**`TransportBoxInfo.tsx`** — add label `"V karanténě"` for Quarantine state. Location field remains hidden for Quarantine (Quarantine boxes have `Location = null` throughout their lifetime).

**`useTransportBoxes.ts`** — no structural changes; location is already optional in the hook.

**Stock metric additions** — add `quarantine` wherever `reserve` appears:
- `InventoryList.tsx` — new Quarantine quantity column and filter option
- `ManufacturingStockAnalysis.tsx` — new Quarantine column in analysis table, `ManufacturingStockSortBy.Quarantine` enum value

**API client** — regenerate TypeScript client after backend changes to pick up new `quarantine` field in `StockData` and new `Quarantine` state value.

---

## Data Migration

None. Existing boxes in Reserve+Karantena stay as-is. Karantena is simply no longer selectable going forward.

---

## Files Affected

### Backend
- `Domain/Features/Logistics/Transport/TransportBoxState.cs`
- `Domain/Features/Logistics/Transport/TransportBoxLocation.cs`
- `Domain/Features/Logistics/Transport/TransportBox.cs`
- `Domain/Features/Catalog/Stock/StockData.cs`
- `Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- `Application/Features/Logistics/UseCases/GetTransportBoxById/GetTransportBoxByIdHandler.cs`
- `Application/Features/Logistics/Contracts/TransportBoxDto.cs`
- `Application/Features/Catalog/CatalogRepository.cs`
- `Domain/Features/Catalog/ICatalogRepository.cs`

### Frontend
- `src/components/pages/LocationSelectionModal.tsx`
- `src/components/transport/box-detail/TransportBoxInfo.tsx`
- `src/api/hooks/useTransportBoxes.ts` (minimal / no change)
- `src/components/pages/InventoryList.tsx`
- `src/components/pages/ManufacturingStockAnalysis.tsx`
- Regenerate OpenAPI TypeScript client

---

## Success Criteria

- Transport boxes can be transitioned to Quarantine state from Opened, without selecting a location
- Quarantine boxes can transition to Received or back to Opened
- `StockData.Quarantine` is populated from boxes in Quarantine state
- Quarantine stock metric is visible in Catalog and Manufacturing Stock Analysis views
- Karantena is no longer selectable as a location in the UI
- Existing Reserve+Karantena boxes are unaffected
- All backend tests pass (`dotnet test`)
- All frontend tests pass (`npm test`)
- `dotnet format` passes with no violations
