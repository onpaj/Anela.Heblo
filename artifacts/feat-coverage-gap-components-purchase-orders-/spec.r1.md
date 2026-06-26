# Specification: Unit Tests for PurchaseOrderValidation

## Summary
Add unit test coverage for `frontend/src/components/purchase-orders/form/PurchaseOrderValidation.tsx`, which currently has zero tests. Cover all five `validateForm` rules and both branches of `clearFieldError` with pure unit tests, locking in the current behavior (including the deliberate silent-skip of partial line rows).

## Background
`PurchaseOrderValidation.tsx` enforces the form-level validation contract for purchase orders before submission to the backend. The sibling file `PurchaseOrderHelpers.test.tsx` tests calculation helpers but does not import or exercise the validators. A weekly coverage-gap routine (CI run #27104028537, 2026-06-08) flagged this module as a high-value gap because:

- The cross-field delivery-date check uses string-constructed `Date` objects, which are timezone-sensitive and easy to regress.
- The per-line validation is gated on `selectedMaterial.productName` being truthy. Rows without a selected material are silently skipped, even if quantity or price are invalid. This is intentional today but undocumented; without a test, a future refactor could remove the gate and cause spurious validation errors on partial rows the user has not finished filling out.
- A failure in either path would allow invalid orders to reach the backend without surface-level detection.

The validators are pure functions, so coverage can be added cheaply with no component rendering.

## Functional Requirements

### FR-1: Test missing required scalar fields in `validateForm`
Cover the three required-field branches: order number (with trim check), supplier, and order date.

**Acceptance criteria:**
- A test asserts `validateForm` produces an error for the order-number field when `orderNumber` is missing, empty string, or whitespace-only.
- A test asserts `validateForm` produces an error for the supplier field when `supplier` is `null` or `undefined`.
- A test asserts `validateForm` produces an error for the order-date field when `orderDate` is `null` or `undefined`.
- Each test confirms the returned errors object keys/messages match what the component currently surfaces.

### FR-2: Test cross-field delivery-date rule in `validateForm`
Cover the only cross-field rule: `expectedDeliveryDate` must not precede `orderDate`.

**Acceptance criteria:**
- A test asserts an error is produced when `expectedDeliveryDate < orderDate` (delivery before order).
- A test asserts NO error is produced when `expectedDeliveryDate > orderDate` (valid ordering).
- A test asserts NO error is produced when `expectedDeliveryDate == orderDate` (boundary — same day), confirming the current strict-less-than comparison.
- Tests use ISO date strings consistent with how the form passes them in production, to lock in behavior under the current `new Date(string)` construction.

### FR-3: Test per-line validation gate in `validateForm`
Cover the conditional that lines without a selected material are skipped entirely, while lines with a material are validated for positive quantity and positive unit price.

**Acceptance criteria:**
- A test asserts that a line whose `selectedMaterial.productName` is falsy (null, undefined, or empty string) produces NO errors even when `quantity` is 0/negative and `unitPrice` is 0/negative. This documents the silent-skip intent.
- A test asserts that a line with a valid `selectedMaterial.productName` and `quantity <= 0` produces a quantity error.
- A test asserts that a line with a valid `selectedMaterial.productName` and `unitPrice <= 0` produces a unit-price error.
- A test asserts that a line with valid material, positive quantity, and positive unit price produces NO line errors.

### FR-4: Test `clearFieldError` both branches
Cover both code paths: returning a new object with the field removed, and returning the original object when the field is absent.

**Acceptance criteria:**
- A test asserts that `clearFieldError(errors, fieldName)` where `fieldName` IS a key in `errors` returns a new object without that key, and the returned object is NOT referentially equal to the input.
- A test asserts that `clearFieldError(errors, fieldName)` where `fieldName` is NOT a key in `errors` returns the original object reference (referential equality preserved).
- A test asserts the original `errors` object is not mutated in either branch.

## Non-Functional Requirements

### NFR-1: Performance
Tests must be pure unit tests with no component rendering, no DOM, no timers. Suite runtime overhead for this file should remain well under 100ms.

### NFR-2: Test framework and conventions
- Use the project's existing frontend test runner (Jest, matching the convention of `PurchaseOrderHelpers.test.tsx`).
- Follow AAA (Arrange-Act-Assert) structure.
- Use descriptive test names following the pattern "returns error when X" / "produces no error when Y".
- Co-locate the test file as `PurchaseOrderValidation.test.tsx` in the same directory as the source.

### NFR-3: Coverage
This file moves from 0% to ≥95% line and branch coverage. No coverage threshold change is required at the project level; this is an additive coverage improvement.

## Data Model
No data model changes. Tests consume the existing types used by `PurchaseOrderValidation.tsx` (form-state shape: order number, supplier, order date, expected delivery date, lines with `selectedMaterial`, `quantity`, `unitPrice`).

## API / Interface Design
No API or interface changes. Tests are purely additive and exercise the existing exported functions `validateForm` and `clearFieldError` from `PurchaseOrderValidation.tsx`.

## Dependencies
- Existing frontend test runner (Jest) already configured for the frontend workspace.
- Test fixtures should be constructed inline (small, focused) — no shared fixture file required.
- No new npm packages.

## Out of Scope
- Refactoring `PurchaseOrderValidation.tsx`. Tests lock in current behavior.
- Component-level tests of the purchase-order form UI.
- Integration or E2E tests covering the form.
- Backend validation parity checks.
- Adding new validation rules or fixing latent bugs (e.g., timezone weirdness in date comparison). Any such finding should be filed as a separate issue, not addressed here.
- Localization / i18n testing of error messages.

## Open Questions
None.

## Status: COMPLETE