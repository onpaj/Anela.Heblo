# Specification: Unit Tests for PurchasePlanningListContext

## Summary

`PurchasePlanningListContext` has two silent no-op guards in `addItem` — a duplicate check and a 20-item capacity cap — that are entirely uncovered by tests, leaving the context at 54.2% line coverage against a 60% threshold. This feature adds a dedicated unit-test file that covers both guards, plus the happy-path operations (`addItem` success, `removeItem`, `clearList`), bringing coverage above threshold and preventing regressions in the core purchase-planning workflow.

## Background

The purchase planning list lets buyers collect materials for a purchase order. `addItem` silently no-ops when a material with the same `productCode` already exists or when the list has reached `PURCHASE_PLANNING_LIST_MAX_ITEMS` (20). Because the caller receives no feedback, correctness of these guards is critical: a broken duplicate check allows the same material twice; an off-by-one cap could drop the last allowed item. The existing test file (`frontend/src/contexts/__tests__/PlanningListContext.test.tsx`) targets a different context (`PlanningListContext`, not `PurchasePlanningListContext`) and provides zero coverage of the file under review.

## Functional Requirements

### FR-1: Duplicate-guard test

A test must verify that calling `addItem` twice with a material that has the same `code` results in exactly one item in the list.

**Acceptance criteria:**
- After two `addItem` calls with identical `code` values, `items.length === 1`.
- The single retained item has the field values from the first call (no overwrite).

### FR-2: Max-capacity-guard test

A test must verify that adding a 21st distinct material to a list already containing 20 items is silently ignored.

**Acceptance criteria:**
- After adding 20 distinct materials, `items.length === 20`.
- Adding a 21st distinct material leaves `items.length === 20` and the 21st item is absent from `items`.

### FR-3: Happy-path addItem test

A test must verify that adding a single material to an empty list produces a list with exactly one correctly-populated item.

**Acceptance criteria:**
- `items.length === 1` after one `addItem` call.
- The item's `productCode`, `productName`, `supplier`, and `supplierCode` match the values passed to `addItem`.
- `hasItems` is `true`.
- The item's `addedAt` field is a `Date` instance (exact value need not be asserted).

### FR-4: removeItem on existing item

A test must verify that `removeItem` with a `productCode` that exists removes that item.

**Acceptance criteria:**
- After adding an item and calling `removeItem` with its code, `items.length === 0`.
- `hasItems` is `false`.

### FR-5: removeItem on non-existent code

A test must verify that `removeItem` with a code not present in the list is a no-op.

**Acceptance criteria:**
- The list length is unchanged after `removeItem` is called with an unknown code.
- No error is thrown.

### FR-6: clearList test

A test must verify that `clearList` empties a non-empty list.

**Acceptance criteria:**
- After adding at least two items and calling `clearList`, `items.length === 0`.
- `hasItems` is `false`.

### FR-7: usePurchasePlanningList throws outside provider

A test must verify that consuming the context outside its provider throws an error with the correct message.

**Acceptance criteria:**
- Rendering a consumer component without `PurchasePlanningListProvider` throws `"usePurchasePlanningList must be used within a PurchasePlanningListProvider"`.

## Non-Functional Requirements

### NFR-1: Performance

Tests run as part of the standard Jest suite (`npm run test`). No individual test should require real timers or async delays; all operations are synchronous state updates testable with React Testing Library's `act` or `fireEvent`.

### NFR-2: Coverage

After this feature is merged, line coverage for `frontend/src/contexts/PurchasePlanningListContext.tsx` must meet or exceed the project's 60% threshold as measured by the CI coverage run.

### NFR-3: Test isolation

Each test must use a fresh provider instance (no shared state between tests). Using a `renderWithProvider` helper that wraps `PurchasePlanningListProvider` satisfies this requirement.

### NFR-4: No production code changes

This task is test-only. The implementation file `frontend/src/contexts/PurchasePlanningListContext.tsx` must not be modified.

## Data Model

No new data model. The existing types from the context file are relevant:

- `PurchasePlanningListItem` — `{ productCode: string; productName: string; supplier: string; supplierCode?: string; addedAt: Date }`
- `addItem` input — `{ code: string; name: string; supplier: string; supplierCode?: string }`
- `PURCHASE_PLANNING_LIST_MAX_ITEMS = 20`

## API / Interface Design

**New file:** `frontend/src/contexts/__tests__/PurchasePlanningListContext.test.tsx`

The file must:
1. Import `PurchasePlanningListProvider` and `usePurchasePlanningList` from `../PurchasePlanningListContext`.
2. Define a `TestComponent` that exposes all context operations via buttons and displays `items.length` and `hasItems` via `data-testid` attributes — following the same pattern used in `PlanningListContext.test.tsx`.
3. Define a `renderWithProvider` helper that wraps `TestComponent` in `PurchasePlanningListProvider`.
4. Implement one `describe` block with one `it` per FR above (FR-1 through FR-7).

For FR-2 (max-capacity), the `TestComponent` must support adding items with arbitrary codes. One approach: expose an input and a button so the test can set the code before clicking. Another approach: render a pre-seeded provider by initialising state through repeated `addItem` calls in the test body using distinct codes programmatically — acceptable as long as tests remain readable. The implementation choice is left to the developer; whatever approach is chosen must not require modifying the production context file.

## Dependencies

- React Testing Library (`@testing-library/react`) — already installed.
- `jest` — already configured.
- No new npm packages required.

## Out of Scope

- Modifying `PurchasePlanningListContext.tsx` (guards, capacity constant, or any logic).
- Adding user-visible feedback (toast, error message) when a no-op occurs — that is a separate UX decision.
- E2E tests for the purchase planning list.
- Testing components that consume the context (only the context itself is in scope).
- Testing `addItem` with optional `supplierCode` omitted vs. provided (covered implicitly by FR-3 happy path; exhaustive permutations are out of scope).

## Open Questions

None.

## Status: COMPLETE
