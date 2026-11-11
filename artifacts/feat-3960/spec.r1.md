# Specification: ShoptetApiInvoiceSource coverage gap (currency filter and null guards)

## Summary
Issue #3960 asks for unit tests of `ShoptetApiInvoiceSource` covering the single-invoice path (found / null) and the date-range path (currency filter, null detail skip). Investigation of the current branch shows `backend/test/Anela.Heblo.Adapters.Shoptet.Tests/Unit/ShoptetApiInvoiceSourceTests.cs` already contains tests for all four requested scenarios (plus case-insensitive currency matching). The remaining work is verification, and closing any residual gap.

## Background
The issue was filed 2026-08-24 from a CI coverage run (18.4% line coverage). The unit test file was present at HEAD (last touched in commit 925de5ca), so the coverage figure is likely stale or measured only from a project that does not run these tests.

## Functional Requirements

### FR-1: Verify existing tests cover the four requested scenarios
**Acceptance criteria:**
- (a) single-invoice found: `GetAllAsync_SingleInvoiceModeFound_ReturnsSingleBatchWithMappedInvoice`
- (b) single-invoice null: `GetAllAsync_SingleInvoiceModeNotFound_ReturnsBatchWithEmptyInvoiceList`
- (c) mixed-currency list: `GetAllAsync_ListModeCurrencyFilter_ExcludesNonMatchingCurrency` and the case-insensitivity theory
- (d) null detail skipped: `GetAllAsync_ListModeNullDetail_ExcludesAffectedCodeWithoutAbortingBatch`
- All pass via `dotnet test` on `Anela.Heblo.Adapters.Shoptet.Tests`.

### FR-2: Confirm measured coverage of the class
Run the test project with coverage and confirm `ShoptetApiInvoiceSource` line coverage is at or above 60%. If the class is not reported (project not included in the CI coverage run), identify why and fix the CI inclusion only if it is a small, surgical change.

### FR-3: Optional small gaps
If coverage shows uncovered lines (for example the null-currency `Price?` branch where `Price` is null, or empty list result), add targeted tests to the same file.

## Non-Functional Requirements
### NFR-1: Performance
Tests are pure mocks (Moq), no I/O.
### NFR-2: Security
None; no production code change.

## Data Model
None.

## API / Interface Design
None. No production code changes.

## Dependencies
xUnit, Moq, FluentAssertions (already used in the test project).

## Out of Scope
Changes to `ShoptetApiInvoiceSource` behaviour; server-side currency filtering; integration tests against live Shoptet.

## Open Questions
None.

## Status: COMPLETE
