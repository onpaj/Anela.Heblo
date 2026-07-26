### task: frontend-wire-move-endpoint

## Goal
Repoint the Marketing Calendar's drag/resize handlers at the newly generated `marketingCalendar_MoveMarketingAction` OpenAPI client method instead of the full-replacement `UpdateMarketingAction` endpoint, so that dragging/resizing a calendar event no longer silently wipes the action's folder links. Add a `useMoveMarketingAction` hook and swap the call site in `MarketingCalendarPage.tsx`.

**Dependency:** this task requires the backend task (`backend-move-use-case`) to be complete and built first, since `marketingCalendar_MoveMarketingAction` and the `MoveMarketingActionRequest` TS class are auto-generated from the backend's OpenAPI spec on build (`docs/development/api-client-generation.md`) — do not hand-write these into `frontend/src/api/generated/api-client`, that file is regenerated, not edited.

## Context
**Bug being fixed:** `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (`handleEventMove`/`handleEventResize`, lines ~207–230) currently builds an `UpdateMarketingActionRequest` from the in-memory `CalendarEvent` and calls the existing `updateMutation` (from `useUpdateMarketingAction`). Because the calendar only ever fetches lightweight `MarketingActionCalendarDto` objects (no `folderLinks` field), this payload omits `folderLinks`, and the backend's `UpdateMarketingActionHandler` treats the omitted value as "clear all folder links" — silently deleting them on every drag/resize. The new backend endpoint (`PATCH /api/MarketingCalendar/{id}/move`, exposed via the generated `marketingCalendar_MoveMarketingAction` client method) only accepts `id`/`startDate`/`endDate`, so it is structurally impossible for this call site to trigger the bug again.

**API hooks use absolute URLs** — construct as `${apiClient.baseUrl}${relativeUrl}` pattern is followed automatically by the generated client method (`client.marketingCalendar_MoveMarketingAction(...)`); no manual URL construction is needed here since it's a typed generated client call, matching the existing `useUpdateMarketingAction` hook's pattern in the same file.

**Query invalidation:** the new hook must invalidate the same three query keys as `useUpdateMarketingAction`'s `onSuccess`, so the calendar view still reflects the new date immediately: `[...QUERY_KEYS.marketingCalendar, "actions"]`, `[...QUERY_KEYS.marketingCalendar, "calendar"]`, `[...QUERY_KEYS.marketingCalendar, "action", id]`.

**Scope boundary — do not touch:** `useUpdateMarketingAction`/`updateMutation` remains wired to the full edit modal (`MarketingActionModal`) exactly as today. This task does not modify that flow, its component, or its hook. `handleEventResize` (lines ~225–230) already delegates to `handleEventMove` and needs no changes itself — swapping `handleEventMove`'s internals is sufficient.

**Dead code cleanup (explicitly called out in the architecture review):** the current `handleEventMove` does a `calendarEvents.find(...)` lookup purely to read `event.title`/`event.actionType`/`event.associatedProducts` off the in-memory `CalendarEvent`, needed only to satisfy `UpdateMarketingActionRequest`'s required fields. Since the new `MoveMarketingActionRequest` payload doesn't carry those fields, this lookup becomes dead code once the call site is swapped and must be removed, not left in place unused. Update the `useCallback` dependency array accordingly (drop `calendarEvents`, add the new mutation).

## Files to create/modify
- `frontend/src/api/hooks/useMarketingCalendar.ts` — MODIFIED. Add `useMoveMarketingAction` hook (new export, alongside existing `useUpdateMarketingAction`).
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — MODIFIED. Swap `handleEventMove`'s mutation call from `useUpdateMarketingAction`/`updateMutation` to the new `useMoveMarketingAction` hook; remove the now-dead `calendarEvents.find(...)` lookup and its `useCallback` dependency.

## Implementation steps
1. In `frontend/src/api/hooks/useMarketingCalendar.ts`, add a new hook mirroring `useUpdateMarketingAction`'s structure exactly (same `getAuthenticatedApiClient()` pattern, same query-invalidation targets):
   ```ts
   export const useMoveMarketingAction = () => {
     const queryClient = useQueryClient();
     return useMutation({
       mutationFn: async ({ id, startDate, endDate }: { id: number; startDate: Date; endDate?: Date }) => {
         const client = await getAuthenticatedApiClient();
         return await client.marketingCalendar_MoveMarketingAction(
           id,
           new MoveMarketingActionRequest({ id, startDate, endDate }),
         );
       },
       onSuccess: (_, { id }) => {
         queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.marketingCalendar, "actions"] });
         queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.marketingCalendar, "calendar"] });
         queryClient.invalidateQueries({ queryKey: [...QUERY_KEYS.marketingCalendar, "action", id] });
       },
     });
   };
   ```
   Import `MoveMarketingActionRequest` from the generated API client module (same import location as other generated request types already used in this file, e.g. `UpdateMarketingActionRequest`).
2. In `MarketingCalendarPage.tsx`:
   - Instantiate the new hook (e.g. `const moveMutation = useMoveMarketingAction();`) alongside the existing `updateMutation`.
   - In `handleEventMove` (currently ~lines 207–223), replace the `updateMutation.mutate(new UpdateMarketingActionRequest({...}))` call with `moveMutation.mutate({ id, startDate: new Date(dateFrom), endDate: new Date(dateTo) })`, passing only `id`/`startDate`/`endDate`.
   - Remove the `calendarEvents.find(...)` lookup that reads `title`/`actionType`/`associatedProducts` — nothing in the new payload needs them.
   - Update the `useCallback` dependency array for `handleEventMove`: drop `calendarEvents`, add `moveMutation` (or whatever the mutation variable is named).
   - Leave `handleEventResize` (~lines 225–230) unchanged — it already delegates to `handleEventMove`.
   - Leave `updateMutation`/`useUpdateMarketingAction` and the edit-modal save path completely untouched.
3. Run `npm run build` to confirm the generated client types (`MoveMarketingActionRequest`, `marketingCalendar_MoveMarketingAction`) resolve correctly against the backend task's output.
4. Run `npm run lint` to catch any stale references or dependency-array issues from the removed lookup.

## Tests to write
No new automated frontend test is mandated by the spec for this call-site swap (existing component/unit test coverage for this handler, if any, should still pass — do not add new component tests as this is out of scope per the spec; regression coverage lives in the backend handler tests from the other task). Manually verify (or via existing E2E suite if it covers the calendar):
- After a drag or resize, the network call observed is `PATCH /api/MarketingCalendar/{id}/move` with a body containing only `id`, `startDate`, `endDate` — no `title`, `associatedProducts`, or `folderLinks` keys.
- An action with existing folder links, dragged to a new date on the calendar, retains all folder links and product associations when subsequently opened in the edit modal.

## Acceptance criteria
- `handleEventMove`/`handleEventResize` no longer reference `UpdateMarketingActionRequest`/`updateMutation` anywhere in their code paths.
- Calendar view still reflects the new date range immediately after a drag/resize (query invalidation via `useMoveMarketingAction`'s `onSuccess` preserved, same keys as before).
- The `calendarEvents.find(...)` lookup is fully removed from `handleEventMove` (no unused variable/dead code left behind).
- `npm run build` and `npm run lint` both pass with no new errors/warnings.
- The full edit modal (`MarketingActionModal`) save path is verified unchanged — still uses `useUpdateMarketingAction`/`PUT {id}` with full payload including `folderLinks`/`associatedProducts`.
