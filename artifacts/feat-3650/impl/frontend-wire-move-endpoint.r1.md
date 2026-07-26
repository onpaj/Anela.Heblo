# Implementation: frontend-wire-move-endpoint

## What was implemented
Repointed the Marketing Calendar's drag/resize handler at the new `marketingCalendar_MoveMarketingAction` endpoint instead of the full-replacement `UpdateMarketingAction` endpoint, so dragging/resizing a calendar event no longer risks silently wiping the action's folder links. Added a `useMoveMarketingAction` hook and swapped the call site in `MarketingCalendarPage.tsx`. Removed the now-dead `calendarEvents.find(...)` lookup and, after re-checking the file, also removed the `updateMutation`/`useUpdateMarketingAction` declaration, which became fully dead code in this file once `handleEventMove` stopped using it (it produced an `@typescript-eslint/no-unused-vars` build warning that the initial pass missed).

## Files created/modified
- `frontend/src/api/hooks/useMarketingCalendar.ts` — added `useMoveMarketingAction` hook, mirroring `useUpdateMarketingAction`'s structure and invalidating the same three query keys (`actions`, `calendar`, `action/{id}`).
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — instantiated `moveMutation`; `handleEventMove` now calls `moveMutation.mutate({ id, startDate, endDate })` instead of building an `UpdateMarketingActionRequest`; removed the dead `calendarEvents.find(...)` lookup and its `useCallback` dependency; removed the now-fully-unused `updateMutation`/`useUpdateMarketingAction` import and declaration (the edit modal, `MarketingActionModal.tsx`, has its own independent instance of this hook and is untouched).

## Tests
No new automated test was mandated for this call-site swap (per the task spec — regression coverage lives in the backend handler tests from the other task). Verified:
- `npm run build` — compiles successfully, no warnings from either changed file.
- `npx eslint` scoped to both changed files — 0 errors, 0 warnings.

## How to verify
1. `cd frontend && npm run build` — compiles cleanly.
2. `npx eslint src/components/marketing/pages/MarketingCalendarPage.tsx src/api/hooks/useMarketingCalendar.ts` — no errors/warnings.
3. Inspect `handleEventMove` in `MarketingCalendarPage.tsx` — no reference to `UpdateMarketingActionRequest`/`updateMutation` remains.
4. Manually (or via E2E): drag an action with existing folder links to a new date, then reopen it in the edit modal — folder links and product associations should be unchanged.

## Notes
- `useUpdateMarketingAction`/`updateMutation` remains fully intact and in use in `MarketingActionModal.tsx` (the full edit modal's save path) — only the now-dead local declaration in `MarketingCalendarPage.tsx` was removed.
- `handleEventResize` required no changes — it already delegates to `handleEventMove`.

## PR Summary
Wired the Marketing Calendar's drag/resize handlers to the new date-only `marketingCalendar_MoveMarketingAction` endpoint via a new `useMoveMarketingAction` hook, replacing the old call into `UpdateMarketingAction` that silently cleared folder links on every drag/resize. Removed the dead code this swap left behind (`calendarEvents.find` lookup and the no-longer-referenced `updateMutation`).

### Changes
- `frontend/src/api/hooks/useMarketingCalendar.ts` — new `useMoveMarketingAction` hook
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — swapped `handleEventMove`'s mutation call, removed dead code

## Status
DONE
