## Module
Marketing

## Finding
`frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` casts every API response to `any` before accessing its fields:

- Line 108: `(calendarQuery.data as any)?.actions`
- Line 122: `(listQuery.data as any)?.actions`
- Line 136: `(listQuery.data as any)?.totalPages ?? 1`
- Line 192: `(detailQuery.data as any)?.action`

The React Query hooks (`useMarketingCalendar`, `useMarketingActions`, `useMarketingAction`) return fully-typed responses from the generated NSwag client. Casting to `any` discards those types, meaning:
- Field renames in the backend (e.g. `startDate` vs `dateFrom`) are not caught by the compiler — the page already works around this at line 127 with `a.startDate ?? a.dateFrom` and line 199 with `a.startDate ?? a.dateFrom`, which is evidence the type confusion is already causing divergence.
- Bugs introduced by API contract changes are invisible at build time and surface only at runtime.

## Why it matters
The project auto-generates a TypeScript client on build precisely to catch contract mismatches at compile time. Bypassing it with `as any` defeats that investment. The existing `?? a.dateFrom` fallbacks (lines 127, 199) show the code is already silently handling an inconsistency between two field names — this is the direct consequence of losing type safety.

Principle violated: the project rule that the frontend TypeScript client provides safe, typed access to the API; `as any` is explicitly contrary to that intent.

## Suggested fix
Use the generated client's typed response interfaces directly. The `useMarketingActions` hook returns `GetMarketingActionsResponse`; `useMarketingCalendar` returns `GetMarketingCalendarResponse`; `useMarketingAction` returns `GetMarketingActionResponse`. Import those types, remove the `as any` casts, and fix the field name inconsistencies by aligning either the backend response field names or the frontend mapping.

---
_Filed by daily arch-review routine on 2026-09-10._
