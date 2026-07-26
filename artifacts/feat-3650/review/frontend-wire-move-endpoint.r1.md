# Code Review: frontend-wire-move-endpoint

## Summary
The implementation matches the spec and arch review exactly: `useMoveMarketingAction` mirrors `useUpdateMarketingAction`'s structure and invalidation keys, and `handleEventMove` now calls the new mutation with only `id`/`startDate`/`endDate`, with the dead `calendarEvents.find(...)` lookup removed. The developer also removed the resulting fully-dead `updateMutation`/`useUpdateMarketingAction` declaration in this file — a reasonable, in-scope cleanup since it was an unused-vars build warning caused directly by this task's own swap, and the modal's independent hook instance was verified untouched.

## Review Result: PASS

### task: frontend-wire-move-endpoint
**Status:** PASS

## Overall Notes
- Verified `frontend/src/api/hooks/useMarketingCalendar.ts:144-175` — `useMoveMarketingAction` matches the spec's prescribed shape verbatim, including the three invalidation keys (`actions`, `calendar`, `action/{id}`).
- Verified `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx:207-216` — `handleEventMove` calls `moveMutation.mutate({ id, startDate: new Date(dateFrom), endDate: new Date(dateTo) })`, deps array is `[moveMutation]` only (no `calendarEvents`). `handleEventResize` (218-223) is unchanged, delegates to `handleEventMove` as required.
- Confirmed no residual references to `UpdateMarketingActionRequest`/`updateMutation`/`useUpdateMarketingAction` anywhere in `MarketingCalendarPage.tsx` (grep, zero matches).
- Confirmed `frontend/src/components/marketing/detail/MarketingActionModal.tsx` still imports and calls `useUpdateMarketingAction()` independently (line 6, 110) — the full-edit save path is untouched, satisfying the acceptance criterion and the arch review's scope boundary.
- Confirmed the generated client already contains `marketingCalendar_MoveMarketingAction` (api-client.ts:7825) and `MoveMarketingActionRequest` (api-client.ts:31286), so the backend dependency task's output is present and wired correctly.
- Ran `npm run build` — compiles successfully, no errors/warnings.
- Ran `npx eslint src/components/marketing/pages/MarketingCalendarPage.tsx src/api/hooks/useMarketingCalendar.ts` — zero output, i.e. 0 errors/0 warnings.
- All five acceptance criteria from the task spec are met.
