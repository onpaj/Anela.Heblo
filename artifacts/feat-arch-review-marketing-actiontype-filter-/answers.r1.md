### Question 1
Confirm "add the filter" over "delete the dead code"

**Answer:** **Add the filter.** Proceed with the complete-the-wiring path as the spec assumes. Do not delete `ActionType` from `MarketingActionQueryCriteria` or the corresponding repository branch.

**Rationale:** Three pieces of evidence make "add" the unambiguously correct choice: (a) a non-clustered DB index `IX_MarketingActions_ActionType` already exists in migration `20260424095051_AddMarketingCalendar.cs:99–102`, which would not have been created if the filter were not intended; (b) `MarketingActionType` is a small, semantically meaningful enum (`SocialMedia`, `Blog`, `Newsletter`, `PR`, `Event`, `Meeting`) and the frontend already renders typed badges/labels per type in `MarketingActionGrid.tsx:20–36`, so filtering by type is a natural user-visible capability with zero new domain modelling; (c) the existing list-page filter bar (`MarketingActionFilters.tsx`) is the obvious host for the new control and recent commits show active marketing work, making this a low-risk additive change rather than dead-code excision.

### Question 2
Index on `MarketingAction.ActionType`

**Answer:** **No migration required — the index already exists.** Migration `20260424095051_AddMarketingCalendar.cs:99–102` creates `IX_MarketingActions_ActionType` as a non-clustered single-column index. Remove this open question from the spec and add an explicit note under NFR-1 confirming the index is already in place.

**Rationale:** Direct evidence from the existing migration shows the index was created when the `MarketingActions` table was first introduced. No further schema work, manual migration, or DBA action is needed to support the new filter at the documented latency target.

### Question 3
Filter UI placement and labelling

**Answer:** Use the Czech label **"Typ akce"** (no English label needed — the existing filter bar is Czech-only) and place the dropdown **as the first control in `MarketingActionFilters.tsx`**, to the left of the existing "Hledat název..." text input. The dropdown's "no filter" option label is **"Všechny typy"**. Use the Czech enum-value labels already defined in `MarketingActionGrid.tsx:29–36` for the dropdown options (`Sociální sítě`, `Blog`, `Newsletter`, `PR`, `Událost`, `Meeting`), centralizing them in a single shared constant exported from `MarketingActionGrid.tsx` (or a small new `marketingActionTypeLabels.ts` if a circular import would otherwise arise) so the badge column and the filter dropdown stay in sync. Style the dropdown to match the existing filter inputs: `border border-gray-300 rounded-md px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-500`. The "Zrušit filtry" reset button (already present) must also clear the action-type selection — extend `EMPTY_FILTERS` and `hasActiveFilters` accordingly. Do **not** introduce `useSearchParams`/URL state for this filter; the existing `MarketingFilters` state is component-state only, and broadening to URL state would touch four other controls and is out of scope for this finding.

**Rationale:** The Czech-only convention is already established by the surrounding placeholders ("Hledat název...", "Zrušit filtry") in the same component, so adding an English label would be inconsistent. Placing the type dropdown first reflects the typical "categorical → textual → temporal" filter ordering and matches how the action-type badge is the leftmost visual identifier in each grid row. Reusing the `ACTION_TYPE_LABELS` map keeps the dropdown and the grid badge in lockstep — a single source of truth is cheaper than two divergent maps. URL state is explicitly excluded because the page's existing filters do not use it; bringing it in here would be scope creep unrelated to the dead-code finding.
