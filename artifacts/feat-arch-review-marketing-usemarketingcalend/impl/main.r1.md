All tasks complete, final review APPROVED (no CRITICAL or HIGH issues; MEDIUM observations are all pre-existing, not introduced by this refactor).

---

# Implementation: Restore Type Safety in `useMarketingCalendar.ts`

## What was implemented

Removed all seven `(client as any).marketingCalendar_*` casts from the Marketing Calendar hook layer and restored full compile-time type checking. Normalized `actionType`/`folderType` wire values from integers to the string-enum form the backend already publishes (`"SocialMedia"`, `"Campaign"`, etc.), aligning the entire create/update flow with the NSwag-generated types end-to-end.

## Files created/modified

- `frontend/src/api/hooks/useMarketingCalendar.ts` — All 7 `as any` casts removed; local payload interfaces replaced with type aliases to `ICreateMarketingActionRequest`, `IUpdateMarketingActionRequest`, `IImportFromOutlookRequest`; typed ApiClient methods called directly; `new Create/Update/ImportFromOutlookRequest(...)` constructors used as TypeScript required class instances
- `frontend/src/components/marketing/detail/MarketingActionModal.tsx` — Option arrays retyped with `MarketingActionType`/`MarketingFolderType` enums; `resolveOptionValue` replaced with `resolveActionType`/`resolveFolderType` helpers; `FormState.actionType` and `FolderLinkInput.folderType` narrowed to enum types; all select handlers updated
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — `ACTION_TYPE_TO_INT` import removed; `handleEventMove` passes `event.actionType` directly
- `frontend/src/components/marketing/calendar/fullcalendarAdapters.ts` — `CalendarEvent.actionType: string` narrowed to `MarketingActionType`; `ACTION_TYPE_TO_INT` table deleted
- `frontend/src/components/marketing/detail/__tests__/MarketingActionModal.test.tsx` — Numeric assertions (`actionType: 0/99`, `folderType: 3`, `select.value "99"/"2"`) updated to string-enum assertions
- `frontend/src/components/marketing/calendar/__tests__/fullcalendarAdapters.test.ts` — `ACTION_TYPE_TO_INT` test block removed

## Tests

- **Modal tests** (39): All pass GREEN — previously failing 6 assertions now confirmed with `MarketingActionType.SocialMedia`, `MarketingActionType.Meeting`, `MarketingFolderType.Campaign`
- **Adapter tests** (17): All pass — `toFcEvent`, `fromFcDates`, `formatDateStr`, `ACTION_TYPE_COLORS` suites unaffected
- **Full suite** (2066): 2061 passed, 5 skipped — same baseline as pre-refactor, zero regressions

## How to verify

```bash
# No as any casts in the hook
grep -n "as any" frontend/src/api/hooks/useMarketingCalendar.ts  # → no output

# TypeScript build
cd frontend && npm run build  # → Compiled successfully

# Lint
cd frontend && npm run lint  # → clean

# Tests
CI=true npm test -- --watchAll=false  # → 2061 passed, 0 failed
```

Manual smoke test (per spec FR-3): exercise list, view, create, update, delete, and import-from-Outlook flows against staging; confirm `actionType` in request bodies is `"SocialMedia"` (string), not `0` (integer).

## Notes

The class-constructor fallback (`new CreateMarketingActionRequest(request)`) was needed — TypeScript requires class instances, not plain literals, for these generated method signatures. This is the documented fallback in the arch review.

Three pre-existing MEDIUM observations surfaced during final review (not introduced by this change):
1. `handleSubmit` omits `label` from folder-link payloads (not in `IMarketingFolderLinkRequest`)
2. `handleEventMove` omits `folderLinks`/`description` from drag-drop update payloads
3. `FOLDER_TYPE_OPTIONS` label/enum-name mapping looks like a placeholder (e.g., `General` → "Obrázky")

## PR Summary

Removes all seven `(client as any).marketingCalendar_*` casts that disabled compile-time type checking on the Marketing Calendar feature. The root cause was a mismatch between local payload interfaces (numeric `actionType`) and the generated NSwag client (string-enum `MarketingActionType`). The fix standardizes on string enums end-to-end: hook payload types are now aliases to the generated `I*Request` interfaces, consumer components build `MarketingActionType`/`MarketingFolderType` values directly, and the obsolete `ACTION_TYPE_TO_INT` translation table is deleted. The backend's `JsonStringEnumConverter` accepts both forms, so the wire-format change from `0` to `"SocialMedia"` is backward-compatible.

### Changes
- `frontend/src/api/hooks/useMarketingCalendar.ts` — 7 `as any` casts removed; payload types aliased to generated `I*Request` interfaces
- `frontend/src/components/marketing/detail/MarketingActionModal.tsx` — form state and submission aligned to `MarketingActionType`/`MarketingFolderType` string enums
- `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` — `handleEventMove` passes typed enum directly, drops numeric lookup
- `frontend/src/components/marketing/calendar/fullcalendarAdapters.ts` — `CalendarEvent.actionType` narrowed; `ACTION_TYPE_TO_INT` table deleted
- `frontend/src/components/marketing/detail/__tests__/MarketingActionModal.test.tsx` — numeric assertions updated to enum assertions
- `frontend/src/components/marketing/calendar/__tests__/fullcalendarAdapters.test.ts` — `ACTION_TYPE_TO_INT` test block removed

## Status
DONE