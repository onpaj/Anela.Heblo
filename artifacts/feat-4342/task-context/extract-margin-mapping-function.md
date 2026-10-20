### task: extract-margin-mapping-function

**Files:**
- Create: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.utils.ts`
- Create: `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts`
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx:11` (add import), `:40-135` (remove inline function, call the import instead)

- [ ] **Step 1: Write a failing smoke test against the not-yet-created module**

Create `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts`:

```typescript
import { mapMarginDataToMonthlyArrays } from "../MarginsChart.utils";

describe("mapMarginDataToMonthlyArrays", () => {
  it("returns 12 zeros for all 8 arrays when marginHistory is empty", () => {
    const result = mapMarginDataToMonthlyArrays([], new Date(2026, 6, 15));

    expect(result.m0PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m1PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m2PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m3PercentageData).toEqual(new Array(12).fill(0));
    expect(result.m0CostLevelData).toEqual(new Array(12).fill(0));
    expect(result.m1CostLevelData).toEqual(new Array(12).fill(0));
    expect(result.m2CostLevelData).toEqual(new Array(12).fill(0));
    expect(result.m3CostLevelData).toEqual(new Array(12).fill(0));
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npm test -- --testPathPattern=MarginsChart.utils --watchAll=false`
Expected: FAIL — `Cannot find module '../MarginsChart.utils'`

- [ ] **Step 3: Create the extracted module**

Create `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.utils.ts`:

```typescript
import { MarginHistoryDto } from "../../../../../api/generated/api-client";

export interface MonthlyMarginArrays {
  m0PercentageData: number[];
  m1PercentageData: number[];
  m2PercentageData: number[];
  m3PercentageData: number[];
  m0CostLevelData: number[];
  m1CostLevelData: number[];
  m2CostLevelData: number[];
  m3CostLevelData: number[];
}

// Map margin history data to monthly arrays (excluding current month).
// `now` is injectable so callers (tests) can pin the "current date" deterministically;
// production code relies on the default of `new Date()`.
export function mapMarginDataToMonthlyArrays(
  marginHistory: MarginHistoryDto[],
  now: Date = new Date(),
): MonthlyMarginArrays {
  const m0PercentageData = new Array(12).fill(0);
  const m1PercentageData = new Array(12).fill(0);
  const m2PercentageData = new Array(12).fill(0);
  const m3PercentageData = new Array(12).fill(0);
  const m0CostLevelData = new Array(12).fill(0);
  const m1CostLevelData = new Array(12).fill(0);
  const m2CostLevelData = new Array(12).fill(0);
  const m3CostLevelData = new Array(12).fill(0);
  const currentYear = now.getFullYear();
  const currentMonth = now.getMonth() + 1;

  // Create maps for quick lookup of margin data by year-month key
  const m0PercentageMap = new Map<string, number>();
  const m1PercentageMap = new Map<string, number>();
  const m2PercentageMap = new Map<string, number>();
  const m3PercentageMap = new Map<string, number>();
  const m0CostLevelMap = new Map<string, number>();
  const m1CostLevelMap = new Map<string, number>();
  const m2CostLevelMap = new Map<string, number>();
  const m3CostLevelMap = new Map<string, number>();

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

      // M0-M2 percentage properties
      m0PercentageMap.set(key, record.m0?.percentage || 0);
      m1PercentageMap.set(key, record.m1?.percentage || 0);
      m2PercentageMap.set(key, record.m2?.percentage || 0);
      m3PercentageMap.set(key, record.m3?.percentage || 0);

      // M0-M2 CostLevel properties
      m0CostLevelMap.set(key, record.m0?.costLevel || 0);
      m1CostLevelMap.set(key, record.m1?.costLevel || 0);
      m2CostLevelMap.set(key, record.m2?.costLevel || 0);
      m3CostLevelMap.set(key, record.m3?.costLevel || 0);
    }
  });

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
    m1PercentageData[i] = m1PercentageMap.get(key) || 0;
    m2PercentageData[i] = m2PercentageMap.get(key) || 0;
    m3PercentageData[i] = m3PercentageMap.get(key) || 0;
    m0CostLevelData[i] = m0CostLevelMap.get(key) || 0;
    m1CostLevelData[i] = m1CostLevelMap.get(key) || 0;
    m2CostLevelData[i] = m2CostLevelMap.get(key) || 0;
    m3CostLevelData[i] = m3CostLevelMap.get(key) || 0;
  }

  return {
    m0PercentageData,
    m1PercentageData,
    m2PercentageData,
    m3PercentageData,
    m0CostLevelData,
    m1CostLevelData,
    m2CostLevelData,
    m3CostLevelData,
  };
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npm test -- --testPathPattern=MarginsChart.utils --watchAll=false`
Expected: PASS

- [ ] **Step 5: Wire `MarginsChart.tsx` to the extracted function**

In `frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx`, add the import after the existing `ChartHelpers` import (currently lines 8-11):

```typescript
import {
  generatePointStyling,
  generateTooltipCallback,
} from "../../charts/ChartHelpers";
import { mapMarginDataToMonthlyArrays } from "./MarginsChart.utils";
```

Then delete the entire inline function definition — the comment plus function body currently at lines 40-124:

```typescript
  // Map margin history data to monthly arrays (excluding current month)
  const mapMarginDataToMonthlyArrays = () => {
    ... (lines 41-124 as read from the current file) ...
  };
```

Replace the destructuring call currently at lines 126-135:

```typescript
  const {
    m0PercentageData,
    m1PercentageData,
    m2PercentageData,
    m3PercentageData,
    m0CostLevelData,
    m1CostLevelData,
    m2CostLevelData,
    m3CostLevelData
  } = mapMarginDataToMonthlyArrays();
```

with a call that passes `marginHistory` explicitly (no `now` argument, so it defaults to `new Date()`, preserving today's runtime behavior exactly):

```typescript
  const {
    m0PercentageData,
    m1PercentageData,
    m2PercentageData,
    m3PercentageData,
    m0CostLevelData,
    m1CostLevelData,
    m2CostLevelData,
    m3CostLevelData
  } = mapMarginDataToMonthlyArrays(marginHistory);
```

Everything else in `MarginsChart.tsx` (`generateMonthLabelsExcludingCurrent`, styling, `chartData`, `chartOptions`, the `hasData` conditional render) is untouched.

- [ ] **Step 6: Run the full frontend test suite to confirm no regression**

Run: `cd frontend && npm test -- --watchAll=false`
Expected: PASS (all existing suites, including `MarginsSummary.test.tsx`, still green; no test references the old inline function directly since none existed before this task)

- [ ] **Step 7: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.utils.ts \
        frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts \
        frontend/src/components/catalog/detail/tabs/MarginsTab/MarginsChart.tsx
git commit -m "refactor(margins-chart): extract mapMarginDataToMonthlyArrays for direct testing"
```

---

