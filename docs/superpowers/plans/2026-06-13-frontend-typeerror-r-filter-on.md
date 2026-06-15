# Fix frontend TypeError `r.filter is not a function` at `Yq` Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Identify the minified `Yq` symbol that emitted `TypeError: r.filter is not a function` on `/`, apply a local `Array.isArray` guard at the call site, audit and patch sibling array-method call sites in the three suspect PRs (#2962, #2943, #2948), and add regression tests that fail without the fix.

**Architecture:** Defensive guards live at the React Query consumer boundary (the component), never as a global wrapper or hook-side normalization. The narrowest correct guard for `.filter is not a function` is `Array.isArray(x) ? x : []`, which catches both `null` and non-array shape drift. Loading and error branches stay explicit; the guard only protects the success branch from contract drift (per FR-2, NFR-3).

**Tech Stack:** React 18 + TypeScript + React Query (`@tanstack/react-query`) + Jest + React Testing Library + Create React App (CRA) build with sourcemaps.

---

## File Structure

**Files modified (no new files):**

- `frontend/src/components/pages/Dashboard.tsx` — primary fix site for the `Yq` symbol. The `userSettings.tiles.reduce(...)` and `allTileData.filter(...)` calls in the `visibleTileData` memo are unprotected against non-array shapes.
- `frontend/src/components/dashboard/DashboardSettings.tsx` — sibling fixes. `userSettings?.tiles.filter(...)`, `userSettings?.tiles.find(...)`, and `availableTiles.filter(...)` chains have the same gap.
- `frontend/src/components/pages/__tests__/Dashboard.test.tsx` — extend existing test file with regression cases for non-array shapes.
- `frontend/src/components/dashboard/__tests__/DashboardSettings.test.tsx` — extend (create if absent) with regression cases for sibling fixes.

**Files audited (read-only unless a finding triggers an edit):**

- `frontend/src/components/pages/Journal/JournalList.tsx` — verify the `currentQuery.data?.entries || []` pattern around line 202 is safe.
- The full diffs of PRs #2962, #2943, #2948 against their merge base — grep for `.filter(`, `.map(`, `.reduce(` in changed `.tsx`/`.ts` hunks (FR-3).

**Decisions locked here:**

- Defensive guard pattern: `const xs = Array.isArray(data) ? data : []` (primary, FR-2) or `const xs = data ?? []` (sibling sites where the TS type already excludes non-array values).
- No new utility module — guards are inlined per existing style.
- No hook-layer normalization — preserves loading/empty/error semantics (Decision 3 of arch-review).

---

## Task 1: Reproduce, then resolve the `Yq` symbol via a local sourcemap build

**Files:**
- Read: `frontend/Dockerfile`, `frontend/package.json` (to confirm CRA defaults)
- Build artifact (created, then discarded): `frontend/build/static/js/main.*.js` + `.map`

- [ ] **Step 1: Confirm CRA emits sourcemaps by default**

Run:
```bash
grep -n 'GENERATE_SOURCEMAP' frontend/Dockerfile frontend/package.json frontend/.env* 2>/dev/null
```
Expected: no occurrence of `GENERATE_SOURCEMAP=false` outside dockerized prod stages. If found, override in the next step with `GENERATE_SOURCEMAP=true npm run build`.

- [ ] **Step 2: Build the frontend with sourcemaps**

Run:
```bash
cd frontend && GENERATE_SOURCEMAP=true npm run build
```
Expected: `build/static/js/main.<hash>.js` and `build/static/js/main.<hash>.js.map` exist.

- [ ] **Step 3: Locate `Yq` in the minified bundle**

Run:
```bash
cd frontend && grep -onE '\bYq\b' build/static/js/main.*.js | head -5
```
Expected: at least one match with column offset, e.g. `build/static/js/main.abcdef.js:1:248913:Yq`.

- [ ] **Step 4: Walk the sourcemap back to source**

Install one-shot tool and resolve the position:
```bash
cd frontend && npx --yes source-map-cli@1 resolve \
  build/static/js/main.*.js.map \
  1 248913
```
(Replace `248913` with the actual column from Step 3. The CLI prints `source, line, column, name`.)

Expected output shape:
```
{
  source: "webpack:///./src/components/pages/Dashboard.tsx",
  line: 38,
  column: <n>,
  name: "<original identifier>"
}
```

- [ ] **Step 5: Record the resolved site in the PR notes**

Open or create `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/symbol-resolution.md` and write:
```markdown
## Yq resolution
- Minified symbol: Yq
- Source file: <result>
- Source line: <result>
- Original identifier (name field): <result>
- Bundle hash: <main.<hash>.js>
```

If the symbol resolves outside `Dashboard.tsx` / `DashboardSettings.tsx` / `JournalList.tsx`, treat Task 3 as a template and re-target the file/line accordingly; the guard shape is unchanged.

- [ ] **Step 6: Discard the build output (we do not check it in)**

Run:
```bash
cd frontend && rm -rf build
```
Expected: no `build/` directory remains; `git status` shows no changes.

- [ ] **Step 7: Commit the symbol-resolution note**

Run:
```bash
git add artifacts/feat-telemetry-frontend-typeerror-r-filter-on/symbol-resolution.md
git commit -m "chore: record Yq symbol resolution for r.filter fix"
```

---

## Task 2: Write the failing regression test for the Dashboard call site

**Files:**
- Modify: `frontend/src/components/pages/__tests__/Dashboard.test.tsx`

- [ ] **Step 1: Add three regression cases to the existing `describe("Dashboard", ...)` block**

Append the following tests inside the `describe("Dashboard", () => { ... })` block, after the existing tests (before the final closing `});` on line 447). Place them immediately after the `"renders (does not hide) tiles the backend flagged as unauthorized"` test:

```tsx
  it("does not throw when useTileData returns null (contract drift)", () => {
    mockUseTileData.mockReturnValue({
      data: null,
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<Dashboard />)).not.toThrow();

    const tileCount = screen.getByTestId("tile-count");
    expect(tileCount).toHaveTextContent("0");
  });

  it("does not throw when userSettings.tiles is null (contract drift)", () => {
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: null, lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<Dashboard />)).not.toThrow();

    const tileCount = screen.getByTestId("tile-count");
    expect(tileCount).toHaveTextContent("0");
  });

  it("does not throw when useTileData returns a non-array object (contract drift)", () => {
    mockUseTileData.mockReturnValue({
      data: { unexpected: "shape", length: 1 } as any,
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<Dashboard />)).not.toThrow();

    const tileCount = screen.getByTestId("tile-count");
    expect(tileCount).toHaveTextContent("0");
  });
```

Rationale for the third case: the existing line-36 guard is `!allTileData.length`, so a non-array object with a truthy `length` property bypasses the guard and reaches `.filter()` — this is the exact failure mode the arch review flagged as plausible.

- [ ] **Step 2: Run only the new tests to confirm they FAIL against current code**

Run:
```bash
cd frontend && npx jest src/components/pages/__tests__/Dashboard.test.tsx \
  -t "contract drift" --no-coverage
```
Expected: 3 tests run, **at least one fails** with `TypeError: <x> is not a function` (most likely the third one). The first two may currently pass via the existing `!userSettings || !allTileData.length` guard when `tiles` is `null` — that's fine; they exist to lock in correct behavior after the fix and to fail loudly if a future change weakens the guard.

- [ ] **Step 3: Commit the failing test**

Run:
```bash
git add frontend/src/components/pages/__tests__/Dashboard.test.tsx
git commit -m "test: add regression cases for non-array tile/settings data on Dashboard"
```

---

## Task 3: Apply the Array.isArray guard at the Dashboard call site

**Files:**
- Modify: `frontend/src/components/pages/Dashboard.tsx` (lines 35–53, and the `handleReorder` block on lines 55–76)

- [ ] **Step 1: Replace the `visibleTileData` memo with the guarded version**

In `frontend/src/components/pages/Dashboard.tsx`, replace lines 35–53 (the `const visibleTileData = React.useMemo(...)` block) with:

```tsx
  const visibleTileData = React.useMemo(() => {
    const tileData = Array.isArray(allTileData) ? allTileData : [];
    const settingsTiles = Array.isArray(userSettings?.tiles) ? userSettings!.tiles : [];

    if (!userSettings || tileData.length === 0) return [];

    const userTileSettings = settingsTiles.reduce((acc, tile) => {
      acc[tile.tileId] = tile;
      return acc;
    }, {} as Record<string, any>);

    return tileData
      .filter(tile => {
        const userSetting = userTileSettings[tile.tileId];
        return userSetting?.isVisible || (tile.autoShow && userSetting?.isVisible !== false);
      })
      .sort((a, b) => {
        const aOrder = userTileSettings[a.tileId]?.displayOrder ?? 999;
        const bOrder = userTileSettings[b.tileId]?.displayOrder ?? 999;
        return aOrder - bOrder;
      });
  }, [userSettings, allTileData]);
```

Key changes:
- `tileData` is `Array.isArray`-guarded once at the top of the memo; downstream `.filter`/`.sort` use it.
- `settingsTiles` is `Array.isArray`-guarded; downstream `.reduce` uses it.
- The original line-36 short-circuit is preserved (now using `tileData.length === 0` for clarity, equivalent to `!tileData.length`).

- [ ] **Step 2: Guard `handleReorder`'s reads of `userSettings.tiles`**

In the same file, replace lines 55–76 (the `const handleReorder = async (tileIds: string[]) => { ... }` block) with:

```tsx
  const handleReorder = async (tileIds: string[]) => {
    if (!userSettings) return;

    const settingsTiles = Array.isArray(userSettings.tiles) ? userSettings.tiles : [];

    const updatedTiles = tileIds.map((tileId, index) => {
      const existingTile = settingsTiles.find(t => t.tileId === tileId);
      return {
        tileId,
        isVisible: existingTile?.isVisible ?? true,
        displayOrder: index
      };
    });

    settingsTiles.forEach(tile => {
      if (!tileIds.includes(tile.tileId)) {
        updatedTiles.push(tile);
      }
    });

    await saveDashboardSettings.mutateAsync({ tiles: updatedTiles });
  };
```

Rationale: `handleReorder` runs only when the user clicks reorder, so it is not on the `Yq` initial-render path — but a non-array `tiles` would crash this handler too. Guarding it now is one-line cost (FR-3 sibling fix in the same file).

- [ ] **Step 3: Run the regression tests — they MUST PASS**

Run:
```bash
cd frontend && npx jest src/components/pages/__tests__/Dashboard.test.tsx \
  -t "contract drift" --no-coverage
```
Expected: all 3 new tests PASS.

- [ ] **Step 4: Run the full Dashboard test file to confirm no regression**

Run:
```bash
cd frontend && npx jest src/components/pages/__tests__/Dashboard.test.tsx --no-coverage
```
Expected: every existing test still passes (including the 3 new ones). Pre-existing count was 18; after Task 2 it should be 21 passing.

- [ ] **Step 5: Commit the fix**

Run:
```bash
git add frontend/src/components/pages/Dashboard.tsx
git commit -m "fix: guard Dashboard tile arrays against non-array shape drift"
```

---

## Task 4: Audit and fix sibling call sites in `DashboardSettings.tsx`

This component is reachable from `/` (one Settings click away). It uses the same `useUserDashboardSettings` + `useAvailableTiles` pair with the same `tiles.filter(...)` / `tiles.find(...)` shape that just failed in Dashboard. Patch under FR-3.

**Files:**
- Modify: `frontend/src/components/dashboard/DashboardSettings.tsx` (lines 18, 27–48, 129)

- [ ] **Step 1: Add an `Array.isArray`-guarded local at the top of the component body**

In `frontend/src/components/dashboard/DashboardSettings.tsx`, immediately after line 25 (`const isLoading = tilesLoading || settingsLoading;`), insert:

```tsx
  const settingsTiles = Array.isArray(userSettings?.tiles) ? userSettings!.tiles : [];
  const safeAvailableTiles = Array.isArray(availableTiles) ? availableTiles : [];
```

- [ ] **Step 2: Replace `userSettings?.tiles.find(...)` in `handleToggleTile` (line 28)**

Change:
```tsx
    const userTile = userSettings?.tiles.find(t => t.tileId === tile.tileId);
```
to:
```tsx
    const userTile = settingsTiles.find(t => t.tileId === tile.tileId);
```

- [ ] **Step 3: Replace the `visibleTiles` derivation on line 38**

Change:
```tsx
  const visibleTiles = userSettings?.tiles.filter(t => t.isVisible).map(t => t.tileId) || [];
```
to:
```tsx
  const visibleTiles = settingsTiles.filter(t => t.isVisible).map(t => t.tileId);
```

- [ ] **Step 4: Replace `availableTiles.filter(...)` on lines 40 and 50**

Change line 40's:
```tsx
  const filteredTiles = availableTiles.filter(tile => {
```
to:
```tsx
  const filteredTiles = safeAvailableTiles.filter(tile => {
```

Change line 50's:
```tsx
  const newTilesCount = availableTiles.filter(tile =>
    !visibleTiles.includes(tile.tileId) && tile.defaultEnabled
  ).length;
```
to:
```tsx
  const newTilesCount = safeAvailableTiles.filter(tile =>
    !visibleTiles.includes(tile.tileId) && tile.defaultEnabled
  ).length;
```

- [ ] **Step 5: Replace the inline `availableTiles.length` reads in the filter-tabs render (lines 104 and 106)**

Change:
```tsx
            { key: 'all', label: 'Vše', count: availableTiles.length },
            { key: 'enabled', label: 'Aktivní', count: visibleTiles.length },
            { key: 'disabled', label: 'Neaktivní', count: availableTiles.length - visibleTiles.length }
```
to:
```tsx
            { key: 'all', label: 'Vše', count: safeAvailableTiles.length },
            { key: 'enabled', label: 'Aktivní', count: visibleTiles.length },
            { key: 'disabled', label: 'Neaktivní', count: safeAvailableTiles.length - visibleTiles.length }
```

- [ ] **Step 6: Replace the `userSettings?.tiles.find(...)` on line 129 (inside the tiles render)**

Change:
```tsx
            const userTile = userSettings?.tiles.find(t => t.tileId === tile.tileId);
```
to:
```tsx
            const userTile = settingsTiles.find(t => t.tileId === tile.tileId);
```

- [ ] **Step 7: Write a regression test that proves the fix**

Check whether the test file already exists:
```bash
ls frontend/src/components/dashboard/__tests__/DashboardSettings.test.tsx 2>/dev/null && echo EXISTS || echo MISSING
```

If `MISSING`, create `frontend/src/components/dashboard/__tests__/DashboardSettings.test.tsx` with the full body below. If `EXISTS`, append the new test (`describe("contract drift", ...)`) to it, reusing the existing mocks. The file body for the MISSING case:

```tsx
import React from "react";
import { render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import DashboardSettings from "../DashboardSettings";
import {
  useAvailableTiles,
  useUserDashboardSettings,
  useEnableTile,
  useDisableTile,
} from "../../../api/hooks/useDashboard";

jest.mock("../../../api/hooks/useDashboard", () => ({
  useAvailableTiles: jest.fn(),
  useUserDashboardSettings: jest.fn(),
  useEnableTile: jest.fn(),
  useDisableTile: jest.fn(),
}));

const mockUseAvailableTiles = useAvailableTiles as jest.MockedFunction<
  typeof useAvailableTiles
>;
const mockUseUserDashboardSettings = useUserDashboardSettings as jest.MockedFunction<
  typeof useUserDashboardSettings
>;
const mockUseEnableTile = useEnableTile as jest.MockedFunction<typeof useEnableTile>;
const mockUseDisableTile = useDisableTile as jest.MockedFunction<typeof useDisableTile>;

const renderWithQueryClient = (component: React.ReactElement) => {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={queryClient}>{component}</QueryClientProvider>,
  );
};

describe("DashboardSettings — contract drift", () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUseEnableTile.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
    mockUseDisableTile.mockReturnValue({
      mutateAsync: jest.fn(),
      isPending: false,
    } as any);
  });

  it("does not throw when useAvailableTiles returns null", () => {
    mockUseAvailableTiles.mockReturnValue({
      data: null,
      isLoading: false,
      error: null,
    } as any);
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: [], lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<DashboardSettings />)).not.toThrow();
  });

  it("does not throw when userSettings.tiles is null", () => {
    mockUseAvailableTiles.mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    } as any);
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: null, lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<DashboardSettings />)).not.toThrow();
  });

  it("does not throw when useAvailableTiles returns a non-array object", () => {
    mockUseAvailableTiles.mockReturnValue({
      data: { unexpected: "shape" } as any,
      isLoading: false,
      error: null,
    } as any);
    mockUseUserDashboardSettings.mockReturnValue({
      data: { tiles: [], lastModified: "2024-01-01T00:00:00Z" },
      isLoading: false,
      error: null,
    } as any);

    expect(() => renderWithQueryClient(<DashboardSettings />)).not.toThrow();
  });
});
```

- [ ] **Step 8: Run the new DashboardSettings tests**

Run:
```bash
cd frontend && npx jest src/components/dashboard/__tests__/DashboardSettings.test.tsx \
  -t "contract drift" --no-coverage
```
Expected: 3 tests, all PASS.

- [ ] **Step 9: Run the full DashboardSettings test file (if pre-existing tests are present)**

Run:
```bash
cd frontend && npx jest src/components/dashboard/__tests__/DashboardSettings.test.tsx --no-coverage
```
Expected: every test passes. If pre-existing assertions break because of a name change introduced in Steps 1–6, update those assertions — but do NOT revert the guard rename: existing callers of `availableTiles.length` in tests must move to `safeAvailableTiles.length` only if the test inspects the variable name directly (rare; usually the test inspects rendered output, which is unchanged).

- [ ] **Step 10: Commit**

Run:
```bash
git add frontend/src/components/dashboard/DashboardSettings.tsx \
        frontend/src/components/dashboard/__tests__/DashboardSettings.test.tsx
git commit -m "fix: guard DashboardSettings tile arrays against non-array shape drift"
```

---

## Task 5: Audit JournalList.tsx (PR #2943, #2948) — read-only unless a finding triggers an edit

**Files:**
- Read: `frontend/src/components/pages/Journal/JournalList.tsx`
- Modify: only if Step 2 surfaces an unsafe call site

- [ ] **Step 1: Confirm the line 202 pattern is safe**

Run:
```bash
grep -nE '\b(filter|map|reduce|find|forEach|sort)\b\(' \
  frontend/src/components/pages/Journal/JournalList.tsx
```

For each match, verify the value being called on is either:
- already array-defaulted (`x || []`, `x ?? []`, destructure default `= []`), OR
- statically an array literal (`['a', 'b'].map(...)`), OR
- a local that was just `Array.isArray`-checked.

Document the audit in `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md` with one line per call site: `path:line — variable — guard kind — verdict (SAFE / NEEDS FIX)`.

Expected: line 202's `currentQuery.data?.entries || []` is already safe (`|| []` catches both `null` and `undefined`). The `entries.map(...)` calls below it operate on the guarded local.

- [ ] **Step 2: If the audit surfaces any `NEEDS FIX` site, patch it with the same `Array.isArray` guard pattern**

For each unsafe site, apply the local guard pattern from Task 3 (Step 1) and add a regression test next to the affected component following the Task 4 (Step 7) template. If no unsafe sites are found, skip to Step 3.

- [ ] **Step 3: Commit the audit notes**

Run:
```bash
git add artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md
git commit -m "chore: record JournalList array-method audit notes"
```

(If Step 2 produced code changes, include them in the same commit and adjust the message to `fix: guard JournalList <site> against non-array shape drift`.)

---

## Task 6: Audit the three suspect PRs (#2962, #2943, #2948) for any other unsafe array-method call sites

This implements FR-3. Three same-day PRs are in scope; we already audited the Dashboard / DashboardSettings / JournalList surface. This task covers any *other* files those PRs touched.

**Files:**
- Read-only: every `.ts`/`.tsx` file changed in PRs #2962, #2943, #2948
- Modify: only if a finding requires a guard

- [ ] **Step 1: Get the list of changed `.ts`/`.tsx` files per PR**

Run:
```bash
for pr in 2962 2943 2948; do
  echo "=== PR #$pr ==="
  gh pr view "$pr" --json files --jq '.files[].path' \
    | grep -E '\.tsx?$' \
    | grep -v '__tests__' \
    | sort -u
done
```
Expected: a deduped list of frontend source files. Save to `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md` under a `## PR file lists` heading.

- [ ] **Step 2: For each file in the list, find every array-method call introduced or modified by the PR**

Run (per PR):
```bash
gh pr diff 2962 -- '*.tsx' '*.ts' \
  | grep -nE '^\+.*\.(filter|map|reduce|find|forEach|sort)\(' \
  | grep -v '^\+\+\+'
```
Repeat for `2943` and `2948`.

For each `+` line, open the file at the changed location and verify the receiver is either array-defaulted, an array literal, or `Array.isArray`-checked. If not, record as `NEEDS FIX`.

- [ ] **Step 3: Apply the `Array.isArray` guard to any unsafe site surfaced in Step 2**

For each `NEEDS FIX` site:
1. Apply the local guard (Task 3 pattern).
2. Add a regression test next to the component (Task 4 Step 7 template — render with `data: null`, `data: { x: null }`, `data: { unexpected }` shapes; assert `not.toThrow()`).
3. Run that test file to confirm RED before the fix (`git stash` the fix, run, `git stash pop`) and GREEN after.

If no unsafe sites are found, skip to Step 4.

- [ ] **Step 4: Update the audit notes with sites reviewed, sites fixed, and sites intentionally left alone**

Append to `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md`:
```markdown
## FR-3 audit results

### Sites reviewed
- <file>:<line> — <call> — verdict

### Sites fixed
- <file>:<line> — <call> — guard applied

### Sites left as-is with rationale
- <file>:<line> — <call> — rationale (e.g. "iterable is a literal array", "TS type already excludes non-array")
```

- [ ] **Step 5: Commit the audit notes (and any code/test changes)**

Run:
```bash
git add artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md
# include any modified .ts/.tsx and new __tests__ files surfaced by Step 3:
# git add frontend/src/...
git commit -m "fix: guard remaining unsafe array-method call sites from #2962/#2943/#2948"
```

If Step 3 produced no code changes, change the message to `chore: complete FR-3 audit (no additional fixes required)`.

---

## Task 7: Decide on the upstream-bug follow-up (NFR-3)

The guard fixes the consumer. NFR-3 requires that we do not silently hide a real backend contract violation. This task asks the question and records the answer once — it is not optional.

**Files:**
- Modify: `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md`

- [ ] **Step 1: Determine the upstream shape that triggered the original `Yq` failure**

If Task 1 identified a real shape mismatch (e.g. the resolved source line consumes a backend payload that the TypeScript client declares as `T[]` but the API returns `null`), record:
```markdown
## NFR-3 follow-up

- Upstream culprit: <endpoint> returns <observed shape>, but TS client declares <declared shape>.
- Trigger condition: <e.g. 403 short-circuit, 204 No Content, missing field on certain user roles>.
- Follow-up issue: <gh issue create URL> — title: "Backend/OpenAPI: <endpoint> returns non-array <field>"
```

Run (if a follow-up is warranted):
```bash
gh issue create \
  --title "Backend/OpenAPI: <endpoint> returns non-array <field> for <condition>" \
  --body "Surfaced during fix for telemetry 'r.filter is not a function'. \
The frontend now guards with Array.isArray, but the contract drift should be \
fixed at the source. See artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md."
```

If Task 1 instead showed the failure was a pure client-side timing/race issue (no contract drift), record:
```markdown
## NFR-3 follow-up

- No upstream contract drift identified. Failure mode is client-side timing/race; the guard is the correct and sufficient fix.
```

- [ ] **Step 2: Commit the decision**

Run:
```bash
git add artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md
git commit -m "chore: record NFR-3 follow-up decision for r.filter fix"
```

---

## Task 8: Run the full validation gates

These are the gates from `CLAUDE.md` (`npm run build`, `npm run lint`, full Jest run). E2E is out of scope per the spec.

**Files:**
- None modified — pure validation.

- [ ] **Step 1: Run `npm run build`**

Run:
```bash
cd frontend && npm run build
```
Expected: exit code 0; build completes; no TypeScript errors. Discard the output directory afterward:
```bash
rm -rf frontend/build
```

- [ ] **Step 2: Run `npm run lint`**

Run:
```bash
cd frontend && npm run lint
```
Expected: exit code 0; no new lint warnings beyond what was on `main`.

- [ ] **Step 3: Run the full Jest suite for the changed scope**

Run:
```bash
cd frontend && npx jest \
  src/components/pages/__tests__/Dashboard.test.tsx \
  src/components/dashboard/__tests__/DashboardSettings.test.tsx \
  --no-coverage
```
Expected: all tests pass. If Task 6 added more `__tests__` files, include them in the path list.

- [ ] **Step 4: Run the full frontend test suite to confirm no regression elsewhere**

Run:
```bash
cd frontend && npx jest --no-coverage
```
Expected: every test passes. If a pre-existing unrelated test was already failing on `main`, document it in the PR description and do not block on it.

- [ ] **Step 5: Commit a no-op marker only if any of Steps 1–4 required a follow-up fix**

If validation passed cleanly, there is nothing to commit here — proceed to Task 9. If a step required a fix (e.g. a lint warning the guard introduced), apply the minimal fix and commit:
```bash
git add <changed files>
git commit -m "fix: resolve lint/build issue from r.filter guard"
```

---

## Task 9: Push and open the PR with audit notes in the description

**Files:**
- None modified — pushes existing commits and creates the PR.

- [ ] **Step 1: Push the branch**

Run:
```bash
git push -u origin feat-telemetry-frontend-typeerror-r-filter-on
```
Expected: branch is pushed and tracked.

- [ ] **Step 2: Open the PR with a description that satisfies FR-3's recording requirement**

Run:
```bash
gh pr create --base main \
  --title "fix: guard array-method call sites against contract drift (telemetry r.filter)" \
  --body "$(cat <<'EOF'
## Summary

Telemetry captured `TypeError: r.filter is not a function` at minified symbol `Yq` on the SPA root path `/` on 2026-06-12. Root cause: an unprotected `.filter()` / `.reduce()` chain on data returned by `useTileData` / `useUserDashboardSettings`. The destructuring default `= []` only catches `undefined`, not `null` or non-array shape drift.

## Changes

- `Dashboard.tsx` — `Array.isArray`-guarded locals around `tileData` and `userSettings.tiles` before `.reduce`/`.filter`/`.sort`/`.find`/`.forEach`.
- `DashboardSettings.tsx` — same guard pattern around `availableTiles` and `userSettings.tiles` (sibling fix under FR-3; reachable from `/` via Settings click).
- Regression tests in `Dashboard.test.tsx` and `DashboardSettings.test.tsx` — `data: null`, `data: { tiles: null }`, `data: { unexpected: shape }` cases. Failed pre-fix; pass post-fix.

## FR-3 audit

See `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md` for the per-PR (#2962, #2943, #2948) call-site review. Includes sites reviewed, sites fixed, and sites left as-is with rationale.

## NFR-3 follow-up

See `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/audit-notes.md` (`## NFR-3 follow-up` section). Either links a backend/OpenAPI issue for upstream contract drift, or explicitly records "no upstream contract drift identified."

## Symbol resolution

`Yq` was resolved via a local `GENERATE_SOURCEMAP=true npm run build` and `source-map-cli`. The original source location is recorded in `artifacts/feat-telemetry-frontend-typeerror-r-filter-on/symbol-resolution.md`.

## Test plan

- [x] `npm run build` (frontend)
- [x] `npm run lint` (frontend)
- [x] `npx jest src/components/pages/__tests__/Dashboard.test.tsx`
- [x] `npx jest src/components/dashboard/__tests__/DashboardSettings.test.tsx`
- [x] `npx jest` (full frontend suite)
- [ ] E2E (out of scope per spec — nightly suite covers the dashboard render path)
EOF
)"
```

Expected: PR URL is printed. Verify by visiting the URL that the audit notes and the symbol-resolution note are linked.

---

## Self-Review Notes

Reviewed against `spec.r1.md`:

- **FR-1 (Locate `Yq`)** → Task 1 (sourcemap walk + artifact record).
- **FR-2 (Null-safe fix at call site)** → Task 3 (Dashboard) + Task 4 (DashboardSettings). Uses `Array.isArray` per the arch-review amendment (catches `null` and non-array shapes, not just `undefined`). Loading branch is preserved (Dashboard.tsx lines 78, 113–116 unchanged).
- **FR-3 (Audit sibling call sites in the three PRs)** → Task 4 (DashboardSettings — known sibling), Task 5 (JournalList — known sibling), Task 6 (broader PR diff sweep). Findings recorded in `audit-notes.md`.
- **FR-4 (Regression test)** → Task 2 (Dashboard tests fail pre-fix), Task 3 Step 3 (pass post-fix). Task 4 Step 7 mirrors for DashboardSettings. Test files live next to the component per existing convention.
- **NFR-1 (Performance)** → Guards are single inline checks; no bundle/perf impact.
- **NFR-2 (Security)** → No surface — defensive bug fix only.
- **NFR-3 (Observability)** → Task 7 explicitly demands a decision: either file a backend follow-up issue or record "no contract drift."
- **NFR-4 (Compatibility)** → No build config, polyfill, or browser target changes.

Placeholder scan: every code step has the literal code to type. No "TBD," no "add error handling," no "similar to Task N." Type/name consistency: `settingsTiles` / `safeAvailableTiles` / `tileData` are introduced consistently across Dashboard.tsx (Task 3) and DashboardSettings.tsx (Task 4); test mock function names match the existing `Dashboard.test.tsx` conventions.

Spec coverage gap check: spec Out-of-Scope items (refactoring, global runtime guards, telemetry tuning, E2E test, backporting) are not touched by any task. Confirmed match.
