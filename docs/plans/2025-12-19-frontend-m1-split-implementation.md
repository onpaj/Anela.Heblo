# Frontend M1 Margin Split Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Update frontend components to display M1_A (economic baseline) and add optional M1_B (actual monthly cost) toggle with scatter points in charts.

**Architecture:** React component updates with TypeScript - fix property references from `m1` to `m1_A`, add M1_B data mapping, and render M1_B as conditional scatter points on existing chart.js visualization.

**Tech Stack:** React, TypeScript, Chart.js, React Chart.js 2

---

## Task 1: Add M1_B Toggle State to MarginsChart

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:1-21`

**Step 1: Add useState import**

Add `useState` to React import at line 1:

```typescript
import React, { useState } from "react";
```

**Step 2: Add showM1B state**

After line 21 (inside the component), add state declaration:

```typescript
const MarginsChart: React.FC<MarginsChartProps> = ({
  marginHistory,
  journalEntries,
}) => {
  // Toggle for showing M1_B actual monthly costs
  const [showM1B, setShowM1B] = useState(false);

  // Generate month labels excluding current month (last 12 months)
  const generateMonthLabelsExcludingCurrent = (): string[] => {
```

**Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors (or same errors as before if unrelated)

**Step 4: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat: add M1_B toggle state to MarginsChart

Add useState hook for toggling M1_B visibility.
Default state: false (M1_B hidden).

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 2: Rename m1 Variables to m1_A in Data Mapping

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:42-62`

**Step 1: Rename variable declarations**

Update line 43 and 47:

```typescript
const mapMarginDataToMonthlyArrays = () => {
  const m0PercentageData = new Array(12).fill(0);
  const m1_APercentageData = new Array(12).fill(0);  // Changed from m1PercentageData
  const m2PercentageData = new Array(12).fill(0);
  const m3PercentageData = new Array(12).fill(0);
  const m0CostLevelData = new Array(12).fill(0);
  const m1_ACostLevelData = new Array(12).fill(0);  // Changed from m1CostLevelData
  const m2CostLevelData = new Array(12).fill(0);
  const m3CostLevelData = new Array(12).fill(0);
```

**Step 2: Rename map variable declarations**

Update lines around 55-62:

```typescript
// Create maps for quick lookup of margin data by year-month key
const m0PercentageMap = new Map<string, number>();
const m1_APercentageMap = new Map<string, number>();  // Changed from m1PercentageMap
const m2PercentageMap = new Map<string, number>();
const m3PercentageMap = new Map<string, number>();
const m0CostLevelMap = new Map<string, number>();
const m1_ACostLevelMap = new Map<string, number>();  // Changed from m1CostLevelMap
const m2CostLevelMap = new Map<string, number>();
const m3CostLevelMap = new Map<string, number>();
```

**Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: TypeScript errors about undefined variables (we'll fix in next task)

**Step 4: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "refactor: rename m1 variables to m1_A in MarginsChart

Rename all m1 variable declarations to m1_A to match backend DTO.
- m1PercentageData -> m1_APercentageData
- m1CostLevelData -> m1_ACostLevelData
- m1PercentageMap -> m1_APercentageMap
- m1CostLevelMap -> m1_ACostLevelMap

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 3: Update m1 Property References to m1_A

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:64-112`

**Step 1: Update marginHistory forEach mapping**

Find the forEach block (around line 64-89) and update m1 references:

```typescript
marginHistory.forEach((record) => {
  if (record.date) {
    const recordDate = new Date(record.date);
    const recordYear = recordDate.getFullYear();
    const recordMonth = recordDate.getMonth() + 1;

    // Skip current month data
    if (recordYear === currentYear && recordMonth === currentMonth) {
      return;
    }

    const key = `${recordYear}-${recordMonth}`;

    // M0-M3 percentage properties
    m0PercentageMap.set(key, record.m0?.percentage || 0);
    m1_APercentageMap.set(key, record.m1_A?.percentage || 0);  // Changed from m1
    m2PercentageMap.set(key, record.m2?.percentage || 0);
    m3PercentageMap.set(key, record.m3?.percentage || 0);

    // M0-M3 CostLevel properties
    m0CostLevelMap.set(key, record.m0?.costLevel || 0);
    m1_ACostLevelMap.set(key, record.m1_A?.costLevel || 0);  // Changed from m1
    m2CostLevelMap.set(key, record.m2?.costLevel || 0);
    m3CostLevelMap.set(key, record.m3?.costLevel || 0);
  }
});
```

**Step 2: Update array assignment loop**

Find the for loop (around line 92-112) and update m1 references:

```typescript
// Fill the arrays with data for the last 12 months (excluding current month)
for (let i = 0; i < 12; i++) {
  const monthsBack = 12 - i;
  let adjustedYear = currentYear;
  let adjustedMonth = currentMonth - monthsBack;

  // Handle year transitions
  if (adjustedMonth <= 0) {
    adjustedYear--;
    adjustedMonth += 12;
  }

  const key = `${adjustedYear}-${adjustedMonth}`;
  m0PercentageData[i] = m0PercentageMap.get(key) || 0;
  m1_APercentageData[i] = m1_APercentageMap.get(key) || 0;  // Changed from m1PercentageData
  m2PercentageData[i] = m2PercentageMap.get(key) || 0;
  m3PercentageData[i] = m3PercentageMap.get(key) || 0;
  m0CostLevelData[i] = m0CostLevelMap.get(key) || 0;
  m1_ACostLevelData[i] = m1_ACostLevelMap.get(key) || 0;  // Changed from m1CostLevelData
  m2CostLevelData[i] = m2CostLevelMap.get(key) || 0;
  m3CostLevelData[i] = m3CostLevelMap.get(key) || 0;
}
```

**Step 3: Update return statement**

Find the return statement (around line 114-124) and update:

```typescript
return {
  m0PercentageData,
  m1_APercentageData,  // Changed from m1PercentageData
  m2PercentageData,
  m3PercentageData,
  m0CostLevelData,
  m1_ACostLevelData,  // Changed from m1CostLevelData
  m2CostLevelData,
  m3CostLevelData
};
```

**Step 4: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: TypeScript errors about destructuring (we'll fix in next task)

**Step 5: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "fix: update m1 property references to m1_A

Update all record.m1 references to record.m1_A to match backend DTO.
- Update forEach mapping: record.m1 -> record.m1_A
- Update array assignments
- Update return statement

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 4: Update Destructuring and Variable Usage

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:126-142`

**Step 1: Update destructuring**

Find the destructuring statement (around line 126-135) and update:

```typescript
const {
  m0PercentageData,
  m1_APercentageData,  // Changed from m1PercentageData
  m2PercentageData,
  m3PercentageData,
  m0CostLevelData,
  m1_ACostLevelData,  // Changed from m1CostLevelData
  m2CostLevelData,
  m3CostLevelData
} = mapMarginDataToMonthlyArrays();
```

**Step 2: Update hasM0M3Data check**

Find the hasM0M3Data check (around line 138-141) and update:

```typescript
// Check if we have M0-M3 data
const hasM0M3Data = m0PercentageData.some(value => value > 0) ||
                    m1_APercentageData.some(value => value > 0) ||  // Changed
                    m2PercentageData.some(value => value > 0) ||
                    m3PercentageData.some(value => value > 0);
```

**Step 3: Update generatePointStyling calls**

Find the styling calls (around line 144-147) and update:

```typescript
// Generate point styling for percentage line charts (12 months without current)
const m0Styling = generatePointStyling(12, journalEntries, "rgba(34, 197, 94, 1)"); // Green
const m1_AStyling = generatePointStyling(12, journalEntries, "rgba(234, 179, 8, 1)"); // Yellow - Changed from m1Styling
const m2Styling = generatePointStyling(12, journalEntries, "rgba(249, 115, 22, 1)"); // Orange
const m3Styling = generatePointStyling(12, journalEntries, "rgba(239, 68, 68, 1)"); // Red
```

**Step 4: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: TypeScript errors about m1_AStyling usage (we'll fix in next task)

**Step 5: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "refactor: update destructuring and variable usage for m1_A

Update variable names in destructuring and checks:
- Destructuring: m1PercentageData -> m1_APercentageData
- hasM0M3Data check updated
- Point styling variable: m1Styling -> m1_AStyling

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 5: Update Bar Chart Dataset for M1_A

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:149-191`

**Step 1: Update M1 bar chart dataset**

Find the costLevelDatasets array (around line 150-191) and update the M1 dataset:

```typescript
const costLevelDatasets = hasM0M3Data ? [
  {
    type: 'bar' as const,
    label: "M0 - Náklady materiálu (Kč/ks)",
    data: m0CostLevelData,
    backgroundColor: "rgba(34, 197, 94, 0.7)", // Green
    borderColor: "rgba(34, 197, 94, 1)",
    borderWidth: 1,
    yAxisID: "y",
    stack: 'costs',
  },
  {
    type: 'bar' as const,
    label: "M1_A - Náklady výroby baseline (Kč/ks)",  // Updated label
    data: m1_ACostLevelData,  // Updated variable name
    backgroundColor: "rgba(234, 179, 8, 0.7)", // Yellow
    borderColor: "rgba(234, 179, 8, 1)",
    borderWidth: 1,
    yAxisID: "y",
    stack: 'costs',
  },
  {
    type: 'bar' as const,
    label: "M2 - Náklady prodeje (Kč/ks)",
    data: m2CostLevelData,
    backgroundColor: "rgba(249, 115, 22, 0.7)", // Orange
    borderColor: "rgba(249, 115, 22, 1)",
    borderWidth: 1,
    yAxisID: "y",
    stack: 'costs',
  },
  {
    type: 'bar' as const,
    label: "M3 - Režijní náklady (Kč/ks)",
    data: m3CostLevelData,
    backgroundColor: "rgba(239, 68, 68, 0.7)", // Red
    borderColor: "rgba(239, 68, 68, 1)",
    borderWidth: 1,
    yAxisID: "y",
    stack: 'costs',
  },
] : [];
```

**Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No new TypeScript errors related to bar chart

**Step 3: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat: update bar chart dataset label and data for M1_A

Update M1 bar chart dataset:
- Label: 'M1_A - Náklady výroby baseline (Kč/ks)'
- Data: m1_ACostLevelData

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 6: Update Line Chart Dataset for M1_A

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:194-255`

**Step 1: Update M1 percentage line dataset**

Find the percentageDatasets array (around line 194-255) and update the M1 dataset:

```typescript
// Add percentage line charts on secondary Y axis
const percentageDatasets = hasM0M3Data ? [
  {
    type: 'line' as const,
    label: "M0 - Marže materiál (%)",
    data: m0PercentageData,
    backgroundColor: "rgba(34, 197, 94, 0.1)",
    borderColor: "rgba(34, 197, 94, 1)",
    borderWidth: 2,
    tension: 0.1,
    pointBackgroundColor: m0Styling.pointBackgroundColors,
    pointBorderColor: m0Styling.pointBackgroundColors,
    pointRadius: m0Styling.pointRadiuses,
    pointHoverRadius: m0Styling.pointHoverRadiuses,
    yAxisID: "y1",
    fill: false,
  },
  {
    type: 'line' as const,
    label: "M1_A - Marže + výroba (baseline) (%)",  // Updated label
    data: m1_APercentageData,  // Updated variable name
    backgroundColor: "rgba(234, 179, 8, 0.1)",
    borderColor: "rgba(234, 179, 8, 1)",
    borderWidth: 2,
    tension: 0.1,
    pointBackgroundColor: m1_AStyling.pointBackgroundColors,  // Updated variable
    pointBorderColor: m1_AStyling.pointBackgroundColors,  // Updated variable
    pointRadius: m1_AStyling.pointRadiuses,  // Updated variable
    pointHoverRadius: m1_AStyling.pointHoverRadiuses,  // Updated variable
    yAxisID: "y1",
    fill: false,
  },
  {
    type: 'line' as const,
    label: "M2 - Marže + prodej (%)",
    data: m2PercentageData,
    backgroundColor: "rgba(249, 115, 22, 0.1)",
    borderColor: "rgba(249, 115, 22, 1)",
    borderWidth: 2,
    tension: 0.1,
    pointBackgroundColor: m2Styling.pointBackgroundColors,
    pointBorderColor: m2Styling.pointBackgroundColors,
    pointRadius: m2Styling.pointRadiuses,
    pointHoverRadius: m2Styling.pointHoverRadiuses,
    yAxisID: "y1",
    fill: false,
  },
  {
    type: 'line' as const,
    label: "M3 - Finální marže (%)",
    data: m3PercentageData,
    backgroundColor: "rgba(239, 68, 68, 0.1)",
    borderColor: "rgba(239, 68, 68, 1)",
    borderWidth: 2,
    tension: 0.1,
    pointBackgroundColor: m3Styling.pointBackgroundColors,
    pointBorderColor: m3Styling.pointBackgroundColors,
    pointRadius: m3Styling.pointRadiuses,
    pointHoverRadius: m3Styling.pointHoverRadiuses,
    yAxisID: "y1",
    fill: false,
  },
] : [];
```

**Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors (all m1 references updated)

**Step 3: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat: update line chart dataset label and data for M1_A

Update M1 percentage line chart dataset:
- Label: 'M1_A - Marže + výroba (baseline) (%)'
- Data: m1_APercentageData
- Styling: m1_AStyling

All m1 references now updated to m1_A.

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 7: Add M1_B Data Mapping Logic

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:64-89`

**Step 1: Add M1_B map declarations**

After the m1_ACostLevelMap declaration (around line 62), add M1_B maps:

```typescript
const m0CostLevelMap = new Map<string, number>();
const m1_ACostLevelMap = new Map<string, number>();
const m2CostLevelMap = new Map<string, number>();
const m3CostLevelMap = new Map<string, number>();

// M1_B maps for actual monthly costs (nullable)
const m1_BPercentageMap = new Map<string, number | null>();
const m1_BCostLevelMap = new Map<string, number | null>();
```

**Step 2: Add M1_B mapping in forEach**

After the m1_A mapping in forEach block (around line 79), add M1_B mapping:

```typescript
// M0-M3 percentage properties
m0PercentageMap.set(key, record.m0?.percentage || 0);
m1_APercentageMap.set(key, record.m1_A?.percentage || 0);
m2PercentageMap.set(key, record.m2?.percentage || 0);
m3PercentageMap.set(key, record.m3?.percentage || 0);

// M1_B is nullable - preserve null for months without production
m1_BPercentageMap.set(
  key,
  record.m1_B?.percentage !== undefined ? record.m1_B.percentage : null
);

// M0-M3 CostLevel properties
m0CostLevelMap.set(key, record.m0?.costLevel || 0);
m1_ACostLevelMap.set(key, record.m1_A?.costLevel || 0);
m2CostLevelMap.set(key, record.m2?.costLevel || 0);
m3CostLevelMap.set(key, record.m3?.costLevel || 0);

// M1_B cost level (nullable)
m1_BCostLevelMap.set(
  key,
  record.m1_B?.costLevel !== undefined ? record.m1_B.costLevel : null
);
```

**Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors

**Step 4: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat: add M1_B data mapping logic

Add M1_B maps and mapping in forEach:
- m1_BPercentageMap: Map<string, number | null>
- m1_BCostLevelMap: Map<string, number | null>
- Preserve null for months without production

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 8: Create M1_B Data Arrays

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:92-124`

**Step 1: Add M1_B array declarations**

After the m3CostLevelData declaration (around line 49), add:

```typescript
const m3CostLevelData = new Array(12).fill(0);

// M1_B data arrays (nullable for months without production)
const m1_BPercentageData = new Array(12).fill(null);
const m1_BCostLevelData = new Array(12).fill(null);
```

**Step 2: Add M1_B array population in for loop**

After the m1_A array assignments in the for loop (around line 109), add:

```typescript
const key = `${adjustedYear}-${adjustedMonth}`;
m0PercentageData[i] = m0PercentageMap.get(key) || 0;
m1_APercentageData[i] = m1_APercentageMap.get(key) || 0;
m2PercentageData[i] = m2PercentageMap.get(key) || 0;
m3PercentageData[i] = m3PercentageMap.get(key) || 0;
m0CostLevelData[i] = m0CostLevelMap.get(key) || 0;
m1_ACostLevelData[i] = m1_ACostLevelMap.get(key) || 0;
m2CostLevelData[i] = m2CostLevelMap.get(key) || 0;
m3CostLevelData[i] = m3CostLevelMap.get(key) || 0;

// M1_B data (preserve null for no production)
m1_BPercentageData[i] = m1_BPercentageMap.get(key) ?? null;
m1_BCostLevelData[i] = m1_BCostLevelMap.get(key) ?? null;
```

**Step 3: Update return statement**

Update the return statement to include M1_B arrays:

```typescript
return {
  m0PercentageData,
  m1_APercentageData,
  m2PercentageData,
  m3PercentageData,
  m0CostLevelData,
  m1_ACostLevelData,
  m2CostLevelData,
  m3CostLevelData,
  m1_BPercentageData,  // Added
  m1_BCostLevelData,   // Added
};
```

**Step 4: Update destructuring**

Update the destructuring to include M1_B arrays:

```typescript
const {
  m0PercentageData,
  m1_APercentageData,
  m2PercentageData,
  m3PercentageData,
  m0CostLevelData,
  m1_ACostLevelData,
  m2CostLevelData,
  m3CostLevelData,
  m1_BPercentageData,  // Added
  m1_BCostLevelData,   // Added
} = mapMarginDataToMonthlyArrays();
```

**Step 5: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors

**Step 6: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat: create M1_B data arrays

Add M1_B percentage and cost level arrays:
- Initialize with null (12 elements)
- Populate in for loop preserving null for no production
- Add to return statement and destructuring

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 9: Add M1_B Scatter Dataset

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:194-255`

**Step 1: Create M1_B conditional dataset**

After the percentageDatasets array definition (around line 255), add M1_B dataset:

```typescript
] : [];

// M1_B scatter dataset (conditional based on toggle)
const m1_BDataset = showM1B && hasM0M3Data ? {
  type: 'scatter' as const,
  label: "M1_B - Skutečné náklady výroby (%)",
  data: m1_BPercentageData
    .map((value, index) => value !== null ? { x: index, y: value } : null)
    .filter((point): point is { x: number; y: number } => point !== null),
  backgroundColor: "rgba(245, 158, 11, 1)", // Amber
  borderColor: "rgba(245, 158, 11, 1)",
  pointRadius: 6,
  pointHoverRadius: 8,
  yAxisID: "y1",
} : null;
```

**Step 2: Update chartData datasets**

Find the chartData definition (around line 257-260) and update:

```typescript
const chartData = {
  labels: monthLabels,
  datasets: [
    ...costLevelDatasets,
    ...percentageDatasets,
    ...(m1_BDataset ? [m1_BDataset] : []),  // Add M1_B conditionally
  ],
};
```

**Step 3: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors

**Step 4: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat: add M1_B scatter dataset to chart

Add conditional M1_B scatter dataset:
- Type: scatter points
- Color: Amber (#f59e0b)
- Only shows when showM1B is true
- Filters out null values (no production months)
- Point radius: 6px (normal), 8px (hover)

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 10: Add M1_B Toggle UI Component

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:320-330`

**Step 1: Update chart header section**

Find the chart rendering section (around line 322-329) and update the header:

```tsx
return (
  <div className="flex-1 bg-gray-50 rounded-lg p-4 mb-4">
    {hasData ? (
      <>
        <div className="flex items-center justify-between mb-4">
          <h3 className="text-lg font-medium text-gray-900">Vývoj nákladů a marží</h3>
          <label className="flex items-center space-x-2 text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={showM1B}
              onChange={(e) => setShowM1B(e.target.checked)}
              className="rounded border-gray-300 text-indigo-600 focus:ring-indigo-500"
            />
            <span>Zobrazit M1_B (skutečné měsíční náklady)</span>
          </label>
        </div>
        <div className="h-96">
          <Chart type="bar" data={chartData} options={chartOptions} />
        </div>
      </>
```

**Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors

**Step 3: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "feat: add M1_B toggle UI component

Add checkbox toggle above chart:
- Label: 'Zobrazit M1_B (skutečné měsíční náklady)'
- Location: Chart header, right side
- Default state: unchecked (false)
- Controls showM1B state

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 11: Update ProductMarginsList Column Header

**Files:**
- Modify: `frontend/src/components/pages/ProductMarginsList.tsx:356-358`

**Step 1: Update M1 column header**

Find the SortableHeader for M1 (around line 356-358) and update:

```tsx
<SortableHeader column="m1_aPercentage" align="right">
  M1 (baseline) %
</SortableHeader>
```

**Step 2: Verify TypeScript compiles**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors

**Step 3: Commit**

```bash
git add frontend/src/components/pages/ProductMarginsList.tsx
git commit -m "feat: clarify M1 column header in ProductMarginsList

Update column header from 'M1 %' to 'M1 (baseline) %'
for clarity that it represents M1_A economic baseline.

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 12: Verify MarginsSummary Component

**Files:**
- Read: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsSummary.tsx`

**Step 1: Verify m1_A usage**

Run: `grep -n "m1_A" frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsSummary.tsx`

Expected output should show:
- Line 46: `record.m1_A?.percentage`
- Line 148: `m.m1_A?.costLevel`
- Line 158: `m.m1_A?.costTotal`

**Step 2: Verify label shows baseline**

Run: `grep -n "M1 (baseline)" frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsSummary.tsx`

Expected output should show:
- Line 142: `<span className="font-medium text-yellow-900">M1 (baseline)</span>`

**Step 3: Document verification**

No changes needed - component already correct.

**Step 4: Commit verification note**

```bash
git commit --allow-empty -m "docs: verify MarginsSummary uses m1_A correctly

Verified MarginsSummary.tsx:
- Line 46: Uses m1_A?.percentage ✓
- Line 148: Uses m1_A?.costLevel ✓
- Line 158: Uses m1_A?.costTotal ✓
- Line 142: Label shows 'M1 (baseline)' ✓

No changes needed.

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 13: Build and Test Frontend

**Files:**
- Test: `frontend/`

**Step 1: Run TypeScript compilation**

Run: `cd frontend && npx tsc --noEmit`

Expected: No TypeScript errors

**Step 2: Run frontend build**

Run: `cd frontend && npm run build`

Expected: Build succeeds without errors

**Step 3: Check for any linting issues**

Run: `cd frontend && npm run lint`

Expected: No critical linting errors (warnings acceptable)

**Step 4: Document build success**

```bash
git commit --allow-empty -m "test: verify frontend builds successfully

Verified:
- TypeScript compilation: No errors ✓
- Frontend build: Success ✓
- Linting: No critical errors ✓

All M1_A/M1_B changes compile and build successfully.

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 14: Visual Testing Checklist

**Files:**
- Test: Manual visual testing via browser

**Step 1: Start development servers**

Run:
```bash
# Terminal 1: Start backend
cd backend/src/Anela.Heblo.API && dotnet run

# Terminal 2: Start frontend
cd frontend && npm start
```

Expected: Both servers start successfully

**Step 2: Navigate to product margins page**

1. Open browser: http://localhost:3000
2. Login if needed
3. Navigate to "Marže produktů" page
4. Click on any product with margin data

**Step 3: Verify M1_A display in MarginsSummary**

Check:
- [ ] M1 row shows "M1 (baseline)" label
- [ ] M1_A percentage displays correctly
- [ ] M1_A cost level displays correctly

**Step 4: Verify M1_A line in MarginsChart**

Check:
- [ ] Yellow line shows "M1_A - Marže + výroba (baseline) (%)" in legend
- [ ] Line displays data points correctly
- [ ] No console errors

**Step 5: Verify M1_B toggle**

Check:
- [ ] Toggle checkbox appears above chart
- [ ] Label: "Zobrazit M1_B (skutečné měsíční náklady)"
- [ ] Default state: unchecked
- [ ] No M1_B scatter points visible by default

**Step 6: Verify M1_B scatter points**

Check:
- [ ] Click toggle checkbox to enable
- [ ] Amber scatter points appear on chart
- [ ] Points only appear for months with production
- [ ] Legend shows "M1_B - Skutečné náklady výroby (%)"
- [ ] Hovering over points shows correct tooltip

**Step 7: Verify ProductMarginsList**

Check:
- [ ] Navigate to list view
- [ ] Column header shows "M1 (baseline) %"
- [ ] M1_A percentages display correctly
- [ ] Tooltips show correct M1_A information

**Step 8: Document visual testing**

```bash
git commit --allow-empty -m "test: complete visual testing checklist

Visual testing completed:
- MarginsSummary: M1_A displays correctly ✓
- MarginsChart M1_A line: Displays correctly ✓
- M1_B toggle: Functions correctly ✓
- M1_B scatter points: Display only for production months ✓
- ProductMarginsList: M1_A displays correctly ✓

All visual requirements verified.

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Task 15: Update Design Document Status

**Files:**
- Modify: `docs/plans/2025-12-19-frontend-m1-split-design.md:1-5`

**Step 1: Update status**

Change line 3:

```markdown
**Status:** ✅ Implemented (2025-12-19)
```

**Step 2: Add implementation commit reference**

After line 6, add:

```markdown
**Implementation Commits:** [commit-range-here]
```

**Step 3: Commit documentation update**

```bash
git add docs/plans/2025-12-19-frontend-m1-split-design.md
git commit -m "docs: mark M1 split frontend implementation as complete

Update design document status to 'Implemented'.
Add implementation commit reference.

All tasks completed:
- M1_A replaces M1 in all components ✓
- M1_B toggle with scatter points added ✓
- Visual testing completed ✓
- Build verification passed ✓

Related to: docs/plans/2025-12-19-frontend-m1-split-design.md"
```

---

## Success Criteria

All tasks completed when:

1. ✅ All `m1` references updated to `m1_A` in MarginsChart.tsx
2. ✅ M1_B toggle control functional and defaults to unchecked
3. ✅ M1_B scatter points render correctly when toggle enabled
4. ✅ M1_B points only appear for months with production
5. ✅ Chart tooltips display correct M1_A and M1_B values
6. ✅ Summary tables show M1_A data correctly
7. ✅ Frontend builds without TypeScript errors
8. ✅ Visual testing confirms correct display
9. ✅ Design document updated with implementation status

---

## Notes

- **DRY:** Reuse existing styling and data mapping patterns
- **YAGNI:** No premature abstractions - implement exactly what's needed
- **TDD:** Frontend uses visual testing rather than unit tests for UI verification
- **Frequent commits:** Each task is a separate commit with clear message

## References

- Design Document: `docs/plans/2025-12-19-frontend-m1-split-design.md`
- Backend Design: `docs/features/M1_margin_calculation_design.md`
- API Client: `frontend/src/api/generated/api-client.ts`
