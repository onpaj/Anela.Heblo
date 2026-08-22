# Code Review: calculation-mode-toggle-tests

## Summary
The implementation adds a complete `calculation-mode toggle` test describe block with four test cases that verify FR-4 functionality: defaults to batch-size mode, switches to ingredient mode when clicked, invokes calculateBySize in batch-size mode, and invokes calculateByIngredient in ingredient mode. All tests pass, lint is clean for this file, and the build succeeds. The implementation correctly reuses existing mocks and helpers and follows RTL best practices.

## Review Result: PASS

### task: calculation-mode-toggle-tests
**Status:** PASS

## Verification

**Test Execution (21 tests total, all passing):**
- ✓ 4 new calculation-mode toggle tests pass
- ✓ Full file: 21 passed, 21 total (12 computePercentage + 1 render + 3 batch-size fallback + 1 URL parameter + 4 calculation-mode toggle)
- ✓ No lint errors for ManufactureBatchCalculator.test.tsx
- ✓ npm run build succeeds without errors

**Spec Compliance:**
1. ✓ **Defaults to batch-size mode**: Test correctly asserts batch-size radio is checked, batch-size label present, ingredient label and combobox absent
2. ✓ **Switches to ingredient mode**: Test verifies clicking "Podle ingredience" unmounts batch-size input and mounts ingredient select + amount input (confirming unmounting, not disabled state, per ternary at lines 314–416)
3. ✓ **Batch-size calculation routing**: Test fills batch-size input (150g), clicks "Vypočítar", asserts `calculateBySize('SEMI001', 150)` called, asserts `calculateByIngredient` not called
4. ✓ **Ingredient calculation routing**: Test switches mode, selects ingredient (ING001), fills amount (30g), clicks button, asserts `calculateByIngredient('SEMI001', 'ING001', 30)` called, asserts `calculateBySize` not called

**Implementation Details:**
- ✓ Reuses prerequisite task mocks: `mockGetBatchTemplate`, `mockCalculateBySize`, `mockCalculateByIngredient`, `triggerProductSelect` without redefining
- ✓ Implements `templateWithIngredient` fixture with ingredients array for testing ingredient-mode paths
- ✓ Implements `renderWithSelectedProduct` helper that sets up mock, renders component, triggers product select, and waits for template to load
- ✓ Uses RTL best practices: `getByLabelText()` for wrapping-label radio selection, `getByRole()` for button and combobox queries, `getByPlaceholderText()` for inputs, `fireEvent.change()` and `fireEvent.click()` for interactions, `waitFor()` for async assertions
- ✓ Properly isolates test state with `mockClear()` where needed to verify only the correct calculation function is invoked
- ✓ Matches component structure exactly: radio inputs are wrapped in `<label>` elements (RTL `getByLabelText` pattern), ternary at lines 314–416 swaps entire input groups, both modes have "Vypočítar" button with respective handlers

**Acceptance Criteria Met:**
- ✓ All 4 required test cases present and correct
- ✓ Full test suite: 21 passed, 21 total (17 existing + 4 new)
- ✓ Lint clean for modified file
- ✓ Build passes
- ✓ Changes committed (commit ad567de)

## Overall Notes
Implementation is complete and correct. All functional requirements are verified. Tests properly isolate state and follow testing library best practices. No issues or improvements required.
