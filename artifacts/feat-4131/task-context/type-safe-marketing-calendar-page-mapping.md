### task: type-safe-marketing-calendar-page-mapping

This is the only task — the change is confined to one component and its existing test file, so it is not split further at the top level. Steps below are the bite-sized (2–5 min) units; each ends with a verifiable command and a commit.

A note on how TDD applies here: this is a **type-safety refactor with an explicit no-behavior-change requirement (NFR-1)**, not a new-behavior feature. So the "red" step is not "the test fails before the fix" in the usual sense — the *runtime* output for real API data is identical before and after. Instead: step 1 adds tests against realistic, typed-shaped fixture data (Date objects, plain-string `actionType`) and step 2 proves they pass on the **current, unfixed** code (this is the regression baseline / safety net). Step 4 proves they *still* pass after the fix (this is what actually demonstrates NFR-1). The genuine "does the compiler now catch the bug" check (what NFR-2 is about) happens in step 6 via `npm run build`, which is where `tsc` — not Jest — is the enforcement mechanism, since Jest's Babel transform strips types and never type-checks.

#### Step 1 — Read the current test file mocks precisely (no code change yet)

Open `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` and confirm it currently looks exactly like this in the four places you're about to touch (if any of these four blocks differ from what's shown, stop and re-read the live file before proceeding — the diffs below are written against this exact content):

```ts
// near the top, right before the useMarketingCalendar hook mock
// Track every render of the calendar mock so tests can verify mount/unmount and the props it receives.
const calendarRenderLog: { viewName: string; initialDate: Date; mountId: number }[] = [];
```

```ts
      calendarRenderLog.push({
        viewName: props.viewName,
        initialDate: new Date(props.initialDate),
        mountId,
      });
```

```ts
jest.mock("../../detail/MarketingActionModal", () => ({
  __esModule: true,
  default: () => null,
}));
```

```ts
jest.mock("../../list/MarketingActionGrid", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: () => React.createElement("div", { "data-testid": "marketing-action-grid" }),
  };
});
```

```ts
// Capture every call to useMarketingCalendar so we can assert the fetch range.
const calendarHookCalls: { startDate: Date; endDate: Date }[] = [];

jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
  useMarketingCalendar: (args) => {
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
  useMoveMarketingAction: () => ({ mutate: jest.fn() }),
}));
```

```ts
beforeEach(() => {
  calendarRenderLog.length = 0;
  calendarHookCalls.length = 0;
  mockGotoDate.mockClear();
  mockIsMobile = false;
  mockHasPermission = () => false;
});
```

No command to run for this step — it's a read-and-confirm step so the edits in step 2 apply cleanly.

#### Step 2 — Make the hook mocks and two child-component mocks data-driven, and add regression-lock tests

Apply these five exact edits to `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx`:

**Edit 2a** — replace:
```ts
// Capture every call to useMarketingCalendar so we can assert the fetch range.
const calendarHookCalls: { startDate: Date; endDate: Date }[] = [];

jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
  useMarketingCalendar: (args) => {
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
  useMoveMarketingAction: () => ({ mutate: jest.fn() }),
}));
```
with:
```ts
// Capture every call to useMarketingCalendar so we can assert the fetch range.
const calendarHookCalls: { startDate: Date; endDate: Date }[] = [];

// Mutable per-test fixtures for the typed-mapping tests below. Shaped like the
// generated MarketingActionCalendarDto / MarketingActionDto that the real
// NSwag client returns: startDate/endDate as Date objects, actionType as a
// plain string (not the MarketingActionType enum).
let mockCalendarActions: any[] = [];
let mockListActions: any[] = [];
let mockListTotalPages: number | undefined = 1;
let mockDetailAction: any = null;

jest.mock("../../../../api/hooks/useMarketingCalendar", () => ({
  useMarketingCalendar: (args) => {
    calendarHookCalls.push({
      startDate: new Date(args.startDate),
      endDate: new Date(args.endDate),
    });
    return { data: { actions: mockCalendarActions }, isLoading: false, error: null };
  },
  useMarketingActions: () => ({
    data: { actions: mockListActions, totalPages: mockListTotalPages },
    isLoading: false,
    error: null,
  }),
  useMarketingAction: () => ({
    data: mockDetailAction ? { action: mockDetailAction } : null,
    isLoading: false,
    error: null,
  }),
  useUpdateMarketingAction: () => ({ mutate: jest.fn() }),
  useMoveMarketingAction: () => ({ mutate: jest.fn() }),
}));
```

**Edit 2b** — replace:
```ts
// Track every render of the calendar mock so tests can verify mount/unmount and the props it receives.
const calendarRenderLog: { viewName: string; initialDate: Date; mountId: number }[] = [];
```
with:
```ts
// Track every render of the calendar mock so tests can verify mount/unmount and the props it receives.
const calendarRenderLog: { viewName: string; initialDate: Date; mountId: number; events: any[] }[] = [];
```
and, further down inside the same `jest.mock("../../calendar/MarketingMonthCalendar", ...)` factory, replace:
```ts
      calendarRenderLog.push({
        viewName: props.viewName,
        initialDate: new Date(props.initialDate),
        mountId,
      });
```
with:
```ts
      calendarRenderLog.push({
        viewName: props.viewName,
        initialDate: new Date(props.initialDate),
        mountId,
        events: props.events,
      });
```

**Edit 2c** — replace:
```ts
jest.mock("../../detail/MarketingActionModal", () => ({
  __esModule: true,
  default: () => null,
}));
```
with:
```ts
// Capture the props MarketingActionModal receives so tests can assert the
// mapped `existingAction` once a detail fetch resolves.
const modalRenderLog: { existingAction: any }[] = [];

jest.mock("../../detail/MarketingActionModal", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: (props) => {
      modalRenderLog.push({ existingAction: props.existingAction });
      return null;
    },
  };
});
```

**Edit 2d** — replace:
```ts
jest.mock("../../list/MarketingActionGrid", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: () => React.createElement("div", { "data-testid": "marketing-action-grid" }),
  };
});
```
with:
```ts
// Capture the props MarketingActionGrid receives so tests can assert the
// mapped `actions` array and `totalPages`.
const gridRenderLog: { actions: any[]; totalPages: number }[] = [];

jest.mock("../../list/MarketingActionGrid", () => {
  const React = require("react");
  return {
    __esModule: true,
    default: (props) => {
      gridRenderLog.push({ actions: props.actions, totalPages: props.totalPages });
      return React.createElement("div", { "data-testid": "marketing-action-grid" });
    },
  };
});
```

**Edit 2e** — replace:
```ts
beforeEach(() => {
  calendarRenderLog.length = 0;
  calendarHookCalls.length = 0;
  mockGotoDate.mockClear();
  mockIsMobile = false;
  mockHasPermission = () => false;
});
```
with:
```ts
beforeEach(() => {
  calendarRenderLog.length = 0;
  calendarHookCalls.length = 0;
  gridRenderLog.length = 0;
  modalRenderLog.length = 0;
  mockGotoDate.mockClear();
  mockIsMobile = false;
  mockHasPermission = () => false;
  mockCalendarActions = [];
  mockListActions = [];
  mockListTotalPages = 1;
  mockDetailAction = null;
});
```

**Edit 2f** — append this new `describe` block at the very end of the file (after the closing `});` of the existing `describe('mobile view', ...)` block, i.e. as the new last thing in the file):
```ts

describe("MarketingCalendarPage — typed API response mapping", () => {
  it("maps calendar action Date fields to YYYY-MM-DD strings and forwards the computed events to the calendar", () => {
    mockCalendarActions = [
      {
        id: 42,
        title: "Letní kampaň",
        actionType: "SocialMedia",
        startDate: new Date(2026, 5, 1),
        endDate: new Date(2026, 5, 3),
        associatedProducts: ["PROD-1"],
        outlookSyncStatus: "Synced",
      },
    ];
    render(<MarketingCalendarPage />);

    const events = calendarRenderLog[calendarRenderLog.length - 1].events;
    expect(events).toEqual([
      {
        id: 42,
        title: "Letní kampaň",
        actionType: "SocialMedia",
        dateFrom: "2026-06-01",
        dateTo: "2026-06-03",
        associatedProducts: ["PROD-1"],
        outlookSyncStatus: "Synced",
      },
    ]);
  });

  it("falls back to empty dateFrom/dateTo strings when a calendar action has no startDate/endDate", () => {
    mockCalendarActions = [
      { id: 7, title: "TBD", actionType: "Blog", associatedProducts: [] },
    ];
    render(<MarketingCalendarPage />);

    const events = calendarRenderLog[calendarRenderLog.length - 1].events;
    expect(events[0].dateFrom).toBe("");
    expect(events[0].dateTo).toBe("");
  });

  it("maps list action Date fields onto dateFrom/dateTo and forwards totalPages to the grid", () => {
    mockListActions = [
      {
        id: 5,
        title: "Newsletter Q3",
        description: "Popis akce",
        actionType: "Newsletter",
        startDate: new Date(2026, 6, 10),
        endDate: new Date(2026, 6, 12),
        associatedProducts: ["PROD-2"],
        folderLinks: [{ folderKey: "abc", folderType: "GoogleDrive" }],
        outlookSyncStatus: "Synced",
      },
    ];
    mockListTotalPages = 3;
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));

    const gridProps = gridRenderLog[gridRenderLog.length - 1];
    expect(gridProps.totalPages).toBe(3);
    expect(gridProps.actions).toEqual([
      {
        id: 5,
        title: "Newsletter Q3",
        detail: "Popis akce",
        actionType: "Newsletter",
        dateFrom: new Date(2026, 6, 10),
        dateTo: new Date(2026, 6, 12),
        associatedProducts: ["PROD-2"],
        folderLinks: [{ folderKey: "abc", folderType: "GoogleDrive" }],
        outlookSyncStatus: "Synced",
      },
    ]);
  });

  it("defaults totalPages to 1 when the list response has no totalPages", () => {
    mockListActions = [];
    mockListTotalPages = undefined;
    render(<MarketingCalendarPage />);
    fireEvent.click(screen.getByRole("button", { name: /Seznam/ }));

    const gridProps = gridRenderLog[gridRenderLog.length - 1];
    expect(gridProps.totalPages).toBe(1);
  });

  it("maps a fetched detail action's Date fields onto the edit modal's existingAction prop", () => {
    mockDetailAction = {
      id: 9,
      title: "Podzimní PR akce",
      description: "Detailní popis",
      actionType: "PR",
      startDate: new Date(2026, 8, 1),
      endDate: new Date(2026, 8, 5),
      associatedProducts: ["PROD-3"],
      folderLinks: [{ folderKey: "xyz", folderType: "SharePoint" }],
    };
    render(<MarketingCalendarPage />);

    const lastModalProps = modalRenderLog[modalRenderLog.length - 1];
    expect(lastModalProps.existingAction).toEqual({
      id: 9,
      title: "Podzimní PR akce",
      detail: "Detailní popis",
      actionType: "PR",
      dateFrom: new Date(2026, 8, 1),
      dateTo: new Date(2026, 8, 5),
      associatedProducts: ["PROD-3"],
      folderLinks: [{ folderKey: "xyz", folderType: "SharePoint" }],
    });
  });
});
```

Run:
```bash
cd frontend && CI=true npm test -- MarketingCalendarPage.test.tsx
```
Expected: `Tests: 29 passed, 29 total` (24 pre-existing + 5 new), `Test Suites: 1 passed, 1 total`. This passes **before** touching `MarketingCalendarPage.tsx` at all, because the current (buggy-typed) implementation already produces this exact runtime output for realistic data — the dead `?? a.dateFrom`/`?? a.dateTo` fallbacks never trigger when `startDate`/`endDate` are real `Date` objects (they only ever fired for a `dateFrom`/`dateTo` field that has never existed on a real response). This run is the regression baseline for NFR-1.

If any of the 5 new tests fail here, do not proceed to step 3 — it means one of the fixture shapes above doesn't match how the *current* code maps data, and the expectation needs to be corrected to match current behavior first (the whole point of this suite is to lock in *existing* behavior, not to assert new behavior).

Commit:
```bash
cd frontend && git add src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx
git commit -m "$(cat <<'EOF'
test(marketing): lock in MarketingCalendarPage's date/totalPages mapping behavior

Adds regression tests against realistic (Date-typed) API response shapes for
the calendar/list/detail mapping in MarketingCalendarPage, ahead of removing
the `as any` casts that currently hide this mapping from the compiler.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NVZ2bx8RV3pwMozZsnTeqU
EOF
)"
```

#### Step 3 — Add the typed import to `MarketingCalendarPage.tsx`

In `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`, find:
```ts
import {
  useMarketingCalendar,
  useMarketingActions,
  useMarketingAction,
  useMoveMarketingAction,
} from '../../../api/hooks/useMarketingCalendar';
import { formatDateStr } from '../calendar/fullcalendarAdapters';
```
Replace with:
```ts
import {
  useMarketingCalendar,
  useMarketingActions,
  useMarketingAction,
  useMoveMarketingAction,
} from '../../../api/hooks/useMarketingCalendar';
import type {
  MarketingActionDto as ApiMarketingActionDto,
  MarketingActionCalendarDto as ApiMarketingActionCalendarDto,
  MarketingActionType,
} from '../../../api/generated/api-client';
import { formatDateStr } from '../calendar/fullcalendarAdapters';
```
(The existing `import type { MarketingActionDto } from '../list/MarketingActionGrid';` on line 7 is untouched — it keeps meaning the local/grid shape everywhere else in this file.)

No test command for this step alone (an unused-but-imported type doesn't fail anything, but there's nothing meaningful to verify in isolation) — proceed directly to step 4, which makes the imports load-bearing.

#### Step 4 — Retype the calendar mapping (FR-1, half of FR-5)

Find:
```ts
  const calendarEvents: CalendarEvent[] = useMemo(
    () =>
      ((calendarQuery.data as any)?.actions ?? []).map((a: any) => ({
        id: a.id!,
        title: a.title ?? '',
        actionType: a.actionType ?? 'Other',
        dateFrom: a.startDate instanceof Date ? formatDateStr(a.startDate) : (a.dateFrom ?? ''),
        dateTo: a.endDate instanceof Date ? formatDateStr(a.endDate) : (a.dateTo ?? ''),
        associatedProducts: a.associatedProducts ?? [],
        outlookSyncStatus: a.outlookSyncStatus,
      })),
    [calendarQuery.data],
  );
```
Replace with:
```ts
  const calendarEvents: CalendarEvent[] = useMemo(
    () =>
      (calendarQuery.data?.actions ?? []).map((a: ApiMarketingActionCalendarDto) => ({
        id: a.id!,
        title: a.title ?? '',
        actionType: (a.actionType ?? 'Other') as MarketingActionType,
        dateFrom: a.startDate ? formatDateStr(a.startDate) : '',
        dateTo: a.endDate ? formatDateStr(a.endDate) : '',
        associatedProducts: a.associatedProducts ?? [],
        outlookSyncStatus: a.outlookSyncStatus,
      })),
    [calendarQuery.data],
  );
```
Notes on this exact diff:
- `calendarQuery.data` is now used with plain optional chaining, no cast — its type is inferred as `GetMarketingCalendarResponse | undefined` by React Query from `useMarketingCalendar`'s `queryFn`.
- The `.map()` callback parameter is now explicitly `ApiMarketingActionCalendarDto` instead of `any`.
- The `instanceof Date` guard + `?? a.dateFrom` else-branch is gone (per FR-5): `a.startDate` is now statically known to be `Date | undefined`, never a pre-formatted string, so the guard was already dead weight — replaced with the simpler `a.startDate ? formatDateStr(a.startDate) : ''`, matching FR-5's specified fallback-to-`''`-on-absence behavior exactly.
- `actionType` now has the `as MarketingActionType` cast explained above — required because `CalendarEvent.actionType` is the enum type but the generated DTO's `actionType` is `string`.

#### Step 5 — Retype the list mapping, totalPages, and the detail effect (FR-2, FR-3, FR-4, rest of FR-5)

Find:
```ts
  const listActions: MarketingActionDto[] = useMemo(
    () =>
      ((listQuery.data as any)?.actions ?? []).map((a: any) => ({
        id: a.id,
        title: a.title,
        detail: a.description,
        actionType: a.actionType,
        dateFrom: a.startDate ?? a.dateFrom,
        dateTo: a.endDate ?? a.dateTo,
        associatedProducts: a.associatedProducts,
        folderLinks: a.folderLinks,
        outlookSyncStatus: a.outlookSyncStatus,
      })),
    [listQuery.data],
  );

  const totalPages: number = (listQuery.data as any)?.totalPages ?? 1;
```
Replace with:
```ts
  const listActions: MarketingActionDto[] = useMemo(
    () =>
      (listQuery.data?.actions ?? []).map((a: ApiMarketingActionDto) => ({
        id: a.id,
        title: a.title,
        detail: a.description,
        actionType: a.actionType,
        dateFrom: a.startDate,
        dateTo: a.endDate,
        associatedProducts: a.associatedProducts,
        folderLinks: a.folderLinks,
        outlookSyncStatus: a.outlookSyncStatus,
      })),
    [listQuery.data],
  );

  const totalPages: number = listQuery.data?.totalPages ?? 1;
```
Notes:
- `dateFrom: a.startDate` / `dateTo: a.endDate` assigned directly (per FR-5) — the local `MarketingActionDto.dateFrom`/`dateTo` fields accept `string | Date`, and `a.startDate`/`a.endDate` are `Date | undefined`, which is assignable as-is; the dead `?? a.dateFrom` / `?? a.dateTo` are simply deleted, no replacement needed.
- `totalPages` needs no cast at all now — `listQuery.data?.totalPages` is `number | undefined`, and `?? 1` produces `number` (unchanged runtime expression, now type-checked, satisfying FR-3).

Then find:
```ts
  React.useEffect(() => {
    if ((detailQuery.data as any)?.action) {
      const a = (detailQuery.data as any).action;
      setEditingAction({
        id: a.id,
        title: a.title,
        detail: a.description ?? a.detail,
        actionType: a.actionType,
        dateFrom: a.startDate ?? a.dateFrom,
        dateTo: a.endDate ?? a.dateTo,
        associatedProducts: a.associatedProducts,
        folderLinks: a.folderLinks,
      });
    }
  }, [detailQuery.data]);
```
Replace with:
```ts
  React.useEffect(() => {
    const a: ApiMarketingActionDto | undefined = detailQuery.data?.action;
    if (a) {
      setEditingAction({
        id: a.id,
        title: a.title,
        detail: a.description,
        actionType: a.actionType,
        dateFrom: a.startDate,
        dateTo: a.endDate,
        associatedProducts: a.associatedProducts,
        folderLinks: a.folderLinks,
      });
    }
  }, [detailQuery.data]);
```
Notes:
- `detailQuery.data?.action` is read once into a typed local (`ApiMarketingActionDto | undefined`) instead of being cast and re-accessed twice.
- `detail: a.description ?? a.detail` becomes `detail: a.description` — dropping `?? a.detail` is required here (not just a nicety): the generated `MarketingActionDto` has no `detail` field at all, so `a.detail` is a compile error the moment `a` stops being `any` (see "Two fixes beyond the letter of the spec" above, fix #1).
- `dateFrom: a.startDate ?? a.dateFrom` / `dateTo: a.endDate ?? a.dateTo` become `dateFrom: a.startDate` / `dateTo: a.endDate`, same reasoning as the list mapping.

At this point every `as any` and every `.dateFrom`/`.dateTo`/`.detail` dead-fallback reference on a generated-DTO-typed value in this file is gone. Grep to confirm:
```bash
cd frontend && grep -n "as any" src/components/marketing/pages/MarketingCalendarPage.tsx
```
Expected: no output (empty result, exit code 1 from grep finding nothing).

#### Step 6 — Verify the fix compiles and behaves identically

Run, in order:
```bash
cd frontend && CI=true npm test -- MarketingCalendarPage.test.tsx
```
Expected: `Tests: 29 passed, 29 total` — identical result to step 2's run, now against the fixed implementation. This is the concrete evidence for NFR-1 (no behavior change): the same fixtures produce the same mapped output before and after the retyping.

```bash
cd frontend && npm run build
```
Expected: build succeeds (`Compiled successfully.` or `Compiled with warnings.` only if pre-existing warnings unrelated to this file already exist — there must be no **new** error or warning referencing `MarketingCalendarPage.tsx`). This is where NFR-2 is actually enforced: if step 4 or 5 were applied incompletely, this is the command that would catch it, e.g.:
  - Leaving in `?? a.dateFrom` anywhere → `error TS2339: Property 'dateFrom' does not exist on type 'ApiMarketingActionCalendarDto'.` (calendar site) or `... on type 'ApiMarketingActionDto'.` (list/detail sites).
  - Leaving in `?? a.detail` → `error TS2339: Property 'detail' does not exist on type 'ApiMarketingActionDto'.`
  - Leaving out the `as MarketingActionType` cast on `actionType` in the calendar mapping → `error TS2322: Type 'string' is not assignable to type 'MarketingActionType'.`

```bash
cd frontend && npm run lint
```
Expected: no new lint errors/warnings for `MarketingCalendarPage.tsx` or its test file.

#### Step 7 — Commit the implementation

```bash
cd frontend && git add src/components/marketing/pages/MarketingCalendarPage.tsx
git commit -m "$(cat <<'EOF'
fix(marketing): replace as-any casts with generated API types in MarketingCalendarPage

Types calendarQuery.data, listQuery.data, and detailQuery.data using the
generated GetMarketingCalendarResponse/GetMarketingActionsResponse/
GetMarketingActionResponse response shapes (inferred automatically once the
casts are removed), and types each .map()/effect callback parameter as the
generated MarketingActionDto/MarketingActionCalendarDto under an
ApiMarketingActionDto/ApiMarketingActionCalendarDto alias to avoid colliding
with the differently-shaped local MarketingActionDto already imported from
MarketingActionGrid.

Also drops the dead `?? a.dateFrom` / `?? a.dateTo` / `?? a.detail`
fallbacks, which referenced properties that have never existed on the
generated response DTOs and only compiled because `as any` hid the error.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01NVZ2bx8RV3pwMozZsnTeqU
EOF
)"
```

#### Step 8 — Final self-review against the spec (no code change; a checklist)

Confirm each of these directly against the diff before considering the task done:

- [ ] `grep -n "as any" frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` returns nothing.
- [ ] `grep -n "\.dateFrom\|\.dateTo" frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` shows only assignments *to* local `dateFrom`/`dateTo` fields (`dateFrom: a.startDate`, `dateFrom: a.startDate ? ... : ''`, etc.) — never a read *from* `a.dateFrom`/`a.dateTo`, and never on a value typed as `ApiMarketingActionDto`/`ApiMarketingActionCalendarDto`.
- [ ] The two `MarketingActionDto` imports (local, unaliased; generated, aliased `ApiMarketingActionDto`) coexist in the file with no naming conflict.
- [ ] `MarketingActionCalendarDto` is imported aliased as `ApiMarketingActionCalendarDto` and used only for the calendar `.map()` callback parameter.
- [ ] `npm run build` and `npm run lint` both pass (step 6).
- [ ] `CI=true npm test -- MarketingCalendarPage.test.tsx` passes with the same test count before and after the implementation change (steps 2 and 6).
- [ ] No file other than `MarketingCalendarPage.tsx` and its test file was modified.
