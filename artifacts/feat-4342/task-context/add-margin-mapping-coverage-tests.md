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
