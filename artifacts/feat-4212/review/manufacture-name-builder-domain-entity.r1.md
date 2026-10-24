# Code Review: manufacture-name-builder-domain-entity

## Summary

The implementation follows the task spec's steps exactly: `IManufactureNameBuilder.Build`
and its implementation now take `ManufactureOrder` instead of `UpdateManufactureOrderDto`,
with no method-body logic change, and the test file's `CreateOrder` fixture was mechanically
rebuilt against `ManufactureOrder`/`ManufactureOrderSemiProduct`/`ManufactureOrderProduct`.
The two files were verified to compile cleanly and consistently in isolation; the only
remaining build errors are the exact, already-anticipated downstream caller breaks that
later tasks in this plan (`confirm-product-completion-workflow-repository-read`,
`confirm-semi-product-workflow-repository-read`) are scoped to fix.

## Review Result: PASS

### task: manufacture-name-builder-domain-entity
**Status:** PASS

## Docs to Update

(None — internal application-layer signature change with no public API or operational impact.)

## Overall Notes

- Property-for-property mapping verified directly against `ManufactureOrder`,
  `ManufactureOrderSemiProduct`, and `ManufactureOrderProduct` in
  `backend/src/Anela.Heblo.Domain/Features/Manufacture/`: `OrderNumber`, `SemiProduct`,
  `SemiProduct.ProductCode`/`ProductName`/`PlannedQuantity`/`ActualQuantity`, `Products`,
  `ProductCode`, `ProductName`, `SemiProductCode`, `PlannedQuantity`, `ActualQuantity` all
  exist with matching names and compatible nullability, so the substitution is safe and
  the 5 existing test bodies' assertions remain valid unchanged.
- `order.Products.All(...)` in `ManufactureNameBuilder.Build` resolves via `ImplicitUsings`
  (enabled in `Anela.Heblo.Application.csproj`), matching how the pre-change file already
  used LINQ without an explicit `using System.Linq;` — no regression there.
- Confirmed the diff touches exactly the two files the task spec names
  (`git diff --stat` against the base of this task shows only
  `ManufactureNameBuilder.cs` and `ManufactureNameBuilderTests.cs`) — the developer's
  temporary verification patches to the two not-yet-converted callers
  (`ConfirmProductCompletionWorkflow.cs`, `ConfirmSemiProductManufactureWorkflow.cs`) and
  their tests were confirmed fully reverted.
- Rebuilt `Anela.Heblo.Application.csproj` independently: exactly 3 `CS1503` errors remain,
  all `UpdateManufactureOrderDto`→`ManufactureOrder` mismatches at the two
  `IManufactureNameBuilder.Build` call sites (`ConfirmProductCompletionWorkflow.cs:150`,
  `ConfirmSemiProductManufactureWorkflow.cs:120`) plus the pre-existing, unrelated
  `IResidueDistributionCalculator.CalculateAsync` caller break at
  `ConfirmProductCompletionWorkflow.cs:77` inherited from the earlier
  `residue-distribution-calculator-domain-entity` task. No new/unexpected error type
  appeared — this is the intentional mid-plan state the task spec anticipates, same
  pattern the prior task's review documented and passed on.
