# Code Review: residue-distribution-calculator-domain-entity

## Summary

The implementation follows the task spec's 6 steps exactly: the interface and
implementation's `CalculateAsync`/`BuildProductDataAsync` signatures now take
`ManufactureOrder` instead of `UpdateManufactureOrderDto`, with no logic
change, and the test file's fixtures were mechanically renamed to match. The
one expected downstream compile error (the still-unconverted caller in
`ConfirmProductCompletionWorkflow.cs`) was verified to be isolated to that
caller and a separate not-yet-done task's test file, confirming this task's
own three files compile and are internally consistent.

## Review Result: PASS

### task: residue-distribution-calculator-domain-entity
**Status:** PASS

## Docs to Update

(None — internal application-layer signature change with no public API or operational impact.)

## Overall Notes

- Property-for-property mapping was verified directly against `ManufactureOrder`, `ManufactureOrderSemiProduct`, and `ManufactureOrderProduct` in `backend/src/Anela.Heblo.Domain/Features/Manufacture/`: `ManufactureType`, `SemiProduct`, `SemiProduct.ProductCode`/`ActualQuantity`/`PlannedQuantity`, `Products`, `ProductCode`, `ActualQuantity`, `PlannedQuantity`, `ProductName` all exist with matching names and nullability, so the substitution is safe.
- The task spec itself anticipates the Application project failing to build at this point (only at the workflow caller site) — this is intentional mid-plan state, not a regression, and is fixed by the later `confirm-product-completion-workflow-repository-read` task.
- No unrelated files were touched; the temporary verification patch to `ConfirmProductCompletionWorkflow.cs` used to isolate this task's compile surface was fully reverted (confirmed via `git diff` showing no changes to that file).
