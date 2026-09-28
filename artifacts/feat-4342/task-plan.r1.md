# MarginsChart Year-Boundary Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add unit test coverage for `MarginsChart`'s `mapMarginDataToMonthlyArrays` — the year-boundary correction, the current-month exclusion filter, and the missing-data fallback-to-0 — with no behavior change.

**Architecture:** Extract the pure, currently-inline `mapMarginDataToMonthlyArrays` function out of `MarginsChart.tsx` into a new colocated module, `MarginsChart.utils.ts`, adding an optional injectable `now: Date` parameter for deterministic testing; then add a colocated Jest test file exercising the extraction directly, with no component rendering or chart-library mocking required.

**Tech Stack:** TypeScript, React, Jest, `@testing-library/react` (already in `frontend/package.json`; no new dependency).

---

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

### task: add-margin-mapping-coverage-tests

**Files:**
- Modify: `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts`

- [ ] **Step 1: Write the failing (not-yet-written) test cases for the year-boundary, mid-year, exclusion, and fallback behaviors**

Replace the contents of `frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts` with:

```typescript
import { mapMarginDataToMonthlyArrays } from "../MarginsChart.utils";
import { MarginHistoryDto } from "../../../../../../api/generated/api-client";

const buildRecord = (
  year: number,
  month: number,
  overrides: Partial<{
    m0Percentage: number;
    m1Percentage: number;
    m2Percentage: number;
    m3Percentage: number;
    m0CostLevel: number;
    m1CostLevel: number;
    m2CostLevel: number;
    m3CostLevel: number;
  }> = {},
): MarginHistoryDto =>
  ({
    date: new Date(year, month - 1, 15),
    m0: { percentage: overrides.m0Percentage ?? 10, costLevel: overrides.m0CostLevel ?? 1 },
    m1: { percentage: overrides.m1Percentage ?? 20, costLevel: overrides.m1CostLevel ?? 2 },
    m2: { percentage: overrides.m2Percentage ?? 30, costLevel: overrides.m2CostLevel ?? 3 },
    m3: { percentage: overrides.m3Percentage ?? 40, costLevel: overrides.m3CostLevel ?? 4 },
  }) as MarginHistoryDto;

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

  it("rolls all 12 slots back into the previous year when run in January", () => {
    // January 2026: monthsBack runs 12..1, so every one of the 12 slots lands in
    // 2025 (Jan 2025 in slot 0, through Dec 2025 in slot 11) — see arch-review.r1.md
    // Decision 3 / spec FR-1 for the hand-derived slot sequence.
    const now = new Date(2026, 0, 10); // January 10, 2026
    const records: MarginHistoryDto[] = [
      buildRecord(2025, 1, { m0Percentage: 11 }),
      buildRecord(2025, 12, { m0Percentage: 99 }),
    ];

    const result = mapMarginDataToMonthlyArrays(records, now);

    expect(result.m0PercentageData[0]).toBe(11); // slot 0 = Jan 2025
    expect(result.m0PercentageData[11]).toBe(99); // slot 11 = Dec 2025
    for (let i = 1; i < 11; i++) {
      expect(result.m0PercentageData[i]).toBe(0);
    }
  });

  it("does not include January of the current year when run in January (it is the excluded current month)", () => {
    const now = new Date(2026, 0, 10); // January 10, 2026
    const records: MarginHistoryDto[] = [buildRecord(2026, 1, { m0Percentage: 77 })];

    const result = mapMarginDataToMonthlyArrays(records, now);

    expect(result.m0PercentageData.every((v) => v === 0)).toBe(true);
  });

  it("splits slots across the previous and current year with no crossover error when run mid-year", () => {
    // July 2026: monthsBack 12..7 land in 2025 (slots 0-5, Jul-Dec 2025),
    // monthsBack 6..1 land in 2026 (slots 6-11, Jan-Jun 2026).
    const now = new Date(2026, 6, 20); // July 20, 2026
    const records: MarginHistoryDto[] = [
      buildRecord(2025, 7, { m1Percentage: 51 }), // slot 0
      buildRecord(2025, 12, { m1Percentage: 52 }), // slot 5
      buildRecord(2026, 1, { m1Percentage: 53 }), // slot 6
      buildRecord(2026, 6, { m1Percentage: 54 }), // slot 11
    ];

    const result = mapMarginDataToMonthlyArrays(records, now);

    expect(result.m1PercentageData).toEqual([51, 0, 0, 0, 0, 52, 53, 0, 0, 0, 0, 54]);
  });

  it("excludes a record in the current month/year but keeps a same-month record from a different year", () => {
    const now = new Date(2026, 6, 20); // July 20, 2026
    const records: MarginHistoryDto[] = [
      buildRecord(2026, 7, { m2Percentage: 88 }), // current month -> excluded
      buildRecord(2025, 7, { m2Percentage: 33 }), // same month number, previous year -> kept
    ];

    const result = mapMarginDataToMonthlyArrays(records, now);

    expect(result.m2PercentageData[0]).toBe(33); // slot 0 = Jul 2025
    expect(result.m2PercentageData.filter((v) => v === 88)).toHaveLength(0);
  });

  it("falls back to 0 for a month with no matching record without affecting adjacent slots", () => {
    const now = new Date(2026, 6, 20); // July 20, 2026
    const records: MarginHistoryDto[] = [
      buildRecord(2025, 7, { m3Percentage: 61 }), // slot 0
      // slots 1-11 intentionally have no matching record
    ];

    const result = mapMarginDataToMonthlyArrays(records, now);

    expect(result.m3PercentageData[0]).toBe(61);
    for (let i = 1; i < 12; i++) {
      expect(result.m3PercentageData[i]).toBe(0);
      expect(result.m3CostLevelData[i]).toBe(0);
    }
  });

  it("falls back to 0 for a metric that is undefined on an otherwise-matched record", () => {
    const now = new Date(2026, 6, 20); // July 20, 2026
    const records: MarginHistoryDto[] = [
      {
        date: new Date(2026, 5, 15), // June 2026, slot 11 (1 month back)
        m0: { percentage: undefined, costLevel: undefined },
      } as MarginHistoryDto,
    ];

    const result = mapMarginDataToMonthlyArrays(records, now);

    expect(result.m0PercentageData[11]).toBe(0);
    expect(result.m0CostLevelData[11]).toBe(0);
  });
});
```

- [ ] **Step 2: Run the tests and verify they pass**

Run: `cd frontend && npm test -- --testPathPattern=MarginsChart.utils --watchAll=false`
Expected: PASS — all 7 test cases green. (These tests characterize existing, already-correct behavior extracted verbatim in the previous task; per `arch-review.r1.md` NFR-2 no implementation change is expected here. If any assertion fails, stop and re-verify the hand-derived expected values against the arithmetic in `arch-review.r1.md` Decision 1-3 before touching `MarginsChart.utils.ts` — a failure here means either the extraction in the previous task was not verbatim, or an expected value in this test was mis-derived, not that the original code has a latent bug that should now be "fixed".)

- [ ] **Step 3: Run the full frontend validation suite**

Run: `cd frontend && npm run build && npm run lint && npm test -- --watchAll=false`
Expected: build succeeds, lint passes with no new warnings/errors, full test suite passes.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/catalog/detail/tabs/MarginsTab/__tests__/MarginsChart.utils.test.ts
git commit -m "test(margins-chart): cover year-boundary, mid-year, exclusion, and fallback cases in mapMarginDataToMonthlyArrays"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (January year-boundary) → `add-margin-mapping-coverage-tests` Step 1, test `"rolls all 12 slots back into the previous year when run in January"` plus the January-exclusion test.
- FR-2 (mid-year, no crossover) → same step, test `"splits slots across the previous and current year with no crossover error when run mid-year"`.
- FR-3 (current-month exclusion) → same step, tests `"does not include January of the current year..."` and `"excludes a record in the current month/year but keeps a same-month record from a different year"`.
- FR-4 (missing-data fallback to 0) → same step, tests `"falls back to 0 for a month with no matching record..."`, `"falls back to 0 for a metric that is undefined..."`, and the empty-array smoke test.
- NFR-1 (determinism) → satisfied by the injected `now` parameter (`extract-margin-mapping-function` Step 3); every test passes a fixed `now`, no fake timers, no wall-clock dependency.
- NFR-2 (no behavior change) → satisfied by the verbatim-move instruction in `extract-margin-mapping-function` Step 3 plus the full-suite regression run in Step 6 of that task.

**Placeholder scan:** No "TBD"/"TODO"/"handle edge cases" language; every step has complete, runnable code and exact commands.

**Type consistency:** `mapMarginDataToMonthlyArrays(marginHistory: MarginHistoryDto[], now: Date = new Date())` returning `MonthlyMarginArrays` is defined once in `extract-margin-mapping-function` Step 3 and used identically (same parameter order, same field names) in `MarginsChart.tsx` Step 5 and both test-file versions in `add-margin-mapping-coverage-tests` Step 1.
