# Specification: Test coverage for `UpdatePurchaseOrderRequestValidator`

## Summary
Add a dedicated FluentValidation unit test suite for `UpdatePurchaseOrderRequestValidator` and its nested `UpdatePurchaseOrderLineRequestValidator`, currently at 0% line coverage. The suite must exercise every conditional branch of the custom `BeAReasonableDate` date-bound check, the 100-line-item cap on `Lines`, and the numeric boundary rules on `Quantity` and `UnitPrice`, using exact boundary values so future refactors that shift these constants are caught by a failing test rather than in production.

## Background
`UpdatePurchaseOrderRequestValidator` is the last validation gate before an `UpdatePurchaseOrder` request reaches the MediatR handler and, ultimately, the database. It currently has no test file at all (confirmed: no `UpdatePurchaseOrderRequestValidatorTests.cs` exists under `backend/test/Anela.Heblo.Tests/Features/Purchase/`), so a CI coverage-gap scan flagged it at 0% against the 60% threshold. The class contains a custom `Must(...)` predicate (`BeAReasonableDate`) with two independent numeric bounds computed via `DateTime.UtcNow.AddYears(...)`, plus a line-count cap and per-line numeric range rules — all of which are silent, easy-to-break constants with no current regression protection. This spec defines the exact test cases needed to close the gap, grounded in the validator's real source (read directly from `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequestValidator.cs`) and the repo's existing FluentValidation test conventions (e.g. `backend/test/Anela.Heblo.Tests/Features/Catalog/Validators/UpdateProductCompositionOrderRequestValidatorTests.cs`).

## Functional Requirements

### FR-1: Test project and file scaffold
Create a new test class `UpdatePurchaseOrderRequestValidatorTests` at:
`backend/test/Anela.Heblo.Tests/Features/Purchase/UpdatePurchaseOrderRequestValidatorTests.cs`

This mirrors the existing sibling file `UpdatePurchaseOrderHandlerTests.cs` in the same `Anela.Heblo.Tests.Features.Purchase` namespace/directory (rather than a separate `Validators` subfolder — this module does not currently use one). Use `FluentValidation.TestHelper` (`TestValidate`, `ShouldHaveValidationErrorFor`, `ShouldNotHaveValidationErrorFor`) and `Xunit`, consistent with `UpdateProductCompositionOrderRequestValidatorTests.cs`.

Provide a private static/instance helper (e.g. `ValidRequest()`) that builds a fully valid `UpdatePurchaseOrderRequest` — `Id > 0`, `SupplierId > 0`, `ExpectedDeliveryDate = null` or a safe in-range date, `Lines` containing exactly one valid `UpdatePurchaseOrderLineRequest` (`MaterialId` non-empty, `Quantity` and `UnitPrice` in range) — so individual tests can mutate only the field under test.

**Acceptance criteria:**
- File exists at the path above, builds under the existing `Anela.Heblo.Tests` project, and runs via `dotnet test`.
- A `ValidRequest()`-equivalent baseline passes `ShouldNotHaveAnyValidationErrors()`.

### FR-2: `ExpectedDeliveryDate` — future bound (`BeAReasonableDate`, upper branch)
Cover the branch `date.Value <= maxFutureDate` where `maxFutureDate = DateTime.UtcNow.AddYears(2)`.

**Acceptance criteria:**
- `ExpectedDeliveryDate = DateTime.UtcNow.AddYears(2)` (exactly at the boundary, computed at test-run time, not a hardcoded date) → `ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate)`.
- `ExpectedDeliveryDate = DateTime.UtcNow.AddYears(2).AddDays(1)` (2 years + 1 day in the future) → `ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)` with message `"Expected delivery date must be reasonable (not more than 2 years in the future)"`.
- Use `DateTime.UtcNow.AddYears(2).AddDays(-1)` (safely inside the bound) as an additional "clearly valid" case to guard against off-by-one direction errors.

### FR-3: `ExpectedDeliveryDate` — past bound (`BeAReasonableDate`, lower branch)
Cover the branch `date.Value >= minPastDate` where `minPastDate = DateTime.UtcNow.AddYears(-10)`.

**Acceptance criteria:**
- `ExpectedDeliveryDate = DateTime.UtcNow.AddYears(-10)` (exactly at the boundary) → `ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate)`.
- `ExpectedDeliveryDate = DateTime.UtcNow.AddYears(-10).AddDays(-1)` (10 years + 1 day in the past) → `ShouldHaveValidationErrorFor(x => x.ExpectedDeliveryDate)` with the same message as FR-2 (the validator uses one shared error message for both bounds — assert message text, not which bound fired).
- Use `DateTime.UtcNow.AddYears(-10).AddDays(1)` (safely inside the bound) as an additional "clearly valid" case.

### FR-4: `ExpectedDeliveryDate` — null passthrough branch
Cover the `if (!date.HasValue) return true;` branch and the `.When(x => x.ExpectedDeliveryDate.HasValue)` gate on the rule itself (both must independently be exercised, since `When` prevents `Must` from even running when null).

**Acceptance criteria:**
- `ExpectedDeliveryDate = null` → `ShouldNotHaveValidationErrorFor(x => x.ExpectedDeliveryDate)`.

### FR-5: `Lines` — 100 line-item cap
Cover `Must(lines => lines.Count <= 100)`.

**Acceptance criteria:**
- A request with exactly 100 valid line items → `ShouldNotHaveValidationErrorFor(x => x.Lines)`.
- A request with exactly 101 valid line items → `ShouldHaveValidationErrorFor(x => x.Lines)` with message `"A purchase order cannot have more than 100 line items"`.
- Generate line items programmatically (e.g. `Enumerable.Range(1, 100).Select(i => new UpdatePurchaseOrderLineRequest { MaterialId = $"MAT-{i}", Quantity = 1m, UnitPrice = 1m })`) rather than hand-writing 100+ literals.

### FR-6: `Lines` — null and empty checks (incidental, currently untested)
The existing `NotNull()` / `NotEmpty()` rules on `Lines` are also at 0% coverage as a side effect of the file having no tests at all. Include minimal coverage alongside the cap tests since they sit on the same `RuleFor(x => x.Lines)` chain and are trivial to add.

**Acceptance criteria:**
- `Lines = null` → `ShouldHaveValidationErrorFor(x => x.Lines)` with message `"Order lines are required"`.
- `Lines = new List<UpdatePurchaseOrderLineRequest>()` (empty) → `ShouldHaveValidationErrorFor(x => x.Lines)` with message `"At least one order line is required"`.

### FR-7: Line item `Quantity` bounds (`UpdatePurchaseOrderLineRequestValidator`)
Cover `GreaterThan(0)` and `LessThanOrEqualTo(999999.99m)`.

**Acceptance criteria:**
- `Quantity = 0` → validation error on the line's `Quantity` with message `"Quantity must be greater than 0"`.
- `Quantity = 0.01m` (smallest valid increment above zero) → no validation error on `Quantity`.
- `Quantity = 999999.99m` (exact upper boundary) → no validation error on `Quantity`.
- `Quantity = 1000000.00m` (just above upper boundary) → validation error with message `"Quantity cannot exceed 999999.99"`.
- Negative `Quantity` (e.g. `-1m`) → validation error with message `"Quantity must be greater than 0"`.
- These assertions run against `UpdatePurchaseOrderRequest.Lines[0].Quantity` via the parent validator (using `ShouldHaveValidationErrorFor("Lines[0].Quantity")` string-path syntax, per the `RuleForEach` + child-validator pattern already used for `Order[0].X` in `UpdateProductCompositionOrderRequestValidatorTests.cs`) so the test also confirms `RuleForEach(x => x.Lines).SetValidator(...)` correctly wires the child validator.
- A standalone `UpdatePurchaseOrderLineRequestValidator` instance may additionally be tested directly against a bare `UpdatePurchaseOrderLineRequest` for simpler boundary assertions (optional, at the test author's discretion) — either approach satisfies this FR as long as both boundaries and both directions of failure are exercised.

### FR-8: Line item `UnitPrice` bounds
Cover `GreaterThanOrEqualTo(0)` and `LessThanOrEqualTo(999999.99m)`.

**Acceptance criteria:**
- `UnitPrice = 0` (inclusive lower boundary) → no validation error on `UnitPrice`.
- `UnitPrice = -0.01m` → validation error with message `"Unit price cannot be negative"`.
- `UnitPrice = 999999.99m` (exact upper boundary) → no validation error on `UnitPrice`.
- `UnitPrice = 1000000.00m` → validation error with message `"Unit price cannot exceed 999999.99"`.

## Non-Functional Requirements

### NFR-1: Performance
Tests must run fully in-memory with no I/O, database, or network dependency (the validator has no external dependencies), consistent with the rest of the `Anela.Heblo.Tests` suite. Full suite addition should add negligible runtime (<100ms total).

### NFR-2: Determinism
Date-boundary tests must compute expected boundaries relative to `DateTime.UtcNow` at test-execution time (mirroring the validator's own `DateTime.UtcNow.AddYears(...)` calls) rather than hardcoding absolute calendar dates, so tests remain correct indefinitely and are not subject to future flakiness or annual bit-rot.

### NFR-3: Isolation
No changes to production code (`UpdatePurchaseOrderRequestValidator.cs`, `UpdatePurchaseOrderRequest.cs`) are in scope — this is a test-only addition. If any of the acceptance criteria above reveal a genuine discrepancy between validator behavior and this spec (there should not be, per the source read), stop and flag it rather than silently adjusting the assertions.

## Data Model
No changes. Relevant existing types (unchanged):
- `UpdatePurchaseOrderRequest { int Id, long SupplierId, DateTime? ExpectedDeliveryDate, ContactVia? ContactVia, string? Notes, List<UpdatePurchaseOrderLineRequest> Lines, string? OrderNumber }`
- `UpdatePurchaseOrderLineRequest { int? Id, string MaterialId, string? Name, decimal Quantity, decimal UnitPrice, string? Notes }`

Both live in `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequest.cs`. Note these DTOs also carry `System.ComponentModel.DataAnnotations` attributes (`[Required]`, `[Range]`, `[StringLength]`) in addition to the FluentValidation rules under test; this spec covers only the FluentValidation validator layer (`UpdatePurchaseOrderRequestValidator` / `UpdatePurchaseOrderLineRequestValidator`), not the DataAnnotations layer, since that is what the coverage gap report identifies.

## API / Interface Design
No production API surface changes. Test-only interface: standard xUnit `[Fact]` / `[Theory]` test methods invoked via `dotnet test` and CI's existing coverage pipeline. No new public members are added to the validator classes.

## Dependencies
- `FluentValidation` and `FluentValidation.TestHelper` (already referenced by the test project, per `UpdateProductCompositionOrderRequestValidatorTests.cs`).
- `Xunit` (already in use).
- No new NuGet packages required.

## Out of Scope
- Testing `UpdatePurchaseOrderHandler` behavior (already covered by `UpdatePurchaseOrderHandlerTests.cs`).
- Testing the DataAnnotations attributes on the request DTOs (`[Range]`, `[Required]`, `[StringLength]`) — these are a separate, ASP.NET-model-binding-driven validation layer, not exercised by `AbstractValidator.TestValidate`.
- Testing `Id`, `SupplierId`, `Notes`, `OrderNumber` simple bound checks (`GreaterThan(0)`, `MaximumLength`) — not called out in the coverage-gap brief as high-risk; may be added opportunistically but are not required acceptance criteria for this ticket.
- Testing `UpdatePurchaseOrderLineRequestValidator`'s `Id`, `MaterialId`, `Name`, `Notes` rules beyond what's needed to build a valid baseline line item — not flagged in the brief.
- Any refactor of the validator's constants (e.g. extracting `2` and `-10` into named constants) — this ticket is test-only.
- Integration/E2E tests — this is a pure unit-test (FluentValidation) gap.

## Open Questions
None.

## Status: COMPLETE
