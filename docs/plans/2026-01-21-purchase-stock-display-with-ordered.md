# Purchase Stock Display - Include Ordered Quantities

**Date:** 2026-01-21
**Status:** Design Complete, Ready for Implementation

## Overview

Enhance the Purchase Stock Analysis view to display and calculate severity based on **effective stock** (Available + Ordered) instead of just available stock. This provides a more realistic view of material availability for purchase planning.

## Current State

- **Display:** Shows only `Available` stock (Erp/Eshop + Transport)
- **Severity calculation:** Based only on `Available` stock
- **Problem:** Items with low physical stock but high ordered quantities show as "Critical" even though stock is incoming

## Desired State

- **Display:** Shows effective stock with ordered detail when `Ordered > 0`
  - Format: `70 (20)` where 70 = effective, 20 = ordered
  - Tooltip: "Skladem 50, objednáno 20"
- **Severity calculation:** Based on `EffectiveStock` (Available + Ordered)
- **Result:** More accurate severity levels reflecting real stock situation

## Technical Design

### 1. Domain Layer - StockData Enhancement

**File:** `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs`

Add computed property:
```csharp
/// <summary>
/// Effective stock including both available and ordered stock for purchase planning
/// </summary>
public decimal EffectiveStock => Available + Ordered;
```

### 2. Application Layer - DTO Extension

**File:** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`

Add properties to `StockAnalysisItemDto`:
```csharp
public double OrderedStock { get; set; }      // NEW
public double EffectiveStock { get; set; }    // NEW
```

### 3. Handler Update

**File:** `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`

Changes in `AnalyzeStockItem`:
- Use `item.Stock.EffectiveStock` for severity calculation
- Use `item.Stock.EffectiveStock` for stock efficiency calculation
- Use `item.Stock.EffectiveStock` for days until stockout calculation
- Populate DTO with all three values: `AvailableStock`, `OrderedStock`, `EffectiveStock`

Changes in `CalculateSummary`:
- Use `EffectiveStock` for `TotalInventoryValue` calculation

### 4. Frontend Display

**File:** `frontend/src/components/pages/PurchaseStockAnalysis.tsx`

Add helper function:
```typescript
const formatStockDisplay = (available: number, ordered: number, effectiveStock: number) => {
  if (ordered > 0) {
    return {
      mainValue: formatNumber(effectiveStock, 0),
      subValue: `+${formatNumber(ordered, 0)} obj.`,
      tooltip: `Skladem ${formatNumber(available, 0)}, objednáno ${formatNumber(ordered, 0)}`
    };
  }
  return {
    mainValue: formatNumber(available, 0),
    subValue: null,
    tooltip: `Skladem ${formatNumber(available, 0)}`
  };
};
```

Update "Skladem" column to display:
- Main value (effective stock) - bold, black
- Sub value (ordered detail) - smaller, gray (when ordered > 0)
- Tooltip on entire cell

### 5. Severity Logic

No changes to `StockSeverityCalculator` interface/implementation - only the input value changes from `Available` to `EffectiveStock`.

All severity thresholds remain the same:
- **Critical:** Below min OR below 20% of optimal
- **Low:** Between 20-70% of optimal
- **Optimal:** Between 70-150% of optimal
- **Overstocked:** Above 150% of optimal

## Implementation Steps

### Backend
1. ✅ Add `EffectiveStock` property to `StockData.cs`
2. ✅ Add `OrderedStock` and `EffectiveStock` to `StockAnalysisItemDto`
3. ✅ Update `GetPurchaseStockAnalysisHandler` to use `EffectiveStock`
4. ✅ Update unit tests in `StockSeverityCalculatorTests.cs`
5. ✅ Update integration tests in `GetPurchaseStockAnalysisHandlerTests.cs`
6. ✅ Run `dotnet test` - all tests pass
7. ✅ Run `dotnet format` - code formatting validated

### Frontend
1. ✅ Regenerate TypeScript API client (automatic via prebuild)
2. ✅ Add `formatStockDisplay` helper function
3. ✅ Update "Skladem" column display
4. ✅ Update unit tests in `PurchaseStockAnalysis.test.tsx`
5. ✅ Run `npm test` - all tests pass
6. ✅ Run `npm run build` - build succeeds

### E2E Validation
1. ✅ Run `./scripts/run-playwright-tests.sh` against staging
2. ✅ Verify visual display of ordered quantities
3. ✅ Verify tooltip content
4. ✅ Verify severity badges reflect effective stock

## Acceptance Criteria

- [ ] When `Ordered = 0`, display shows only available stock (e.g., "50")
- [ ] When `Ordered > 0`, display shows "70 (20)" format (effective + ordered detail)
- [ ] Tooltip shows breakdown: "Skladem 50, objednáno 20"
- [ ] Severity calculation uses `EffectiveStock` for all states
- [ ] Critical/Low items with high ordered quantities move to better severity
- [ ] Stock efficiency percentage reflects effective stock
- [ ] Days until stockout calculation uses effective stock
- [ ] Total inventory value includes ordered stock
- [ ] All backend tests pass
- [ ] All frontend tests pass
- [ ] E2E tests validate visual display

## Testing Strategy

### Unit Tests
- `StockSeverityCalculatorTests`: Verify severity with ordered stock
- `GetPurchaseStockAnalysisHandlerTests`: Verify DTO population and calculations
- `PurchaseStockAnalysis.test.tsx`: Verify display logic

### Integration Tests
- Handler returns correct `OrderedStock` and `EffectiveStock` values
- Severity changes appropriately with ordered quantities

### E2E Tests
- Visual validation on staging environment
- Tooltip content verification
- Responsive design check

## Migration Notes

- **No database migrations required** - `Ordered` field already exists in `StockData`
- **No breaking changes** - Adding new properties to DTO (backward compatible)
- **API client regeneration** - Automatic via build process
- **Data migration** - None needed

## Dependencies

None - standalone change within Purchase module.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Severity becomes too optimistic with large orders | Orders represent real incoming stock - this is desired behavior |
| Frontend display too cluttered | Following existing UX pattern (main value + detail) |
| Performance impact | Minimal - computed property, no additional queries |

## Future Enhancements

- Color-code ordered quantity indicator (green = recent order, yellow = old order)
- Show expected delivery date for ordered stock
- Filter by "has ordered stock" criterion
- Separate severity for "physically critical but ordered" items
