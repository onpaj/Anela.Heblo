# Implementation: residue-distribution-calculator-domain-entity

## What was implemented

Changed `IResidueDistributionCalculator.CalculateAsync` and its implementation
`ResidueDistributionCalculator` to take the domain entity `ManufactureOrder`
instead of the UI-facing `UpdateManufactureOrderDto`, per the task spec's
exact steps 1-2. No method-body logic changed — this is a pure parameter-type
substitution, since `ManufactureOrder`/`ManufactureOrderSemiProduct`/
`ManufactureOrderProduct` expose the same property names and nullability used
by the calculator (`ManufactureType`, `SemiProduct`, `SemiProduct.ProductCode`,
`SemiProduct.ActualQuantity`/`PlannedQuantity`, `Products`, `ProductCode`,
`ActualQuantity`, `PlannedQuantity`, `ProductName`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs` — interface signature now takes `ManufactureOrder`; removed the now-unused `UpdateManufactureOrder` using.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs` — `CalculateAsync` and private `BuildProductDataAsync` now take `ManufactureOrder`; removed the now-unused `UpdateManufactureOrder` using.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ResidueDistributionCalculatorTests.cs` — mechanical three-way rename throughout (inline object initializers, `BuildOrder`, `BuildOrderWithZeroProduct` helpers): `UpdateManufactureOrderDto` → `ManufactureOrder`, `UpdateManufactureOrderSemiProductDto` → `ManufactureOrderSemiProduct`, `UpdateManufactureOrderProductDto` → `ManufactureOrderProduct`; removed the now-unused `UpdateManufactureOrder` using. No assertions or parameter names/defaults changed.

## Tests

`ResidueDistributionCalculatorTests.cs` — all 12 existing test cases retained unchanged in behavior/assertions, only their fixture types renamed to match the new `ManufactureOrder`-based signature.

## How to verify

1. `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj` — per the task spec this is *expected* to fail, but only at the one remaining caller `ConfirmProductCompletionWorkflow.cs:77` (`_residueCalculator.CalculateAsync(updateResult.Order!, ...)`, `updateResult.Order` is still typed `UpdateManufactureOrderDto`). That caller is task `confirm-product-completion-workflow-repository-read`'s scope, not this task's.
2. To confirm this task's own three files are correct in isolation despite that expected downstream compile error, I temporarily replaced the one call-site argument in `ConfirmProductCompletionWorkflow.cs` with `default!` (verification only), ran `dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`, confirmed **zero errors** in `ResidueDistributionCalculatorTests.cs` (the only errors reported were 12 pre-existing errors in `ConfirmProductCompletionWorkflowTests.cs`, which belongs to a different, not-yet-done task), then reverted the temporary edit (`git diff` on that file now shows no changes — confirmed clean).
3. Once tasks `confirm-product-completion-workflow-repository-read` and `confirm-semi-product-workflow-repository-read` land, `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ResidueDistributionCalculatorTests"` is expected to pass all 12 tests unchanged (pure type substitution, no assertion values changed).

## Notes

Per the task spec's own Step 3, the Application project is expected to fail
to build at this point in the plan — only at the workflow call site(s), which
later tasks in this same feature fix. This task's own files
(`IResidueDistributionCalculator.cs`, `ResidueDistributionCalculator.cs`,
`ResidueDistributionCalculatorTests.cs`) compile cleanly in isolation, as
verified by the temporary-patch method described above. No deviations from
the task spec.

## PR Summary
Changed `IResidueDistributionCalculator`/`ResidueDistributionCalculator` to operate on the `ManufactureOrder` domain entity instead of the `UpdateManufactureOrderDto` application DTO, aligning the residue-distribution calculation with the rest of the domain-entity-based manufacture workflow refactor. This is a pure parameter-type substitution — no calculation logic changed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs` — interface now takes `ManufactureOrder`
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs` — implementation now takes `ManufactureOrder`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ResidueDistributionCalculatorTests.cs` — test fixtures renamed to the domain-entity types

## Status
DONE
