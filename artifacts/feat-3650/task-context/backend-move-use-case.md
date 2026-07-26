### task: backend-move-use-case

## Goal
Add a new, narrowly-scoped `MoveMarketingAction` MediatR use case (request/response contracts, handler, controller endpoint) that performs a date-only update of a `MarketingAction` — without ever touching `FolderLinks` or `ProductAssociations` — plus a handler unit test that locks in this guarantee. This fixes the underlying bug where the calendar's drag/resize interactions currently reuse `UpdateMarketingAction`, whose handler unconditionally calls `ReplaceFolderLinks(request.FolderLinks?...)`, and a `null`/omitted value there is treated as "clear everything" — silently deleting every folder link on the action as a side effect of a pure date move.

## Context
**Root cause:** `UpdateMarketingActionHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`, lines ~95–98) unconditionally calls `action.ReplaceProductAssociations(...)` and `action.ReplaceFolderLinks(request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)), now)`. `MarketingAction.ReplaceFolderLinks` (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`, lines ~174–203) documents and implements `null` as "clear all links." This is correct for the full-edit modal (which always loads the complete `MarketingActionDto` including `folderLinks`), but wrong for the calendar's drag/resize path, which only ever has a `MarketingActionCalendarDto` (no `folderLinks` field at all).

**Fix approach (architecturally reviewed and approved):** rather than adding conditional "partial update" branching to the shared handler (rejected — would need nullable-wrapper sentinel types not used elsewhere in the codebase, and re-introduces "omitted vs. explicitly cleared" ambiguity at every future call site), add a dedicated `MoveMarketingAction` use case whose request type structurally cannot carry collection data. This exactly mirrors the existing per-verb use case pattern in this module (`CreateMarketingAction`, `UpdateMarketingAction`, `DeleteMarketingAction`, `GetMarketingAction(s)`, `GetMarketingCalendar`, `ImportFromOutlook` — each is `UseCases/{UseCase}/{UseCase}Handler.cs` + `Contracts/{UseCase}Request.cs`).

**Domain method reuse:** `MarketingAction.UpdateDetails(title, description, actionType, startDate, endDate, modifiedByUserId, modifiedByUsername, utcNow)` (`MarketingAction.cs`, lines ~235–253) already only mutates `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername` — it never touches `ProductAssociations`/`FolderLinks`. The bug lives entirely in the handler layer (which additionally calls the two `Replace*` methods), not in this domain method. So: do **not** add a new `MoveDates` domain method — reuse `UpdateDetails`, re-supplying the entity's own current `Title`/`Description`/`ActionType` unchanged so only the dates actually move.

**Error codes already exist** in `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs` — no new codes needed: `UnauthorizedMarketingAccess`, `MarketingActionNotFound`, `MarketingCalendarAccessDenied`, `MarketingCalendarSyncFailed`, `DatabaseError`.

**No FluentValidation validator needed** — this module has no FluentValidation validators today; `[Required]` data annotations on `StartDate` are consistent with how `UpdateMarketingActionRequest`/`CreateMarketingActionRequest` validate.

**MediatR registration is automatic** (assembly scan in `MarketingModule.AddMarketingModule`) — no DI wiring change needed once the handler implements `IRequestHandler<MoveMarketingActionRequest, MoveMarketingActionResponse>`.

**DTOs are classes, never records** (project-wide rule — OpenAPI client generators mishandle record parameter order).

## Files to create/modify
- `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MoveMarketingActionRequest.cs` — NEW. Contains `MoveMarketingActionRequest` and `MoveMarketingActionResponse`.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` — NEW. Handler implementation.
- `backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs` — MODIFIED. Add the `MoveMarketingAction` controller action (nothing existing removed).
- `backend/test/Anela.Heblo.Tests/Application/Marketing/MoveMarketingActionHandlerTests.cs` — NEW. Follow the conventions of the sibling `UpdateMarketingActionHandlerTests.cs` in the same folder (not `backend/test/Anela.Heblo.Tests/Features/Marketing/` — that folder split is inconsistent already; match the direct sibling being modeled against).

## Implementation steps
1. Create `MoveMarketingActionRequest.cs`:
   ```csharp
   public class MoveMarketingActionRequest : IRequest<MoveMarketingActionResponse>
   {
       public int Id { get; set; }

       [Required]
       public DateTime StartDate { get; set; }

       public DateTime? EndDate { get; set; }
   }

   public class MoveMarketingActionResponse : BaseResponse
   {
       public int Id { get; set; }
       public DateTime ModifiedAt { get; set; }
       public string Message { get; set; } = "Marketing action moved successfully";

       public MoveMarketingActionResponse() : base() { }
       public MoveMarketingActionResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
           : base(errorCode, parameters) { }
   }
   ```
   Note: `MoveMarketingActionRequest` carries only `Id`/`StartDate`/`EndDate` — deliberately no `Title`, `Description`, `ActionType`, `AssociatedProducts`, or `FolderLinks` fields. Do not add any of these later "for convenience" — that would reopen the exact bug this task fixes.
2. Create `MoveMarketingActionHandler.cs`, modeled on `UpdateMarketingActionHandler` (same constructor-injected dependencies: `IMarketingActionRepository`, `ICurrentUserService`, `IOutlookCalendarSync`, `IOptionsMonitor<MarketingCalendarOptions>`, `ILogger<MoveMarketingActionHandler>`), implementing `IRequestHandler<MoveMarketingActionRequest, MoveMarketingActionResponse>` with this control flow:
   1. Resolve current user via `ICurrentUserService`. If not authenticated, return `new MoveMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess)` **without calling the repository**.
   2. `await _repository.GetByIdAsync(request.Id)`. If not found, return `new MoveMarketingActionResponse(ErrorCodes.MarketingActionNotFound)`.
   3. Call `action.UpdateDetails(title: action.Title, description: action.Description, actionType: action.ActionType, startDate: request.StartDate, endDate: request.EndDate, modifiedByUserId: currentUser.Id, modifiedByUsername: currentUser.Name, utcNow: now)`. Re-supply the entity's own current title/description/actionType unchanged — only dates change.
   4. Do **not** call `ReplaceProductAssociations` or `ReplaceFolderLinks` anywhere in this handler. This is the entire point of the fix — no branch should exist that reaches them.
   5. If `_options.CurrentValue.PushEnabled` is true **and** `action.OutlookEventId` is already set (non-null): call `await _outlookCalendarSync.UpdateEventAsync(action, ct)`, then `action.MarkOutlookSynced(...)` (same as `UpdateMarketingActionHandler`). If `OutlookEventId` is absent, skip Outlook sync entirely — do **not** call `CreateEventAsync`. This is a deliberate divergence from `UpdateMarketingActionHandler` (which has both create and update branches) — be careful not to copy-paste the create branch too. On `OutlookCalendarSyncException`, map to `MarketingCalendarAccessDenied`/`MarketingCalendarSyncFailed` exactly as `UpdateMarketingActionHandler` does.
   6. `await _repository.UpdateAsync(action)` then `await _repository.SaveChangesAsync()` (or the module's equivalent unit-of-work call — match `UpdateMarketingActionHandler`'s exact call sequence). On failure, catch and return `new MoveMarketingActionResponse(ErrorCodes.DatabaseError)` — do not let the exception propagate past the handler. Match `UpdateMarketingActionHandler`'s catch block and log message shape.
   7. Log at `Information` level: `"MarketingAction {ActionId} moved by user {UserId}"`.
   8. Return `new MoveMarketingActionResponse { Id = action.Id, ModifiedAt = action.ModifiedAt }` on success.
3. In `MarketingCalendarController.cs`, add a new controller action (do not modify or remove any existing action):
   ```csharp
   /// <summary>
   /// Move (reschedule) a marketing action — date-only update used by calendar drag/resize.
   /// Does not modify title, description, action type, product associations, or folder links.
   /// </summary>
   [HttpPatch("{id:int}/move")]
   [FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]
   [ProducesResponseType(typeof(MoveMarketingActionResponse), StatusCodes.Status200OK)]
   [ProducesResponseType(StatusCodes.Status404NotFound)]
   [ProducesResponseType(StatusCodes.Status401Unauthorized)]
   public async Task<ActionResult<MoveMarketingActionResponse>> MoveMarketingAction(
       int id,
       [FromBody] MoveMarketingActionRequest request)
   {
       request.Id = id;
       var response = await _mediator.Send(request);
       return HandleResponse(response);
   }
   ```
   Route: `PATCH /api/MarketingCalendar/{id}/move`. Same authorization gate as `PUT {id}`/`DELETE {id}` on this controller — no new permission is introduced. The route `{id}` always overwrites any `id` present in the request body, matching the existing `PUT {id}` convention.
4. Build the backend (`dotnet build`) so the OpenAPI spec regenerates and the new endpoint/types are picked up (this is required as a prerequisite for the frontend task, even though this task itself doesn't touch frontend code).

## Tests to write
In `MoveMarketingActionHandlerTests.cs`, following `UpdateMarketingActionHandlerTests.cs` conventions (mocking framework, fixture/seed style, assertion style):
- Seed an action with 2+ folder links and 1+ product associations; call the handler with a request containing only `Id`/`StartDate`/`EndDate`; assert `FolderLinks` and `ProductAssociations` on the persisted entity are byte-for-byte unchanged after the move.
- Assert `Title`, `Description`, `ActionType` are unchanged after a move.
- Assert `StartDate`/`EndDate` reflect the request values after the move.
- Unauthenticated request (mock `ICurrentUserService` returns not-authenticated) returns `UnauthorizedMarketingAccess` and the repository is never called (verify no `GetByIdAsync`/`UpdateAsync` invocation).
- Request for a non-existent `Id` (mock `GetByIdAsync` returns null) returns `MarketingActionNotFound`.
- When the action has a synced Outlook event (`OutlookEventId` set) and push is enabled, assert `IOutlookCalendarSync.UpdateEventAsync` is invoked exactly once with the action carrying the updated dates.
- When the action has no `OutlookEventId`, assert `IOutlookCalendarSync.CreateEventAsync` and `UpdateEventAsync` are **never** invoked.
- Mock `SaveChangesAsync` (or repository equivalent) to throw/fail; assert the handler returns `ErrorCodes.DatabaseError` and does not throw past the handler.
- Add/confirm a test that `UpdateMarketingActionHandler`'s existing full-replace behavior is unchanged (no regression) — this may already exist; verify it still passes, don't duplicate if already covered.

## Acceptance criteria
- `MoveMarketingActionRequest` has no `Title`/`Description`/`ActionType`/`AssociatedProducts`/`FolderLinks` members — structurally impossible to send collection data through this endpoint.
- `MoveMarketingActionResponse` follows the `BaseResponse` error-code pattern (`Success`, `ErrorCode`, `Parameters`) used by every other response in this module.
- Endpoint reachable at `PATCH /api/MarketingCalendar/{id}/move`, gated by `[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]` (401/403 behavior consistent with existing `PUT {id}`).
- All new unit tests pass; no existing `UpdateMarketingActionHandler` test regresses.
- `dotnet build` and `dotnet format` succeed with no new warnings/errors introduced by these files.
- After building, the generated OpenAPI/TypeScript client (regenerated automatically per `docs/development/api-client-generation.md`) exposes `marketingCalendar_MoveMarketingAction(id, request)` and a `MoveMarketingActionRequest` TS class — verify this exists before starting the frontend task.
