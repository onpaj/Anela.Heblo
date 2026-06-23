# Architecture Review: Unit Tests for PurchasePlanningListContext

## Skip Design: true

## Architectural Fit Assessment

This feature is test-only. It adds a new test file under `frontend/src/contexts/__tests__/` with zero production code changes. The pattern is already established: `PlanningListContext.test.tsx` in the same directory tests a structurally identical context (`PlanningListContext`) using the same `@testing-library/react` stack and the same `renderWithProvider` + `TestComponent` approach.

`PurchasePlanningListContext` is structurally a superset of `PlanningListContext` — same guards, same capacity constant, same state shape — with two additional fields (`supplier`, `supplierCode`) on items and on the `addItem` input. The new test file is a direct port of the existing pattern with those extra fields filled in.

The only non-trivial challenge is FR-2 (max-capacity test): the existing `TestComponent` pattern hard-codes two specific items via fixed buttons, which cannot drive 20 distinct `addItem` calls. This is resolved by adding a controlled input + button to `TestComponent` so tests can inject arbitrary codes programmatically — or, equivalently, by calling `addItem` via `act` in a loop from the test body after gaining access to the context through a ref. The controlled-input approach is preferred because it keeps tests interacting through the rendered DOM rather than reaching into React internals.

## Proposed Architecture

### Component Overview

```
frontend/src/contexts/
  __tests__/
    PlanningListContext.test.tsx          (existing — DO NOT TOUCH)
    PurchasePlanningListContext.test.tsx  (NEW)
  PurchasePlanningListContext.tsx         (existing production file — DO NOT TOUCH)
```

The new test file is entirely self-contained. It imports only from `../PurchasePlanningListContext` and from `@testing-library/react`. No shared test utilities, no new helpers in other directories.

### Key Design Decisions

#### Decision 1: TestComponent exposes a controlled code input for FR-2

**Options considered:**
- Option A: `TestComponent` exposes a text input whose value is used as the `code` argument when an "Add" button is clicked. Tests drive 20 distinct `addItem` calls by setting `input.value` and clicking the button in a loop.
- Option B: `TestComponent` exposes a ref to the raw context so tests call `addItem` directly via `act(() => contextRef.current.addItem(...))`.
- Option C: Render the provider with pre-seeded children by calling `addItem` inside a `useEffect` with a dependency array — fragile and asynchronous.

**Chosen approach:** Option A — controlled input + button.

**Rationale:** Option A is the purest RTL approach (tests interact through the same DOM surface a user would). Option B bypasses the rendered tree and couples tests to React internals. Option C introduces async timing. The controlled input adds minimal complexity to `TestComponent` and keeps all test assertions driven by `screen` queries, consistent with the pattern in `PlanningListContext.test.tsx`.

#### Decision 2: One describe block, one it per FR

**Options considered:**
- Flat `it` blocks at module scope.
- Single `describe("PurchasePlanningListContext")` wrapping all `it` blocks.
- Nested `describe` blocks grouped by method (`addItem`, `removeItem`, `clearList`).

**Chosen approach:** Single `describe("PurchasePlanningListContext")` with flat `it` blocks, one per FR.

**Rationale:** Matches `PlanningListContext.test.tsx` exactly. Nested describes are not used in the existing suite; introducing them here would be an inconsistency that provides no benefit for seven tests.

#### Decision 3: No shared state between tests

**Options considered:**
- `beforeEach` that calls `renderWithProvider` once and stores the result.
- Each `it` calls `renderWithProvider()` independently.

**Chosen approach:** Each `it` calls `renderWithProvider()` independently.

**Rationale:** React Testing Library's `render` cleans up after each test automatically (via `afterEach(cleanup)`). Independent calls per test are idiomatic RTL and are what `PlanningListContext.test.tsx` already does.

## Implementation Guidance

### Directory / Module Structure

Create exactly one file:

```
frontend/src/contexts/__tests__/PurchasePlanningListContext.test.tsx
```

No other files are created or modified.

### Interfaces and Contracts

**Imports required:**
```ts
import React from "react";
import { render, screen, fireEvent } from "@testing-library/react";
import {
  PurchasePlanningListProvider,
  usePurchasePlanningList,
} from "../PurchasePlanningListContext";
```

**TestComponent contract:**

`TestComponent` must render:
- `data-testid="has-items"` — text `"yes"` or `"no"` based on `hasItems`
- `data-testid="items-count"` — text equal to `items.length`
- `data-testid="item-{productCode}"` — one per item in the list
- `data-testid="code-input"` — a text `<input>` whose current value is used as `code` when the "Add" button is clicked
- `data-testid="add-item"` — button that calls `addItem` using the current input value and fixed values for `name`, `supplier`, and optionally `supplierCode`
- `data-testid="clear-list"` — button that calls `clearList()`
- For each rendered item: a "Remove" button that calls `removeItem(item.productCode)`

**renderWithProvider helper:**
```ts
const renderWithProvider = () =>
  render(
    <PurchasePlanningListProvider>
      <TestComponent />
    </PurchasePlanningListProvider>
  );
```

**addItem input shape (production context):**
```ts
{ code: string; name: string; supplier: string; supplierCode?: string }
```
Tests must supply all required fields (`code`, `name`, `supplier`). `supplierCode` is optional and may be omitted or supplied as needed.

**Item field mapping (production context):**
| addItem arg | stored as |
|---|---|
| `code` | `productCode` |
| `name` | `productName` |
| `supplier` | `supplier` |
| `supplierCode` | `supplierCode` |

FR-3 requires asserting `productCode`, `productName`, `supplier`, and `supplierCode` from `items[0]` via the context — these are not DOM-visible by default. The test can either (a) expose them via additional `data-testid` spans in `TestComponent`, or (b) access the items array through the context directly. Approach (a) is preferred for consistency with the DOM-first RTL pattern. Add `data-testid="item-{productCode}-name"`, `data-testid="item-{productCode}-supplier"`, etc. as spans inside each list item if field-level assertions are needed beyond what `data-testid="item-{productCode}"` provides.

### Data Flow

**FR-1 (duplicate guard):**
```
fireEvent.change(input, {target: {value: "MAT01"}})
fireEvent.click(addButton)  → addItem({code:"MAT01",...}) → list=[item1]
fireEvent.click(addButton)  → addItem({code:"MAT01",...}) → duplicate check hits → list=[item1]
assert items-count = 1
```

**FR-2 (max-capacity guard):**
```
for i in 1..20:
  fireEvent.change(input, {target: {value: `MAT${i}`}})
  fireEvent.click(addButton)
assert items-count = 20
fireEvent.change(input, {target: {value: "MAT21"}})
fireEvent.click(addButton)  → capacity check hits → list unchanged
assert items-count = 20
assert item-MAT21 not in document
```

**FR-7 (outside provider):**
```ts
const consoleSpy = jest.spyOn(console, "error").mockImplementation();
expect(() => render(<TestComponent />)).toThrow(
  "usePurchasePlanningList must be used within a PurchasePlanningListProvider"
);
consoleSpy.mockRestore();
```
The `console.error` spy is necessary because React logs the thrown error to the console before re-throwing, which would pollute test output. This mirrors the pattern used in `PlanningListContext.test.tsx` line 124.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| FR-2 loop (20 fireEvent calls) is slow | Low | fireEvent is synchronous; 20 calls complete in milliseconds. No mitigation needed. |
| Existing max-capacity test in PlanningListContext.test.tsx is broken (lines 104-119) — it only adds one item due to the duplicate guard | Low | That file is out of scope. Do not fix it; do not reference its max-capacity test as a model. Write FR-2 correctly using the controlled-input approach described above. |
| addedAt field assertion — exact Date value is non-deterministic | Low | Assert `expect(item.addedAt).toBeInstanceOf(Date)` only, as specified in FR-3. |
| supplierCode is optional — undefined vs. omitted distinction | Low | FR-3 scope is a single happy-path call. Supply supplierCode in FR-3 to fully exercise the field; omitting it is also acceptable since the spec says exhaustive permutations are out of scope. |

## Specification Amendments

**Amendment 1 — Broken max-capacity test in PlanningListContext.test.tsx**

The spec cites `PlanningListContext.test.tsx` as the reference pattern. Lines 104-119 of that file contain a max-capacity test that is incorrect: it adds the same item (`add-test-1`) 21 times, so the duplicate guard fires on attempt 2, not the capacity guard. The test passes only because it asserts count = 1 (which happens to be true due to the duplicate guard, not the capacity guard). Do not replicate this approach. Use the controlled-input loop described above.

**Amendment 2 — Field-level assertions for FR-3**

The spec requires asserting `productCode`, `productName`, `supplier`, and `supplierCode` from FR-3. These values are in React state, not the DOM. `TestComponent` must expose them as DOM text nodes (via `data-testid` spans) so RTL can assert them without accessing React internals. This is an implementation detail not made explicit in the spec.

## Prerequisites

None. All dependencies (`@testing-library/react`, `jest`, React) are already installed. No migrations, config changes, or infrastructure work required before implementation begins.
