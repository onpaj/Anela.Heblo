# Specification: Type-safe API response handling in MarketingCalendarPage

## Summary
`MarketingCalendarPage.tsx` casts the responses of `useMarketingCalendar`, `useMarketingActions`, and `useMarketingAction` to `any` in four places, discarding the types the NSwag-generated client already provides. This spec replaces those casts with the generated `GetMarketingCalendarResponse`, `GetMarketingActionsResponse`, and `GetMarketingActionResponse` types, and removes the `a.startDate ?? a.dateFrom` / `a.endDate ?? a.dateTo` fallbacks the casts were hiding — the underlying API DTOs never had a `dateFrom`/`dateTo` field, so those fallbacks are dead code, not evidence of a real backend naming split.

## Background
The project generates a typed TypeScript API client from the backend's OpenAPI schema specifically so that contract changes are caught at compile time (see `docs/development/api-client-generation.md`). `MarketingCalendarPage.tsx` bypasses this by casting `calendarQuery.data`, `listQuery.data`, and `detailQuery.data` to `any` before reading fields off them (lines 108, 122, 136, 192 in the current file), and further types the `.map()` callback parameters as `any`.

Investigation of the generated client (`frontend/src/api/generated/api-client.ts`) and the backend contracts (`backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingActionDto.cs` and `MarketingActionCalendarDto.cs`) shows:
- The backend DTOs (`MarketingActionDto`, `MarketingActionCalendarDto`) expose only `StartDate` / `EndDate` (`DateTime` / `DateTime?`). There is no `DateFrom`/`DateTo` field anywhere in the backend.
- The generated TypeScript types mirror this exactly: `MarketingActionDto` and `MarketingActionCalendarDto` (in `api-client.ts`) expose only `startDate?: Date` / `endDate?: Date | undefined`. Neither generated type has ever had a `dateFrom`/`dateTo` field.
- Separately, the frontend has its own **locally-defined, differently-shaped** type also named `MarketingActionDto` (in `frontend/src/components/marketing/list/MarketingActionGrid.tsx`), which uses `dateFrom?: string | Date` / `dateTo?: string | Date` as its internal convention, plus the `CalendarEvent` type (in `fullcalendarAdapters.ts`) which uses `dateFrom` / `dateTo` as `YYYY-MM-DD` strings.

Conclusion: **there is no backend/frontend field-name inconsistency to reconcile.** The `?? a.dateFrom` / `?? a.dateTo` fallbacks in `MarketingCalendarPage.tsx` reference a property (`dateFrom`/`dateTo`) that has never existed on the API response types — they can only have been written because `as any` suppressed the compiler error that would otherwise have caught this immediately. The real (and unavoidable) naming difference is between the **generated API DTO** (`startDate`/`endDate`, `Date`) and the **local presentational types** (`dateFrom`/`dateTo`, `string | Date`) that `MarketingActionGrid` and the calendar adapters already use by design. The fix is entirely on the frontend mapping layer: map `startDate`/`endDate` from the typed API response into `dateFrom`/`dateTo` on the local presentational shapes, and delete the dead fallback expressions.

The same identifier `MarketingActionDto` is used for two structurally different types (the generated API DTO and the local grid-display DTO) imported into the same file. This name collision is almost certainly why `as any` was used in the first place — a naive typed import would produce a name clash or a confusing type migration. The fix must alias one of the two on import.

## Functional Requirements

### FR-1: Type `calendarQuery.data` with the generated response type
Replace `(calendarQuery.data as any)?.actions` with a properly typed access path using `GetMarketingCalendarResponse` (return type of `useMarketingCalendar`, which resolves to `MarketingActionCalendarDto[] | undefined` for `.actions`). The `.map()` callback parameter must be typed as the generated `MarketingActionCalendarDto` (imported under an alias, e.g. `ApiMarketingActionCalendarDto`, to avoid confusion with the local `MarketingActionDto`) instead of `any`.

**Acceptance criteria:**
- No `as any` remains on `calendarQuery.data` or within its `.map()` callback.
- `a.id`, `a.title`, `a.actionType`, `a.startDate`, `a.endDate`, `a.associatedProducts`, `a.outlookSyncStatus` are accessed through the generated `MarketingActionCalendarDto` type, with TypeScript enforcing their actual optionality (`id?`, `startDate?: Date`, etc.).
- `tsc`/`npm run build` reports no new errors and no remaining implicit `any` in this block.

### FR-2: Type `listQuery.data` with the generated response type
Replace `(listQuery.data as any)?.actions` with typed access via `GetMarketingActionsResponse` (return type of `useMarketingActions`), whose `.actions` field is `MarketingActionDto[] | undefined` (the generated one). Type the `.map()` callback parameter as the generated `MarketingActionDto`, imported under an alias (e.g. `ApiMarketingActionDto`) distinct from the local `MarketingActionDto` type already imported from `MarketingActionGrid` for the component's return type.

**Acceptance criteria:**
- No `as any` remains on `listQuery.data` or within its `.map()` callback.
- The two same-named `MarketingActionDto` types (generated vs. local/grid) coexist in the file via an explicit import alias, with no shadowing or implicit `any` fallback.
- `tsc`/`npm run build` reports no new errors.

### FR-3: Type `listQuery.data.totalPages`
Replace `(listQuery.data as any)?.totalPages ?? 1` with access through the same typed `GetMarketingActionsResponse` used in FR-2 (`totalPages?: number`).

**Acceptance criteria:**
- No `as any` remains at this line.
- `totalPages` resolves to `number`, defaulting to `1` when the response or field is undefined (unchanged runtime behavior).

### FR-4: Type `detailQuery.data` with the generated response type
Replace `(detailQuery.data as any)?.action` and `(detailQuery.data as any).action` with typed access via `GetMarketingActionResponse` (return type of `useMarketingAction`), whose `.action` field is `MarketingActionDto | undefined` (the generated one, same alias as FR-2).

**Acceptance criteria:**
- No `as any` remains in the `useEffect` block that populates `editingAction`.
- `a.id`, `a.title`, `a.description`, `a.actionType`, `a.startDate`, `a.endDate`, `a.associatedProducts`, `a.folderLinks` are accessed through the generated `MarketingActionDto` type.
- `tsc`/`npm run build` reports no new errors.

### FR-5: Remove the dead `dateFrom`/`dateTo` fallbacks and map dates correctly
At all three sites that currently read `a.startDate ?? a.dateFrom` / `a.endDate ?? a.dateTo` (calendar mapping ~line 112-113, list mapping ~line 127-128, detail effect ~line 199-200), remove the `?? a.dateFrom` / `?? a.dateTo` fallback since the generated types being introduced in FR-1/2/4 will make these a compile error (the property does not exist on the API DTOs) — this is the mechanism that makes the current bug visible at build time, matching the brief's stated goal.
- **Calendar mapping** (produces `CalendarEvent`, which needs `dateFrom`/`dateTo` as `YYYY-MM-DD` strings): keep the existing `instanceof Date` guard pattern but drop the dead else-branch fallback to `a.dateFrom`/`a.dateTo`; fall back to `''` directly when `startDate`/`endDate` is absent, e.g. `a.startDate ? formatDateStr(a.startDate) : ''`.
- **List mapping and detail effect** (produce the local grid `MarketingActionDto`, whose `dateFrom`/`dateTo` accept `string | Date`): assign `a.startDate` / `a.endDate` directly (`dateFrom: a.startDate`, `dateTo: a.endDate`) since the local type already accepts a `Date`, dropping the meaningless `?? a.dateFrom` / `?? a.dateTo`.

**Acceptance criteria:**
- No reference to `.dateFrom` or `.dateTo` remains on any value typed as a generated API response DTO (`MarketingActionDto`/`MarketingActionCalendarDto` from `api-client.ts`).
- Calendar view, list view, and the edit modal continue to display the same dates for existing data as before this change (verified by existing/adjusted component tests or manual check against current staging data).
- `tsc`/`npm run build` reports no new errors.

## Non-Functional Requirements

### NFR-1: No behavior change
This is a type-safety refactor. Rendered dates, pagination counts, calendar events, and the edit-modal prefill must be pixel-for-pixel identical to current behavior for all currently-exercised data shapes (i.e., real API responses, where `dateFrom`/`dateTo` never appear). No new runtime fallback path is introduced or removed in a way that changes output.

### NFR-2: Compile-time safety
After this change, `npm run build` (which runs `tsc`) must catch, at compile time, any future backend field rename or removal on `MarketingActionDto`, `MarketingActionCalendarDto`, `GetMarketingActionsResponse`, `GetMarketingCalendarResponse`, or `GetMarketingActionResponse` that this component depends on. No `as any`, `as unknown as X`, or untyped `.map((a: any) => ...)` may remain in the touched code paths.

### NFR-3: No backend changes required
Per the Background analysis, the backend's `StartDate`/`EndDate` naming is already consistent between `MarketingActionDto` and `MarketingActionCalendarDto`, and matches the generated TypeScript types exactly. No backend contract change, and no NSwag client regeneration, is required to implement this fix.

## Data Model
No new entities. Clarifying the three type families already in play, since their name collisions are the root cause being fixed:

| Type | Location | Shape | Used for |
|---|---|---|---|
| `MarketingActionDto` (generated) | `frontend/src/api/generated/api-client.ts` | `startDate?: Date`, `endDate?: Date \| undefined`, plus id/title/description/actionType/... | Raw API response payload for list (`GetMarketingActionsResponse.actions`) and detail (`GetMarketingActionResponse.action`) queries |
| `MarketingActionCalendarDto` (generated) | `frontend/src/api/generated/api-client.ts` | `startDate?: Date`, `endDate?: Date \| undefined`, plus id/title/actionType/associatedProducts/outlookSyncStatus | Raw API response payload for `GetMarketingCalendarResponse.actions` |
| `MarketingActionDto` (local) | `frontend/src/components/marketing/list/MarketingActionGrid.tsx` | `dateFrom?: string \| Date`, `dateTo?: string \| Date`, plus id/title/detail/actionType/associatedProducts/folderLinks/outlookSyncStatus | Presentational shape consumed by `MarketingActionGrid` and the edit modal |
| `CalendarEvent` | `frontend/src/components/marketing/calendar/fullcalendarAdapters.ts` | `dateFrom: string` (`YYYY-MM-DD`), `dateTo: string` (`YYYY-MM-DD`, inclusive), plus id/title/actionType/associatedProducts/outlookSyncStatus | Presentational shape consumed by `MarketingMonthCalendar` / FullCalendar |

`MarketingCalendarPage.tsx` is the mapping boundary between the two generated (API) types and the two local (presentational) types. The fix must import the generated `MarketingActionDto` and `MarketingActionCalendarDto` under aliases (e.g. `ApiMarketingActionDto`, `ApiMarketingActionCalendarDto`) to avoid colliding with the local `MarketingActionDto` already imported from `MarketingActionGrid`.

## API / Interface Design
No API surface changes. Internal-only change to `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`:
- Import `GetMarketingActionsResponse`, `GetMarketingCalendarResponse`, `GetMarketingActionResponse`, and the generated `MarketingActionDto` / `MarketingActionCalendarDto` (aliased) from `../../../api/generated/api-client`.
- `calendarQuery.data: GetMarketingCalendarResponse | undefined` (inferred from `useMarketingCalendar`'s return type — no cast needed once the hook's generic return type flows through `useQuery`).
- `listQuery.data: GetMarketingActionsResponse | undefined`.
- `detailQuery.data: GetMarketingActionResponse | undefined`.
- Each `.map()` callback parameter typed explicitly as the corresponding generated DTO instead of `any`.

## Dependencies
- `frontend/src/api/generated/api-client.ts` (already generated; contains all types needed — no regeneration required).
- `frontend/src/api/hooks/useMarketingCalendar.ts` (`useMarketingCalendar`, `useMarketingActions`, `useMarketingAction` — already return the correctly-typed generated responses via React Query's inferred generics; no hook changes required).
- `frontend/src/components/marketing/calendar/fullcalendarAdapters.ts` (`formatDateStr`, `CalendarEvent` — unchanged, reused as-is).
- `frontend/src/components/marketing/list/MarketingActionGrid.tsx` (local `MarketingActionDto` — unchanged, reused as-is).

## Out of Scope
- Any change to backend controllers, DTOs, or the OpenAPI schema (none is needed; see Background/NFR-3).
- Regenerating the NSwag client.
- Renaming the local `dateFrom`/`dateTo` convention in `MarketingActionGrid.tsx` or `CalendarEvent` to match `startDate`/`endDate` — that is a larger, unrelated refactor affecting other consumers of those types.
- Auditing or fixing `as any` casts in other marketing components or other pages of the app (e.g. `ImportFromOutlookModal.tsx`, `MarketingActionModal.tsx`) beyond the four call sites named in the brief.
- Adding new unit/component tests beyond what's needed to confirm NFR-1 (no behavior change); if the module has no existing test coverage for this page, manual verification against staging data is acceptable.

## Open Questions
None.

## Status: COMPLETE
