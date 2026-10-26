# Specification: Unit test coverage for PurchaseOrderForm planning-list logic

## Summary
`frontend/src/components/pages/PurchaseOrderForm.tsx` has 0% line coverage. Add Jest/React Testing Library tests covering supplier-name normalisation, planning-item filtering, and `handleAddFromPlanningList` success/fallback/catch paths. No production behaviour changes.

## Background
- `normalizeSupplierName` (inline in the component) lowercases, collapses whitespace, strips non-word characters, then applies `s.r.o.`, `a.s.`, `spol. s.r.o.` regexes.
- `filteredPlanningItems` (useMemo): no supplier selected or "show all" toggle -> all items; otherwise exact match on normalised names, then bidirectional substring match.
- `handleAddFromPlanningList` fetches `/api/catalog/materials-for-purchase` via `getAuthenticatedApiClient().http.fetch`, finds exact productCode match, otherwise builds a fallback material with `lastPurchasePrice: 0`; the catch path also builds a zero-price line. Successful add calls `removeItem` from `usePurchasePlanningList`.

## Functional Requirements

### FR-1: Supplier name normalisation behaviour
Verify through the rendered component (the helper is not exported) that planning items are shown/hidden correctly for Czech suffix variants.
**Acceptance criteria:**
- "Acme s.r.o." matches planning supplier "ACME sro", "Acme s. r. o.", "Acme s.r.o" (spaced/unspaced/case variants).
- "Foo a.s." matches "Foo as" / "FOO A.S.".
- "Bar spol. s.r.o." matches "Bar spol s.r.o.".
- Unrelated supplier names do not match.

### FR-2: filteredPlanningItems logic
**Acceptance criteria:**
- No supplier selected: all items listed.
- Supplier selected + "show all" toggle on: all items listed.
- Supplier selected: exact-normalised items listed; non-matching excluded.
- Substring behaviour is documented by a test (e.g. "Acme" vs "Acme Trading") as current behaviour, not changed.
- Empty-name supplier edge case characterised.

### FR-3: handleAddFromPlanningList
**Acceptance criteria:**
- API success with exact productCode match: line added with material price (rounded), quantity 1, item removed from planning list.
- API success without match: fallback material, unitPrice 0, item removed.
- Non-ok response / fetch rejection: catch path adds a zero-price line (characterise existing behaviour) and `console.error` is called (spied/silenced).
- Empty first line replaced rather than appended; non-empty first line gets a new line appended.

## Non-Functional Requirements
### NFR-1: Performance
Tests run in the normal Jest suite, each under a few seconds; no real network.
### NFR-2: Security
No secrets; fetch fully mocked.

## Data Model
None changed.

## API / Interface Design
No production API changes. Mock `../../api/client` (`getAuthenticatedApiClient` returning `{ baseUrl, http: { fetch } }`), `usePurchasePlanningList`, purchase order mutation/query hooks, and child components as needed.

## Dependencies
Jest, React Testing Library, existing test conventions in `frontend/src/components/pages/__tests__/`.

## Out of Scope
- Changing the normalisation or fallback behaviour (issue notes zero-price and substring false-positive risks; fix would be a separate issue).
- Exporting helpers solely for testing (unless architect decides otherwise).
- Submit/validation/edit-mode flows beyond what is needed for rendering.

## Open Questions
None.

## Status: COMPLETE
