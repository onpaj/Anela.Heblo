# Specification: ManufactureOrderFilters test coverage

## Summary
Add React Testing Library unit tests for `frontend/src/components/manufacture/list/ManufactureOrderFilters.tsx` (0/78 lines covered, threshold 60%). Tests lock down the filter-to-request mapping, reset behaviour and the collapsed-header 3-state `manualActionRequired` quick toggle. No production code change is in scope.

## Background
`handleApplyFilters` builds a `GetManufactureOrdersRequest` from raw input state: strings use `|| null`, `state` uses `=== ""`, dates use `new Date(str)` unvalidated. A wrong mapping silently returns wrong order lists. Code inspection shows the quick toggle (collapsed header) calls `setManualActionRequiredInput(next)` then `setTimeout(() => handleApplyFilters(), 50)`; the timeout closes over the render-time `handleApplyFilters`, so it reads the PREVIOUS `manualActionRequiredInput` (stale closure). The expanded panel uses a `<select>` for the same field (not a toggle). Component is only used by `ManufactureOrderList.tsx`.

## Functional Requirements

### FR-1: Apply mapping
Expanded panel, fill fields, click "Použít filtry".
**Acceptance criteria:**
- `onFiltersChange` is called once with `orderNumber`, `state`, `dateFrom`/`dateTo` (Date instances), `responsiblePerson`, `productCode`, `erpDocumentNumber`, `manualActionRequired`, `lotNumber` set to entered values.
- `onApplyFilters` is called after `onFiltersChange`.
- Pressing Enter in a text input also applies.

### FR-2: Empty to null
**Acceptance criteria:**
- With nothing entered, all nine fields are `null`.
- Clearing product code after typing yields `productCode: null`.
- State select "" yields `state: null`.

### FR-3: Invalid date characterization
**Acceptance criteria:**
- A test documents current behaviour for an unparsable date (an `Invalid Date` instance is passed). jsdom date inputs sanitize invalid values to "", so this is covered at best via the type=date sanitization; if not reachable through the UI, the test asserts valid ISO dates map to `new Date(str)` and the gap is noted in a comment.

### FR-4: Manual action select
**Acceptance criteria:**
- Choosing "true"/"false"/"" in the "Ruční zásah" select then applying yields `true`/`false`/`null`.

### FR-5: Collapsed quick toggle
**Acceptance criteria:**
- Collapsed by default; header shows "Klikněte pro rozbalení filtrů", then "Aktivní filtry" after an applied filter.
- Clicking the toggle with fake timers and advancing 50ms triggers `onFiltersChange`. The test asserts the ACTUAL emitted value sequence over three clicks (stale-closure behaviour: null, true, false for clicks 1-3, i.e. one step behind the label) and carries a comment/`it` name flagging this as the known bug in issue 4435, so fixing it later is a deliberate test update.

### FR-6: Reset
**Acceptance criteria:**
- "Vymazat filtry" calls `onFiltersChange` with all nine fields `null`, clears inputs, and does not call `onApplyFilters`.

### FR-7: Coverage
- File line coverage >= 60% (target >= 90%).

## Non-Functional Requirements
### NFR-1: Performance
Tests run in under a few seconds, no real timers beyond the 50ms (use fake timers).
### NFR-2: Security
None; no secrets or network.

## Data Model
None. `GetManufactureOrdersRequest` from `api/hooks/useManufactureOrders.ts`.

## API / Interface Design
Component props: `onFiltersChange(filters)`, `onApplyFilters(): Promise<void>`. Test file: `frontend/src/components/manufacture/list/__tests__/ManufactureOrderFilters.test.tsx` (matches the `components/common/__tests__` convention).

## Dependencies
Jest + RTL + user-event (existing). Mocks: `useManufactureSettingsQuery`, `CatalogAutocomplete`, `ResponsiblePersonCombobox`, `react-i18next` (as needed).

## Out of Scope
- Fixing the stale-closure bug, `|| null` inconsistency, date validation (follow-up issues).
- E2E tests, process docs (no process change).

## Open Questions
None.

## Status: COMPLETE
