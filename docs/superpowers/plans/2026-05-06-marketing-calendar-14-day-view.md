# Marketing Calendar 14-Day View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a 14-day `dayGrid` view to the Marketing Calendar so all events on each day render without "+N more" overflow truncation, switchable from a 3-button toolbar (`5 týdnů | 14 dní | Seznam`).

**Architecture:** Purely presentational, frontend-only change. `MarketingMonthCalendar` gains a required `viewName: 'fiveWeeks' | 'twoWeeks'` prop driving an internal `views` registry; `MarketingCalendarPage` widens its `ViewMode` union, replaces the 2-button toolbar with a 3-button segmented control, and remounts the calendar via `key={viewMode}` on each switch so FullCalendar reinitializes cleanly. View-scoped CSS adds an 80 px minimum cell height for the new view; the existing parent `overflow-auto` provides the single scroll context.

**Tech Stack:** React 18, TypeScript (strict), FullCalendar v6 (`@fullcalendar/react`, `@fullcalendar/daygrid`, `@fullcalendar/interaction`, `@fullcalendar/core/locales/cs`), Tailwind CSS, `lucide-react`, Jest + React Testing Library. No backend changes.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx` | Modify | Add required `viewName` prop, build internal `CALENDAR_VIEWS` registry, derive `height` and wrapper class from `viewName`, remove top-level `dayMaxEvents`, export `CalendarViewName` type. |
| `frontend/src/components/marketing/calendar/marketingCalendar.css` | Modify | Append two `.marketing-calendar.two-weeks`-scoped rules (cell `min-height` 80 px and `overflow: visible` on day events). |
| `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` | Modify | Widen `ViewMode` to `'fiveWeeks' \| 'twoWeeks' \| 'list'`, default to `'fiveWeeks'`, replace 2-button toolbar with 3-button segmented control, add `handleViewModeChange`, make fallback fetch range view-aware, render calendar with `key={viewMode} viewName={viewMode}`. |
| `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx` | Create | Unit tests for `viewName` driving `initialView`, `CALENDAR_VIEWS` registry shape (both views with correct `dayMaxEvents`), conditional `height`, conditional `two-weeks` wrapper class. |
| `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` | Create | Unit tests for 3-button toolbar rendering, default active view, switching between `5 týdnů` / `14 dní` / `Seznam`, calendar mount/unmount and `viewName` propagation, view-aware fallback fetch range. |

---

## Task 1: Bootstrap the `MarketingMonthCalendar` test file with a `@fullcalendar/react` mock

**Files:**
- Create: `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`
- Reference (read-only): `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`

**Background:** FullCalendar performs heavy DOM work and depends on layout APIs that JSDOM doesn't fully implement. We mock `@fullcalendar/react` at the module level so the test can assert on the props that `MarketingMonthCalendar` passes to it (`initialView`, `views`, `height`) without rendering FullCalendar's real internals. The mock captures the most recently received props on a mutable object that each test reads.

- [ ] **Step 1: Create the test file with the FullCalendar mock and a render helper that fails on first run because the component does not yet accept `viewName`.**

```tsx
import React from "react";
import { render } from "@testing-library/react";
import MarketingMonthCalendar from "../MarketingMonthCalendar";
import type { CalendarEvent } from "../fullcalendarAdapters";

// Capture the props passed to FullCalendar so each test can assert on them.
const fullCalendarPropsRef: { current: Record<string, any> | null } = {
  current: null,
};

jest.mock("@fullcalendar/react", () => {
  const React = require("react");
  const FullCalendarMock = React.forwardRef(
    (props: Record<string, any>, _ref: any) => {
      fullCalendarPropsRef.current = props;
      return <div data-testid="fullcalendar-mock" />;
    },
  );
  FullCalendarMock.displayName = "FullCalendarMock";
  return { __esModule: true, default: FullCalendarMock };
});

// Plugins are imported by the SUT but never invoked under the mock.
jest.mock("@fullcalendar/daygrid", () => ({ __esModule: true, default: {} }));
jest.mock("@fullcalendar/interaction", () => ({
  __esModule: true,
  default: {},
}));
jest.mock("@fullcalendar/core/locales/cs", () => ({
  __esModule: true,
  default: {},
}));

const noop = () => undefined;

const baseProps = {
  events: [] as CalendarEvent[],
  initialDate: new Date("2026-05-06T00:00:00"),
  onEventClick: noop,
  onEventMove: noop,
  onEventResize: noop,
  onDateRangeSelect: noop,
  onDatesSet: noop,
  calendarRef: React.createRef<any>(),
};

beforeEach(() => {
  fullCalendarPropsRef.current = null;
});

describe("MarketingMonthCalendar — viewName prop", () => {
  it("passes viewName='fiveWeeks' as initialView to FullCalendar", () => {
    render(
      <MarketingMonthCalendar {...baseProps} viewName="fiveWeeks" />,
    );
    expect(fullCalendarPropsRef.current?.initialView).toBe("fiveWeeks");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails.**

Run: `cd frontend && npx jest src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx --no-coverage`
Expected: TypeScript / runtime failure — `MarketingMonthCalendar` does not yet accept a `viewName` prop. Exact message will mention "Property 'viewName' does not exist on type" or the component will render with `initialView="fiveWeeks"` only because that's still hardcoded.

- [ ] **Step 3: Add the required `viewName` prop to `MarketingMonthCalendar` and pass it to FullCalendar's `initialView`. Export the `CalendarViewName` type.**

In `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`, replace the existing `interface MarketingMonthCalendarProps { ... }` block (lines 12–22) with:

```tsx
export type CalendarViewName = 'fiveWeeks' | 'twoWeeks';

interface MarketingMonthCalendarProps {
  events: CalendarEvent[];
  initialDate: Date;
  viewName: CalendarViewName;
  onEventClick: (id: number) => void;
  onEventMove: (id: number, dateFrom: string, dateTo: string) => void;
  onEventResize: (id: number, dateFrom: string, dateTo: string) => void;
  onDateRangeSelect: (dateFrom: string, dateTo: string) => void;
  onDatesSet: (visibleStart: Date, visibleEnd: Date, currentStart: Date) => void;
  calendarRef: React.RefObject<FullCalendar>;
  className?: string;
}
```

Then update the function signature destructuring (currently lines 24–34) to include `viewName`:

```tsx
const MarketingMonthCalendar: React.FC<MarketingMonthCalendarProps> = ({
  events,
  initialDate,
  viewName,
  onEventClick,
  onEventMove,
  onEventResize,
  onDateRangeSelect,
  onDatesSet,
  calendarRef,
  className,
}) => {
```

Finally, change the `FullCalendar` element's `initialView` (currently line 72) from the hardcoded literal to the prop:

```tsx
        initialView={viewName}
```

Leave the rest of the file untouched in this step. The single-entry `views={{ fiveWeeks: { ... } }}` registry, `dayMaxEvents={true}`, and `height="100%"` stay as-is until later tasks — but the test from Step 1 should now pass because `initialView={viewName}` resolves to `"fiveWeeks"`.

- [ ] **Step 4: Run the test to verify it passes.**

Run: `cd frontend && npx jest src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx --no-coverage`
Expected: PASS, 1 test.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx \
        frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx
git commit -m "feat: add required viewName prop to MarketingMonthCalendar"
```

---

## Task 2: Register both views in `CALENDAR_VIEWS` and remove the top-level `dayMaxEvents`

**Files:**
- Modify: `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`
- Modify: `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`

**Background:** The current `views` prop registers only `fiveWeeks`. We need both `fiveWeeks` and `twoWeeks` registered simultaneously and per-view `dayMaxEvents` (per the architecture review's decision 4). The top-level `dayMaxEvents={true}` is removed so the per-view config is the single source of truth.

- [ ] **Step 1: Add a failing test that asserts both views are present in the registry with correct `dayMaxEvents` settings.**

Append a new `describe` block to `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`:

```tsx
describe("MarketingMonthCalendar — views registry", () => {
  it("registers both fiveWeeks and twoWeeks with correct dayMaxEvents", () => {
    render(
      <MarketingMonthCalendar {...baseProps} viewName="fiveWeeks" />,
    );
    const views = fullCalendarPropsRef.current?.views;
    expect(views).toBeDefined();
    expect(views.fiveWeeks).toEqual({
      type: "dayGrid",
      duration: { weeks: 5 },
      dayMaxEvents: true,
    });
    expect(views.twoWeeks).toEqual({
      type: "dayGrid",
      duration: { weeks: 2 },
      dayMaxEvents: false,
    });
  });

  it("does not pass a top-level dayMaxEvents prop", () => {
    render(
      <MarketingMonthCalendar {...baseProps} viewName="fiveWeeks" />,
    );
    expect(fullCalendarPropsRef.current).not.toHaveProperty("dayMaxEvents");
  });
});
```

- [ ] **Step 2: Run the new tests to verify they fail.**

Run: `cd frontend && npx jest src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx --no-coverage`
Expected: FAIL — `views.twoWeeks` is `undefined`, and `dayMaxEvents` is still set on the top level.

- [ ] **Step 3: Define `CALENDAR_VIEWS` and wire it into FullCalendar; remove the top-level `dayMaxEvents`.**

In `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`, add this constant immediately above the component definition (i.e., right after the `import` block, before `interface MarketingMonthCalendarProps`):

```tsx
const CALENDAR_VIEWS = {
  fiveWeeks: { type: 'dayGrid', duration: { weeks: 5 }, dayMaxEvents: true },
  twoWeeks:  { type: 'dayGrid', duration: { weeks: 2 }, dayMaxEvents: false },
} as const;
```

In the `FullCalendar` JSX, replace the existing inline `views={{ ... }}` block (currently lines 73–78) with:

```tsx
        views={CALENDAR_VIEWS}
```

In the same JSX block, delete the line `dayMaxEvents={true}` (currently line 86) entirely.

- [ ] **Step 4: Run the tests to verify all three tests in this file pass.**

Run: `cd frontend && npx jest src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx --no-coverage`
Expected: PASS, 3 tests.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx \
        frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx
git commit -m "feat: register twoWeeks view in MarketingMonthCalendar registry"
```

---

## Task 3: Derive `height` and wrapper CSS class from `viewName`

**Files:**
- Modify: `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`
- Modify: `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`

**Background:** The 14-day view needs `height='auto'` (so cells expand vertically) and a `two-weeks` CSS class on the wrapper for view-scoped styling. The 5-week view keeps `height='100%'` and no extra class. The wrapper class composition order is `marketing-calendar` → optional ` two-weeks` → ` h-full` → optional caller `className`, matching the architecture review's specificity guidance.

- [ ] **Step 1: Add failing tests for the conditional `height` and wrapper class.**

Append to `frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx`:

```tsx
describe("MarketingMonthCalendar — height and wrapper class", () => {
  it("uses height='100%' for fiveWeeks", () => {
    render(
      <MarketingMonthCalendar {...baseProps} viewName="fiveWeeks" />,
    );
    expect(fullCalendarPropsRef.current?.height).toBe("100%");
  });

  it("uses height='auto' for twoWeeks", () => {
    render(
      <MarketingMonthCalendar {...baseProps} viewName="twoWeeks" />,
    );
    expect(fullCalendarPropsRef.current?.height).toBe("auto");
  });

  it("does not add 'two-weeks' class to the wrapper for fiveWeeks", () => {
    const { container } = render(
      <MarketingMonthCalendar {...baseProps} viewName="fiveWeeks" />,
    );
    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).toContain("marketing-calendar");
    expect(wrapper.className).not.toContain("two-weeks");
  });

  it("adds 'two-weeks' class to the wrapper for twoWeeks", () => {
    const { container } = render(
      <MarketingMonthCalendar {...baseProps} viewName="twoWeeks" />,
    );
    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.className).toContain("marketing-calendar");
    expect(wrapper.className).toContain("two-weeks");
  });

  it("appends caller-provided className after the view class", () => {
    const { container } = render(
      <MarketingMonthCalendar
        {...baseProps}
        viewName="twoWeeks"
        className="extra-class"
      />,
    );
    const wrapper = container.firstChild as HTMLElement;
    // Order: 'marketing-calendar two-weeks h-full extra-class'
    expect(wrapper.className).toBe(
      "marketing-calendar two-weeks h-full extra-class",
    );
  });
});
```

- [ ] **Step 2: Run the new tests to verify they fail.**

Run: `cd frontend && npx jest src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx --no-coverage`
Expected: FAIL — `height` is still hardcoded `"100%"`, wrapper never has `two-weeks`.

- [ ] **Step 3: Derive `calendarHeight` and `wrapperClassName` inside the component and use them in the JSX.**

In `frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx`, immediately after `const fcEvents = useMemo(...)` (currently line 35), add:

```tsx
  const calendarHeight = viewName === 'twoWeeks' ? 'auto' : '100%';
  const wrapperClassName =
    `marketing-calendar${viewName === 'twoWeeks' ? ' two-weeks' : ''} h-full`
    + (className ? ` ${className}` : '');
```

Replace the wrapper `<div className={...}>` (currently line 68) with:

```tsx
    <div className={wrapperClassName}>
```

Replace the FullCalendar `height="100%"` literal (currently line 87) with:

```tsx
        height={calendarHeight}
```

- [ ] **Step 4: Run the tests to verify all 8 tests in this file pass.**

Run: `cd frontend && npx jest src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx --no-coverage`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/components/marketing/calendar/MarketingMonthCalendar.tsx \
        frontend/src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx
git commit -m "feat: derive height and wrapper class from viewName"
```

---

## Task 4: Add view-scoped CSS for the 14-day view

**Files:**
- Modify: `frontend/src/components/marketing/calendar/marketingCalendar.css`

**Background:** The 14-day grid needs taller minimum cells (80 px) and `overflow: visible` on the events container so unbounded event lists are not clipped. Both rules are scoped under `.marketing-calendar.two-weeks` so the 5-week view's tighter cells (which rely on FullCalendar's "+N more" overflow logic) are unaffected. There is no automated test for raw CSS; we verify by appending the rules verbatim and running the production build to confirm no PostCSS errors.

- [ ] **Step 1: Append the two scoped rules to the end of `marketingCalendar.css`.**

In `frontend/src/components/marketing/calendar/marketingCalendar.css`, append (preserve the trailing newline already at end of file):

```css

/* 14-day (twoWeeks) view — expanded cells with no event overflow */
.marketing-calendar.two-weeks .fc-daygrid-day-frame {
  min-height: 80px;
}

.marketing-calendar.two-weeks .fc-daygrid-day-events {
  overflow: visible;
}
```

- [ ] **Step 2: Verify the CSS compiles by running the frontend build.**

Run: `cd frontend && npm run build`
Expected: build succeeds; no PostCSS errors involving `marketingCalendar.css`. (A few warnings unrelated to this file are acceptable.)

- [ ] **Step 3: Commit.**

```bash
git add frontend/src/components/marketing/calendar/marketingCalendar.css
git commit -m "feat: add view-scoped CSS for 14-day calendar view"
```

---

## Task 5: Bootstrap the `MarketingCalendarPage` test file with isolating mocks

**Files:**
- Create: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`
- Reference (read-only): `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`

**Background:** `MarketingCalendarPage` pulls in many siblings (modals, hooks, the import flow). To unit-test toolbar and view-mode logic, we mock the heavy children and the data hooks. `MarketingMonthCalendar` is mocked to expose its received `viewName` and `key`-driven mount lifecycle as a `data-*` attribute. Because Task 5 only sets up the harness and a baseline test that passes against today's code, no production change is needed in this task.

- [ ] **Step 1: Create the test file with module-level mocks and a default-render test.**

```tsx
import React from "react";
import { render, screen } from "@testing-library/react";
import MarketingCalendarPage from "../MarketingCalendarPage";

// Track every render of the calendar mock so tests can verify mount/unmount.
const calendarRenderLog: { viewName: string; mountId: number }[] = [];
let calendarMountCounter = 0;

jest.mock("../../calendar/MarketingMonthCalendar", () => {
  const React = require("react");
  function MarketingMonthCalendarMock(props: { viewName: string }) {
    const mountId = React.useMemo(() => ++calendarMountCounter, []);
    React.useEffect(() => {
      calendarRenderLog.push({ viewName: props.viewName, mountId });
    });
    return (
      <div
        data-testid="marketing-month-calendar"
        data-view-name={props.viewName}
        data-mount-id={String(mountId)}
      />
    );
  }
  return { __esModule: true, default: MarketingMonthCalendarMock };
});

jest.mock("../../detail/MarketingActionModal", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("../../detail/ImportFromOutlookModal", () => ({
  __esModule: true,
  default: () => null,
}));

jest.mock("../../list/MarketingActionGrid", () => ({
  __esModule: true,
  default: () => <div data-testid="marketing-action-grid" />,
}));

jest.mock("../../list/MarketingActionFilters", () => ({
  __esModule: true,
  default: () => <div data-testid="marketing-action-filters" />,
  EMPTY_FILTERS: { searchText: "", dateFrom: "", dateTo: "" },
}));

jest.mock("../../../manufacture/calendar/CalendarNavigation", () => ({
  __esModule: true,
  default: () => <div data-testid="calendar-navigation" />,
}));

// Capture every call to useMarketingCalendar so we can assert the fetch range.
const calendarHookCalls: { startDate: Date; endDate: Date }[] = [];

jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
  useMarketingCalendar: (args: { startDate: Date; endDate: Date }) => {
    calendarHookCalls.push({
      startDate: new Date(args.startDate),
      endDate: new Date(args.endDate),
    });
    return { data: { actions: [] }, isLoading: false, error: null };
  },
  useMarketingActions: () => ({
    data: { actions: [], totalPages: 1 },
    isLoading: false,
    error: null,
  }),
  useMarketingAction: () => ({ data: null, isLoading: false, error: null }),
  useUpdateMarketingAction: () => ({ mutate: jest.fn() }),
}));

jest.mock("../../../../auth/useAuth", () => ({
  useAuth: () => ({
    getUserInfo: () => ({ roles: [] }),
  }),
}));

beforeEach(() => {
  calendarRenderLog.length = 0;
  calendarHookCalls.length = 0;
  calendarMountCounter = 0;
});

describe("MarketingCalendarPage — default render", () => {
  it("renders the page title", () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByText("Marketingový kalendář")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it passes against unchanged production code.**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: PASS, 1 test. (No production code touched yet; this only verifies the harness.)

- [ ] **Step 3: Commit.**

```bash
git add frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "test: add MarketingCalendarPage test harness with mocks"
```

---

## Task 6: Widen `ViewMode` and replace toolbar with the 3-button segmented control

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`
- Modify: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

**Background:** The `ViewMode` union changes from `'calendar' | 'list'` to `'fiveWeeks' | 'twoWeeks' | 'list'`. The toolbar gains a third button (`14 dní`). A new `handleViewModeChange` centralizes the `setViewMode` + `setVisibleRange(null)` reset. The `viewMode === 'calendar'` JSX guard becomes `viewMode !== 'list'`. The default view stays `'fiveWeeks'`.

- [ ] **Step 1: Add failing tests for toolbar rendering, default active button, and view switching mount/unmount.**

Append to `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`:

```tsx
import { fireEvent } from "@testing-library/react";

describe("MarketingCalendarPage — toolbar", () => {
  it("renders all three view buttons with Czech labels", () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByRole("button", { name: /5 týdnů/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /14 dní/ })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Seznam/ })).toBeInTheDocument();
  });

  it("highlights '5 týdnů' as the default active view", () => {
    render(<MarketingCalendarPage />);
    const fiveWeeks = screen.getByRole("button", { name: /5 týdnů/ });
    expect(fiveWeeks.className).toContain("bg-indigo-600");
    const twoWeeks = screen.getByRole("button", { name: /14 dní/ });
    const list = screen.getByRole("button", { name: /Seznam/ });
    expect(twoWeeks.className).not.toContain("bg-indigo-600");
    expect(list.className).not.toContain("bg-indigo-600");
  });

  it("renders the calendar with viewName='fiveWeeks' on initial load", () => {
    render(<MarketingCalendarPage />);
    const calendar = screen.getByTestId("marketing-month-calendar");
    expect(calendar.getAttribute("data-view-name")).toBe("fiveWeeks");
  });

  it("clicking '14 dní' remounts the calendar with viewName='twoWeeks'", () => {
    render(<MarketingCalendarPage />);
    const initial = screen.getByTestId("marketing-month-calendar");
    const initialMountId = initial.getAttribute("data-mount-id");

    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));

    const after = screen.getByTestId("marketing-month-calendar");
    expect(after.getAttribute("data-view-name")).toBe("twoWeeks");
    // key={viewMode} forces a fresh mount, so the mount id must change.
    expect(after.getAttribute("data-mount-id")).not.toBe(initialMountId);
  });

  it("clicking 'Seznam' unmounts the calendar and renders the list", () => {
    render(<MarketingCalendarPage />);
    expect(screen.getByTestId("marketing-month-calendar")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));

    expect(
      screen.queryByTestId("marketing-month-calendar"),
    ).not.toBeInTheDocument();
    expect(screen.getByTestId("marketing-action-grid")).toBeInTheDocument();
  });

  it("returning from Seznam to '5 týdnů' remounts the calendar with viewName='fiveWeeks'", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));
    fireEvent.click(screen.getByRole("button", { name: /5 týdnů/ }));

    const calendar = screen.getByTestId("marketing-month-calendar");
    expect(calendar.getAttribute("data-view-name")).toBe("fiveWeeks");
  });
});
```

- [ ] **Step 2: Run the tests to verify they fail.**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: FAIL — current toolbar has only `Kalendář` and `Seznam`; there is no `5 týdnů` or `14 dní` button.

- [ ] **Step 3: Update the page: widen `ViewMode`, default to `'fiveWeeks'`, add `handleViewModeChange`, replace toolbar JSX, change conditional render guard, pass `key`/`viewName` to `MarketingMonthCalendar`.**

In `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`:

(a) Replace the `type ViewMode = 'calendar' | 'list';` declaration (currently line 43) with:

```tsx
type ViewMode = 'fiveWeeks' | 'twoWeeks' | 'list';
```

(b) Replace the `useState` initializer (currently line 46) with:

```tsx
  const [viewMode, setViewMode] = useState<ViewMode>('fiveWeeks');
```

(c) Add a new handler immediately after the `const calendarRef = useRef<FullCalendar>(null);` line (currently line 57). Insert this right after the `useRef` line:

```tsx
  const handleViewModeChange = (mode: ViewMode) => {
    setViewMode(mode);
    if (mode !== 'list') setVisibleRange(null);
  };
```

(d) Replace the entire view-toggle `<div>` (currently lines 229–252, the block starting with `{/* View toggle */}` through its closing `</div>` immediately before the `Nová akce` button) with this 3-button segmented control:

```tsx
          {/* View toggle */}
          <div className="flex border border-gray-200 rounded-lg overflow-hidden">
            <button
              onClick={() => handleViewModeChange('fiveWeeks')}
              className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
                viewMode === 'fiveWeeks'
                  ? 'bg-indigo-600 text-white'
                  : 'text-gray-600 hover:bg-gray-50'
              }`}
            >
              <Calendar className="h-4 w-4" />
              5 týdnů
            </button>
            <button
              onClick={() => handleViewModeChange('twoWeeks')}
              className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
                viewMode === 'twoWeeks'
                  ? 'bg-indigo-600 text-white'
                  : 'text-gray-600 hover:bg-gray-50'
              }`}
            >
              <Calendar className="h-4 w-4" />
              14 dní
            </button>
            <button
              onClick={() => handleViewModeChange('list')}
              className={`px-3 py-2 text-sm flex items-center gap-1.5 transition-colors ${
                viewMode === 'list'
                  ? 'bg-indigo-600 text-white'
                  : 'text-gray-600 hover:bg-gray-50'
              }`}
            >
              <List className="h-4 w-4" />
              Seznam
            </button>
          </div>
```

(e) Change the conditional render guard (currently line 274) from `viewMode === 'calendar'` to `viewMode !== 'list'`:

```tsx
        {viewMode !== 'list' ? (
```

(f) Add `key` and `viewName` props to the `<MarketingMonthCalendar>` element (currently lines 294–304). Replace the opening `<MarketingMonthCalendar` line with:

```tsx
                <MarketingMonthCalendar
                  key={viewMode}
                  viewName={viewMode}
```

Leave the remaining props on that element untouched. (TypeScript will narrow `viewMode` here because the `viewMode !== 'list'` guard is in scope above this JSX, so `viewMode` is `'fiveWeeks' | 'twoWeeks'` — exactly `CalendarViewName`. No cast needed.)

- [ ] **Step 4: Run the page tests to verify they pass.**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: PASS, 7 tests (1 from Task 5 + 6 from this task).

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/components/marketing/pages/MarketingCalendarPage.tsx \
        frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "feat: replace toolbar with 3-button segmented control (5 týdnů / 14 dní / Seznam)"
```

---

## Task 7: Make the fallback fetch range view-aware

**Files:**
- Modify: `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`
- Modify: `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`

**Background:** Until FullCalendar's `datesSet` callback fires, the page falls back to a manually computed `{ startDate, endDate }`. Today this is hardcoded to 35 days. It must become 14 days when `viewMode === 'twoWeeks'`. The `useMemo` dependency array gains `viewMode`. We assert behavior by inspecting the captured calls into the mocked `useMarketingCalendar` hook.

- [ ] **Step 1: Add failing tests for view-aware fallback range.**

Append to `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`:

```tsx
const MS_PER_DAY = 24 * 60 * 60 * 1000;

function daysBetween(start: Date, end: Date): number {
  return Math.round((end.getTime() - start.getTime()) / MS_PER_DAY);
}

describe("MarketingCalendarPage — fallback fetch range", () => {
  it("requests a 35-day window for the default fiveWeeks view", () => {
    render(<MarketingCalendarPage />);
    expect(calendarHookCalls.length).toBeGreaterThan(0);
    const last = calendarHookCalls[calendarHookCalls.length - 1];
    expect(daysBetween(last.startDate, last.endDate)).toBe(35);
  });

  it("requests a 14-day window after switching to twoWeeks", () => {
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /14 dní/ }));
    const last = calendarHookCalls[calendarHookCalls.length - 1];
    expect(daysBetween(last.startDate, last.endDate)).toBe(14);
  });
});
```

- [ ] **Step 2: Run the new tests to verify they fail.**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: FAIL — switching to `twoWeeks` still produces a 35-day window.

- [ ] **Step 3: Make the fallback range view-aware.**

In `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`, replace the `useMemo` block at lines 64–72:

```tsx
  const { startDate, endDate } = useMemo(() => {
    if (visibleRange) {
      return { startDate: visibleRange.start, endDate: visibleRange.end };
    }
    const start = getCalendarStartForToday();
    const end = new Date(start);
    end.setDate(start.getDate() + (viewMode === 'twoWeeks' ? 14 : 35));
    return { startDate: start, endDate: end };
  }, [visibleRange, viewMode]);
```

(Only two changes vs. the existing block: the `setDate` line uses a ternary, and `viewMode` is added to the dependency array.)

- [ ] **Step 4: Run all page tests to verify they pass.**

Run: `cd frontend && npx jest src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx --no-coverage`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit.**

```bash
git add frontend/src/components/marketing/pages/MarketingCalendarPage.tsx \
        frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "feat: make fallback calendar fetch range view-aware (14 vs 35 days)"
```

---

## Task 8: Final verification (build, lint, full test pass, manual smoke)

**Files:** none (verification only)

**Background:** A full build + lint + test pass guards against regressions in adjacent code paths the unit tests don't cover (e.g., type narrowing of `viewMode` for the `viewName` prop, untouched module imports). The manual smoke step verifies the parent `overflow-auto` scroll context behaves correctly across viewports — something automated tests under JSDOM cannot exercise.

- [ ] **Step 1: Run the full frontend test suite for the affected area.**

Run: `cd frontend && npx jest src/components/marketing/calendar src/components/marketing/pages --no-coverage`
Expected: PASS, all calendar and pages tests green (8 from `MarketingMonthCalendar.test.tsx` + 9 from `MarketingCalendarPage.test.tsx` minimum, plus any previously existing tests in those subtrees).

- [ ] **Step 2: Run TypeScript build.**

Run: `cd frontend && npm run build`
Expected: build succeeds; no new TypeScript errors. Confirm the `MarketingCalendarPage.tsx` `viewName={viewMode}` line compiles without a cast (the `viewMode !== 'list'` guard narrows `viewMode` to `CalendarViewName`).

- [ ] **Step 3: Run the linter on the touched files.**

Run: `cd frontend && npx eslint src/components/marketing/calendar/MarketingMonthCalendar.tsx src/components/marketing/calendar/__tests__/MarketingMonthCalendar.test.tsx src/components/marketing/pages/MarketingCalendarPage.tsx src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`
Expected: no lint errors on these files.

- [ ] **Step 4: Manual smoke test in dev server.**

Start the frontend dev server: `cd frontend && npm start` (or run the project's standard dev command). Then in a browser:

1. Navigate to the Marketing Calendar page. Verify the toolbar shows three buttons in the order `5 týdnů | 14 dní | Seznam`, and `5 týdnů` is highlighted (indigo background, white text).
2. Click `14 dní`. Verify:
   - The button highlight moves to `14 dní`.
   - The grid now shows exactly 2 rows of 7 days (14 cells).
   - No `+N more` link appears anywhere.
   - Each day cell is at least roughly the height of two events, even when empty.
   - When the grid plus header exceed the viewport height, the page's outer container scrolls (one scrollbar, not nested).
3. With `14 dní` active, click an existing event. Verify the edit modal opens.
4. Drag an event by one day. Verify it moves and persists (page does not crash; no console errors).
5. Resize an event by one day at the edge. Verify it resizes.
6. Click and drag across two empty days. Verify the create modal opens with the prefilled range.
7. Click `Seznam`. Verify the calendar disappears and the list renders.
8. Click `5 týdnů`. Verify the calendar reappears with 5 rows; days that had multiple events show the `+N more` link again.
9. Click `<` (Prev) while in `14 dní` mode. Verify the visible window jumps back exactly 14 days.
10. Click `Today` while in `14 dní` mode. Verify today is visible somewhere in the grid (placement depends on the anchor; `getCalendarStartForToday()` puts today in the second row of the 2-row grid).

- [ ] **Step 5: Commit any incidental changes (if a smoke test surfaced a small needed tweak — e.g., a console warning fix). Otherwise skip.**

```bash
# Only if there are uncommitted changes from Step 4 follow-ups:
git status
git add <files>
git commit -m "fix: <short description from smoke test>"
```

If `git status` shows a clean tree, no commit is needed and the feature is complete.

---

## Spec Coverage Audit

| Spec requirement | Implemented in |
|------------------|----------------|
| FR-1 (3-button toolbar, default `5 týdnů`, correct icons, exactly one highlighted) | Task 6 (Steps 3d, 1) |
| FR-2 (14-day view: `dayGrid` `{ weeks: 2 }`, `dayMaxEvents=false`, no truncation, ≥80 px cells, parent scroll) | Tasks 2, 3, 4 |
| FR-3 (5-week view preserved with `dayMaxEvents=true`) | Task 2 (per-view config keeps `fiveWeeks: dayMaxEvents: true`) |
| FR-4 (view switching: remount via `key`, `visibleRange` reset on switch away from list, fallback windows correct, `Seznam` unmounts) | Tasks 6, 7 |
| FR-5 (Prev/Next jump 14 days; Today re-anchors to week 2) | No code change required — `CalendarNavigation` calls `calendarRef.current?.getApi().prev()/next()/gotoDate(getCalendarStartForToday())` already; FullCalendar moves by the active view's duration. Task 8 manual smoke verifies. |
| FR-6 (event interactions preserved across views) | No code change required — handlers are passed identically. Task 8 manual smoke verifies. |
| NFR-1 (perf <300 ms, single fetch per switch) | Architectural decision (Task 6 `key={viewMode}` + Task 7 view-aware fallback range relying on React Query dedup); Task 8 manual smoke verifies. |
| NFR-2 (visual consistency, scoped CSS) | Task 4 |
| NFR-3 (TypeScript strict, `CalendarViewName` exported) | Task 1 (Step 3); Task 8 (Step 2 verifies) |
| NFR-4 (unit-test coverage of `viewName`/registry/toolbar/switching) | Tasks 1, 2, 3, 5, 6 |
| NFR-5 (Czech labels, `cs` locale unchanged) | Task 6 (Step 3d) |
| NFR-6 (real `<button>` with discoverable name) | Task 6 (Step 3d) — buttons retain native `<button>` tag with visible text labels |
| Spec amendment 1 (remove top-level `dayMaxEvents`) | Task 2 (Step 3) |
| Spec amendment 2 (wrapper class composition order) | Task 3 (Step 3) — `marketing-calendar` → ` two-weeks` → ` h-full` → ` ${className}` |
| Spec amendment 3 (anchor invariant: today in week 2) | No code change — `getCalendarStartForToday()` is preserved. Task 8 (Step 4 #10) verifies. |
| Spec amendment 4 (`onDatesSet` Date semantics unchanged) | No code change — `handleDatesSet` already passes `Date` objects through |
| Spec amendment 5 (assert registry shape, not just registration) | Task 2 (Step 1 — `expect(views.fiveWeeks).toEqual({ ..., dayMaxEvents: true })` and `views.twoWeeks` likewise) |
