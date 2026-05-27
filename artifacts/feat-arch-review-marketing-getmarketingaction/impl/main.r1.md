All tasks complete. Here is the output artifact:

---

# Implementation: Extract MarketingAction → MarketingActionDto mapping to DTO factory

## What was implemented

A pure structural refactor moving the `MarketingAction → MarketingActionDto` projection out of `GetMarketingActionsHandler.MapToDto` into a new `public static MarketingActionDto FromEntity(MarketingAction action)` factory method on the DTO itself. Both handlers now call the single shared factory; the old cross-handler dependency is gone.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MarketingActionDto.cs` — added `FromEntity` static factory (verbatim projection from `MapToDto`); added `using System.Linq` and `using Anela.Heblo.Domain.Features.Marketing`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingAction/GetMarketingActionHandler.cs` — removed cross-handler `using` and replaced `GetMarketingActionsHandler.MapToDto(action)` with `MarketingActionDto.FromEntity(action)`
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/GetMarketingActions/GetMarketingActionsHandler.cs` — replaced `Select(MapToDto)` with `Select(MarketingActionDto.FromEntity)`; deleted `internal static MapToDto` method
- `backend/test/Anela.Heblo.Tests/Application/Marketing/MarketingActionDtoTests.cs` (new) — parity test exercising all 16 fields, enum-to-string projections, and `Distinct()` deduplication

## Tests
- `MarketingActionDtoTests.FromEntity_ProjectsAllFields_ForFullyPopulatedAction` — new; asserts all 16 DTO fields map correctly including deduplication
- All existing Marketing handler tests pass (138 tests)

## How to verify
```bash
dotnet build
dotnet format --verify-no-changes
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing"
grep -R "GetMarketingActionsHandler.MapToDto" backend/   # must return nothing
```

## Notes
- The `"NotSynced"` default on `OutlookSyncStatus` was pre-existing in the DTO; not introduced by this refactor
- Pre-existing test failure in `LocalizationCoverageTests` is unrelated to this change
- Follow-up candidates: `CreateLotHandler.MapToDto` and `CreateEansHandler.MapToDto` have the same structural issue but are explicitly out of scope for this PR

## PR Summary

Moves `MarketingAction → MarketingActionDto` projection out of `GetMarketingActionsHandler.MapToDto` into a static factory `MarketingActionDto.FromEntity` on the DTO itself, so both the single-item and list handlers consume it without cross-handler coupling. This eliminates a Single Responsibility violation flagged by the daily arch-review: the list handler was previously the owner of shared mapping logic.

The factory body is a verbatim copy of the old `MapToDto` — byte-identical projection, no behaviour change. A new parity unit test pins all 16 fields including `Distinct()` deduplication and enum-to-string projections.

### Changes
- `MarketingActionDto.cs` — new `FromEntity` static factory
- `GetMarketingActionHandler.cs` — removed cross-handler dependency
- `GetMarketingActionsHandler.cs` — switched to `FromEntity`, deleted `MapToDto`
- `MarketingActionDtoTests.cs` — new parity test

**PR:** https://github.com/onpaj/Anela.Heblo/pull/1804

## Status
DONE