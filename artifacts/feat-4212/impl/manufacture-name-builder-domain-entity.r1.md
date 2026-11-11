# Implementation: manufacture-name-builder-domain-entity

## What was implemented

`IManufactureNameBuilder.Build` (and its implementation `ManufactureNameBuilder.Build`)
now takes the domain entity `ManufactureOrder` instead of the application-layer DTO
`UpdateManufactureOrderDto`, per the task spec's mechanical signature change. No
method body logic changed — `order.SemiProduct`, `order.SemiProduct.ProductCode`,
`order.SemiProduct.ProductName`, and `order.Products.All(p => p.ProductCode == semiCode)`
all resolve identically on `ManufactureOrder` (verified property-for-property against
`ManufactureOrder`, `ManufactureOrderSemiProduct`, `ManufactureOrderProduct` in
`backend/src/Anela.Heblo.Domain/Features/Manufacture/`).

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs` — `IManufactureNameBuilder.Build` and its implementation now take `ManufactureOrder` instead of `UpdateManufactureOrderDto`; removed the now-unused `UpdateManufactureOrder` using directive.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs` — `CreateOrder` helper now builds a `ManufactureOrder`/`ManufactureOrderSemiProduct`/`ManufactureOrderProduct` graph instead of the `UpdateManufactureOrderDto` equivalents; removed the now-unused using directive. All 5 `[Fact]` bodies unchanged.

## Tests

`ManufactureNameBuilderTests.cs` — 5 existing tests, only their `CreateOrder` fixture type changed:
- `Build_WhenProductCodeShorterThan6Chars_DoesNotThrow`
- `Build_WhenSemiProduct_PrependsMSuffix`
- `Build_WhenSinglephaseProduct_ReturnsSemiCodeOnly`
- `Build_WhenResultExceeds40Chars_TruncatesSafely`
- `Build_WhenCodeShorterThanPrefix_UsesFullCode`

## How to verify

`cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureNameBuilderTests"`

This could not be run to a literal pass/fail in this session (see Notes) — verified
instead by the isolation technique described below, the same one the prior task
(`residue-distribution-calculator-domain-entity`) used for the same reason.

## Notes

- **Expected mid-plan compile breaks, not a regression.** This is one step of a
  multi-task plan that changes `ManufactureOrder`-adjacent interfaces bottom-up; the
  whole Application project (and therefore the Tests project) is *already* broken at
  baseline from the earlier `residue-distribution-calculator-domain-entity` task
  (`ConfirmProductCompletionWorkflow.cs(77,72)`, since its caller — the
  `confirm-product-completion-workflow-repository-read` task — hasn't run yet). This
  task's own signature change adds exactly two more, equally expected, downstream
  breaks at the two callers of `IManufactureNameBuilder.Build`:
  `ConfirmProductCompletionWorkflow.cs:150` and
  `ConfirmSemiProductManufactureWorkflow.cs:120` (plus the corresponding
  `It.IsAny<UpdateManufactureOrderDto>()` Moq setups in
  `ConfirmProductCompletionWorkflowTests.cs` and
  `ConfirmSemiProductManufactureWorkflowTests.cs`). These are fixed by the
  `confirm-product-completion-workflow-repository-read` and
  `confirm-semi-product-workflow-repository-read` tasks later in this plan, and
  `full-verification-and-cleanup` confirms the whole solution is green at the end.
- **Isolation verification performed:** to confirm this task's two files are
  correct and internally consistent in spite of the pre-existing/expected breaks
  elsewhere, I temporarily patched the not-yet-converted caller call sites (and the
  matching Moq `It.IsAny<>()` setups) in place, ran `dotnet build` on
  `Anela.Heblo.Application.csproj`, confirmed only the exact three expected
  `UpdateManufactureOrderDto`→`ManufactureOrder` mismatches remained (no new/different
  error types), then reverted every temporary patch with `git checkout --` before
  committing — leaving only the two files listed above changed. `git diff --stat`
  confirms no other file is modified by this commit.
- No other line in either file changed beyond the type substitutions called for by
  the task spec.

## PR Summary

`IManufactureNameBuilder.Build` now takes the `ManufactureOrder` domain entity
instead of the `UpdateManufactureOrderDto` application DTO, continuing this issue's
plan to move `ManufactureOrder`-related workflow helpers off DTOs and onto the
domain entity directly. No behavior change — only the parameter type and the test
fixture's construction type changed; all method-body logic and all 5 existing unit
tests are unchanged.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs` — `Build(ManufactureOrder, ErpManufactureType)` instead of `Build(UpdateManufactureOrderDto, ErpManufactureType)`
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs` — `CreateOrder` helper now builds a `ManufactureOrder` graph

## Status
DONE
