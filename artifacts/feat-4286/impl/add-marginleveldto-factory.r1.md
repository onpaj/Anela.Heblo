# Implementation: add-marginleveldto-factory

## What was implemented
Added a static factory method `MarginLevelDto.FromDomain(MarginLevel level)` that
centralizes the field-by-field copy (`Percentage`, `Amount`, `CostLevel`,
`CostTotal`) from the domain `MarginLevel` type to the `MarginLevelDto` contract.
This is the shared mapping that the two follow-up tasks will use to replace the
12 duplicated inline `new MarginLevelDto { ... }` constructions in
`GetProductMarginsHandler.cs` and `GetCatalogDetailHandler.cs`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs` — added `using Anela.Heblo.Domain.Features.Catalog;` and the `FromDomain` static factory method.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs` — new test file with 2 tests.

## Tests
- `MarginLevelDtoTests.FromDomain_CopiesAllFourFieldsVerbatim` — verifies all four fields are forwarded verbatim from a non-zero `MarginLevel`.
- `MarginLevelDtoTests.FromDomain_ZeroLevel_ProducesZeroDto` — verifies `MarginLevel.Zero` maps to a zeroed DTO.

Both tests pass: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`.

## How to verify
```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarginLevelDtoTests"
```

## Notes
No behavior change — this task only adds the factory method; the 12 call sites
in the two handlers are replaced by the next two tasks in the plan.

## PR Summary
Added `MarginLevelDto.FromDomain(MarginLevel)`, a static factory centralizing
the field-by-field copy used at every M0-M3 call site across the Catalog
module's margin handlers, with two new unit tests covering the non-zero and
zero cases.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/MarginLevelDto.cs` — added `FromDomain` factory method
- `backend/test/Anela.Heblo.Tests/Features/Catalog/MarginLevelDtoTests.cs` — new test file

## Status
DONE
