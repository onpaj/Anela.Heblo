Plan saved to `artifacts/feat-arch-review-marketing-usemarketingcalend/plan.r1.md`.

**Summary**

The plan decomposes the refactor into 7 sequential tasks, ordered to keep the codebase coherent at commit boundaries:

1. Update `MarketingActionModal.test.tsx` to pin string-enum assertions (RED).
2. Drop the `ACTION_TYPE_TO_INT` test block.
3. Rewrite `useMarketingCalendar.ts` with generated `I*Request` types and remove all seven `as any` casts.
4. Narrow `CalendarEvent.actionType` to `MarketingActionType` in `fullcalendarAdapters.ts`.
5. Coordinated consumer update — `MarketingActionModal` (form state + options + handlers) and `MarketingCalendarPage.handleEventMove` — restoring a green build.
6. Delete the now-unused `ACTION_TYPE_TO_INT` constant.
7. Full validation gate (`npm run build`, `npm run lint`, `grep "as any"`, focused + full Jest, staging smoke test).

Each task lists exact file paths, the full code to write or replace, the verification command, expected output, and a commit message. The plan calls out the intermediate compile-broken state between Tasks 3–5 explicitly so executors don't chase phantom errors, and documents the class-constructor fallback in case TS rejects the plain-object payload.