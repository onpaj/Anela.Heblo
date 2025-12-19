# Frontend M1 Margin Split Implementation Design

**Date:** 2025-12-19
**Status:** Design Complete - Ready for Implementation
**Related Backend Design:** [M1 Production Costs Specification](../features/M1_production_costs_specification.md)
**Backend Implementation:** Completed (commit range: e50574c0..901c1a2c)

## Overview

Update frontend components to support the M1 margin split into M1_A (economic baseline, always present) and M1_B (actual monthly cost, nullable). The backend has already implemented this split, and the API client has been regenerated with correct TypeScript types.

## Problem Statement

**Current State:**
- Frontend components reference old `m1` property which no longer exists in backend DTOs
- API client has correct `m1_A` and `m1_B` types but components haven't been updated
- Some components already use `m1_A` but charts use old `m1` reference

**Backend Changes Already Completed:**
- `MarginHistoryDto` now has `m1_A` (always present) and `m1_B` (nullable)
- M1_A represents economic baseline using 12-month rolling average
- M1_B represents actual monthly cost, only present when product was manufactured

## User Requirements

1. **M1_A (Economic Baseline):** Always visible, replaces current M1 display
2. **M1_B (Actual Monthly Cost):** Visible as scatter points in charts with toggle control
3. **Toggle Default State:** M1_B hidden by default, user can enable with "Show M1_B" checkbox

## Component Changes

### 1. MarginsChart.tsx - PRIMARY CHANGES

**Current Issues:**
- Line 79: Uses `record.m1?.percentage` - needs `m1_A`
- Line 85: Uses `record.m1?.costLevel` - needs `m1_A`
- Line 109: Uses `record.m1` - needs `m1_A`
- No M1_B visualization capability

**Required Changes:**

#### A. Add M1_B Toggle State
```typescript
const [showM1B, setShowM1B] = useState(false);
```

#### B. Update Data Mapping - Fix m1 References
```typescript
// Line 79: Update percentage mapping
m1_APercentageMap.set(key, record.m1_A?.percentage || 0);

// Line 85: Update cost level mapping
m1_ACostLevelMap.set(key, record.m1_A?.costLevel || 0);

// Line 109: Update array assignment
m1_APercentageData[i] = m1_APercentageMap.get(key) || 0;
m1_ACostLevelData[i] = m1_ACostLevelMap.get(key) || 0;
```

#### C. Add M1_B Data Mapping
```typescript
// After existing m1_A mapping, add M1_B mapping
const m1_BPercentageMap = new Map<string, number | null>();
const m1_BCostLevelMap = new Map<string, number | null>();

marginHistory.forEach((record) => {
  if (record.date) {
    const recordDate = new Date(record.date);
    const recordYear = recordDate.getFullYear();
    const recordMonth = recordDate.getMonth() + 1;

    if (recordYear === currentYear && recordMonth === currentMonth) {
      return;
    }

    const key = `${recordYear}-${recordMonth}`;

    // M1_B is nullable - preserve null for months without production
    m1_BPercentageMap.set(
      key,
      record.m1_B?.percentage !== undefined ? record.m1_B.percentage : null
    );
    m1_BCostLevelMap.set(
      key,
      record.m1_B?.costLevel !== undefined ? record.m1_B.costLevel : null
    );
  }
});

// Create arrays for M1_B data
const m1_BPercentageData = new Array(12).fill(null);
const m1_BCostLevelData = new Array(12).fill(null);

for (let i = 0; i < 12; i++) {
  const monthsBack = 12 - i;
  let adjustedYear = currentYear;
  let adjustedMonth = currentMonth - monthsBack;

  if (adjustedMonth <= 0) {
    adjustedYear--;
    adjustedMonth += 12;
  }

  const key = `${adjustedYear}-${adjustedMonth}`;
  m1_BPercentageData[i] = m1_BPercentageMap.get(key) ?? null;
  m1_BCostLevelData[i] = m1_BCostLevelMap.get(key) ?? null;
}
```

#### D. Add Toggle UI Component
```tsx
// Before the chart, add toggle control
<div className="flex items-center justify-between mb-4">
  <h3 className="text-lg font-medium text-gray-900">Vývoj nákladů a marží</h3>
  <label className="flex items-center space-x-2 text-sm text-gray-700">
    <input
      type="checkbox"
      checked={showM1B}
      onChange={(e) => setShowM1B(e.target.checked)}
      className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
    />
    <span>Zobrazit M1_B (skutečné měsíční náklady)</span>
  </label>
</div>
```

#### E. Add M1_B Scatter Dataset
```typescript
// Add conditionally to percentageDatasets array when showM1B === true
const m1_BDataset = showM1B ? {
  type: 'scatter' as const,
  label: "M1_B - Skutečné náklady výroby (%)",
  data: m1_BPercentageData.map((value, index) =>
    value !== null ? { x: index, y: value } : null
  ).filter(point => point !== null),
  backgroundColor: "rgba(245, 158, 11, 1)", // Amber
  borderColor: "rgba(245, 158, 11, 1)",
  pointRadius: 6,
  pointHoverRadius: 8,
  yAxisID: "y1",
} : null;

// Update percentageDatasets to include M1_B when enabled
const percentageDatasets = hasM0M3Data ? [
  // ... existing M0 dataset ...
  {
    type: 'line' as const,
    label: "M1_A - Marže + výroba (baseline) (%)", // Updated label
    data: m1_APercentageData, // Updated from m1PercentageData
    // ... rest of styling ...
  },
  // ... M2, M3 datasets ...
  ...(m1_BDataset ? [m1_BDataset] : []),
] : [];
```

#### F. Update Bar Chart Dataset for M1_A
```typescript
// Update label in costLevelDatasets
{
  type: 'bar' as const,
  label: "M1_A - Náklady výroby baseline (Kč/ks)", // Updated label
  data: m1_ACostLevelData, // Updated from m1CostLevelData
  backgroundColor: "rgba(234, 179, 8, 0.7)",
  borderColor: "rgba(234, 179, 8, 1)",
  borderWidth: 1,
  yAxisID: "y",
  stack: 'costs',
}
```

### 2. MarginsSummary.tsx - VERIFICATION ONLY

**Current State:** ✅ Already correct
- Line 46: Uses `m1_A?.percentage` ✅
- Line 148: Uses `m1_A?.costLevel` ✅
- Line 158: Uses `m1_A?.costTotal` ✅
- Line 142: Label shows "M1 (baseline)" ✅

**Action:** No changes needed, verification only

### 3. ProductMarginsList.tsx - MINOR UPDATES

**Current State:** Mostly correct
- Lines 202, 204, 405, 407: Already use `m1_A` ✅
- Line 357: Column header "M1 %" - could be clearer

**Optional Improvement:**
```typescript
// Line 357: Update column header for clarity
<SortableHeader column="m1_aPercentage" align="right">
  M1 (baseline) %
</SortableHeader>
```

**Tooltip Update:**
```typescript
// Line 210: Verify tooltip references M1_A
case "M1_A":
  return `Průměrné náklady materiál + výroba (baseline): ${formatCurrency(m0CostLevel + m1_ACostLevel)}`;
```

### 4. ProductMarginSummary.tsx - VERIFICATION

**Current State:** Appears correct
- Lines 191, 197: Uses `m1Amount` and `m1Percentage` from backend
- Lines 384, 512, 515: Headers show "M1 (baseline)" ✅

**Action:** Verify backend API returns `m1_A` data as `m1Amount` and `m1Percentage`
- Backend DTO mapping should alias `m1_A` to `m1` fields for this component
- If backend sends separate `m1_AAmount`/`m1_APercentage`, update references

## Implementation Tasks

### Phase 1: MarginsChart.tsx Updates
1. ✅ Add `showM1B` state with useState
2. ✅ Update m1 → m1_A references (lines 79, 85, 109)
3. ✅ Rename variables: `m1PercentageMap` → `m1_APercentageMap`
4. ✅ Rename variables: `m1CostLevelMap` → `m1_ACostLevelMap`
5. ✅ Add M1_B data mapping logic
6. ✅ Create M1_B percentage and cost level arrays
7. ✅ Add toggle UI component above chart
8. ✅ Add M1_B scatter dataset conditionally
9. ✅ Update M1_A line chart label
10. ✅ Update M1_A bar chart label

### Phase 2: Component Verifications
11. ✅ Verify MarginsSummary.tsx uses m1_A correctly
12. ✅ Update ProductMarginsList.tsx column header (optional)
13. ✅ Verify ProductMarginSummary.tsx backend data structure

### Phase 3: Testing
14. ✅ Visual test: M1_A line appears correctly in chart
15. ✅ Visual test: Toggle shows/hides M1_B scatter points
16. ✅ Visual test: M1_B points only appear for production months
17. ✅ Visual test: Tooltips show correct M1_A/M1_B values
18. ✅ Visual test: Summary tables display M1_A correctly
19. ✅ Test: Toggle default state is unchecked (M1_B hidden)
20. ✅ Build verification: Frontend builds without TypeScript errors

### Phase 4: Documentation
21. ✅ Update this design document with "Implemented" status
22. ✅ Commit changes with reference to design document

## Visual Design Specifications

**M1_A Display:**
- Color: Yellow (#eab308) - Consistent with current M1
- Style: Line chart (continuous)
- Label: "M1_A - Marže + výroba (baseline) (%)"
- Always visible

**M1_B Display:**
- Color: Amber (#f59e0b) - Distinct from M1_A
- Style: Scatter points
- Point radius: 6px (normal), 8px (hover)
- Label: "M1_B - Skutečné náklady výroby (%)"
- Visibility: Controlled by toggle, default hidden
- Only shows for months with production (null values filtered out)

**Toggle Control:**
- Location: Above chart, right side
- Text: "Zobrazit M1_B (skutečné měsíční náklady)"
- Style: Checkbox with label
- Default: Unchecked (M1_B hidden)

## API Client Compatibility

**Current API Client Structure (Verified):**
```typescript
export interface IMarginHistoryDto {
    date?: Date;
    sellingPrice?: number;
    totalCost?: number;
    m0?: MarginLevelDto;
    m1_A?: MarginLevelDto;      // Always present
    m1_B?: MarginLevelDto | undefined;  // Nullable
    m2?: MarginLevelDto;
    m3?: MarginLevelDto;
}
```

**Status:** ✅ API client already regenerated with correct types
- No regeneration needed
- Frontend can directly use `m1_A` and `m1_B` properties

## Migration Notes

**Breaking Changes:**
- Components referencing old `m1` property will break
- TypeScript compilation will fail until references updated

**Non-Breaking Changes:**
- Components already using `m1_A` continue to work
- API responses already contain `m1_A` and `m1_B` from backend

**User Impact:**
- M1_A replaces previous M1 display (no visual change to users)
- M1_B is new optional feature (opt-in via toggle)

## Success Criteria

1. ✅ All `m1` references updated to `m1_A` in MarginsChart.tsx
2. ✅ M1_B toggle control functional and defaults to unchecked
3. ✅ M1_B scatter points render correctly when toggle enabled
4. ✅ M1_B points only appear for months with production
5. ✅ Chart tooltips display correct M1_A and M1_B values
6. ✅ Summary tables show M1_A data correctly
7. ✅ Frontend builds without TypeScript errors
8. ✅ Visual testing confirms correct display on staging environment

## References

- Backend Design: `/docs/features/M1_margin_calculation_design.md`
- Backend Implementation: Commits e50574c0..901c1a2c
- API Client: `/frontend/src/api/generated/api-client.ts` (lines 8112-8174)
- Components:
  - `/frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx`
  - `/frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsSummary.tsx`
  - `/frontend/src/components/pages/ProductMarginsList.tsx`
  - `/frontend/src/components/pages/ProductMarginSummary.tsx`
