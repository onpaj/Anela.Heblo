# Architecture Review: ManufactureOrderFilters tests

## Skip Design: true
Test-only change; no new UI.

## Architectural Fit Assessment
Fits existing convention: co-located `__tests__` folders with `*.test.tsx` (e.g. `components/common/__tests__/CatalogAutocomplete.test.tsx`). Per `docs/architecture/testing-strategy.md` FE unit tests use Jest + RTL. No production code touched (CLAUDE.md: surgical changes).

## Proposed Architecture

### Component Overview
Test file renders `ManufactureOrderFilters` with `jest.fn()` props. Child components that hit the network (`CatalogAutocomplete`, `ResponsiblePersonCombobox`) and `useManufactureSettingsQuery` are mocked with `jest.mock`; the autocomplete mock renders a plain `<input>` wired to `onSelect` so product code can be typed and cleared. Collapsed view renders its own `CatalogAutocomplete` too, so the mock must tolerate two instances (only one is mounted at a time).

### Key Design Decisions

#### Decision 1: Characterize, do not fix, the stale-closure toggle
**Chosen approach:** assert current behaviour with an explicit comment referencing issue 4435.
**Rationale:** the issue is a coverage gap; fixing is a behaviour change and out of scope. Adding a failing test would break CI.

#### Decision 2: Fake timers for the 50ms auto-apply
Use `jest.useFakeTimers()` + `act(() => jest.advanceTimersByTime(50))` and `userEvent.setup({ advanceTimers: jest.advanceTimersByTime })`.

#### Decision 3: Translation
Component uses `useTranslation` only for state labels; use real i18n if a test setup exists, else mock `react-i18next` with `t: k => k`. Select state by option value, not label.

## Implementation Guidance

### Directory / Module Structure
- Create `frontend/src/components/manufacture/list/__tests__/ManufactureOrderFilters.test.tsx`.

### Interfaces and Contracts
Assert with `expect.objectContaining` or full object equality on all nine keys. Dates: `expect(arg.dateFrom).toEqual(new Date("2026-01-15"))`.

### Data Flow
click -> `setState` + `onFiltersChange(filters)` -> `await onApplyFilters()`.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Stale-closure assertions encode a bug | Low | Name/comment test clearly |
| Fake timers + user-event hang | Medium | `advanceTimers` option, or use `fireEvent` for toggle |
| Invalid Date unreachable via type=date input in jsdom | Low | Document in test comment |
| Mock module paths wrong | Low | Mirror relative import paths used in component |

## Specification Amendments
None.

## Prerequisites
None.
