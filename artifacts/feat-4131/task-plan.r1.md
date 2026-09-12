# Implementation Plan: Type-safe API response handling in MarketingCalendarPage

## Goal

Remove the 4 `as any` casts in `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (on `calendarQuery.data`, `listQuery.data` ×2, `detailQuery.data`) and replace them with the real generated NSwag client types, so `tsc`/`npm run build` catches any future backend field rename or removal on `MarketingActionDto` / `MarketingActionCalendarDto`. Alongside this, remove the dead `?? a.dateFrom` / `?? a.dateTo` fallback expressions that only compiled because `as any` suppressed the type error — those properties have never existed on the generated response types.

This is a pure type-safety refactor. **No runtime behavior change** is intended for any real API response (NFR-1). No backend change, no NSwag regeneration (NFR-3).

## Scope

**Files touched (exactly two, both already exist):**

| File | Responsibility | Change |
|---|---|---|
| `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` | The component under test; owns the mapping boundary between generated API DTOs and the two local presentational shapes (`CalendarEvent`, local `MarketingActionDto`) | Add a type-only import of the generated DTOs (aliased) + `MarketingActionType`; retype the 3 `.map()`/effect call sites; drop the dead `dateFrom`/`dateTo`/`detail` fallbacks; fix one latent enum-typing issue this refactor surfaces (see "Two fixes beyond the letter of the spec" below) |
| `frontend/src/components/marketing/pages/__tests__/MarketingCalendarPage.test.tsx` | Existing component test suite (24 tests, Jest + React Testing Library, hooks mocked directly — no MSW) | Extend the existing hook/child-component mocks to be data-driven per test, and add a new `describe` block that locks in the exact mapping behavior (dates, totalPages, detail prefill) this refactor must not change |

**Files read but NOT touched** (confirmed correct as-is by direct inspection — this is the load-bearing evidence for NFR-3 and for "no other file needs to change"):
- `frontend/src/api/generated/api-client.ts` — generated NSwag client, contains all needed types already (see "Type reference" below).
- `frontend/src/api/hooks/useMarketingCalendar.ts` — `useMarketingCalendar`, `useMarketingActions`, `useMarketingAction` each return `await client.xxx(...)` directly from their `queryFn`, so `useQuery`'s generic already infers the correct generated response type on `.data` — no hook change needed, no cast needed to get a typed `.data`.
- `frontend/src/components/marketing/calendar/fullcalendarAdapters.ts` — `CalendarEvent`, `formatDateStr` — reused as-is.
- `frontend/src/components/marketing/list/MarketingActionGrid.tsx` — local `MarketingActionDto` interface — reused as-is.

**Out of scope** (per spec): backend/OpenAPI changes, NSwag regeneration, renaming the local `dateFrom`/`dateTo` convention, and fixing the identical `as any` + dead-fallback pattern in `frontend/src/components/marketing/calendar/MobileAgendaView.tsx` (flagged by arch-review as a good follow-up ticket, not this one).

## Type reference (verified directly against `frontend/src/api/generated/api-client.ts`)

```ts
// api-client.ts:32166 — the GENERATED MarketingActionDto (aliased ApiMarketingActionDto in the plan below)
export class MarketingActionDto {
    id?: number;
    title?: string;
    description?: string | undefined;
    actionType?: string;                 // plain string, NOT the MarketingActionType enum
    startDate?: Date;
    endDate?: Date | undefined;
    createdAt?: Date;
    modifiedAt?: Date;
    createdByUserId?: string;
    createdByUsername?: string | undefined;
    modifiedByUserId?: string | undefined;
    modifiedByUsername?: string | undefined;
    associatedProducts?: string[];
    folderLinks?: MarketingActionFolderLinkDto[];   // { folderKey?: string; folderType?: string }
    outlookSyncStatus?: string;
    outlookEventId?: string | undefined;
    // NOTE: no `dateFrom`, no `dateTo`, no `detail` field. Never has been.
}

// api-client.ts:32401 — the GENERATED MarketingActionCalendarDto (aliased ApiMarketingActionCalendarDto)
export class MarketingActionCalendarDto {
    id?: number;
    title?: string;
    actionType?: string;                 // plain string, NOT the MarketingActionType enum
    startDate?: Date;
    endDate?: Date | undefined;
    associatedProducts?: string[];
    outlookSyncStatus?: string;
    // NOTE: no `dateFrom`, no `dateTo`.
}

// api-client.ts:32101 — GetMarketingActionsResponse.actions?: MarketingActionDto[]; totalPages?: number; (return type of useMarketingActions, inferred automatically, no import needed)
// api-client.ts:32327 — GetMarketingActionResponse.action?: MarketingActionDto | undefined; (return type of useMarketingAction, inferred automatically)
// api-client.ts:32360 — GetMarketingCalendarResponse.actions?: MarketingActionCalendarDto[]; (return type of useMarketingCalendar, inferred automatically)
```

```ts
// frontend/src/components/marketing/list/MarketingActionGrid.tsx:8 — the LOCAL MarketingActionDto (imported unaliased, unchanged)
export interface MarketingActionDto {
  id?: number;
  title?: string;
  detail?: string;
  actionType?: string;
  dateFrom?: string | Date;
  dateTo?: string | Date;
  associatedProducts?: string[];
  folderLinks?: Array<{ path?: string; label?: string; folderType?: string }>;
  outlookSyncStatus?: string;
}

// frontend/src/components/marketing/calendar/fullcalendarAdapters.ts:4 — CalendarEvent (unchanged)
export interface CalendarEvent {
  id: number;
  title: string;
  actionType: MarketingActionType;   // the ENUM, imported from api-client.ts
  dateFrom: string;                  // YYYY-MM-DD
  dateTo: string;                    // YYYY-MM-DD, inclusive
  associatedProducts: string[];
  outlookSyncStatus?: string;
}
```

Because `GetMarketingActionsResponse`, `GetMarketingActionResponse`, and `GetMarketingCalendarResponse` are inferred automatically on `.data` by React Query once the `as any` casts are deleted, **this plan does not import those three response-envelope type names** — only `MarketingActionDto`/`MarketingActionCalendarDto` (aliased) and `MarketingActionType` are actually referenced in the file, and importing unused type names would be needless surface area. (The spec's own "API/Interface Design" section lists all five names in one `import type` block; the arch-review's own text already clarifies the three response types aren't needed for an explicit annotation. This plan follows that clarification rather than the literal import list, to keep the diff surgical.)

## Two fixes beyond the literal FR/FR-5 text (found by reading the real code, verified with the TypeScript compiler)

Reading the actual file surfaced two spots where a literal, word-for-word implementation of the spec's proposed code would not compile. Both are additional instances of the exact same root cause the spec is fixing (a property access that only worked because `as any` hid it) — I verified each in isolation with `tsc --strict`:

1. **`detailQuery`'s `useEffect` (current line 197) has a *third* dead fallback the spec's FR-5 text doesn't mention**: `detail: a.description ?? a.detail`. `a.detail` does not exist on the generated `MarketingActionDto` (only `description` does) — same bug pattern as `dateFrom`/`dateTo`, just on a different field. Confirmed with `tsc`: `error TS2339: Property 'detail' does not exist on type 'ApiMarketingActionDto'.` **Fix: drop `?? a.detail`, keep `a.description` only** — this is the exact same treatment FR-5 already prescribes for the date fields, just extended to this field too, and is required for the file to compile at all once the `as any` is removed.

2. **The calendar mapping's `actionType: a.actionType ?? 'Other'` does not compile once `a` is typed**, for a different reason than dates: `CalendarEvent.actionType` is typed as the `MarketingActionType` **enum** (imported from `api-client.ts`), but the generated `MarketingActionCalendarDto.actionType` is a plain `string`. TypeScript's string enums are nominally typed — a plain `string` (even one containing a valid member's literal value, e.g. `'SocialMedia'`) is never assignable to the enum type without an explicit assertion; verified with `tsc --strict`: `error TS2322: Type 'string' is not assignable to type 'MarketingActionType'.` This is exactly the latent risk the arch-review's own risk table anticipated ("optional `actionType` fields feeding non-optional local fields") but its suggested mitigation ("keep the existing `?? 'Other'` defaulting") is not by itself sufficient — the defaulting has to be paired with a type assertion. **Fix: `actionType: (a.actionType ?? 'Other') as MarketingActionType`** — a single, narrow assertion between two string-shaped types (not `as any`, not `as unknown as X`, so it does not violate NFR-2's explicit ban list), which preserves the exact current runtime value (including the pre-existing, not-a-real-enum-member `'Other'` fallback string) bit-for-bit, satisfying NFR-1.

Both are called out explicitly in the task below so the diff matches this plan exactly.

## Requirements coverage

| Requirement | Covered by |
|---|---|
| FR-1 (calendar typed, no `as any`, callback typed) | Task step 3 |
| FR-2 (list typed, no `as any`, callback typed, alias coexistence) | Task step 3 |
| FR-3 (`totalPages` typed) | Task step 3 |
| FR-4 (detail typed, no `as any`) | Task step 3 |
| FR-5 (dead `dateFrom`/`dateTo` fallbacks removed, dates mapped correctly) | Task step 3 |
| NFR-1 (no behavior change) | Task steps 1–2 (regression-lock tests, proven to pass unchanged before *and* after) |
| NFR-2 (compile-time safety, no `as any`/`as unknown as X`/untyped `any` map param) | Task step 3 code + step 5 build verification |
| NFR-3 (no backend/NSwag change) | Confirmed by "Files read but NOT touched" above — no backend or `api-client.ts` file is edited |

## Tech stack / tooling notes (confirmed from the actual repo, not assumed)

- Frontend: Create React App (`react-scripts`), TypeScript `strict: true` (see `frontend/tsconfig.json`), no `noUnusedLocals`/`noUnusedParameters`.
- `npm run build` (`react-scripts build`) type-checks via `ForkTsCheckerWebpackPlugin` — but **only the files reachable from the webpack entry point** (`src/index.tsx`). Test files (`__tests__/**`, `*.test.tsx`) are never imported by production code, so they are not type-checked by `npm run build`; this is why the existing test file already has untyped mock-callback parameters (e.g. `default: ({ onPrevious, onNext, onToday }) => ...` in the `CalendarNavigation` mock) without failing the build. Follow that same existing convention for new mock code below — do not add explicit prop types to the inline mock functions.
- Tests: Jest + React Testing Library (`react-scripts test`), run via `CI=true npm test -- <pattern>` for a single non-watch run. This specific test file mocks `../../../../api/hooks/useMarketingCalendar` directly (no MSW), and the child components (`MarketingMonthCalendar`, `MarketingActionGrid`, `MarketingActionModal`, `ImportFromOutlookModal`, `MarketingActionFilters`, `CalendarNavigation`, `MobileAgendaView`) via `jest.mock`. This plan extends that exact pattern rather than introducing a new testing approach.
- Lint: `.eslintrc.json` extends `react-app`/`react-app/jest` (CRA defaults) plus one repo-specific `no-restricted-syntax` rule (unrelated to this change).

---

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
