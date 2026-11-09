# Specification: SupplierAutocomplete unit test coverage

## Summary
Add React Testing Library unit tests for `frontend/src/components/common/SupplierAutocomplete.tsx` (currently 0/65 lines covered). Tests lock in keyboard navigation, the `onSelect(null)` clear path and click-outside close. No production code changes.

## Background
The component is used in purchase order forms (`PurchaseOrderHeader.tsx`). A keyboard regression would block keyboard-only users; a regression of the clear path would leave stale supplier values in forms. The brief comes from a weekly coverage-gap routine.

## Functional Requirements

### FR-1: Test file and harness
Create `frontend/src/components/common/__tests__/SupplierAutocomplete.test.tsx`, mocking `../../../api/hooks/useSuppliers` (`useSupplierSearch`) so no network/debounce is involved.
**Acceptance criteria:**
- Tests run under `npm test` with no network access.
- Mock returns `{ suppliers, isLoading }` controllable per test.

### FR-2: Keyboard navigation
**Acceptance criteria:**
- ArrowDown in the input with suppliers available opens the list and focuses the first `role="option"`.
- ArrowDown with no suppliers does not move focus.
- ArrowDown/ArrowUp on options moves focus to next/previous option; ArrowDown on last option is a no-op.
- ArrowUp on option index 0 returns focus to the input.
- Enter and Space on an option call `onSelect(supplier)` and close the list.
- Escape in input closes the list; Escape on an option closes the list and focuses the input.

### FR-3: Clear path
**Acceptance criteria:**
- Clearing the input when `value` is set calls `onSelect(null)` once.
- Clearing the input when `value` is null does not call `onSelect`.
- Typing a non-empty value does not call `onSelect`.

### FR-4: Click-outside
**Acceptance criteria:**
- `mousedown` on `document.body` closes an open dropdown (`aria-expanded` false, listbox removed).
- `mousedown` inside the input or the dropdown does not close it.
- Listener is removed on unmount (spy on `removeEventListener`).

### FR-5: Supporting behaviour (coverage of remaining lines)
**Acceptance criteria:**
- Renders `value.name` in input; resets when value becomes null.
- Click on an option calls `onSelect` and sets input text.
- Loading, empty ("Žádní dodavatelé nenalezeni") and hint states render; error text and disabled state render.

## Non-Functional Requirements

### NFR-1: Performance
Suite runs in a few seconds; no real timers or sleeps.

### NFR-2: Security
N/A.

## Data Model
`SupplierDto` fixtures (id, name, code, email, phone, note) built inline.

## API / Interface Design
No production interface changes. Component props: `value`, `onSelect`, `placeholder`, `error`, `className`, `disabled`.

## Dependencies
Jest, React Testing Library (`@testing-library/react`, `user-event` if present), existing mock pattern from `ResponsiblePersonCombobox.test.tsx`.

## Out of Scope
Changing component behaviour; E2E tests; the debounce logic in `useSupplierSearch`; fixing `aria-selected` or other a11y issues.

## Open Questions
None.

## Status: COMPLETE
