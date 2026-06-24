# Access Management — Mobile Responsiveness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every screen under `/admin/access` (Users/Groups list grids, Group/User detail pages, and the shared TransferList picker) usable on a 375px-wide phone, while leaving the desktop layout (≥768px) byte-for-byte unchanged.

**Architecture:** Frontend-only, CSS/JSX changes inside 5 existing files. Mobile is detected with the existing `useIsMobile()` hook (`(max-width: 767px)`, matches Tailwind's `md` breakpoint). Grids and TransferList branch their render tree on `useIsMobile()` (JS conditional, *not* CSS `md:hidden`); the two detail pages use Tailwind responsive utility classes only. No backend, API, DTO, route, or picker-component changes.

**Tech Stack:** React 18 + TypeScript, Tailwind CSS 3.4, `@dnd-kit/core`, `react-scripts test` (Jest + Testing Library), lucide-react icons.

---

## Assumptions & Deviations from the spec

Read this before starting — two decisions intentionally diverge from the literal spec text, with reasons. Both preserve the spec's *intent* (table on desktop, cards on mobile; desktop untouched).

1. **Grids switch table↔cards with a `useIsMobile()` JS conditional, NOT CSS `md:hidden` / `hidden md:block`.**
   The spec (sections 4 & 5) suggests CSS visibility classes. That cannot be used here: jsdom does not apply CSS, so a CSS-only dual render puts **both** the table and the cards in the DOM during tests. The existing `UsersGrid.test.tsx` and `GroupsGrid.test.tsx` use singular queries (`getByText("Alice")`, `getByRole("button", { name: "Alice" })`); duplicated rows/buttons would make those queries throw "multiple elements found", breaking ~12 currently-green tests. The codebase already solves mobile/desktop swaps with a JS conditional in `DashboardGrid.tsx` (`useIsMobile()`), and the *same spec* uses `useIsMobile()` for TransferList — so this keeps the approach consistent and the existing suite green. The visual result is identical.

2. **TransferList desktop branch is refactored to share a `renderRows()` helper with the mobile branch (DRY).** The emitted desktop DOM is identical to today's; only the internal JSX is deduplicated so the grouped/flat row logic isn't copy-pasted into the new mobile branch.

3. **Tab counts** in TransferList mobile show the *currently visible* (search-filtered) item count per side — i.e. `availableItems.length` / `assignedItems.length`, the same arrays the desktop columns already render. With no active search these equal the totals.

4. **GroupDetailPage button group gets `md:ml-auto`** in addition to the spec's classes, to preserve the existing desktop "buttons pushed to the far right" look (the old layout used `justify-between`, which is dropped when switching to `flex-wrap`).

5. **Pure-CSS pages are verified by build + existing-test regression + visual check, not new className-asserting unit tests.** GroupDetailPage (Task 2) and UserDetailPage (Task 3) only change Tailwind classes with no behavior change; their existing test files assert zero class names, so brittle className tests add no value. Tasks with real behavior (TransferList tabs, the two card grids) are TDD.

---

## File Structure

| File | Responsibility | Change |
|------|----------------|--------|
| `frontend/src/components/access-management/TransferList.tsx` | Two-pane transfer picker | Add mobile tab-switched single-pane branch; extract `renderRows()`; share search bar between branches |
| `frontend/src/pages/GroupDetailPage.tsx` | Group create/edit page | Header row → `flex-wrap`; button group full-width on mobile |
| `frontend/src/pages/UserDetailPage.tsx` | User edit page | Outer padding `p-3 sm:p-8`; header `flex-wrap` |
| `frontend/src/components/pages/access/UsersGrid.tsx` | Users list | Mobile card list via `useIsMobile()`; search input full-width on mobile |
| `frontend/src/components/pages/access/GroupsGrid.tsx` | Groups list | Mobile card list via `useIsMobile()`; search input full-width on mobile |
| `frontend/src/components/access-management/__tests__/TransferList.test.tsx` | Existing tests | Add `jest.mock` for `useMediaQuery` (default desktop) + a mobile `describe` block |
| `frontend/src/components/pages/access/__tests__/UsersGrid.mobile.test.tsx` | **New** test file | Mobile card behavior |
| `frontend/src/components/pages/access/__tests__/GroupsGrid.mobile.test.tsx` | **New** test file | Mobile card behavior |

---

## Reference: project-specific facts the engineer needs

- **Test runner:** `cd frontend && CI=true npx react-scripts test <path> --watchAll=false`. Do **not** invoke `npx jest` directly — it produces TS parse errors in this repo (use `react-scripts test`).
- **`useIsMobile()`** lives in `frontend/src/hooks/useMediaQuery.ts` and returns `useMediaQuery('(max-width: 767px)')`.
- **In tests, `window.matchMedia` is globally mocked** in `frontend/src/setupTests.ts` to return `{ matches: false }` → `useIsMobile()` defaults to **false (desktop)**. To test the mobile path, `jest.mock("<relative>/hooks/useMediaQuery", () => ({ useIsMobile: jest.fn(() => true) }))`, mirroring `frontend/src/components/dashboard/__tests__/DashboardGrid.test.tsx`.
- **Validation gate (run before declaring done):** `cd frontend && npm run build && npm run lint`. `npm run build` is stricter than `tsc --noEmit` — always run the build.
- **`line-clamp-2`** is available (Tailwind 3.4, already used across the repo) — no plugin needed.

---

## Task 1: TransferList — mobile tab-switched single pane

**Files:**
- Modify: `frontend/src/components/access-management/TransferList.tsx`
- Test: `frontend/src/components/access-management/__tests__/TransferList.test.tsx`

- [ ] **Step 1: Add the `useMediaQuery` mock and write failing mobile tests**

Open `frontend/src/components/access-management/__tests__/TransferList.test.tsx`. Add this `jest.mock` immediately after the imports at the top of the file (before the existing `const items = ...`). It defaults to desktop so the existing tests are unaffected:

```tsx
jest.mock("../../../hooks/useMediaQuery", () => ({
  useIsMobile: jest.fn(() => false),
}));
```

Then append this new `describe` block to the **end** of the file (after the last existing `describe`'s closing `});`):

```tsx
describe("TransferList — mobile", () => {
  const { useIsMobile } = require("../../../hooks/useMediaQuery");

  beforeEach(() => {
    useIsMobile.mockReturnValue(true);
  });

  afterEach(() => {
    useIsMobile.mockReturnValue(false);
  });

  it("shows available and assigned tabs with counts", () => {
    render(
      <TransferList
        available={items}
        assignedIds={["b"]}
        onChange={jest.fn()}
        labels={{ available: "Available", assigned: "Assigned" }}
      />
    );
    expect(
      screen.getByRole("tab", { name: /available \(2\)/i })
    ).toBeInTheDocument();
    expect(
      screen.getByRole("tab", { name: /assigned \(1\)/i })
    ).toBeInTheDocument();
  });

  it("shows only the available side by default and hides assigned items", () => {
    render(
      <TransferList available={items} assignedIds={["b"]} onChange={jest.fn()} />
    );
    expect(screen.getByText("Item A")).toBeInTheDocument();
    expect(screen.getByText("Item C")).toBeInTheDocument();
    expect(screen.queryByText("Item B")).not.toBeInTheDocument();
  });

  it("switches to the assigned tab and shows assigned items", () => {
    render(
      <TransferList available={items} assignedIds={["b"]} onChange={jest.fn()} />
    );
    fireEvent.click(screen.getByRole("tab", { name: /assigned/i }));
    expect(screen.getByText("Item B")).toBeInTheDocument();
    expect(screen.queryByText("Item A")).not.toBeInTheDocument();
  });

  it("still assigns via the + button on mobile", () => {
    const onChange = jest.fn();
    render(
      <TransferList available={items} assignedIds={["b"]} onChange={onChange} />
    );
    fireEvent.click(screen.getByRole("button", { name: /assign item a/i }));
    expect(onChange).toHaveBeenCalledWith(["b", "a"]);
  });

  it("search filters the active tab", () => {
    render(
      <TransferList
        available={items}
        assignedIds={[]}
        onChange={jest.fn()}
        searchable
        searchPlaceholder="Search…"
      />
    );
    fireEvent.change(screen.getByLabelText("Search…"), {
      target: { value: "Item A" },
    });
    expect(screen.getByText("Item A")).toBeInTheDocument();
    expect(screen.queryByText("Item C")).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `cd frontend && CI=true npx react-scripts test src/components/access-management/__tests__/TransferList.test.tsx --watchAll=false`
Expected: the new `TransferList — mobile` tests FAIL (no `tab` role rendered; assigned items still visible). The original `describe("TransferList")` tests still PASS (desktop default).

- [ ] **Step 3: Rewrite the `TransferList` component to add the mobile branch**

In `frontend/src/components/access-management/TransferList.tsx`:

3a. Change the React import (line 1) to add `useRef`:

```tsx
import React, { useRef, useState } from "react";
```

3b. Add the hook import after the `@dnd-kit/utilities` import (after line 13):

```tsx
import { useIsMobile } from "../../hooks/useMediaQuery";
```

3c. Replace the **entire** `TransferList` function (currently lines 177–309, from `function TransferList({` down to its closing `}` just before `export default TransferList;`) with this exact implementation:

```tsx
function TransferList({
  available,
  assignedIds,
  onChange,
  groupBy,
  labels = {},
  fillHeight,
  searchable,
  searchPlaceholder = "Search…",
  highlightedIds,
  highlightLabel,
}: TransferListProps) {
  const isMobile = useIsMobile();
  const highlighted = new Set(highlightedIds);
  // A small activation distance keeps the per-row +/− button clicks from being
  // swallowed by the drag sensor — a plain click no longer starts a drag.
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 8 } }),
    useSensor(KeyboardSensor),
  );

  const [query, setQuery] = useState("");
  const [activeTab, setActiveTab] = useState<"available" | "assigned">(
    "available",
  );
  const listRef = useRef<HTMLDivElement>(null);
  const trimmedQuery = query.trim().toLowerCase();

  const availableItems = available
    .filter((item) => !assignedIds.includes(item.id))
    .filter((item) => !trimmedQuery || matchesQuery(item, trimmedQuery));
  const assignedItems = available
    .filter((item) => assignedIds.includes(item.id))
    .filter((item) => !trimmedQuery || matchesQuery(item, trimmedQuery));

  const handleAssign = (id: string) => onChange([...assignedIds, id]);
  const handleRemove = (id: string) =>
    onChange(assignedIds.filter((existing) => existing !== id));

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (!over) return;
    const itemId = String(active.id);
    const targetZone = String(over.id);
    if (targetZone === "assigned" && !assignedIds.includes(itemId)) {
      handleAssign(itemId);
    } else if (targetZone === "available" && assignedIds.includes(itemId)) {
      handleRemove(itemId);
    }
  };

  const availableGroups = groupBy ? buildGroups(availableItems, groupBy) : null;

  const renderRows = (
    rowItems: TransferItem[],
    direction: "assign" | "remove",
    grouped: Map<string, TransferItem[]> | null,
  ) => {
    const row = (item: TransferItem) => (
      <ItemRow
        key={item.id}
        item={item}
        direction={direction}
        onMove={() =>
          direction === "assign"
            ? handleAssign(item.id)
            : handleRemove(item.id)
        }
        highlighted={highlighted.has(item.id)}
        highlightLabel={highlightLabel}
      />
    );
    if (!grouped) return rowItems.map(row);
    return Array.from(grouped.entries()).map(([section, sectionItems]) => (
      <div key={section}>
        <div className="text-xs font-medium text-gray-500 px-1 pt-3 pb-0.5 first:pt-0">
          {section}
        </div>
        {sectionItems.map(row)}
      </div>
    ));
  };

  const searchBar = searchable && (
    <div className="relative mb-3 flex-shrink-0">
      <div className="absolute inset-y-0 left-0 pl-3 flex items-center pointer-events-none">
        <Search className="h-4 w-4 text-gray-400" />
      </div>
      <input
        type="text"
        aria-label={searchPlaceholder}
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder={searchPlaceholder}
        className="focus:ring-indigo-500 focus:border-indigo-500 block w-full pl-10 pr-3 py-2 text-sm border border-gray-300 rounded-md"
      />
    </div>
  );

  if (isMobile) {
    const showAvailable = activeTab === "available";
    const paneItems = showAvailable ? availableItems : assignedItems;
    const direction: "assign" | "remove" = showAvailable ? "assign" : "remove";
    const grouped = showAvailable ? availableGroups : null;
    const emptyMessage = showAvailable ? "All items assigned" : "None assigned";

    const switchTab = (tab: "available" | "assigned") => {
      setActiveTab(tab);
      if (listRef.current) listRef.current.scrollTop = 0;
    };

    const tabClass = (active: boolean) =>
      `flex-1 px-3 py-2 text-sm font-medium border-b-2 ${
        active
          ? "border-indigo-600 text-indigo-600"
          : "border-transparent text-gray-500 hover:text-gray-700"
      }`;

    return (
      <div className={fillHeight ? "flex flex-col h-full min-h-0" : ""}>
        {searchBar}
        <div className="flex flex-shrink-0 mb-2" role="tablist">
          <button
            type="button"
            role="tab"
            aria-selected={showAvailable}
            onClick={() => switchTab("available")}
            className={tabClass(showAvailable)}
          >
            {labels.available ?? "Available"} ({availableItems.length})
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={!showAvailable}
            onClick={() => switchTab("assigned")}
            className={tabClass(!showAvailable)}
          >
            {labels.assigned ?? "Assigned"} ({assignedItems.length})
          </button>
        </div>
        <div
          ref={listRef}
          className={`space-y-1 ${
            fillHeight ? "flex-1 min-h-0 overflow-y-auto" : "min-h-48"
          }`}
        >
          {renderRows(paneItems, direction, grouped)}
          {paneItems.length === 0 && (
            <div className="text-sm text-gray-400 text-center py-6">
              {emptyMessage}
            </div>
          )}
        </div>
      </div>
    );
  }

  return (
    <DndContext sensors={sensors} onDragEnd={handleDragEnd}>
      <div className={fillHeight ? "flex flex-col h-full min-h-0" : ""}>
        {searchBar}
        <div
          className={`grid grid-cols-2 gap-4${fillHeight ? " flex-1 min-h-0" : ""}`}
        >
          <DropZone
            id="available"
            label={labels.available ?? "Available"}
            emptyMessage="All items assigned"
            isEmpty={availableItems.length === 0}
            variant="left"
            fillHeight={fillHeight}
          >
            {renderRows(availableItems, "assign", availableGroups)}
          </DropZone>

          <DropZone
            id="assigned"
            label={labels.assigned ?? "Assigned"}
            emptyMessage="None assigned"
            isEmpty={assignedItems.length === 0}
            variant="right"
            fillHeight={fillHeight}
          >
            {renderRows(assignedItems, "remove", null)}
          </DropZone>
        </div>
      </div>
    </DndContext>
  );
}
```

- [ ] **Step 4: Run the full TransferList test file to verify all pass**

Run: `cd frontend && CI=true npx react-scripts test src/components/access-management/__tests__/TransferList.test.tsx --watchAll=false`
Expected: PASS — both the original desktop tests and the new mobile tests.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/access-management/TransferList.tsx frontend/src/components/access-management/__tests__/TransferList.test.tsx
git commit -m "feat(access): mobile tab-switched TransferList"
```

---

## Task 2: GroupDetailPage — wrap header on mobile

**Files:**
- Modify: `frontend/src/pages/GroupDetailPage.tsx:251` (header row) and `:264` (button group)

This task is pure Tailwind class changes (no behavior change). Verification is build + existing-test regression + a visual check, not a new unit test (see Deviation #5).

- [ ] **Step 1: Change the header row container**

In `frontend/src/pages/GroupDetailPage.tsx`, find (line 251):

```tsx
      <div className="flex-shrink-0 flex items-center justify-between gap-4 mb-3">
```

Replace with:

```tsx
      <div className="flex-shrink-0 flex flex-wrap items-center gap-x-4 gap-y-2 mb-3">
```

- [ ] **Step 2: Change the Save/Cancel button group**

In the same file, find (line 264):

```tsx
        <div className="flex gap-3 flex-shrink-0">
```

Replace with:

```tsx
        <div className="flex gap-3 w-full justify-end md:w-auto md:ml-auto">
```

(`w-full` forces the buttons onto their own row on mobile and `justify-end` right-aligns them; `md:w-auto md:ml-auto` restores the desktop "pushed to the far right" layout that the dropped `justify-between` used to provide.)

- [ ] **Step 3: Run the existing GroupDetailPage tests to confirm no regression**

Run: `cd frontend && CI=true npx react-scripts test src/pages/__tests__/GroupDetailPage.test.tsx --watchAll=false`
Expected: PASS (these tests assert behavior, not classes — they must stay green).

- [ ] **Step 4: Visual check at 375px**

Run the app (`cd frontend && npm start`), open `/admin/access/groups/<some-id>` in a 375px-wide viewport (browser devtools device toolbar → iPhone 13 mini). Confirm: back link + title on row 1; Save + Cancel on row 2, right-aligned; no horizontal overflow. On a desktop-width window, confirm the header looks exactly as before (buttons far right).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/GroupDetailPage.tsx
git commit -m "feat(access): wrap GroupDetailPage header on mobile"
```

---

## Task 3: UserDetailPage — padding and header wrap

**Files:**
- Modify: `frontend/src/pages/UserDetailPage.tsx:122` (loading container), `:139` (outer container), `:140` (header)

Pure Tailwind class changes; verification is build + existing-test regression + visual check.

- [ ] **Step 1: Shrink the outer container padding on mobile**

In `frontend/src/pages/UserDetailPage.tsx`, find (line 139):

```tsx
    <div className="p-8 max-w-5xl mx-auto space-y-8">
```

Replace with:

```tsx
    <div className="p-3 sm:p-8 max-w-5xl mx-auto space-y-8">
```

- [ ] **Step 2: Apply the same padding to the loading state for consistency**

In the same file, find the loading early-return (line 122):

```tsx
      <div className="p-8 max-w-5xl mx-auto">
```

Replace with:

```tsx
      <div className="p-3 sm:p-8 max-w-5xl mx-auto">
```

- [ ] **Step 3: Let the header wrap on narrow screens**

In the same file, find (line 140):

```tsx
      <div className="flex items-center gap-4">
```

Replace with:

```tsx
      <div className="flex flex-wrap items-center gap-4 min-w-0">
```

- [ ] **Step 4: Run the existing UserDetailPage tests to confirm no regression**

Run: `cd frontend && CI=true npx react-scripts test src/pages/__tests__/UserDetailPage.test.tsx --watchAll=false`
Expected: PASS.

- [ ] **Step 5: Visual check at 375px**

Open `/admin/access/users/<some-id>` at 375px. Confirm: outer padding is tight (`p-3`), title wraps below the back link if long, Save/Cancel buttons at the bottom are reachable with no horizontal scroll. On desktop width, padding is back to `p-8` and the header is a single row.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/pages/UserDetailPage.tsx
git commit -m "feat(access): tighten UserDetailPage padding and wrap header on mobile"
```

---

## Task 4: UsersGrid — mobile card layout

**Files:**
- Modify: `frontend/src/components/pages/access/UsersGrid.tsx`
- Test: `frontend/src/components/pages/access/__tests__/UsersGrid.mobile.test.tsx` (new)

- [ ] **Step 1: Write the failing mobile test file**

Create `frontend/src/components/pages/access/__tests__/UsersGrid.mobile.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import UsersGrid from "../UsersGrid";

const mockNavigate = jest.fn();
const mockSetActiveMutate = jest.fn();
const mockSetCanPackMutate = jest.fn();
const mockCreateLocalUserMutate = jest.fn();

let mockUsersData: { users: unknown[] } = { users: [] };

jest.mock("../../../../hooks/useMediaQuery", () => ({
  useIsMobile: jest.fn(() => true),
}));

jest.mock("../../../../api/hooks/useAccessManagement", () => ({
  useUsers: () => ({ data: mockUsersData, isLoading: false, isError: false }),
  useSetUserActive: () => ({ mutate: mockSetActiveMutate, isPending: false }),
  useSetUserCanPack: () => ({
    mutate: mockSetCanPackMutate,
    isPending: false,
    isError: false,
  }),
  useCreateLocalUser: () => ({
    mutate: mockCreateLocalUserMutate,
    isPending: false,
    isError: false,
  }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const user = (overrides: Record<string, unknown>) => ({
  id: "id",
  displayName: "Name",
  email: "name@test.com",
  source: "Entra",
  isActive: true,
  canPack: false,
  groupIds: [],
  lastLoginAt: undefined,
  ...overrides,
});

const renderGrid = () =>
  render(
    <MemoryRouter>
      <UsersGrid />
    </MemoryRouter>,
  );

beforeEach(() => {
  jest.clearAllMocks();
  mockUsersData = { users: [] };
});

describe("UsersGrid — mobile cards", () => {
  it("renders a card with the user's name and email (no table)", () => {
    mockUsersData = {
      users: [user({ id: "a", displayName: "Alice", email: "alice@test.com" })],
    };
    renderGrid();

    expect(screen.getByText("Alice")).toBeInTheDocument();
    expect(screen.getByText("alice@test.com")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("navigates to the detail page when the name is tapped", () => {
    mockUsersData = { users: [user({ id: "a", displayName: "Alice" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: "Alice" }));

    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/users/a");
  });

  it("toggles active state from the card", () => {
    mockUsersData = {
      users: [user({ id: "a", displayName: "Alice", isActive: true })],
    };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: /toggle active alice/i }));

    expect(mockSetActiveMutate).toHaveBeenCalledWith(
      expect.objectContaining({ id: "a" }),
    );
  });

  it("omits the make-packer action on the card", () => {
    mockUsersData = {
      users: [user({ id: "a", displayName: "Alice", canPack: false })],
    };
    renderGrid();

    expect(
      screen.queryByRole("button", { name: /toggle can pack alice/i }),
    ).not.toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test src/components/pages/access/__tests__/UsersGrid.mobile.test.tsx --watchAll=false`
Expected: FAIL — a `<table>` is still rendered and the make-packer button is present (mobile branch not implemented yet).

- [ ] **Step 3: Add `useIsMobile` and the mobile card branch**

3a. In `frontend/src/components/pages/access/UsersGrid.tsx`, add the hook import after the existing hook import block (after line 9, the `useCreateLocalUser` import line). Add:

```tsx
import { useIsMobile } from "../../../hooks/useMediaQuery";
```

3b. Inside the component, just after `const navigate = useNavigate();` (line 53), add:

```tsx
  const isMobile = useIsMobile();
```

3c. Make the search wrapper full-width on mobile. Find (line 115):

```tsx
            <div className="flex-1 max-w-xs">
```

Replace with:

```tsx
            <div className="flex-1 min-w-0 md:max-w-xs">
```

3d. Replace the entire **"Grid"** block — the `<div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">` … `</div>` that wraps the table (currently lines 204–314, ending just before the `<Pagination` element) — with this conditional:

```tsx
      {/* Grid (table on desktop, cards on mobile) */}
      {isMobile ? (
        <div className="flex-1 overflow-auto space-y-2">
          {grid.pageItems.map((u) => (
            <div key={u.id} className="bg-white shadow rounded-lg p-4">
              <div className="flex items-start justify-between gap-2">
                <button
                  onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                  className="font-medium text-gray-900 text-left truncate"
                >
                  {u.displayName}
                </button>
                <div className="flex items-center gap-1 flex-shrink-0">
                  {u.source === "Local" ? (
                    <span className="rounded bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">
                      Local
                    </span>
                  ) : (
                    <span className="rounded bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600">
                      Entra
                    </span>
                  )}
                  {u.isActive ? (
                    <span className="rounded bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">
                      Active
                    </span>
                  ) : (
                    <span className="rounded bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700">
                      Disabled
                    </span>
                  )}
                </div>
              </div>
              <p className="text-sm text-gray-500 truncate">{u.email}</p>
              <p className="text-sm text-gray-500 mt-1">
                {u.groupIds?.length ?? 0} groups
                {u.canPack && (
                  <span className="ml-2 rounded bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700">
                    Packer
                  </span>
                )}
              </p>
              <div className="flex items-center justify-between mt-2">
                <span className="text-xs text-gray-500">
                  Last login: {formatLastLogin(u.lastLoginAt) || "—"}
                </span>
                <div className="flex items-center gap-3">
                  <button
                    onClick={() =>
                      u.id && navigate(`/admin/access/users/${u.id}`)
                    }
                    className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
                    aria-label={`Edit ${u.displayName}`}
                  >
                    <Edit className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() =>
                      u.id &&
                      setActive.mutate({
                        id: u.id,
                        request: new SetUserActiveRequest({
                          userId: u.id,
                          isActive: !u.isActive,
                        }),
                      })
                    }
                    disabled={setActive.isPending && setActive.variables?.id === u.id}
                    className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                    aria-label={`Toggle active ${u.displayName}`}
                  >
                    {u.isActive ? "Disable" : "Enable"}
                  </button>
                </div>
              </div>
            </div>
          ))}

          {grid.totalCount === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500">No users found.</p>
            </div>
          )}
        </div>
      ) : (
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50 sticky top-0 z-10">
                <tr>
                  <SortableHeader column="displayName" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Name
                  </SortableHeader>
                  <SortableHeader column="email" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Email
                  </SortableHeader>
                  <SortableHeader column="source" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Source
                  </SortableHeader>
                  <SortableHeader column="groups" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Groups
                  </SortableHeader>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Packer
                  </th>
                  <th scope="col" className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Status
                  </th>
                  <SortableHeader column="lastLoginAt" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Last login
                  </SortableHeader>
                  <th scope="col" className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {grid.pageItems.map((u) => (
                  <tr key={u.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                      <button
                        onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                        className="text-gray-900 hover:text-indigo-600 text-left"
                      >
                        {u.displayName}
                      </button>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{u.email}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {u.source === "Local" ? (
                        <span className="rounded bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-700">Local</span>
                      ) : (
                        <span className="rounded bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600">Entra</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{u.groupIds?.length ?? 0}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {u.canPack ? (
                        <span className="rounded bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-700">Packer</span>
                      ) : (
                        <span className="text-gray-400">—</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {u.isActive ? (
                        <span className="rounded bg-green-100 px-2 py-0.5 text-xs font-medium text-green-700">Active</span>
                      ) : (
                        <span className="rounded bg-red-100 px-2 py-0.5 text-xs font-medium text-red-700">Disabled</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{formatLastLogin(u.lastLoginAt)}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => u.id && setCanPack.mutate({ id: u.id, canPack: !u.canPack })}
                          disabled={setCanPack.isPending && setCanPack.variables?.id === u.id}
                          className={`text-sm ${u.canPack ? "text-indigo-600" : "text-gray-500"} hover:underline`}
                          aria-label={`Toggle can pack ${u.displayName}`}
                        >
                          {u.canPack ? "Packer ✓" : "Make packer"}
                        </button>
                        <button
                          onClick={() => u.id && navigate(`/admin/access/users/${u.id}`)}
                          className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
                          aria-label={`Edit ${u.displayName}`}
                        >
                          <Edit className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() =>
                            u.id &&
                            setActive.mutate({
                              id: u.id,
                              request: new SetUserActiveRequest({ userId: u.id, isActive: !u.isActive }),
                            })
                          }
                          disabled={setActive.isPending && setActive.variables?.id === u.id}
                          className={`text-sm ${u.isActive ? "text-red-600" : "text-green-600"} hover:underline`}
                          aria-label={`Toggle active ${u.displayName}`}
                        >
                          {u.isActive ? "Disable" : "Enable"}
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {grid.totalCount === 0 && (
              <div className="text-center py-8">
                <p className="text-gray-500">No users found.</p>
              </div>
            )}
          </div>
        </div>
      )}
```

(The `else` branch is the original table block verbatim — desktop output is unchanged.)

- [ ] **Step 4: Run the mobile test and the existing desktop test to verify both pass**

Run: `cd frontend && CI=true npx react-scripts test src/components/pages/access/__tests__/UsersGrid --watchAll=false`
Expected: PASS — both `UsersGrid.test.tsx` (desktop) and `UsersGrid.mobile.test.tsx` (mobile).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/access/UsersGrid.tsx frontend/src/components/pages/access/__tests__/UsersGrid.mobile.test.tsx
git commit -m "feat(access): mobile card layout for UsersGrid"
```

---

## Task 5: GroupsGrid — mobile card layout

**Files:**
- Modify: `frontend/src/components/pages/access/GroupsGrid.tsx`
- Test: `frontend/src/components/pages/access/__tests__/GroupsGrid.mobile.test.tsx` (new)

- [ ] **Step 1: Write the failing mobile test file**

Create `frontend/src/components/pages/access/__tests__/GroupsGrid.mobile.test.tsx`:

```tsx
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import GroupsGrid from "../GroupsGrid";

const mockNavigate = jest.fn();
const mockDeleteMutate = jest.fn();

let mockGroupsData: { groups: unknown[] } = { groups: [] };

jest.mock("../../../../hooks/useMediaQuery", () => ({
  useIsMobile: jest.fn(() => true),
}));

jest.mock("../../../../api/hooks/useAccessManagement", () => ({
  useGroups: () => ({ data: mockGroupsData, isLoading: false, isError: false }),
  useCatalogue: () => ({ data: { permissions: [] } }),
  useDeleteGroup: () => ({ mutate: mockDeleteMutate, isPending: false }),
}));

jest.mock("react-router-dom", () => ({
  ...jest.requireActual("react-router-dom"),
  useNavigate: () => mockNavigate,
}));

const group = (overrides: Record<string, unknown>) => ({
  id: "id",
  name: "Group",
  description: "A group",
  permissionCount: 0,
  memberCount: 0,
  parentCount: 0,
  ...overrides,
});

const renderGrid = () =>
  render(
    <MemoryRouter>
      <GroupsGrid />
    </MemoryRouter>,
  );

beforeEach(() => {
  jest.clearAllMocks();
  mockGroupsData = { groups: [] };
});

describe("GroupsGrid — mobile cards", () => {
  it("renders a card with the group name and a stats line (no table)", () => {
    mockGroupsData = {
      groups: [
        group({
          id: "a",
          name: "Admins",
          permissionCount: 12,
          memberCount: 5,
          parentCount: 2,
        }),
      ],
    };
    renderGrid();

    expect(screen.getByText("Admins")).toBeInTheDocument();
    expect(
      screen.getByText(/12 permissions · 5 members · 2 parents/i),
    ).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("navigates to the detail page when the name is tapped", () => {
    mockGroupsData = { groups: [group({ id: "a", name: "Admins" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: "Admins" }));

    expect(mockNavigate).toHaveBeenCalledWith("/admin/access/groups/a");
  });

  it("deletes a group from the card", () => {
    mockGroupsData = { groups: [group({ id: "a", name: "Admins" })] };
    renderGrid();

    fireEvent.click(screen.getByRole("button", { name: /delete admins/i }));

    expect(mockDeleteMutate).toHaveBeenCalledWith("a");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npx react-scripts test src/components/pages/access/__tests__/GroupsGrid.mobile.test.tsx --watchAll=false`
Expected: FAIL — a `<table>` is rendered and there is no `·`-separated stats line.

- [ ] **Step 3: Add `useIsMobile` and the mobile card branch**

3a. In `frontend/src/components/pages/access/GroupsGrid.tsx`, add the hook import after the `useClientGrid` import (after line 14):

```tsx
import { useIsMobile } from "../../../hooks/useMediaQuery";
```

3b. Inside the component, just after `const navigate = useNavigate();` (line 35), add:

```tsx
  const isMobile = useIsMobile();
```

3c. Make the search wrapper full-width on mobile. Find (line 74):

```tsx
            <div className="flex-1 max-w-xs">
```

Replace with:

```tsx
            <div className="flex-1 min-w-0 md:max-w-xs">
```

3d. Replace the entire **"Grid"** block — the `<div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">` … `</div>` that wraps the table (currently lines 102–172, ending just before the `<Pagination` element) — with this conditional:

```tsx
      {/* Grid (table on desktop, cards on mobile) */}
      {isMobile ? (
        <div className="flex-1 overflow-auto space-y-2">
          {grid.pageItems.map((g) => (
            <div key={g.id} className="bg-white shadow rounded-lg p-4">
              <div className="flex items-start justify-between gap-2">
                <button
                  onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                  className="font-medium text-gray-900 text-left"
                >
                  {g.name}
                </button>
                <div className="flex items-center gap-2 flex-shrink-0">
                  <button
                    onClick={() =>
                      g.id && navigate(`/admin/access/groups/${g.id}`)
                    }
                    className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
                    aria-label={`Edit ${g.name}`}
                  >
                    <Edit className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => g.id && deleteGroup.mutate(g.id)}
                    disabled={deleteGroup.isPending}
                    className="text-sm text-red-600 hover:underline"
                    aria-label={`Delete ${g.name}`}
                  >
                    Delete
                  </button>
                </div>
              </div>
              {g.description && (
                <p className="text-sm text-gray-500 line-clamp-2 mt-1">
                  {g.description}
                </p>
              )}
              <p className="text-xs text-gray-500 mt-2">
                {g.permissionCount} permissions · {g.memberCount} members ·{" "}
                {g.parentCount} parents
              </p>
            </div>
          ))}

          {grid.totalCount === 0 && (
            <div className="text-center py-8">
              <p className="text-gray-500">No groups found.</p>
            </div>
          )}
        </div>
      ) : (
        <div className="flex-1 bg-white shadow rounded-lg overflow-hidden flex flex-col min-h-0">
          <div className="flex-1 overflow-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50 sticky top-0 z-10">
                <tr>
                  <SortableHeader column="name" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Name
                  </SortableHeader>
                  <SortableHeader column="description" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Description
                  </SortableHeader>
                  <SortableHeader column="permissions" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Permissions
                  </SortableHeader>
                  <SortableHeader column="members" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Members
                  </SortableHeader>
                  <SortableHeader column="parents" sortBy={grid.sortBy} sortDescending={grid.sortDescending} onSort={grid.handleSort}>
                    Parents
                  </SortableHeader>
                  <th scope="col" className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">
                    Actions
                  </th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {grid.pageItems.map((g) => (
                  <tr key={g.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium">
                      <button
                        onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                        className="text-gray-900 hover:text-indigo-600 text-left"
                      >
                        {g.name}
                      </button>
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-500">{g.description}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{g.permissionCount}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{g.memberCount}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">{g.parentCount}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-right">
                      <div className="flex items-center justify-end gap-2">
                        <button
                          onClick={() => g.id && navigate(`/admin/access/groups/${g.id}`)}
                          className="p-2 text-gray-400 hover:text-indigo-600 rounded-lg hover:bg-gray-50"
                          aria-label={`Edit ${g.name}`}
                        >
                          <Edit className="w-4 h-4" />
                        </button>
                        <button
                          onClick={() => g.id && deleteGroup.mutate(g.id)}
                          disabled={deleteGroup.isPending}
                          className="text-sm text-red-600 hover:underline"
                          aria-label={`Delete ${g.name}`}
                        >
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {grid.totalCount === 0 && (
              <div className="text-center py-8">
                <p className="text-gray-500">No groups found.</p>
              </div>
            )}
          </div>
        </div>
      )}
```

(The `else` branch is the original table block verbatim — desktop output is unchanged.)

- [ ] **Step 4: Run the mobile test and the existing desktop test to verify both pass**

Run: `cd frontend && CI=true npx react-scripts test src/components/pages/access/__tests__/GroupsGrid --watchAll=false`
Expected: PASS — both `GroupsGrid.test.tsx` (desktop) and `GroupsGrid.mobile.test.tsx` (mobile).

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/access/GroupsGrid.tsx frontend/src/components/pages/access/__tests__/GroupsGrid.mobile.test.tsx
git commit -m "feat(access): mobile card layout for GroupsGrid"
```

---

## Task 6: Final validation

**Files:** none (verification only)

- [ ] **Step 1: Run all access-management tests together**

Run:
```bash
cd frontend && CI=true npx react-scripts test \
  src/components/access-management \
  src/components/pages/access \
  src/pages/__tests__/GroupDetailPage.test.tsx \
  src/pages/__tests__/UserDetailPage.test.tsx \
  --watchAll=false
```
Expected: all PASS.

- [ ] **Step 2: Build and lint (the required project gate)**

Run: `cd frontend && npm run build && npm run lint`
Expected: build succeeds (it is stricter than `tsc --noEmit`), lint reports no new errors.

- [ ] **Step 3: Manual responsive sweep at 375px**

Run the app and, at a 375px-wide viewport, walk every screen:
- `/admin/access` → Users tab: filter bar wraps, search is full-width, user cards render (name, source/status badges, email, group count, last-login, Edit + Disable/Enable). No make-packer button. No horizontal scroll.
- `/admin/access` → Groups tab: group cards render (name, Edit + Delete, 2-line description clamp, `N permissions · N members · N parents`). "New group" button visible.
- Open a group → Permissions/Included groups/Members pickers each show the TransferList as **tabs** (Available / Assigned with counts); switching tabs swaps the list; +/− buttons work; search filters the active tab. Header: title row 1, Save/Cancel row 2.
- Open a user → tight padding, header wraps, Group membership picker shows tabs, Save/Cancel reachable.
- Resize to ≥768px and confirm every screen reverts to the original table / two-column DnD layout.

- [ ] **Step 4: Final commit (only if Steps 1–2 surfaced fixes)**

```bash
git add -A
git commit -m "chore(access): mobile responsiveness validation fixes"
```

---

## Self-Review (completed during planning)

**Spec coverage:**
- §1 TransferList mobile tabs → Task 1 ✅ (tabs with counts, search above tabs filtering active tab, ItemRow unchanged, scroll reset on switch, DnD desktop-only, local `useState` default `'available'`).
- §2 GroupDetailPage header → Task 2 ✅ (`flex-wrap`, button group full-width on mobile; `md:ml-auto` added to preserve desktop — Deviation #4).
- §3 UserDetailPage padding + header → Task 3 ✅ (`p-3 sm:p-8`, header `flex-wrap min-w-0`; loading container updated too).
- §4 UsersGrid cards → Task 4 ✅ (card anatomy, name/edit→navigate, inline Disable/Enable, make-packer omitted, search full-width, filter bar & create form unchanged, shared Pagination).
- §5 GroupsGrid cards → Task 5 ✅ (card anatomy, name/edit→navigate, inline Delete, `·` stats line `text-xs text-gray-500`, 2-line clamp, search full-width, New group unchanged).
- "Files changed" table → all 5 source files covered; no backend/API/picker changes ✅.
- Out-of-scope items (Pagination, E2E, TransferList desktop tests) → respected; existing desktop tests kept green ✅.

**Deviations from spec** are listed in "Assumptions & Deviations" — the only material one is JS-conditional vs CSS `md:hidden` for the grids (Deviation #1), forced by the jsdom test constraint.

**Placeholder scan:** none — every code step contains complete content.

**Type consistency:** `renderRows(items, direction, grouped)` signature is used identically in mobile and desktop branches; `activeTab` union `"available" | "assigned"` consistent across `useState`, `switchTab`, and `tabClass`; DTO field names (`displayName`, `email`, `source`, `groupIds`, `canPack`, `isActive`, `lastLoginAt`, `permissionCount`, `memberCount`, `parentCount`) verified against `AppUserDto` / `GroupSummaryDto` in the generated client.
