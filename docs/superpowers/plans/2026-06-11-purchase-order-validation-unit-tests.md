# PurchaseOrderValidation Unit Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Jest unit-test file that locks in the current behavior of `validateForm` and `clearFieldError` in `PurchaseOrderValidation.tsx`, raising coverage from 0% to ≥95% line/branch.

**Architecture:** Single new test file under `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`, mirroring the placement and conventions of the sibling `PurchaseOrderHelpers.test.tsx`. Tests are pure characterization tests of two exported functions — no rendering, no DOM, no timers, no new dependencies. A local factory builds a baseline-valid `FormData`; each test overrides only the fields under test.

**Tech Stack:** Jest (via `react-scripts` 5.0.1), TypeScript 4.9, existing generated NSwag class `PurchaseOrderLineDto`, existing form types from `PurchaseOrderTypes.tsx`.

---

## File Structure

**Create:**
- `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx` — the only new file.

**Read (do not modify):**
- `frontend/src/components/purchase-orders/form/PurchaseOrderValidation.tsx` — system under test.
- `frontend/src/components/purchase-orders/form/PurchaseOrderTypes.tsx` — `FormData` shape.
- `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderHelpers.test.tsx` — convention reference.
- `frontend/src/api/generated/api-client.ts` — `PurchaseOrderLineDto`, `SupplierDto`, `ContactVia`.
- `frontend/src/api/hooks/useMaterials.ts` — `MaterialForPurchaseDto`.

## Conventions Locked In (do not deviate)

These come from the architecture review and the sibling test file. Treat them as constraints.

1. **Test file location** is `__tests__/PurchaseOrderValidation.test.tsx`, **not** co-located at the form root. (Spec amendment #1.)
2. **`PurchaseOrderLineDto` is instantiated as** `Object.assign(new PurchaseOrderLineDto(), { ... })`. Never a plain object cast — it's a generated NSwag class, not an interface.
3. **Dates are `YYYY-MM-DD` strings** (matching `<input type="date">.value`). Never `Date` objects, never ISO timestamps with `T...Z`.
4. **Field name is `selectedSupplier`**, not `supplier` (spec amendment #2).
5. **Date fields are typed `string` in `FormData`**. For "missing" cases use empty string `""`. To test the `undefined` defensive case, cast through `Partial<FormData>`. Do **not** use `null`.
6. **Error keys and messages** are asserted exactly as the SUT produces them (Czech strings — see the contract table in Task 0).

## Test Coverage Map

Every spec requirement maps to a task:

| Spec | Requirement | Task |
|------|-------------|------|
| FR-1 | orderNumber empty / whitespace / valid | Task 2 |
| FR-1 | selectedSupplier null / valid | Task 3 |
| FR-1 | orderDate empty string / undefined / valid | Task 4 |
| FR-2 | expectedDeliveryDate < / > / == orderDate | Task 5 |
| FR-3 | line without material → silent skip | Task 6 |
| FR-3 | line with material + whitespace material name → material error | Task 7 |
| FR-3 | line with material + quantity ≤ 0 / unitPrice ≤ 0 / all valid | Task 7 |
| FR-4 | clearFieldError both branches + no-mutation | Task 8 |
| NFR-3 | ≥95% line/branch coverage on the SUT | Task 9 |

---

## Task 0: Read the system under test and the convention reference

This task is informational only — no code is written.

**Files:**
- Read: `frontend/src/components/purchase-orders/form/PurchaseOrderValidation.tsx`
- Read: `frontend/src/components/purchase-orders/form/PurchaseOrderTypes.tsx`
- Read: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderHelpers.test.tsx`

- [ ] **Step 1: Confirm error-key/message contract**

The test assertions must use exactly these strings. They are read directly from `PurchaseOrderValidation.tsx`:

| Field | Error key | Czech message |
|-------|-----------|---------------|
| Order number empty/whitespace | `orderNumber` | `Číslo objednávky je povinné` |
| Supplier missing | `selectedSupplier` | `Dodavatel je povinný` |
| Order date missing | `orderDate` | `Datum objednávky je povinné` |
| Delivery before order | `expectedDeliveryDate` | `Datum dodání nemůže být před datem objednávky` |
| Line N material whitespace-only | `line_${N}_material` | `Vyberte materiál ze seznamu` |
| Line N quantity ≤ 0 / falsy | `line_${N}_quantity` | `Množství musí být větší než 0` |
| Line N unitPrice ≤ 0 / falsy | `line_${N}_price` | `Jednotková cena musí být větší než 0` |

- [ ] **Step 2: Confirm the working directory and test command**

All test commands run from `frontend/`. The watch-mode default of `react-scripts test` is suppressed with `--watchAll=false`.

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

No commit at this task.

---

## Task 1: Create the test file with imports, factory helpers, and an empty describe block

**Files:**
- Create: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

- [ ] **Step 1: Create the file with the full scaffolding**

The file imports, declares two local factory helpers, and opens an empty top-level `describe`. The factories return a **baseline-valid** `FormData` and a **baseline-valid** line; tests will override only the fields they exercise.

`makeFormData` defaults: order number `"PO-1"`, a non-null supplier, order date `"2026-01-01"`, expected delivery `"2026-01-05"`, empty lines array. These defaults make the baseline pass `validateForm` cleanly with `{}`.

`makeLine` defaults: a `PurchaseOrderLineDto` with quantity `1`, unitPrice `1`, and `selectedMaterial` set to a valid material whose `productName` is `"Material A"`. Defaults make the baseline line pass validation.

```tsx
import {
  validateForm,
  clearFieldError,
} from "../PurchaseOrderValidation";
import { FormData } from "../PurchaseOrderTypes";
import {
  PurchaseOrderLineDto,
  SupplierDto,
} from "../../../../api/generated/api-client";
import { MaterialForPurchaseDto } from "../../../../api/hooks/useMaterials";

type FormLine = FormData["lines"][number];

const makeSupplier = (
  overrides: Partial<SupplierDto> = {},
): SupplierDto =>
  Object.assign(new SupplierDto(), {
    id: 1,
    name: "Acme",
    ...overrides,
  });

const makeMaterial = (
  overrides: Partial<MaterialForPurchaseDto> = {},
): MaterialForPurchaseDto =>
  ({
    productCode: "MAT001",
    productName: "Material A",
    ...overrides,
  }) as MaterialForPurchaseDto;

const makeLine = (overrides: Partial<FormLine> = {}): FormLine =>
  Object.assign(new PurchaseOrderLineDto(), {
    quantity: 1,
    unitPrice: 1,
    selectedMaterial: makeMaterial(),
    ...overrides,
  }) as FormLine;

const makeFormData = (overrides: Partial<FormData> = {}): FormData => ({
  orderNumber: "PO-1",
  selectedSupplier: makeSupplier(),
  orderDate: "2026-01-01",
  expectedDeliveryDate: "2026-01-05",
  contactVia: null,
  notes: "",
  lines: [],
  ...overrides,
});

describe("PurchaseOrderValidation", () => {
  it("returns no errors for a baseline-valid form with no lines", () => {
    const errors = validateForm(makeFormData());
    expect(errors).toEqual({});
  });
});
```

- [ ] **Step 2: Run the single smoke test to verify the file compiles and the baseline is valid**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 1 passing test. If the baseline assertion fails, the factory defaults are wrong — fix the defaults before continuing (do not weaken the assertion).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): scaffold PurchaseOrderValidation test file"
```

---

## Task 2: FR-1 — orderNumber required-field tests

**Files:**
- Modify: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

- [ ] **Step 1: Add a `describe("validateForm")` and a nested `describe("orderNumber")` with three tests**

Insert this nested block **inside** the existing top-level `describe("PurchaseOrderValidation", ...)`, **before** the baseline `it` so test ordering reads top-down by FR.

```tsx
describe("validateForm", () => {
  describe("orderNumber", () => {
    it("returns error when orderNumber is empty string", () => {
      const errors = validateForm(makeFormData({ orderNumber: "" }));
      expect(errors.orderNumber).toBe("Číslo objednávky je povinné");
    });

    it("returns error when orderNumber is whitespace-only", () => {
      const errors = validateForm(makeFormData({ orderNumber: "   " }));
      expect(errors.orderNumber).toBe("Číslo objednávky je povinné");
    });

    it("produces no orderNumber error when orderNumber is a non-empty trimmed string", () => {
      const errors = validateForm(makeFormData({ orderNumber: "PO-42" }));
      expect(errors).not.toHaveProperty("orderNumber");
    });
  });
});
```

Move the baseline `it("returns no errors for a baseline-valid form with no lines", ...)` to sit **above** the `describe("validateForm", ...)` block, or leave it where it is — placement does not affect coverage. Recommendation: leave the baseline as the first `it` under `PurchaseOrderValidation` and add `describe("validateForm")` after it.

- [ ] **Step 2: Run the file's tests**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 4 passing tests (baseline + 3 orderNumber).

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): cover orderNumber required-field branches"
```

---

## Task 3: FR-1 — selectedSupplier required-field tests

**Files:**
- Modify: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

- [ ] **Step 1: Append a `describe("selectedSupplier")` block inside `describe("validateForm")`**

Place it directly after the `describe("orderNumber")` block.

```tsx
describe("selectedSupplier", () => {
  it("returns error when selectedSupplier is null", () => {
    const errors = validateForm(makeFormData({ selectedSupplier: null }));
    expect(errors.selectedSupplier).toBe("Dodavatel je povinný");
  });

  it("produces no selectedSupplier error when a supplier is set", () => {
    const errors = validateForm(makeFormData());
    expect(errors).not.toHaveProperty("selectedSupplier");
  });
});
```

- [ ] **Step 2: Run the file's tests**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 6 passing tests.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): cover selectedSupplier required-field branch"
```

---

## Task 4: FR-1 — orderDate required-field tests

**Files:**
- Modify: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

- [ ] **Step 1: Append a `describe("orderDate")` block inside `describe("validateForm")`**

Place it directly after the `describe("selectedSupplier")` block. Empty string is the realistic case (uncontrolled `<input type="date">.value` before the user picks anything). `undefined` is the defensive case and requires a cast through `Partial<FormData>` because the `FormData` type declares `orderDate: string`. The expected-delivery default also must be cleared in the empty-string test so the cross-field rule does not surface its own error and pollute the assertion target.

```tsx
describe("orderDate", () => {
  it("returns error when orderDate is empty string", () => {
    const errors = validateForm(
      makeFormData({ orderDate: "", expectedDeliveryDate: "" }),
    );
    expect(errors.orderDate).toBe("Datum objednávky je povinné");
  });

  it("returns error when orderDate is undefined (defensive)", () => {
    const errors = validateForm(
      makeFormData({
        orderDate: undefined as unknown as string,
        expectedDeliveryDate: "",
      }),
    );
    expect(errors.orderDate).toBe("Datum objednávky je povinné");
  });

  it("produces no orderDate error when orderDate is a valid date string", () => {
    const errors = validateForm(makeFormData());
    expect(errors).not.toHaveProperty("orderDate");
  });
});
```

- [ ] **Step 2: Run the file's tests**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 9 passing tests.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): cover orderDate required-field branch"
```

---

## Task 5: FR-2 — expectedDeliveryDate vs orderDate cross-field rule

**Files:**
- Modify: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

- [ ] **Step 1: Append a `describe("expectedDeliveryDate vs orderDate")` block inside `describe("validateForm")`**

Use a multi-day gap for the strict before/after cases to dodge timezone-offset shifts in `new Date("YYYY-MM-DD")`. For the boundary `==` case, use the **same exact string** on both sides — identical inputs produce identical `Date` objects regardless of timezone.

```tsx
describe("expectedDeliveryDate vs orderDate", () => {
  it("returns error when expectedDeliveryDate is before orderDate", () => {
    const errors = validateForm(
      makeFormData({
        orderDate: "2026-01-10",
        expectedDeliveryDate: "2026-01-05",
      }),
    );
    expect(errors.expectedDeliveryDate).toBe(
      "Datum dodání nemůže být před datem objednávky",
    );
  });

  it("produces no expectedDeliveryDate error when delivery is after order", () => {
    const errors = validateForm(
      makeFormData({
        orderDate: "2026-01-01",
        expectedDeliveryDate: "2026-01-05",
      }),
    );
    expect(errors).not.toHaveProperty("expectedDeliveryDate");
  });

  it("produces no expectedDeliveryDate error when delivery equals order (boundary)", () => {
    const sameDay = "2026-01-01";
    const errors = validateForm(
      makeFormData({
        orderDate: sameDay,
        expectedDeliveryDate: sameDay,
      }),
    );
    expect(errors).not.toHaveProperty("expectedDeliveryDate");
  });

  it("produces no expectedDeliveryDate error when expectedDeliveryDate is empty", () => {
    const errors = validateForm(
      makeFormData({
        orderDate: "2026-01-01",
        expectedDeliveryDate: "",
      }),
    );
    expect(errors).not.toHaveProperty("expectedDeliveryDate");
  });
});
```

The fourth test covers the short-circuit `formData.expectedDeliveryDate &&` branch of the cross-field rule and is required for branch coverage.

- [ ] **Step 2: Run the file's tests**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 13 passing tests.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): cover expectedDeliveryDate cross-field rule"
```

---

## Task 6: FR-3 — per-line silent-skip when no material is selected

**Files:**
- Modify: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

This task locks in the **deliberate silent-skip**: a row without `selectedMaterial.productName` produces zero errors even when its `quantity` and `unitPrice` are clearly invalid (`0`, negative, undefined). The gate is the truthy check on `selectedMaterial && selectedMaterial.productName` at `PurchaseOrderValidation.tsx:30-31`. The error keys use `line_${index}_…`.

- [ ] **Step 1: Append a `describe("per-line validation")` block inside `describe("validateForm")` with the silent-skip tests**

```tsx
describe("per-line validation", () => {
  it("produces no line errors when selectedMaterial is null (silent skip)", () => {
    const line = makeLine({
      selectedMaterial: null,
      quantity: 0,
      unitPrice: 0,
    });
    const errors = validateForm(makeFormData({ lines: [line] }));
    expect(errors).not.toHaveProperty("line_0_material");
    expect(errors).not.toHaveProperty("line_0_quantity");
    expect(errors).not.toHaveProperty("line_0_price");
  });

  it("produces no line errors when selectedMaterial.productName is empty string (silent skip)", () => {
    const line = makeLine({
      selectedMaterial: makeMaterial({ productName: "" }),
      quantity: -5,
      unitPrice: -1,
    });
    const errors = validateForm(makeFormData({ lines: [line] }));
    expect(errors).not.toHaveProperty("line_0_material");
    expect(errors).not.toHaveProperty("line_0_quantity");
    expect(errors).not.toHaveProperty("line_0_price");
  });

  it("produces no line errors when selectedMaterial.productName is undefined (silent skip)", () => {
    const line = makeLine({
      selectedMaterial: makeMaterial({ productName: undefined }),
      quantity: 0,
      unitPrice: 0,
    });
    const errors = validateForm(makeFormData({ lines: [line] }));
    expect(errors).not.toHaveProperty("line_0_material");
    expect(errors).not.toHaveProperty("line_0_quantity");
    expect(errors).not.toHaveProperty("line_0_price");
  });
});
```

- [ ] **Step 2: Run the file's tests**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 16 passing tests.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): lock in silent-skip for partial line rows"
```

---

## Task 7: FR-3 — per-line validation when material IS selected

**Files:**
- Modify: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

Cover the three per-line error branches that fire **only when the material gate passes**: whitespace-only material name, quantity ≤ 0/falsy, unitPrice ≤ 0/falsy. Add the all-valid line case for completeness. Also add a two-line test to verify the index-based error key.

- [ ] **Step 1: Append the following tests inside `describe("per-line validation")`**

```tsx
it("returns material error when productName is whitespace-only (gate passes via truthy check, inner trim fails)", () => {
  const line = makeLine({
    selectedMaterial: makeMaterial({ productName: "   " }),
    quantity: 1,
    unitPrice: 1,
  });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors.line_0_material).toBe("Vyberte materiál ze seznamu");
});

it("returns quantity error when quantity is 0", () => {
  const line = makeLine({ quantity: 0 });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors.line_0_quantity).toBe("Množství musí být větší než 0");
});

it("returns quantity error when quantity is negative", () => {
  const line = makeLine({ quantity: -1 });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors.line_0_quantity).toBe("Množství musí být větší než 0");
});

it("returns quantity error when quantity is undefined", () => {
  const line = makeLine({ quantity: undefined });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors.line_0_quantity).toBe("Množství musí být větší než 0");
});

it("returns unit price error when unitPrice is 0", () => {
  const line = makeLine({ unitPrice: 0 });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors.line_0_price).toBe("Jednotková cena musí být větší než 0");
});

it("returns unit price error when unitPrice is negative", () => {
  const line = makeLine({ unitPrice: -0.01 });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors.line_0_price).toBe("Jednotková cena musí být větší než 0");
});

it("returns unit price error when unitPrice is undefined", () => {
  const line = makeLine({ unitPrice: undefined });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors.line_0_price).toBe("Jednotková cena musí být větší než 0");
});

it("produces no line errors when material, quantity, and unitPrice are all valid", () => {
  const line = makeLine({ quantity: 2, unitPrice: 9.99 });
  const errors = validateForm(makeFormData({ lines: [line] }));
  expect(errors).not.toHaveProperty("line_0_material");
  expect(errors).not.toHaveProperty("line_0_quantity");
  expect(errors).not.toHaveProperty("line_0_price");
});

it("uses the line index in error keys for the second line", () => {
  const validLine = makeLine({ quantity: 1, unitPrice: 1 });
  const invalidLine = makeLine({ quantity: 0, unitPrice: 0 });
  const errors = validateForm(
    makeFormData({ lines: [validLine, invalidLine] }),
  );
  expect(errors).not.toHaveProperty("line_0_quantity");
  expect(errors).not.toHaveProperty("line_0_price");
  expect(errors.line_1_quantity).toBe("Množství musí být větší než 0");
  expect(errors.line_1_price).toBe("Jednotková cena musí být větší než 0");
});
```

- [ ] **Step 2: Run the file's tests**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 25 passing tests.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): cover per-line material/quantity/price branches"
```

---

## Task 8: FR-4 — clearFieldError both branches + immutability

**Files:**
- Modify: `frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx`

This task covers the two branches of `clearFieldError`:

- "Present" branch (`errors[field]` is **truthy**): returns a **new** object with the field removed, original not mutated.
- "Absent" branch (`errors[field]` is falsy — missing key **or** empty-string value): returns the **same** object reference.

The empty-string case is included because the SUT uses `errors[field]` (truthy check), not `field in errors` — see spec amendment #5.

- [ ] **Step 1: Append a `describe("clearFieldError")` block at the top level of `describe("PurchaseOrderValidation")`, after `describe("validateForm")`**

```tsx
describe("clearFieldError", () => {
  it("returns a new object without the field when the field has a truthy value", () => {
    const errors = { foo: "msg-foo", bar: "msg-bar" };
    const result = clearFieldError(errors, "foo");
    expect(result).not.toBe(errors);
    expect(result).toEqual({ bar: "msg-bar" });
  });

  it("does not mutate the original errors object when removing a field", () => {
    const errors = { foo: "msg-foo", bar: "msg-bar" };
    clearFieldError(errors, "foo");
    expect(errors).toEqual({ foo: "msg-foo", bar: "msg-bar" });
  });

  it("returns the same reference when the field is not present", () => {
    const errors = { foo: "msg-foo" };
    const result = clearFieldError(errors, "missing");
    expect(result).toBe(errors);
  });

  it("returns the same reference when the field has an empty-string (falsy) value", () => {
    const errors = { foo: "" };
    const result = clearFieldError(errors, "foo");
    expect(result).toBe(errors);
  });

  it("does not mutate the original errors object when the field is absent", () => {
    const errors = { foo: "msg-foo" };
    clearFieldError(errors, "missing");
    expect(errors).toEqual({ foo: "msg-foo" });
  });
});
```

- [ ] **Step 2: Run the file's tests**

```bash
cd frontend
npm test -- --watchAll=false --testPathPattern=PurchaseOrderValidation
```

Expected: 30 passing tests.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): cover clearFieldError both branches"
```

---

## Task 9: Verify coverage ≥95% on the SUT and run the wider validation gates

**Files:** None (verification only).

- [ ] **Step 1: Run coverage scoped to the SUT**

```bash
cd frontend
npm test -- --watchAll=false \
  --testPathPattern=PurchaseOrderValidation \
  --coverage \
  --collectCoverageFrom='src/components/purchase-orders/form/PurchaseOrderValidation.tsx'
```

Expected: the coverage table row for `PurchaseOrderValidation.tsx` shows **≥95% statements, branches, functions, and lines**. If any column is below 95%, identify the uncovered branch from the coverage output (e.g. a missing falsy-quantity case) and add a targeted test in the most relevant existing `describe` block. Re-run until ≥95%.

- [ ] **Step 2: Run the full frontend test suite to confirm no regressions**

```bash
cd frontend
npm test -- --watchAll=false
```

Expected: all tests pass, including the existing `PurchaseOrderHelpers.test.tsx`.

- [ ] **Step 3: Run the build and lint gates required by `CLAUDE.md`**

```bash
cd frontend
npm run build
npm run lint
```

Expected: both commands exit 0. `npm run build` is required because the project's TypeScript strict mode catches type mistakes that `react-scripts test` (which uses Babel) may swallow.

- [ ] **Step 4: If Step 1 required adding tests, commit the coverage top-ups**

```bash
git add frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
git commit -m "test(purchase-orders): top up coverage to ≥95%"
```

If no top-ups were needed, skip the commit.

---

## Self-Review

**Spec coverage:**

- FR-1 orderNumber: Task 2 ✓
- FR-1 selectedSupplier (spec said "supplier" — corrected to `selectedSupplier` per arch amendment #2): Task 3 ✓
- FR-1 orderDate (spec said `null | undefined` — corrected to empty-string + undefined per arch amendment #3): Task 4 ✓
- FR-2 expectedDeliveryDate cross-field (before / after / equal boundary, plus empty short-circuit): Task 5 ✓
- FR-3 silent-skip for partial rows (null, empty-string productName, undefined productName): Task 6 ✓
- FR-3 whitespace material branch (spec amendment #4): Task 7 ✓
- FR-3 quantity / unitPrice / all-valid cases: Task 7 ✓
- FR-4 clearFieldError present + absent + no-mutation (spec amendment #5 covered by the empty-string test): Task 8 ✓
- NFR-1 (no rendering, no DOM, no timers): all tests are direct function calls ✓
- NFR-2 (Jest, AAA structure, descriptive names, file location): Task 1 establishes structure; location uses `__tests__/` per arch amendment #1 ✓
- NFR-3 (≥95% line/branch coverage): Task 9 verifies ✓

**Placeholder scan:** No TBDs, no "implement later", no "similar to Task N" — every code step shows the actual code to write.

**Type consistency:** `makeFormData`, `makeLine`, `makeMaterial`, `makeSupplier` are defined in Task 1 and referenced consistently in Tasks 2–8. The `FormLine` type alias defined in Task 1 (`FormData["lines"][number]`) is used in `makeLine`'s signature. Error keys (`orderNumber`, `selectedSupplier`, `orderDate`, `expectedDeliveryDate`, `line_${N}_material`, `line_${N}_quantity`, `line_${N}_price`) and messages match the SUT exactly.
