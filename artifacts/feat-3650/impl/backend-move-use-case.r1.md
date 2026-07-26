# Implementation: backend-move-use-case

## What was implemented
Added a new, narrowly-scoped `MoveMarketingAction` MediatR use case that performs a date-only update of a `MarketingAction`, without ever touching `FolderLinks` or `ProductAssociations`. This fixes the bug where the calendar's drag/resize interactions reused `UpdateMarketingAction`, whose handler unconditionally called `ReplaceFolderLinks(request.FolderLinks?...)` — a `null`/omitted value there is treated as "clear everything," silently deleting every folder link on the action as a side effect of a pure date move.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MoveMarketingActionRequest.cs` — new `MoveMarketingActionRequest` (`Id`, `StartDate` [Required], `EndDate`) and `MoveMarketingActionResponse` (extends `BaseResponse`). Structurally cannot carry collection data.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` — new handler: auth check → `GetByIdAsync` → `action.UpdateDetails(...)` re-supplying existing title/description/actionType so only dates move → conditional Outlook sync (`UpdateEventAsync` only if `OutlookEventId` already set; never `CreateEventAsync`) → persist → return response. DB/Outlook failures mapped to existing `ErrorCodes`.
- `backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs` — added `PATCH {id}/move` action, gated by the same `[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]` as the existing `PUT {id}`.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/MoveMarketingActionHandlerTests.cs` — new handler unit tests.

## Tests
`MoveMarketingActionHandlerTests.cs` covers:
- Folder links and product associations are byte-for-byte unchanged after a move.
- Title/Description/ActionType unchanged; StartDate/EndDate updated.
- Unauthenticated request → `UnauthorizedMarketingAccess`, repository never called.
- Non-existent `Id` → `MarketingActionNotFound`.
- Synced Outlook event (`OutlookEventId` set) + push enabled → `UpdateEventAsync` invoked once.
- No `OutlookEventId` → `CreateEventAsync`/`UpdateEventAsync` never invoked.
- `SaveChangesAsync` failure → `DatabaseError`, no exception propagates.

Ran the full Marketing-scoped backend test suite: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing"` → **Passed! Failed: 0, Passed: 203, Skipped: 0, Total: 203**. Also ran a full `dotnet test Anela.Heblo.sln` earlier in the session to confirm no regressions outside the Marketing namespace.

## How to verify
1. `cd backend && dotnet build` — confirms the solution builds and regenerates the OpenAPI client (`frontend/src/api/generated/api-client.ts` now exposes `marketingCalendar_MoveMarketingAction` and `MoveMarketingActionRequest`/`MoveMarketingActionResponse`).
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing"` — all 203 Marketing tests pass.
3. Inspect `MoveMarketingActionHandler.Handle` — no call to `ReplaceProductAssociations` or `ReplaceFolderLinks` anywhere in the method body.

## Notes
- The generated `frontend/src/api/generated/api-client.ts` was regenerated as part of the backend build and is included in this commit so the frontend task can consume it directly.
- An unrelated, pre-existing warning appeared during build from the `Anela.Heblo.AccessMatrixGen` tool (`access-matrix.generated.json` JSON parse error, MSB3073, exit code 134) — this is a non-fatal build step unrelated to this change (it doesn't reference Marketing or the new files) and did not block compilation or test execution.

## PR Summary
Added a `MoveMarketingAction` MediatR use case (`PATCH /api/MarketingCalendar/{id}/move`) that updates only a marketing action's dates, reusing the existing `MarketingAction.UpdateDetails` domain method and deliberately never calling `ReplaceProductAssociations`/`ReplaceFolderLinks`. This gives the calendar's drag/resize flow a date-only endpoint to call instead of the full-replacement `UpdateMarketingAction`, closing the path where an omitted `folderLinks` field on a drag/resize payload was silently clearing all of an action's folder links.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MoveMarketingActionRequest.cs` — new request/response contracts
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` — new handler
- `backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs` — new `PATCH {id}/move` endpoint
- `backend/test/Anela.Heblo.Tests/Application/Marketing/MoveMarketingActionHandlerTests.cs` — new handler tests
- `frontend/src/api/generated/api-client.ts` — regenerated OpenAPI client (adds `marketingCalendar_MoveMarketingAction`)

## Status
DONE
