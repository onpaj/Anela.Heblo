Confirmed: dates come from `<input type="date">` as `YYYY-MM-DD` strings, defaulting to empty string. Now writing the review.

# Architecture Review: Unit Tests for PurchaseOrderValidation

## Skip Design: true

This is a pure test-coverage addition with no UI/UX impact, no visual components, no new screens, and no design decisions. The production module under test (`PurchaseOrderValidation.tsx`) is untouched.

## Architectural Fit Assessment

The proposal aligns cleanly with existing conventions. `PurchaseOrderValidation.tsx` exports two pure functions (`validateForm`, `clearFieldError`) — exactly the shape already covered by the sibling `PurchaseOrderHelpers.test.tsx`. Both modules belong to the same vertical slice (`frontend/src/components/purchase-orders/form/`) and share the same domain types (`FormData`, `PurchaseOrderLineDto`, `MaterialForPurchaseDto`).

Integration points:
- **Test runner**: Jest via `react-scripts` (CRA) — already configured in `frontend/package.json`. `@testing-library/jest-dom` is available but **not needed** here (no DOM).
- **Type imports**: `FormData` from `../PurchaseOrderTypes`, `PurchaseOrderLineDto` from `../../../../api/generated/api-client` (a generated **class**, not interface — instantiated with `Object.assign(new PurchaseOrderLineDto(), {...})`, as already established in `PurchaseOrderHelpers.test.tsx`).
- **No new dependencies**, no test infrastructure changes, no coverage threshold changes.

The risk is minimal: pure-function tests in an established pattern.

## Proposed Architecture

### Component Overview

```
frontend/src/components/purchase-orders/form/
├── PurchaseOrderValidation.tsx                  (SUT — unchanged)
├── PurchaseOrderTypes.tsx                       (FormData, line shape)
├── PurchaseOrderHelpers.tsx                     (sibling, already tested)
└── __tests__/
    ├── PurchaseOrderHelpers.test.tsx            (existing — reference convention)
    └── PurchaseOrderValidation.test.tsx         (NEW — this feature)
```

Single new file. No production code changes.

### Key Design Decisions

#### Decision 1: Test file location — `__tests__/` subdirectory, NOT co-located
**Options considered:**
- (a) Co-locate as `form/PurchaseOrderValidation.test.tsx` (what the spec says)
- (b) Place under `form/__tests__/PurchaseOrderValidation.test.tsx` (matches existing `PurchaseOrderHelpers.test.tsx`)

**Chosen approach:** (b) `__tests__/` subdirectory.

**Rationale:** The spec (NFR-2) states "co-locate in the same directory as the source" but this contradicts the actual convention in this folder. `PurchaseOrderHelpers.test.tsx` already lives in `form/__tests__/`. Following the spec verbatim would create two divergent placement conventions in a single 200-line folder. Consistency beats the spec wording here — this is a spec amendment, not a deviation. Jest's default `<rootDir>` discovery handles both locations equally, so there is no tooling cost.

#### Decision 2: Test fixture construction — inline factory helper, no shared fixture file
**Options considered:**
- (a) Plain inline object literals per test (terse but repetitive for the `FormData` shape)
- (b) A local `makeFormData(overrides: Partial<FormData>): FormData` factory inside the test file
- (c) A shared fixture module under `form/__tests__/fixtures/`

**Chosen approach:** (b) Local factory inside the test file.

**Rationale:** Spec NFR-2/Dependencies says "Test fixtures should be constructed inline (small, focused) — no shared fixture file required." A single local factory keeps each test arrange-block to one line of overrides, satisfies the "inline/focused" intent, and keeps the file self-contained. The factory returns a fully-populated default `FormData` (valid order number, supplier, dates, empty `lines`); each test overrides only what it exercises. This is the same pattern `PurchaseOrderHelpers.test.tsx` uses for lines (`createMockLine`).

#### Decision 3: How to instantiate `PurchaseOrderLineDto`
**Options considered:**
- (a) Cast a plain object literal: `{ quantity: 1, unitPrice: 1 } as PurchaseOrderLineDto`
- (b) `Object.assign(new PurchaseOrderLineDto(), { ... })`

**Chosen approach:** (b) — match the established pattern in `PurchaseOrderHelpers.test.tsx`.

**Rationale:** `PurchaseOrderLineDto` is a generated NSwag **class**, not an interface. Plain casts skip the constructor and have historically caused subtle failures with generated classes in this repo. The existing sibling test already settled this — re-use the pattern verbatim.

#### Decision 4: Dates passed as ISO `YYYY-MM-DD` strings
**Options considered:**
- (a) Pass `Date` objects
- (b) Pass full ISO timestamps with timezone
- (c) Pass `YYYY-MM-DD` strings, matching the `<input type="date">` value

**Chosen approach:** (c).

**Rationale:** Verified in `PurchaseOrderHeader.tsx:76,99` — both date fields are bound directly to `<input type="date">.value`, which is always a `YYYY-MM-DD` string. Using the same shape in tests locks in the real production code path through `new Date(string)` and surfaces any future timezone regression. Do **not** introduce ISO timestamps with `T00:00:00Z` etc. — they would test a contract the form never sends.

## Implementation Guidance

### Directory / Module Structure

Single new file:
```
frontend/src/components/purchase-orders/form/__tests__/PurchaseOrderValidation.test.tsx
```

No other files created or modified.

### Interfaces and Contracts

**SUT exports (consumed by the test, not modified):**
```typescript
export const validateForm = (formData: FormData): Record<string, string>
export const clearFieldError = (
  errors: Record<string, string>,
  field: string,
): Record<string, string>
```

**Test file structure (skeleton — not implementation):**
```typescript
import { validateForm, clearFieldError } from "../PurchaseOrderValidation";
import { FormData } from "../PurchaseOrderTypes";
import { PurchaseOrderLineDto, SupplierDto } from "../../../../api/generated/api-client";
import { MaterialForPurchaseDto } from "../../../../api/hooks/useMaterials";

// Local factory — returns a baseline VALID FormData; tests override fields under test.
const makeFormData = (overrides: Partial<FormData> = {}): FormData => ({ ... });
const makeLine = (overrides): FormData["lines"][number] => ...;

describe("PurchaseOrderValidation", () => {
  describe("validateForm", () => {
    describe("orderNumber", () => { /* FR-1 cases */ });
    describe("selectedSupplier", () => { /* FR-1 cases */ });
    describe("orderDate", () => { /* FR-1 cases */ });
    describe("expectedDeliveryDate vs orderDate", () => { /* FR-2 cases */ });
    describe("per-line validation", () => { /* FR-3 cases */ });
  });
  describe("clearFieldError", () => { /* FR-4 cases */ });
});
```

**Error key contract being locked in** (read directly from `PurchaseOrderValidation.tsx` — assert exactly these strings):
| Field | Error key | Czech message |
|-------|-----------|---------------|
| Order number empty/whitespace | `orderNumber` | `"Číslo objednávky je povinné"` |
| Supplier missing | `selectedSupplier` | `"Dodavatel je povinný"` |
| Order date missing | `orderDate` | `"Datum objednávky je povinné"` |
| Delivery before order | `expectedDeliveryDate` | `"Datum dodání nemůže být před datem objednávky"` |
| Line N quantity ≤ 0 | `line_${N}_quantity` | `"Množství musí být větší než 0"` |
| Line N unitPrice ≤ 0 | `line_${N}_price` | `"Jednotková cena musí být větší než 0"` |
| Line N material whitespace-only | `line_${N}_material` | `"Vyberte materiál ze seznamu"` |

### Data Flow

For each test the flow is:
```
makeFormData(overrides) → validateForm(formData) → errors: Record<string,string>
                                                          │
                                                          ├─ expect(errors).toHaveProperty(...)
                                                          ├─ expect(errors).not.toHaveProperty(...)
                                                          └─ expect(errors[key]).toBe("...")
```

For `clearFieldError`:
```
const errors = { foo: "msg", bar: "msg" };
const result = clearFieldError(errors, "foo");
expect(result).not.toBe(errors);           // new object branch
expect(result).toEqual({ bar: "msg" });    // foo removed
expect(errors).toEqual({ foo: "msg", bar: "msg" }); // original NOT mutated

const result2 = clearFieldError(errors, "missing");
expect(result2).toBe(errors);              // SAME reference (early return branch)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Test file placed per-spec at folder root instead of `__tests__/`, splitting convention | Low | Decision 1 above — override the spec, follow existing convention |
| Date tests pass locally but flake in CI due to timezone offset (`new Date("2026-01-01")` is parsed as UTC midnight; local-tz comparison can shift the day) | Medium | Use strings with at least one day gap (e.g. `2026-01-01` vs `2026-01-05`) for "before/after" cases. For the `==` boundary case, use the **same exact string** on both sides — identical input yields identical `Date` objects regardless of tz |
| Test instantiates `PurchaseOrderLineDto` with plain object cast, hits subtle generated-class footgun | Low | Decision 3 — use `Object.assign(new PurchaseOrderLineDto(), {...})` per existing convention |
| Spec missed a 7th error branch: the `line_${index}_material` whitespace-only case inside the `hasValidMaterial` block (`PurchaseOrderValidation.tsx:34`) | Medium | See Specification Amendments below — add it to FR-3 |
| Test asserts on translated Czech strings; if i18n is later introduced, all tests break | Low | Acceptable trade-off — spec NFR-2 says lock in current behavior. If/when i18n lands, error-key assertions remain valid; the message strings become a separate concern |
| `clearFieldError`'s "present" check uses `errors[field]` truthy-check, not `field in errors` — a key with empty-string value would hit the "absent" branch | Low | Document this in a dedicated test: `clearFieldError({ foo: "" }, "foo")` returns the **same** reference. This is a real branch and current behavior; the spec's "fieldName IS a key in errors" wording is imprecise — see amendments |

## Specification Amendments

The spec is largely correct but needs four small corrections before implementation:

1. **Test file location (NFR-2)** — Change "Co-locate the test file as `PurchaseOrderValidation.test.tsx` in the same directory as the source" to "Place the test file at `__tests__/PurchaseOrderValidation.test.tsx` under the source directory, matching the existing `PurchaseOrderHelpers.test.tsx` convention."

2. **Field name (FR-1)** — Spec says "supplier field". The actual field is `selectedSupplier` (see `PurchaseOrderTypes.tsx:17` and `PurchaseOrderValidation.tsx:10`). Tests must assert on the key `selectedSupplier`, not `supplier`.

3. **Date field types (FR-1)** — Spec says "`orderDate` is `null` or `undefined`". Per `PurchaseOrderTypes.tsx:18`, `orderDate` and `expectedDeliveryDate` are typed as `string`, not `string | null`. The validator's check is a falsy check (`!formData.orderDate`), which fires on empty string. Tests should cover **empty string** as the realistic case, with `undefined` as an additional defensive case (cast through `Partial<FormData>`). Drop `null` — TypeScript prevents it at the call site.

4. **Missing line-validation branch (FR-3)** — Add an acceptance criterion: "A test asserts that a line whose `selectedMaterial.productName` is whitespace-only (e.g. `'   '`) produces a `line_${index}_material` error with message `'Vyberte materiál ze seznamu'`." This branch (`PurchaseOrderValidation.tsx:34`) is reachable because the `hasValidMaterial` gate uses truthy-check (whitespace is truthy) while the inner check uses `.trim()`. Without this test, the only material-error branch is uncovered and ≥95% branch coverage (NFR-3) is unreachable.

5. **`clearFieldError` "present" semantics (FR-4)** — Sharpen acceptance criterion 1 from "fieldName IS a key in errors" to "fieldName has a truthy value in errors". Add one additional micro-case: `clearFieldError({ foo: "" }, "foo")` returns the same reference (empty string is falsy, so the function takes the early-return branch). This exposes a subtle but real behavior.

## Prerequisites

None. All infrastructure exists:
- Jest is configured via `react-scripts` (CRA).
- `@testing-library/jest-dom` is installed but not required for these tests.
- `tsconfig` already covers `__tests__/` paths (`PurchaseOrderHelpers.test.tsx` compiles).
- No migrations, no env vars, no feature flags, no new packages.

Implementation can start immediately. Estimated effort: ~1 hour (matches brief.md estimate), with the only thinking required being the timezone-safe date-string choices.