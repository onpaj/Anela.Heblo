# Implementation: switch-handler-to-reschedule

## What was implemented

Replaced the `action.UpdateDetails(...)` call in `MoveMarketingActionHandler.Handle` with a call to the new `action.Reschedule(...)` domain method (added in the prior task, `add-reschedule-domain-method`). The handler now expresses "move this marketing action" as a dedicated domain operation instead of piggy-backing on the general-purpose details-update method with unchanged title/description/actionType arguments.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` — replaced the `UpdateDetails(title, description, actionType, startDate, endDate, modifiedByUserId, modifiedByUsername, utcNow)` call with `Reschedule(startDate, endDate, modifiedByUserId, modifiedByUsername, utcNow)`. No other line in the file changed (auth check, not-found check, Outlook push block, DB save error handling, logging, and response construction are untouched).

## Tests

No new test file for this task (per the task context, FR-2's acceptance criteria are verified by the existing `MoveMarketingActionHandlerTests.cs` suite continuing to pass unmodified). Ran that suite before and after the change:

- Baseline (before edit): 11/11 passed.
- After edit: 11/11 passed, same set of facts (`Handle_LeavesFolderLinksAndProductAssociationsUnchanged`, `Handle_LeavesTitleDescriptionActionTypeUnchanged`, `Handle_UpdatesStartAndEndDate`, `Handle_ReturnsUnauthorized_WhenUserIsNotAuthenticated`, `Handle_ReturnsNotFound_WhenActionDoesNotExist`, `Handle_UpdatesOutlookEvent_WhenActionHasEventIdAndPushEnabled`, `Handle_SkipsOutlookSync_WhenActionHasNoEventId`, `Handle_SkipsOutlook_WhenPushDisabled`, `Handle_ReturnsForbiddenError_WhenOutlookUpdateThrows403`, `Handle_ReturnsSyncError_WhenOutlookUpdateThrowsNon403`, `Handle_ReturnsDatabaseError_WhenDbSaveFails`).

Also ran the full backend test suite (`dotnet test`): 7158 passed, 4 skipped, 110 failed (195 individual result lines counting theory cases) — every failure is a pre-existing environment-only failure unrelated to this change: Docker/Testcontainers unavailable in this sandbox (`LeafletRepositoryIntegrationTests`, `LeafletDocumentRepositoryPagedTests`, `GridLayoutRepositoryUpsertIntegrationTests`, various SQL-shape/persistence integration tests, etc.) and live external-API integration tests that require Flexi/Shoptet connectivity or fixtures not configured in this sandbox (`FlexiCatalogSalesClientIntegrationTests`, `ShoptetApiInvoiceSourceIntegrationTests`, etc.). None of the failing tests reference Marketing or `MoveMarketingAction`/`MarketingAction`. This matches the baseline failure set for this environment (no Docker daemon, no live Flexi/Shoptet credentials) and is unrelated to this task's change.

## How to verify

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MoveMarketingActionHandlerTests"
dotnet build
dotnet format --verify-no-changes
```

All three succeed: 11/11 tests pass, build has 0 errors (79 pre-existing nullable warnings unrelated to this file), and `dotnet format --verify-no-changes` reports no violations.

## Notes

- No deviations from the task context — the exact replacement specified in Step 2 was applied verbatim.
- The full-suite `dotnet test` run in this sandbox has 110 pre-existing failures caused by missing Docker/live external API access, not by this change; verified by name-matching every failure and confirming none touch Marketing code.

## PR Summary

`MoveMarketingActionHandler` now calls the new `MarketingAction.Reschedule` domain method instead of `UpdateDetails` when moving an action's start/end dates, so the "move" use case no longer masquerades as a general details update that happens to pass through unchanged title/description/actionType values.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` — switched the reschedule call site from `UpdateDetails` to `Reschedule`

## Status
DONE
