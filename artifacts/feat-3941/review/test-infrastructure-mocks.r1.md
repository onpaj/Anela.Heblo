# Code Review: test-infrastructure-mocks

## Summary
The implementation successfully restructures the two `jest.mock` factories in the test file to expose module-scoped function references that can be configured per test, and adds a `CatalogAutocomplete` mock that enables simulating manual product selection. All specification requirements are met, existing tests remain untouched and passing, and the scaffolding for future test cases is in place.

## Review Result: PASS

### task: test-infrastructure-mocks
**Status:** PASS

## Overall Notes
- Mock restructuring is correct: module-scoped `jest.fn()` refs (`mockGetBatchTemplate`, `mockCalculateBySize`, `mockCalculateByIngredient`) declared before their use in the mock factory, enabling per-test configuration without state leakage.
- `CatalogAutocomplete` mock correctly renders the value and exposes a testable select button that invokes the real `onSelect` prop, enabling future tests to simulate product selection workflows.
- `beforeEach` hook properly resets all mocks to `{ success: false }` and clears `mockAutocompleteProduct`, ensuring test isolation.
- `triggerProductSelect(product)` helper and `testProduct` fixture are correctly implemented and ready for use by follow-up tasks.
- All 10 `computePercentage` unit tests and the 1 smoke test remain unmodified and verified passing (11 total).
- Imports added for scaffolding (`MemoryRouter`, `Routes`, `Route`, `waitFor`) are present as specified and do not interfere with the existing test suite.
