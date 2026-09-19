# Implementation: confirm-product-completion-workflow-repository-read

## What was implemented

`ConfirmProductCompletionWorkflow` now fetches the persisted `ManufactureOrder` domain
entity directly from `IManufactureOrderRepository` after the quantity update, instead of
relying on `UpdateManufactureOrderResponse.Order` (an `UpdateManufactureOrderDto`, an
HTTP-response shape) as its internal data carrier for the rest of the workflow. This
mirrors the pattern already used for `IResidueDistributionCalculator.CalculateAsync` and
`IManufactureNameBuilder.Build`, which were switched to accept `ManufactureOrder` in
earlier tasks of this feature.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`
  — added `IManufactureOrderRepository _repository` dependency; `ExecuteAsync` now calls
  `_repository.GetOrderByIdAsync(orderId, cancellationToken)` after the quantity update and
  returns a `ResourceNotFound` error if the order can't be found; `SubmitToErpAsync` and
  `UpdateBoMIngredientsAsync` now take `ManufactureOrder` instead of
  `UpdateManufactureOrderDto`; added `using Anela.Heblo.Application.Shared;` for
  `ErrorCodes`. Workflow steps renumbered (comment-only) to reflect the new fetch step.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs`
  — added `IManufactureOrderRepository` mock wired to the constructor and defaulted (via
  `GetOrderByIdAsync`) to a new `CreateOrder()` domain-entity fixture; replaced the DTO
  fixtures `CreateSuccessfulUpdateOrderResponseWithManyProducts` /
  DTO-literal-per-test usage with domain-entity equivalents (`CreateOrderWithManyProducts`,
  and per-test `_repositoryMock` overrides) for every test that previously built a custom
  `UpdateManufactureOrderDto` (`ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit`,
  `ExecuteAsync_WithDirectSemiproductRow_FiltersItFromErpItems`,
  `ExecuteAsync_WithDirectSemiproductRow_PassesDirectOutputFieldsInSubmitRequest`,
  `ExecuteAsync_SinglePhaseOrder_IncludesProductMatchingSemiproductAndEmitsNoDirectOutput`);
  mechanically renamed all 13 `_residueCalculatorMock.Setup(x => x.CalculateAsync(It.IsAny<...>` and
  `_nameBuilderMock.Setup(x => x.Build(...))` type arguments from `UpdateManufactureOrderDto` to
  `ManufactureOrder`.

## Tests

`ConfirmProductCompletionWorkflowTests` (14 tests) — all pass. Verified via a scoped,
temporary local build/test run (see Notes) since the full solution currently has one
pre-existing, out-of-scope compile error in a sibling file belonging to the next task.

## How to verify

```
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests"
```

Note: a full, unscoped `dotnet build`/`dotnet test` of the whole solution will currently
still fail with one error, entirely inside
`ConfirmSemiProductManufactureWorkflow.cs`/`ConfirmSemiProductManufactureWorkflowTests.cs`
(the next task in this plan, `confirm-semi-product-workflow-repository-read`, not yet
started) — `IResidueDistributionCalculator.CalculateAsync` and `IManufactureNameBuilder.Build`
were already switched to take `ManufactureOrder` in an earlier task, and
`ConfirmSemiProductManufactureWorkflow` still passes them an `UpdateManufactureOrderDto`.
This is pre-existing and unrelated to this task's diff (confirmed by `git stash`ing this
task's changes and re-running the build: the same error is present with or without this
diff).

## Notes

- The task-context's Step 7 said "there are 6" `It.IsAny<UpdateManufactureOrderDto>()`
  occurrences to rename; the file actually had 13 (12 `CalculateAsync` setups + 1
  `Build` setup already counted separately in Step 5). All were renamed — this is a purely
  mechanical type-argument change, not a logic change.
- Two additional tests not named in Step 6
  (`ExecuteAsync_WithDirectSemiproductRow_PassesDirectOutputFieldsInSubmitRequest` and
  `ExecuteAsync_SinglePhaseOrder_IncludesProductMatchingSemiproductAndEmitsNoDirectOutput`)
  also built custom `UpdateManufactureOrderDto` orders inline via
  `updateOrderResponse.Order`. Since the workflow no longer reads that field, leaving them
  as-is would have silently made these tests exercise the wrong order (the constructor's
  default `CreateOrder()` fixture) instead of the order shape they're specifically testing
  (a direct-semiproduct row / a SinglePhase order). Updated them the same way as the two
  tests Step 6 named explicitly, wiring their custom order into `_repositoryMock` instead.
- To verify locally despite the pre-existing sibling-file compile error, I temporarily
  cast one out-of-scope call site to `dynamic` and moved the sibling test file aside,
  built/ran only `ConfirmProductCompletionWorkflowTests`, then reverted both temporary
  changes via `git checkout --` before committing anything. No trace of that verification
  detour is in this diff.

## PR Summary
`ConfirmProductCompletionWorkflow` no longer threads `UpdateManufactureOrderDto` (the
`UpdateManufactureOrder` use case's HTTP-response DTO) through its internal steps. After
updating product quantities, it now re-fetches the `ManufactureOrder` domain entity from
`IManufactureOrderRepository` and passes that to the residue calculator, name builder, ERP
submission and BoM update steps — consistent with the domain-entity-based signatures those
collaborators already exposed after earlier tasks in this feature.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs` — added repository dependency; fetch `ManufactureOrder` post-update instead of using the response DTO
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs` — updated all fixtures/mocks to the domain entity

## Status
DONE
