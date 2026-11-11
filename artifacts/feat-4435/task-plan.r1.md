# Implementation Plan: ManufactureOrderFilters tests

Goal: raise `ManufactureOrderFilters.tsx` line coverage from 0% to >= 60% with RTL tests, no production changes.

### task: write-filter-tests
**Files:** Create `frontend/src/components/manufacture/list/__tests__/ManufactureOrderFilters.test.tsx`

- [ ] Step 1: Read `components/common/__tests__/CatalogAutocomplete.test.tsx` and `components/__tests__/JournalEntryForm.test.tsx` for mock/setup conventions (i18n, user-event).
- [ ] Step 2: Create the test file with mocks (`useManufactureSettingsQuery`, `CatalogAutocomplete`, `ResponsiblePersonCombobox`, `react-i18next` if no global setup) and helpers `renderFilters`, `expand`.
- [ ] Step 3: Add tests: (a) apply with all fields filled maps values, dates are `Date`; `onApplyFilters` called; (b) Enter in order-number input applies; (c) nothing entered -> all nine fields null; (d) type then clear product code -> `productCode: null`; (e) state select "" -> null, a value -> numeric enum; (f) "Ruční zásah" select true/false/"" -> true/false/null.
- [ ] Step 4: Add collapsed-toggle tests with fake timers: default summary text; click toggle, advance 50ms, assert `onFiltersChange` call values across three clicks documenting the stale-closure behaviour (comment referencing issue 4435); after apply summary shows "Aktivní filtry".
- [ ] Step 5: Add reset test: fill, expand, click "Vymazat filtry" -> all-null request, inputs cleared, `onApplyFilters` not called.
- [ ] Step 6: Run `cd frontend && npx jest src/components/manufacture/list --coverage --collectCoverageFrom=src/components/manufacture/list/ManufactureOrderFilters.tsx` (or `npm test -- --watchAll=false ...`); confirm pass and >= 60% lines. Adjust asserted toggle sequence to the observed actual behaviour.
- [ ] Step 7: Run `npm run lint` and `npm run build` in `frontend`; fix any issues in the new file only.
- [ ] Step 8: Commit `test(manufacture): cover ManufactureOrderFilters mapping, reset and toggle`.
