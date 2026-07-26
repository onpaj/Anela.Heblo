# Code Review: backend-move-use-case

## Summary
The implementation adds a narrowly-scoped `MoveMarketingAction` MediatR use case (contracts, handler, controller endpoint) and a matching unit test suite exactly as specified. Verified directly against source: the request type structurally excludes `Title`/`Description`/`ActionType`/`AssociatedProducts`/`FolderLinks`, the handler never calls `ReplaceProductAssociations`/`ReplaceFolderLinks`, the Outlook sync branch correctly diverges from `UpdateMarketingActionHandler` (update-only, never create), and the controller endpoint matches the spec's route/attributes/behavior exactly. Build and the full Marketing-scoped test suite (203 tests) pass.

## Review Result: PASS

### task: backend-move-use-case
**Status:** PASS

## Verification performed
- Read `MoveMarketingActionRequest.cs`: `Id`, `[Required] StartDate`, `EndDate` only — no collection or free-text fields, matching spec verbatim. `MoveMarketingActionResponse` extends `BaseResponse` with the same constructor shape as sibling responses.
- Read `MoveMarketingActionHandler.cs`: auth check (returns `UnauthorizedMarketingAccess` without touching the repository) → `GetByIdAsync` (returns `MarketingActionNotFound` if null) → `action.UpdateDetails(action.Title, action.Description, action.ActionType, request.StartDate, request.EndDate, ...)` (re-supplies existing values, only dates actually move) → conditional Outlook sync (`UpdateEventAsync` only when `PushEnabled && OutlookEventId` is set; `CreateEventAsync` is never referenced anywhere in this file, correctly avoiding the divergence risk called out in the arch review) → `UpdateAsync`/`SaveChangesAsync` wrapped in try/catch returning `DatabaseError` → info log → success response with `Id`/`ModifiedAt`. No call to `ReplaceProductAssociations` or `ReplaceFolderLinks` exists anywhere in the file (confirmed via full read, not just grep).
- Confirmed `MarketingAction.UpdateDetails` (domain, lines 235–253) only mutates `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername` — never `ProductAssociations`/`FolderLinks`. Reuse is safe per spec's Decision 2.
- Read `MarketingCalendarController.cs`: new `[HttpPatch("{id:int}/move")]` action, `[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]` — identical gate to `PUT {id}`/`DELETE {id}`. Route `id` overwrites `request.Id`, matching the `PUT {id}` convention. No existing actions modified or removed.
- Read `MoveMarketingActionHandlerTests.cs`: all required scenarios from the task spec are present and use the sibling `MarketingActionTestBuilder`/`TestOptionsMonitor` test infrastructure consistently: folder-links/product-associations byte-for-byte unchanged, title/description/actionType unchanged, dates updated, unauthenticated → `UnauthorizedMarketingAccess` with repository never invoked, not-found → `MarketingActionNotFound`, Outlook update invoked once when `OutlookEventId` set + push enabled (and `CreateEventAsync` never invoked), Outlook sync fully skipped when `OutlookEventId` absent, DB failure → `DatabaseError`. Three additional tests beyond the spec's list (push-disabled skip, 403→access-denied mapping, non-403→sync-failed mapping) are present as bonus coverage, not a gap.
- Ran `dotnet build Anela.Heblo.sln` from the worktree root: succeeds (only pre-existing warnings in unrelated files, e.g. `ShoptetShipmentClientTests.cs`, `GetFinancialOverviewHandlerTests.cs`).
- Ran `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Marketing" --no-build`: **Passed! Failed: 0, Passed: 203, Skipped: 0, Total: 203** — confirms no regression to `UpdateMarketingActionHandlerTests` or any other Marketing test.
- Confirmed `frontend/src/api/generated/api-client.ts` contains `marketingCalendar_MoveMarketingAction(id, request)`, `class MoveMarketingActionRequest`, and `class MoveMarketingActionResponse` — satisfies the acceptance criterion that the frontend task can consume the regenerated client.
- Confirmed all changes are committed on the feature branch (`dbb16a4`, `1ccbb3d`), consistent with the pipeline's expectations.

## Docs to Update
None — no project documentation describes this endpoint elsewhere, and none of the changes contradict existing docs (`docs/architecture/development_guidelines.md`'s DTO-as-class rule is followed).

## Overall Notes
Implementation is a clean, minimal-diff match to both the task spec and the architecture review's decisions (new use case over conditional branching, `UpdateDetails` reuse over a new domain method, new `PATCH .../move` endpoint over route reuse). No issues found; nothing to flag for revision.
