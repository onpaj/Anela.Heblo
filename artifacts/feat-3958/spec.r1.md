# Specification: Unit tests for ManufactureCost date-based material cost switch

## Summary
Add direct unit tests for `Anela.Heblo.Domain.Features.Catalog.ManufactureCost`, currently at 0% line coverage. Tests pin the date cutoff (2025-08-25) that selects between `MaterialCostFromReceiptDocument` and `MaterialCostFromPurchasePrice`, and the `Total` computation. No production code changes.

## Background
`ManufactureCost.MaterialCost` returns `MaterialCostFromReceiptDocument` when `Date > LaborRemovedFromBoMDate` (2025-08-25 00:00, strict greater-than), otherwise `MaterialCostFromPurchasePrice`. `Total = MaterialCost + HandlingCost`. Errors here silently skew manufacture cost, pricing and margin reporting. Issue #3958 (tech-debt, coverage-gap).

Observed current behaviour (source of truth for tests): the boundary instant `2025-08-25T00:00` is NOT after the cutoff, so it uses purchase price; `2025-08-25T00:00:00.0000001`/any later time on 25 Aug, e.g. 12:00, uses receipt document. The issue text's "boundary date itself" is therefore resolved as: exact cutoff -> purchase price (characterization).

## Functional Requirements

### FR-1: Before cutoff uses purchase price
**Acceptance criteria:**
- Date 2025-08-24 (and an early date such as 2024-01-01) yields `MaterialCost == MaterialCostFromPurchasePrice`, ignoring the receipt-document value.

### FR-2: After cutoff uses receipt document
**Acceptance criteria:**
- Date 2025-08-26 (and a later date such as 2026-01-01) yields `MaterialCost == MaterialCostFromReceiptDocument`, ignoring the purchase-price value.

### FR-3: Boundary behaviour
**Acceptance criteria:**
- `new DateTime(2025, 8, 25)` yields purchase price.
- `new DateTime(2025, 8, 25, 12, 0, 0)` (same day, later time) yields receipt document.
- `new DateTime(2025, 8, 24, 23, 59, 59)` yields purchase price.
- Test names/comments document the strict `>` semantics so a change to `>=` fails a test.

### FR-4: Total
**Acceptance criteria:**
- `Total == MaterialCost + HandlingCost` on both sides of the cutoff, using distinct values for all three components so a wrong source is detected.
- Default instance (all zero, `Date` = `DateTime.MinValue`) has `Total == 0`.

## Non-Functional Requirements
### NFR-1: Performance
Pure in-memory tests, milliseconds.
### NFR-2: Security
None.
### NFR-3: Style
xUnit + FluentAssertions, AAA, matching `backend/test/Anela.Heblo.Tests/Features/Catalog/` conventions. Tests must not depend on the current clock.

## Data Model
No changes. `ManufactureCost` is an unchanged domain class (`Date`, `HandlingCost`, `MaterialCostFromReceiptDocument`, `MaterialCostFromPurchasePrice`, computed `MaterialCost`, `Total`).

## API / Interface Design
None. Test file: `backend/test/Anela.Heblo.Tests/Features/Catalog/ManufactureCostTests.cs`.

## Dependencies
xUnit, FluentAssertions (already referenced by the Anela.Heblo.Tests project).

## Out of Scope
- Changing the cutoff, its visibility (private static non-readonly field) or making it configurable.
- Refactoring `ManufactureCost` or its consumers.
- Process doc changes (no behaviour change).

## Open Questions
None.

## Status: COMPLETE
