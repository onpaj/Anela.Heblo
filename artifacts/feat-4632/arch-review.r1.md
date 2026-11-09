# Architecture Review: SupplierAutocomplete unit test coverage

## Skip Design: true
Test-only change; no new or changed UI.

## Architectural Fit Assessment
Fits existing convention: component tests live in `frontend/src/components/common/__tests__/` (e.g. `ResponsiblePersonCombobox.test.tsx`), mock the data hook with `jest.mock`, render under RTL. The component only depends on `useSupplierSearch` (verified in `api/hooks/useSuppliers.ts`: debounced react-query hook returning `{ suppliers, isLoading }`), so mocking the hook removes the need for a QueryClientProvider, the API client and timers.

## Proposed Architecture

### Component Overview
One new file: `__tests__/SupplierAutocomplete.test.tsx`. Mocked module `../../../api/hooks/useSuppliers`; component under test rendered directly. No production code is touched.

### Key Design Decisions

#### Decision 1: Mock the hook, not the API client
**Options considered:** mock `getAuthenticatedApiClient` + QueryClientProvider; mock `useSupplierSearch`.
**Chosen approach:** mock `useSupplierSearch` with a `jest.fn` returning `{ suppliers, isLoading }`.
**Rationale:** avoids the 300ms debounce and async flakiness; the hook is not the unit under test.

#### Decision 2: Drive events with fireEvent / user-event as available
**Chosen approach:** use `fireEvent.keyDown` on input/options and `fireEvent.mouseDown(document.body)` for click-outside; assert `document.activeElement`.
**Rationale:** the component uses native focus() on `tabIndex=-1` divs, which jsdom supports; `mousedown` is the exact event the listener uses.

#### Decision 3: Options render only when open and (searchTerm || suppliers.length)
**Chosen approach:** the mock returns suppliers regardless of the term, so focusing the input (`onFocus` sets open) is enough to render the listbox for keyboard tests; type into the input via `fireEvent.change` for search-term states.

## Implementation Guidance

### Directory / Module Structure
`frontend/src/components/common/__tests__/SupplierAutocomplete.test.tsx` only.

### Interfaces and Contracts
Mock shape: `useSupplierSearch(term) => { suppliers: SupplierDto[]; isLoading: boolean }`. Selectors: `getByRole('combobox')`, `queryByRole('listbox')`, `getAllByRole('option')`.

### Data Flow
Keyboard: input keyDown ArrowDown -> querySelector first option -> focus. Option keyDown -> index +/-1 focus, index 0 ArrowUp -> input focus. Clear: change to "" with `value` set -> `onSelect(null)`. Outside click: document mousedown -> `setIsOpen(false)`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| `handleInputFocus` re-opens the list after Escape focusing input (Escape on option calls `setIsOpen(false)` then `input.focus()`; focus event may reopen) | Medium | Assert behaviour as observed after running; if input was already focused no focus event fires. Verify empirically and adjust assertion rather than component |
| Input blurred in `handleSupplierSelect` | Low | Assert `onSelect` and closed list, not focus |
| Module-level `mock` variable naming under jest hoisting | Low | Prefix with `mock` |

## Specification Amendments
None.

## Prerequisites
None.
