# Marketing Calendar – 14-day view starts at current week Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Change the marketing calendar's "14 dní" (two-week) view so it anchors on the current week (today in row 1) while preserving the 5-week view's existing "today in week 2" behavior.

**Architecture:** Single-file frontend change. The page-level container `MarketingCalendarPage.tsx` owns the calendar anchor state. Replace the shared `getCalendarStartForToday()` helper with a view-aware variant (`'fiveWeeks' | 'twoWeeks'`) so the −7 day offset only applies to the 5-week mode. Reset `currentDate` on view-mode toggle so the keyed remount of `MarketingMonthCalendar` receives the correct `initialDate`. The `useMemo` that computes the fallback fetch range narrows `viewMode` to exclude `'list'`.

**Tech Stack:** React 18 + TypeScript, Jest + React Testing Library, FullCalendar (mocked in tests).

---

## File Structure

**Files modified (production):**
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — replace the shared anchor helper, narrow `viewMode` in the fallback `useMemo`, reset `currentDate` on view-mode toggle, route `goToToday` through the new helper.

**Files modified (tests):**
- `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` — enhance the `MarketingMonthCalendar` mock to capture `initialDate`, enhance the `CalendarNavigation` mock to expose the `onToday` callback, add new assertions for the 14-day anchor, the 5-week regression anchor, the toggle behavior, and the "Dnes" button behavior.

**No new files. No backend changes. No DTO or migration changes.**

---

## Task 1: Enhance test mocks (no production change)

**Goal:** Add the test-side infrastructure required to observe `initialDate` and invoke the `onToday` callback. This is a refactor of the existing mocks; existing assertions must continue to pass.

**Files:**
- Modify: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

- [ ] **Step 1: Read the existing test file**

Run: `cat frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx | head -90`
Expected: confirm the current mock shapes for `MarketingMonthCalendar` and `CalendarNavigation`.

- [ ] **Step 2: Replace the `calendarRenderLog` type and the `MarketingMonthCalendar` mock so it captures `initialDate` and exposes a `gotoDate` stub via the forwarded `calendarRef`**

Replace lines 5–25 of `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`:

```tsx
// Track every render of the calendar mock so tests can verify mount/unmount and the props it receives.
const calendarRenderLog: { viewName: string; initialDate: Date; mountId: number }[] = [];
const gotoDateMock = jest.fn();

jest.mock("../../calendar/MarketingMonthCalendar", () => {
  const React = require("react");
  let calendarMountCounter = 0;
  function MarketingMonthCalendarMock(props: {
    viewName: string;
    initialDate: Date;
    calendarRef?: React.MutableRefObject<unknown>;
  }) {
    const mountId = React.useMemo(() => ++calendarMountCounter, []);
    React.useEffect(() => {
      calendarRenderLog.push({
        viewName: props.viewName,
        initialDate: new Date(props.initialDate),
        mountId,
      });
      if (props.calendarRef) {
        props.calendarRef.current = {
          getApi: () => ({
            prev: jest.fn(),
            next: jest.fn(),
            gotoDate: gotoDateMock,
          }),
        };
      }
    });
    return (
      <div
        data-testid="marketing-month-calendar"
        data-view-name={props.viewName}
        data-mount-id={String(mountId)}
        data-initial-date={props.initialDate.toISOString()}
      />
    );
  }
  return { __esModule: true, default: MarketingMonthCalendarMock };
});
```

- [ ] **Step 3: Replace the `CalendarNavigation` mock so it renders a clickable "Dnes" button wired to `onToday` (and `Prev`/`Next` wired to `onPrevious`/`onNext`)**

Replace lines 48–51 of the same file:

```tsx
jest.mock("../../../manufacture/calendar/CalendarNavigation", () => ({
  __esModule: true,
  default: ({
    onPrevious,
    onNext,
    onToday,
  }: {
    onPrevious: () => void;
    onNext: () => void;
    onToday: () => void;
  }) => (
    <div data-testid="calendar-navigation">
      <button data-testid="nav-prev" onClick={onPrevious}>
        Prev
      </button>
      <button data-testid="nav-today" onClick={onToday}>
        Dnes
      </button>
      <button data-testid="nav-next" onClick={onNext}>
        Next
      </button>
    </div>
  ),
}));
```

- [ ] **Step 4: Reset `gotoDateMock` in `beforeEach`**

Find the existing `beforeEach` (lines 79–82) and append the reset:

```tsx
beforeEach(() => {
  calendarRenderLog.length = 0;
  calendarHookCalls.length = 0;
  gotoDateMock.mockClear();
});
```

- [ ] **Step 5: Run the existing test suite to verify the refactored mocks don't break existing assertions**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: all existing tests pass (8 tests).

- [ ] **Step 6: Commit**

```bash
git add frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "test: enrich MarketingCalendarPage mocks to expose initialDate and onToday"
```

---

## Task 2: Add failing unit tests for the new behavior (TDD red)

**Goal:** Write the assertions described in the spec. They must fail before the production change lands.

**Files:**
- Modify: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

- [ ] **Step 1: Add three helper functions at module scope just below `daysBetween` (around line 154)**

Append below the existing `daysBetween` helper:

```tsx
function startOfWeekMondayForTest(date: Date): Date {
  const clone = new Date(date);
  const dow = clone.getDay();
  const daysToMonday = dow === 0 ? 6 : dow - 1;
  clone.setHours(0, 0, 0, 0);
  clone.setDate(clone.getDate() - daysToMonday);
  clone.setHours(0, 0, 0, 0);
  return clone;
}

function expectSameInstant(actual: Date, expected: Date) {
  expect(actual.getTime()).toBe(expected.getTime());
}
```

These helpers compute the expected anchor for assertions; the production helper is module-private and is tested through observable behavior.

- [ ] **Step 2: Update the existing test "requests a 35-day window for the default fiveWeeks view" to also assert the anchor**

Replace the existing test body (lines 157–162) with:

```tsx
it("requests a 35-day window for the default fiveWeeks view, anchored one week before current Monday", () => {
  render(<MarketingCalendarPage />);
  expect(calendarHookCalls.length).toBeGreaterThan(0);
  const last = calendarHookCalls[calendarHookCalls.length - 1];
  expect(daysBetween(last.startDate, last.endDate)).toBe(35);

  const currentMonday = startOfWeekMondayForTest(new Date());
  const expected = new Date(currentMonday);
  expected.setDate(expected.getDate() - 7);
  expectSameInstant(last.startDate, expected);
  expect(last.startDate.getHours()).toBe(0);
  expect(last.startDate.getMinutes()).toBe(0);
  expect(last.startDate.getSeconds()).toBe(0);
  expect(last.startDate.getMilliseconds()).toBe(0);
});
```

- [ ] **Step 3: Update the existing test "requests a 14-day window after switching to twoWeeks" to assert the new current-week anchor**

Replace the existing test body (lines 164–169) with:

```tsx
it("requests a 14-day window anchored on the current Monday after switching to twoWeeks", () => {
  render(<MarketingCalendarPage />);
  fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
  const last = calendarHookCalls[calendarHookCalls.length - 1];
  expect(daysBetween(last.startDate, last.endDate)).toBe(14);

  const expected = startOfWeekMondayForTest(new Date());
  expectSameInstant(last.startDate, expected);
  expect(last.startDate.getHours()).toBe(0);
  expect(last.startDate.getMinutes()).toBe(0);
  expect(last.startDate.getSeconds()).toBe(0);
  expect(last.startDate.getMilliseconds()).toBe(0);
});
```

- [ ] **Step 4: Add a new `describe` block for view-mode toggle behavior**

Append to the bottom of the file:

```tsx
describe("MarketingCalendarPage — view-mode toggle resets the anchor", () => {
  it("toggling fiveWeeks → twoWeeks updates initialDate to current Monday", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));

    const lastRender = calendarRenderLog[calendarRenderLog.length - 1];
    expect(lastRender.viewName).toBe("twoWeeks");

    const expected = startOfWeekMondayForTest(new Date());
    expectSameInstant(lastRender.initialDate, expected);
  });

  it("toggling twoWeeks → fiveWeeks updates initialDate to Monday minus 7 days", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    fireEvent.click(screen.getByRole("button", { name: /5 týdnů/ }));

    const lastRender = calendarRenderLog[calendarRenderLog.length - 1];
    expect(lastRender.viewName).toBe("fiveWeeks");

    const expected = startOfWeekMondayForTest(new Date());
    expected.setDate(expected.getDate() - 7);
    expectSameInstant(lastRender.initialDate, expected);
  });

  it("repeated toggling 5w → 14d → 5w → 14d consistently lands on the correct anchor each time", () => {
    render(<MarketingCalendarPage />);
    const currentMonday = startOfWeekMondayForTest(new Date());
    const fiveWeekAnchor = new Date(currentMonday);
    fiveWeekAnchor.setDate(fiveWeekAnchor.getDate() - 7);

    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    expectSameInstant(
      calendarRenderLog[calendarRenderLog.length - 1].initialDate,
      currentMonday,
    );

    fireEvent.click(screen.getByRole("button", { name: /5 týdnů/ }));
    expectSameInstant(
      calendarRenderLog[calendarRenderLog.length - 1].initialDate,
      fiveWeekAnchor,
    );

    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    expectSameInstant(
      calendarRenderLog[calendarRenderLog.length - 1].initialDate,
      currentMonday,
    );
  });

  it("toggling to Seznam does not change initialDate (calendar unmounts, no new render)", () => {
    render(<MarketingCalendarPage />);
    const beforeLen = calendarRenderLog.length;
    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));
    // After switching to list, no further calendar renders should occur.
    expect(calendarRenderLog.length).toBe(beforeLen);
  });
});
```

- [ ] **Step 5: Add a new `describe` block for the "Dnes" button behavior**

Append to the bottom of the file:

```tsx
describe("MarketingCalendarPage — 'Dnes' button respects active view", () => {
  it("in 14-day mode, clicking 'Dnes' calls gotoDate with current Monday", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    fireEvent.click(screen.getByTestId("nav-today"));

    expect(gotoDateMock).toHaveBeenCalled();
    const arg = gotoDateMock.mock.calls[gotoDateMock.mock.calls.length - 1][0];
    expectSameInstant(arg, startOfWeekMondayForTest(new Date()));
  });

  it("in 5-week mode, clicking 'Dnes' calls gotoDate with Monday minus 7 days", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByTestId("nav-today"));

    expect(gotoDateMock).toHaveBeenCalled();
    const arg = gotoDateMock.mock.calls[gotoDateMock.mock.calls.length - 1][0];
    const expected = startOfWeekMondayForTest(new Date());
    expected.setDate(expected.getDate() - 7);
    expectSameInstant(arg, expected);
  });
});
```

- [ ] **Step 6: Run the test suite and confirm the new tests fail (RED)**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: at least the four toggle tests and two "Dnes" tests fail, and the rewritten "twoWeeks" anchor test fails. The existing eight tests not related to the anchor continue to pass.

Sample expected failure output:

```
● MarketingCalendarPage — view-mode toggle resets the anchor
  › toggling fiveWeeks → twoWeeks updates initialDate to current Monday

  expect(received).toBe(expected) // Object.is equality

  Expected: <timestamp of current Monday>
  Received: <timestamp of Monday minus 7 days>
```

- [ ] **Step 7: Commit the failing tests**

```bash
git add frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "test: add failing assertions for 14-day current-week anchor, toggle reset, and Dnes button"
```

---

## Task 3: Implement the production change (TDD green)

**Goal:** Update `MarketingCalendarPage.tsx` so all failing tests pass. Six edits to a single file.

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`

- [ ] **Step 1: Replace the existing `getCalendarStartForToday` helper (and its leading comment) with the new `startOfWeekMonday` + view-aware `getCalendarStartForToday` pair**

Replace lines 32–41 of `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`:

```tsx
function startOfWeekMonday(date: Date): Date {
  const clone = new Date(date);
  const dow = clone.getDay(); // 0 = Sunday
  const daysToMonday = dow === 0 ? 6 : dow - 1;
  clone.setHours(0, 0, 0, 0);
  clone.setDate(clone.getDate() - daysToMonday);
  clone.setHours(0, 0, 0, 0);
  return clone;
}

function getCalendarStartForToday(viewMode: 'fiveWeeks' | 'twoWeeks'): Date {
  const monday = startOfWeekMonday(new Date());
  if (viewMode === 'fiveWeeks') {
    monday.setDate(monday.getDate() - 7);
  }
  return monday;
}
```

Rationale: `startOfWeekMonday` clones its input so it never mutates the caller's `Date`. The trailing `setHours(0,0,0,0)` is defence-in-depth against a DST transition shifting the hour after `setDate`.

- [ ] **Step 2: Update the lazy initializer for `currentDate` state**

Find line 47 (`const [currentDate, setCurrentDate] = useState(getCalendarStartForToday);`) and replace it with:

```tsx
  const [currentDate, setCurrentDate] = useState(() => getCalendarStartForToday('fiveWeeks'));
```

- [ ] **Step 3: Update `handleViewModeChange` to reset `currentDate` on calendar-mode toggles**

Find lines 59–62 (the current `handleViewModeChange`) and replace with:

```tsx
  const handleViewModeChange = (mode: ViewMode) => {
    setViewMode(mode);
    if (mode !== 'list') {
      setVisibleRange(null);
      setCurrentDate(getCalendarStartForToday(mode));
    }
  };
```

- [ ] **Step 4: Update the `useMemo` that computes the fallback `startDate`/`endDate` to pass `viewMode` (narrowed to exclude `'list'`)**

Find lines 67–75 and replace with:

```tsx
  const { startDate, endDate } = useMemo(() => {
    if (visibleRange) {
      return { startDate: visibleRange.start, endDate: visibleRange.end };
    }
    const mode = viewMode === 'list' ? 'fiveWeeks' : viewMode;
    const start = getCalendarStartForToday(mode);
    const end = new Date(start);
    end.setDate(start.getDate() + (mode === 'twoWeeks' ? 14 : 35));
    return { startDate: start, endDate: end };
  }, [visibleRange, viewMode]);
```

Rationale: the `useMemo` runs unconditionally; the page renders the list view in a sibling JSX branch but the memo still executes and still feeds `useMarketingCalendar`. Narrowing `'list'` → `'fiveWeeks'` preserves existing fetch behavior for the list view.

- [ ] **Step 5: Update `goToToday` to use the active view mode**

Find line 141 and replace with:

```tsx
  const goToToday = () => {
    if (viewMode === 'list') return;
    calendarRef.current?.getApi().gotoDate(getCalendarStartForToday(viewMode));
  };
```

Rationale: in list mode there is no calendar API to call. The early return makes the intent explicit.

- [ ] **Step 6: Run the test suite and confirm all tests pass (GREEN)**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: all tests pass.

Sample expected output:

```
PASS src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
  MarketingCalendarPage — default render
    ✓ renders the page title
  MarketingCalendarPage — toolbar
    ✓ renders all three view buttons with Czech labels
    ✓ highlights '5 týdnů' as the default active view
    ✓ renders the calendar with viewName='fiveWeeks' on initial load
    ✓ clicking '14 dní' remounts the calendar with viewName='twoWeeks'
    ✓ clicking 'Seznam' unmounts the calendar and renders the list
    ✓ returning from Seznam to '5 týdnů' remounts the calendar with viewName='fiveWeeks'
  MarketingCalendarPage — fallback fetch range
    ✓ requests a 35-day window for the default fiveWeeks view, anchored one week before current Monday
    ✓ requests a 14-day window anchored on the current Monday after switching to twoWeeks
  MarketingCalendarPage — view-mode toggle resets the anchor
    ✓ toggling fiveWeeks → twoWeeks updates initialDate to current Monday
    ✓ toggling twoWeeks → fiveWeeks updates initialDate to Monday minus 7 days
    ✓ repeated toggling 5w → 14d → 5w → 14d consistently lands on the correct anchor each time
    ✓ toggling to Seznam does not change initialDate (calendar unmounts, no new render)
  MarketingCalendarPage — 'Dnes' button respects active view
    ✓ in 14-day mode, clicking 'Dnes' calls gotoDate with current Monday
    ✓ in 5-week mode, clicking 'Dnes' calls gotoDate with Monday minus 7 days
```

- [ ] **Step 7: Commit the production change**

```bash
git add frontend/src/components/marketing/pages/MarketingCalendarPage.tsx
git commit -m "fix(marketing-calendar): anchor 14-day view on the current week"
```

---

## Task 4: Verify build, lint, and manual smoke

**Goal:** Run the project-mandated validation gates (`npm run build`, `npm run lint`) and step through the manual verification steps from the spec.

**Files:**
- None (validation only).

- [ ] **Step 1: Run lint**

Run: `cd frontend && npm run lint`
Expected: exit code 0, no warnings or errors introduced by the changed files.

- [ ] **Step 2: Run the production build**

Run: `cd frontend && npm run build`
Expected: build succeeds, no TypeScript errors.

- [ ] **Step 3: Run the full test file once more for a final green confirmation**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: all tests pass.

- [ ] **Step 4: Run a broader unit-test pass to ensure no neighbouring tests broke**

Run: `cd frontend && npx jest src/components/marketing --no-coverage`
Expected: every test under `src/components/marketing` passes (no regressions in sibling components).

- [ ] **Step 5: Manual smoke (developer-driven; no automated check)**

Start the dev server: `cd frontend && npm start`

Then in a browser at `http://localhost:3001/marketing/calendar`:

1. Click **14 dní** — verify the first row contains today's date and the second row is the following week.
2. Click **5 týdnů** — verify today appears in row 2.
3. Toggle 5 týdnů → 14 dní → 5 týdnů → 14 dní — each transition lands on the correct anchor.
4. In 14-day mode, click **>** twice (or **<**) and then **Dnes** — verify the view returns to current-week-first.
5. In 5-week mode, navigate forward, then click **Dnes** — verify today returns to row 2.
6. Click **Seznam** then back to **5 týdnů** — verify the calendar mounts with today in row 2.

If the dev server is already running for another task, you can skip step "start the dev server" and just navigate.

- [ ] **Step 6: No commit needed (validation only)**

If lint/build/tests all pass and the manual smoke is clean, this task ends without a commit. Move to the finishing workflow.

---

## Self-Review Notes

**Spec coverage check:**
- FR-1 (14-day anchors on current week): covered by Task 3 Steps 1–4 and Task 2 Steps 3 and the "twoWeeks → twoWeeks anchor" test.
- FR-2 (5-week view unchanged): covered by the regression test in Task 2 Step 2.
- FR-3 (view toggle resets anchor): covered by Task 3 Step 3 and the toggle tests in Task 2 Step 4.
- FR-4 ("Dnes" respects active view): covered by Task 3 Step 5 and the "Dnes" tests in Task 2 Step 5.
- FR-5 (Monday-based week computation): covered by `startOfWeekMonday` in Task 3 Step 1.
- NFR-1 (perf): unchanged surface — same calls per render.
- NFR-2 (backwards compatibility): no API changes.
- NFR-3 (security): no surface change.
- NFR-4 (code quality): immutable date arithmetic (clones the input), discriminated union excludes `'list'`, no unrelated edits.

**Architecture review amendments applied:**
1. `useMemo` narrowing implemented as `const mode = viewMode === 'list' ? 'fiveWeeks' : viewMode;` (Task 3 Step 4).
2. Defence-in-depth DST guard: second `setHours(0, 0, 0, 0)` after `setDate` in `startOfWeekMonday` (Task 3 Step 1).
3. Non-mutation: the helper clones its argument by `new Date(date)` before any setter call — input is not mutated.

**Placeholder scan:** none. Every code block is complete; every command is exact.

**Type consistency:** `startOfWeekMonday(date: Date): Date` and `getCalendarStartForToday(viewMode: 'fiveWeeks' | 'twoWeeks'): Date` signatures match across all references.
