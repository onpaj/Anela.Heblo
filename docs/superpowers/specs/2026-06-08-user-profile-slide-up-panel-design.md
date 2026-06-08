# User Profile Slide-Up Panel Design

**Date:** 2026-06-08
**Branch:** onpaj/auth-user-identity-linking

## Goal

Replace the centered full-screen modal overlay in `UserProfile.tsx` with a slide-up panel anchored to the sidebar's bottom user row. The panel overlays the sidebar nav content above it, closes on click-outside or toggle, and animates in/out with a slide-up + fade.

## Files Changed

- `frontend/src/components/auth/UserProfile.tsx` — main change: replace modal with slide-up panel
- `frontend/src/components/layout/Sidebar.tsx` — minor: add `relative` to both bottom section wrappers

## Panel Positioning

Both bottom section wrapper divs in `Sidebar.tsx` receive `relative` positioning. This makes them the nearest positioned ancestor for the absolute panel.

**Expanded sidebar** (w-64):
```
absolute bottom-full inset-x-0
```
Stretches left-to-right across the full bottom row width (including the collapse button). Covers nav items above it.

**Compact sidebar** (w-16):
```
absolute bottom-full left-0 w-60
```
Pops rightward from the 64px sidebar into the content area. Fixed width `w-60` (240px).

The sidebar has no `overflow-hidden` on its outer shell — only `<nav>` has `overflow-y-auto`. The absolutely positioned panel will correctly overlay the nav content without being clipped.

## Animation

Headless UI `Transition` v2 (already installed: `@headlessui/react ^2.2.7`):

```
enter:     "transition ease-out duration-200"
enterFrom: "opacity-0 translate-y-2"
enterTo:   "opacity-100 translate-y-0"
leave:     "transition ease-in duration-150"
leaveFrom: "opacity-100 translate-y-0"
leaveTo:   "opacity-0 translate-y-2"
```

Slides up 8px and fades in on open; reverses on close.

## Open / Close Behavior

- **Open:** click the trigger button (avatar or full-width user row)
- **Close (toggle):** click the trigger button again
- **Close (outside):** `mousedown` event listener via `useRef` wrapping the entire component (trigger + panel); fires `setShowPanel(false)` when click lands outside

No background overlay. No X button inside the panel.

## Panel Shell

```
absolute bottom-full ... z-10
rounded-t-xl shadow-lg bg-white border border-gray-100 overflow-hidden
```

- Rounded top corners only (`rounded-t-xl`) — the bottom visually connects to the trigger row
- Elevated shadow to separate it from nav content
- `overflow-hidden` on the panel itself to clip content to rounded corners
- `max-h-[80vh] overflow-y-auto` on the inner content container to handle overflow gracefully on small screens

## Panel Content

Identical to the current modal content — no sections removed or reordered:

1. Identity row (avatar, name, email, last login)
2. Roles section (id-token roles, blue chips)
3. Permissions section (DB permissions: amber super-user badge or emerald chips)
4. Groups section (DB groups, blue chips)
5. Logout button row

## State Rename

`showModal` → `showPanel` throughout `UserProfile.tsx` for semantic accuracy.

## Tests

No structural test changes required. The existing 6 tests in `UserProfile.test.tsx` click the trigger button and assert on panel content — both behaviors are identical. The `openModal` helper is renamed to `openPanel` for clarity.

## Out of Scope

- No changes to panel content or data sources
- No mobile-specific layout changes (mobile sidebar already hides behind a menu toggle; behavior unchanged)
- No animation on the sidebar collapse/expand itself
