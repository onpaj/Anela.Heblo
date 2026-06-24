# Access Management — Mobile Responsiveness

**Date:** 2026-06-24  
**Scope:** All screens under `/admin/access` — list pages (UsersGrid, GroupsGrid) and detail pages (GroupDetailPage, UserDetailPage), plus the shared TransferList component used by all pickers.  
**Target device:** iPhone 13 mini (375px viewport width). Breakpoint: `< 768px` (Tailwind `md`, matches existing `useIsMobile()` hook).

---

## Problem

The access management screens were designed desktop-first and are unusable on mobile:

1. **TransferList** — `grid grid-cols-2` renders two narrow columns at 375px; items are unreadable and the +/− buttons are too small to tap reliably.
2. **GroupDetailPage header** — back link + h1 title + Save + Cancel buttons all in one flex row overflow on small screens.
3. **UserDetailPage** — `p-8` outer padding consumes ~64px of a 375px screen.
4. **UsersGrid / GroupsGrid** — 8-column and 6-column tables are completely unusable on mobile; cells use `whitespace-nowrap` which prevents wrapping.

---

## Design

### 1. TransferList — tab-switched single pane on mobile

**File:** `frontend/src/components/access-management/TransferList.tsx`

On mobile (`useIsMobile()` → true), replace the two-column `grid grid-cols-2` layout with a tab-switched single pane:

- Two tabs above the list: **"Available (N)"** and **"Assigned (N)"** where N is the count of items in each side. Active tab is underlined in indigo (matching existing tab style in the app).
- The search bar sits above the tabs and filters whichever tab is currently active.
- The active tab renders its item list below. Items use the existing `ItemRow` component unchanged — same +/− buttons, badges, sublabels.
- Switching tabs resets scroll position to the top.
- `DndContext` and `DropZone` only render on desktop (when `useIsMobile()` is false). On mobile the drag sensors are not mounted — the +/− buttons remain the only interaction mechanism.
- Local tab state (`useState<'available' | 'assigned'>`) lives inside `TransferList`; it defaults to `'available'`.

On desktop (≥ 768px): layout is **unchanged** — the current two-column `grid grid-cols-2` with DnD.

No changes to the `TransferListProps` interface. No changes to any of the four picker components (PermissionPicker, GroupsPicker, MembersPicker, IncludedGroupsPicker) — the fix is entirely inside TransferList.

---

### 2. GroupDetailPage — header layout

**File:** `frontend/src/pages/GroupDetailPage.tsx`

**Current:** Single flex row: `[← Access management] [Edit group] .............. [Save] [Cancel]`

**Mobile fix:** Wrap to two rows using `flex-wrap`:
- Row 1: Back link + h1 title (left-aligned, full width on mobile)
- Row 2: Save + Cancel buttons (right-aligned via `ml-auto` or `justify-end` wrapper)

Implementation: change the outer header div from `flex items-center justify-between gap-4` to `flex flex-wrap items-center gap-x-4 gap-y-2`. The button group gets `w-full flex justify-end md:w-auto` so it takes full width on mobile and auto width on desktop.

The Name/Description `grid grid-cols-2` already renders fine — two inputs side-by-side is usable at 375px with the card's padding. No change needed there.

---

### 3. UserDetailPage — padding and header

**File:** `frontend/src/pages/UserDetailPage.tsx`

- Outer container: `p-8 max-w-5xl mx-auto` → `p-3 sm:p-8 max-w-5xl mx-auto`
- Header div `flex items-center gap-4`: add `flex-wrap min-w-0` so the h1 title wraps cleanly below the back link on narrow screens.
- Save/Cancel buttons at the bottom already sit at page bottom — no positional change, they naturally benefit from the padding fix.

---

### 4. UsersGrid — mobile card layout

**File:** `frontend/src/components/pages/access/UsersGrid.tsx`

On mobile, hide the table and render a card list instead. On desktop, hide the cards and show the existing table. Use `md:hidden` / `hidden md:block` CSS classes.

**Card anatomy (per user):**

```
┌─────────────────────────────────────────┐
│ Display Name (bold)    [Source] [Status] │
│ email@example.com (gray, truncated)      │
│ N groups  [Packer badge if canPack]      │
│ Last login: DD.MM.YYYY          [Edit ✏] │
│                        [Disable/Enable]  │
└─────────────────────────────────────────┘
```

- Tapping the display name OR the edit icon navigates to `/admin/access/users/:id`.
- "Disable" / "Enable" inline text button triggers `setActive` mutation directly (same as table row).
- "Make packer" / "Packer ✓" is omitted from the mobile card — this is an edge-case admin action that can be performed from the detail page. Keeping cards uncluttered.
- Cards use `bg-white shadow rounded-lg p-4 mb-2` consistent with existing card styles.
- The filter bar above the cards is unchanged — it already has `flex-wrap` and flows onto multiple rows on mobile. The `max-w-xs` search input becomes `flex-1 min-w-0` on mobile (full width).
- The "Create local operator" form stacks below the filters naturally via `flex-wrap`.
- Pagination component is shared and appears below both card and table layouts.

---

### 5. GroupsGrid — mobile card layout

**File:** `frontend/src/components/pages/access/GroupsGrid.tsx`

Same approach: `md:hidden` card list / `hidden md:block` table.

**Card anatomy (per group):**

```
┌─────────────────────────────────────────┐
│ Group Name (bold)           [Edit] [Del] │
│ Description text (gray, 2-line clamp)    │
│ 12 permissions · 5 members · 2 parents  │
└─────────────────────────────────────────┘
```

- Tapping name or edit icon navigates to `/admin/access/groups/:id`.
- Delete button triggers `deleteGroup.mutate` inline.
- Stats line uses `·` separator and `text-xs text-gray-500`.
- Filter bar: search input goes full-width; "New group" button sits beside it and is visible on both mobile and desktop — no change needed.

---

## Files changed

| File | Change |
|------|--------|
| `frontend/src/components/access-management/TransferList.tsx` | Mobile tab-switched pane (uses `useIsMobile`) |
| `frontend/src/pages/GroupDetailPage.tsx` | Header flex-wrap fix |
| `frontend/src/pages/UserDetailPage.tsx` | Padding + header wrap fix |
| `frontend/src/components/pages/access/UsersGrid.tsx` | Mobile card layout |
| `frontend/src/components/pages/access/GroupsGrid.tsx` | Mobile card layout |

No new files. No backend changes. No API changes. No changes to picker components (PermissionPicker, GroupsPicker, MembersPicker, IncludedGroupsPicker).

---

## Out of scope

- The `Pagination` component — inspect it if it needs mobile fixes in a follow-up.
- E2E tests — the nightly suite runs on staging which is desktop viewport; no E2E updates required.
- Unit tests for TransferList tab mode — the existing TransferList tests cover the desktop path and should continue to pass unchanged; a follow-up can add mobile tab coverage.
