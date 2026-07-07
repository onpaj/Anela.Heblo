# Implementation: use-imapper-in-classification-history-handler

## What was implemented
Replaced the 17-line manual `Select` projection in `GetClassificationHistoryHandler.Handle` with a single `_mapper.Map<List<ClassificationHistoryDto>>(historyItems)` call, injecting `IMapper` via the constructor (matching the sibling `GetClassificationRulesHandler`). The existing `InvoiceClassificationMappingProfile` already fully covers the `ClassificationHistory` → `ClassificationHistoryDto` mapping (including `InvoiceId ← AbraInvoiceId` and null-safe `RuleName ← ClassificationRule?.Name`), so no profile changes were needed.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationHistory/GetClassificationHistoryHandler.cs` — added `using AutoMapper;`, added `private readonly IMapper _mapper;` field, updated constructor to accept and assign `IMapper mapper` (order: repository, mapper, logger), replaced the manual projection with `_mapper.Map<List<ClassificationHistoryDto>>(historyItems)`. Response envelope and repository call untouched.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationMappingProfileTests.cs` — added two tests: `Map_ClassificationHistory_To_Dto_WithClassificationRule_MapsInvoiceIdAndRuleName` (non-null `ClassificationRule`, set via reflection since the navigation property has a private setter — consistent with the existing pattern used in `ClassificationHistoryRepositoryTests.cs`) and `Map_ClassificationHistory_To_Dto_WithoutClassificationRule_RuleNameIsNull` (no rule set). Both assert `InvoiceId` comes from `AbraInvoiceId` and `RuleName` behaves correctly (populated vs. null).

## Tests
- `InvoiceClassificationMappingProfileTests.cs` — added the two new `ClassificationHistory` mapping tests described above, exercising the exact mapping now used by the handler.
- Full InvoiceClassification test suite run: `dotnet test --filter "FullyQualifiedName~InvoiceClassification"` → 88 passed, 0 failed.

## How to verify
1. `cd backend && dotnet build ../Anela.Heblo.sln` (or from repo root: `dotnet build Anela.Heblo.sln`) — succeeds, 0 errors.
2. `dotnet format Anela.Heblo.sln --include backend/src/.../GetClassificationHistoryHandler.cs backend/test/.../InvoiceClassificationMappingProfileTests.cs --verify-no-changes` — reports no changes (clean).
3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceClassification"` — 88/88 pass.

## Notes
- `ClassificationHistory.ClassificationRule` has a private setter (EF navigation property); the new tests set it via reflection, mirroring the existing convention already used in `ClassificationHistoryRepositoryTests.cs` for `Timestamp`.
- No changes made to `InvoiceClassificationMappingProfile`, `ClassificationHistoryDto`, the domain entity, repository, contracts, controller, or DI registration, per scope constraints.
- `artifacts/` directory left untouched (not staged/committed) by the developer subagent.

## PR Summary
`GetClassificationHistoryHandler` now delegates its DTO mapping to `IMapper` instead of hand-rolling a 17-line `Select` projection, bringing it in line with its sibling `GetClassificationRulesHandler` and eliminating a duplicate of the mapping logic already defined in `InvoiceClassificationMappingProfile`. Added unit test coverage in `InvoiceClassificationMappingProfileTests` for both the rule-present and rule-null cases to lock in the null-safe `RuleName` behavior and the `InvoiceId ← AbraInvoiceId` rename.

### Changes
- `backend/src/Anela.Heblo.Application/Features/InvoiceClassification/UseCases/GetClassificationHistory/GetClassificationHistoryHandler.cs` — inject `IMapper`, replace manual projection with `_mapper.Map<List<ClassificationHistoryDto>>(...)`.
- `backend/test/Anela.Heblo.Tests/Features/InvoiceClassification/InvoiceClassificationMappingProfileTests.cs` — add two tests covering `ClassificationHistory` → `ClassificationHistoryDto` mapping with and without a linked rule.

## Status
DONE
