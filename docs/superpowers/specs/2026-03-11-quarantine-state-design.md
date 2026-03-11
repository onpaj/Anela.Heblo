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

**`TransportBox` entity** — add `ToQuarantine()` method (no location parameter):
```csharp
public void ToQuarantine(DateTime date, string userName)
{
    ChangeState(TransportBoxState.Quarantine, date, userName, TransportBoxState.Opened);
}
```

**State machine transitions:**
- `Opened → Quarantine` (no condition, no location required)
- `Quarantine → Received` (quarantine cleared, stock enters warehouse)
- `Quarantine → Opened` (reverted back for re-packing)

**`StockData`** — add `Quarantine` property:
```csharp
public decimal Quarantine { get; set; }
```
`Total` updated to: `Available + Reserve + Quarantine`

### Application Layer

**`ChangeTransportBoxStateHandler`** — add `HandleOpenToQuarantine()` method (no location validation, parallel to `HandleOpenToReserve()`). `HandleReceived()` requires no changes.

**`GetTransportBoxByIdHandler`** — add Czech label:
```csharp
TransportBoxState.Quarantine => "V karanténě"
```

**`TransportBoxDto`** — add `IsInQuarantine` property (parallel to `IsInReserve`).

**`CatalogRepository`:**
- Add `GetProductsInQuarantine()` (filters boxes by `Quarantine` state, same pattern as `GetProductsInReserve()`)
- Call from `RefreshReserveData()` (or rename to `RefreshStockStateData()`)
- Apply per product:
  ```csharp
  product.Stock.Quarantine = CachedInQuarantineData.ContainsKey(product.ProductCode)
      ? CachedInQuarantineData[product.ProductCode] : 0;
  ```

### Infrastructure Layer

**No database migration required:**
- `Quarantine` enum value added at the end — existing int mappings unaffected
- `Location` is stored as `string?`, so removing `Karantena` from the enum has no database impact
- Existing boxes in Reserve state with location "Karantena" remain unchanged

### Frontend Layer

**`LocationSelectionModal.tsx`** — remove `Karantena` from `LOCATIONS` array.

**State transition UI** — add Quarantine as a selectable transition from Opened state. No location modal shown for Quarantine transition.

**`TransportBoxInfo.tsx`** — add label `"V karanténě"` for Quarantine state. Location field remains hidden for Quarantine.

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
