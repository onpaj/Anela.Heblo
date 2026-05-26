# Implementation: Refactor GetAvailableRuleTypes to MediatR Handler

## What was implemented

Purely structural refactor of the `InvoiceClassification` module. Moved the DTO projection logic out of `InvoiceClassificationController.GetAvailableRuleTypes()` into a new MediatR handler `GetClassificationRuleTypesHandler`. Removed the direct Domain dependency (`IEnumerable<IClassificationRule>`) from the API controller, restoring the correct API → Application → Domain dependency direction.

No behavior change. HTTP contract (route, verb, status code, JSON array shape) is identical.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/GetClassificationRuleTypesRequest.cs` — empty MediatR IRequest marker class
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/GetClassificationRuleTypesResponse.cs` — BaseResponse subclass carrying `List<ClassificationRuleTypeDto> RuleTypes`
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationRuleTypes/GetClassificationRuleTypesHandler.cs` — handler that ctor-injects `IEnumerable<IClassificationRule>` and projects to DTOs via `Task.FromResult`
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetClassificationRuleTypesHandlerTests.cs` — 3 xUnit tests (empty collection, multi-rule projection, order preservation)
- `backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs` — removed `IClassificationRule` field/constructor param/Domain using; added GetClassificationRuleTypes using; updated action to async MediatR dispatch with RuleTypes unwrap

## Tests

`backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/GetClassificationRuleTypesHandlerTests.cs`:
- `Handle_WithEmptyRuleCollection_ReturnsEmptyList` — verifies empty input → empty RuleTypes, Success=true
- `Handle_WithMultipleRules_ProjectsEachToDto` — 3 mocked rules → 3 DTOs with correct Identifier/DisplayName/Description
- `Handle_PreservesEnumerationOrder` — order of input rules preserved in output DTOs

All 3985 tests in the test suite pass.

## How to verify

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-arch-review-invoiceclassification-getava

# Build
dotnet build backend/Anela.Heblo.sln

# Handler tests
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~GetClassificationRuleTypesHandlerTests"

# Full test suite
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj

# Confirm no Domain reference remains in controller
grep -n "IClassificationRule\|_classificationRules\|Domain.Features.InvoiceClassification" \
  backend/src/Anela.Heblo.API/Controllers/InvoiceClassificationController.cs
# Expected: no output
```

## Notes

The controller action returns `Ok(response.RuleTypes)` (bare array) rather than `Ok(response)` (envelope object) — an intentional deviation from sibling actions. This preserves the existing HTTP contract. A comment in the action body explains the reason. The handler itself follows sibling conventions (returns `BaseResponse`-derived envelope).

No DI registration changes required — the new handler is auto-discovered via MediatR's `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` assembly scan in `ApplicationModule.cs`.

## PR Summary

Structural refactor eliminating the one remaining API → Domain layering violation in `InvoiceClassificationController`. The `GetAvailableRuleTypes` action was the only action in the file not using MediatR — it held a direct constructor dependency on `IEnumerable<IClassificationRule>` (a Domain interface) and performed DTO projection inline. This change moves the projection into a new `GetClassificationRuleTypesHandler` in the Application layer, making the controller a thin dispatcher consistent with all sibling actions.

The HTTP contract is unchanged: the controller unwraps `response.RuleTypes` to return the same bare JSON array as before (documented with an inline comment). The handler returns a `BaseResponse`-derived envelope internally, matching sibling handler conventions.

### Changes
- `GetClassificationRuleTypesRequest.cs` — new empty IRequest marker
- `GetClassificationRuleTypesResponse.cs` — new BaseResponse subclass with List<ClassificationRuleTypeDto> RuleTypes
- `GetClassificationRuleTypesHandler.cs` — new handler projecting IEnumerable<IClassificationRule> → DTOs via Task.FromResult
- `GetClassificationRuleTypesHandlerTests.cs` — 3 unit tests (empty, multi-rule, order)
- `InvoiceClassificationController.cs` — removed Domain dependency, updated action to MediatR dispatch

## Status
DONE
