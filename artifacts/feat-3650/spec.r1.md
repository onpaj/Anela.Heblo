# Specification: Marketing Calendar Drag/Resize Silently Deletes Folder Links

## Summary
Dragging or resizing a marketing action on the Marketing Calendar currently reuses the full-replacement `UpdateMarketingAction` endpoint, sending a payload built from the lightweight calendar DTO that has no `folderLinks` field. Because the handler always calls `ReplaceFolderLinks(request.FolderLinks?.Select(...), now)`, a missing/`null` value is treated as "clear everything," permanently deleting every folder link on the action as a side effect of a pure date move. This spec introduces a dedicated, narrowly-scoped `MoveMarketingAction` use case (handler + contracts + controller endpoint) for date-only updates, and repoints the calendar's drag/resize handlers at it instead of `UpdateMarketingAction`.

## Background
`UpdateMarketingAction` is designed for the full edit modal, where the user has loaded the complete `MarketingActionDto` (including `associatedProducts` and `folderLinks`) and explicitly manages those collections. It therefore applies full-replacement semantics to both collections on every call — appropriate there, but not for the calendar's drag-and-resize interactions, which only ever fetch and mutate `MarketingActionCalendarDto` (id, title, actionType, startDate, endDate, associatedProducts, outlookSyncStatus — no `folderLinks` at all).

`frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (`handleEventMove`/`handleEventResize`, lines 207–230) builds an `UpdateMarketingActionRequest` from the in-memory `CalendarEvent`, omitting `folderLinks` because the source DTO never had it. On the backend, `UpdateMarketingActionHandler.Handle` (lines 95–98) unconditionally calls `action.ReplaceFolderLinks(request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)), now)`. `MarketingAction.ReplaceFolderLinks` (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`, lines 174–203) documents and implements `null` as "clear all links" — so every drag or resize wipes out OneDrive/file-storage folder links, and the deletion is committed in the same `SaveChangesAsync` call with no way to recover through the UI.

This is a Single Responsibility violation: one handler is serving two conceptually different operations — a full edit (title/description/type/dates/products/folders) and a date-only move — and there's no way at the handler level to tell which one is happening. The fix is to give the date-only move its own use case that never touches collections it wasn't given, rather than trying to patch `UpdateMarketingActionHandler` with conditional logic.

## Functional Requirements

### FR-1: New `MoveMarketingAction` use case (request/response contracts)
Add `MoveMarketingActionRequest` / `MoveMarketingActionResponse` in `backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MoveMarketingActionRequest.cs`, following the existing contract style used by `UpdateMarketingActionRequest`/`DeleteMarketingActionRequest` in the same folder (class-based DTOs, `BaseResponse`-derived response, `IRequest<TResponse>` — not the bare `IRequest` shown in the brief's illustrative snippet, to stay consistent with every other use case in this module).

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

**Acceptance criteria:**
- `MoveMarketingActionRequest` carries only `Id`, `StartDate`, `EndDate` — no `Title`, `Description`, `ActionType`, `AssociatedProducts`, or `FolderLinks` fields exist on the type, so it is structurally impossible to send collection data through this endpoint.
- `MoveMarketingActionResponse` follows the `BaseResponse` error-code pattern used by every other response in this module (`Success`, `ErrorCode`, `Parameters`).

### FR-2: `MoveMarketingActionHandler` — date-only update, no collection replacement
Add `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs`, modeled on `UpdateMarketingActionHandler` but stripped to the date-move concern:

1. Resolve the current user via `ICurrentUserService`; if not authenticated, return `ErrorCodes.UnauthorizedMarketingAccess` (same code/shape as `UpdateMarketingActionHandler`).
2. Load the action via `IMarketingActionRepository.GetByIdAsync`; if not found, return `ErrorCodes.MarketingActionNotFound`.
3. Call `action.UpdateDetails(title: action.Title, description: action.Description, actionType: action.ActionType, startDate: request.StartDate, endDate: request.EndDate, modifiedByUserId: currentUser.Id, modifiedByUsername: currentUser.Name, utcNow: now)` — i.e. re-supply the entity's existing title/description/actionType unchanged and only the incoming dates change. This reuses `UpdateDetails` rather than adding a new domain method, since `UpdateDetails` already has no side effects on `ProductAssociations`/`FolderLinks`.
4. **Do not** call `ReplaceProductAssociations` or `ReplaceFolderLinks` under any circumstance — this is the entire point of the fix.
5. If Outlook push is enabled (`_options.CurrentValue.PushEnabled`) and the action already has an `OutlookEventId`, call `IOutlookCalendarSync.UpdateEventAsync(action, ct)` and `action.MarkOutlookSynced(...)`, exactly as `UpdateMarketingActionHandler` does, so a dragged/resized event's date change still propagates to the linked Outlook event. If there is no `OutlookEventId` yet, skip Outlook sync entirely (do **not** create a new Outlook event from a move — creation is the full-edit flow's responsibility). On `OutlookCalendarSyncException`, return the same `MarketingCalendarAccessDenied` / `MarketingCalendarSyncFailed` mapping used today.
6. Persist via `_repository.UpdateAsync` + `SaveChangesAsync`; on failure return `ErrorCodes.DatabaseError`, matching `UpdateMarketingActionHandler`'s catch block and log message.
7. Log at `Information` level: `"MarketingAction {ActionId} moved by user {UserId}"`.
8. Return `MoveMarketingActionResponse { Id = action.Id, ModifiedAt = action.ModifiedAt }` on success.

**Acceptance criteria:**
- Calling the handler on an action that has folder links and product associations, with a request containing only `Id`/`StartDate`/`EndDate`, leaves `FolderLinks` and `ProductAssociations` on the persisted entity byte-for-byte unchanged (verified via repository round-trip in an integration/unit test).
- `Title`, `Description`, and `ActionType` are unchanged after a move.
- `StartDate`/`EndDate` reflect the request values after the move.
- Unauthenticated requests return `UnauthorizedMarketingAccess` without touching the repository.
- A request for a non-existent `Id` returns `MarketingActionNotFound`.
- When the action has a synced Outlook event and push is enabled, `IOutlookCalendarSync.UpdateEventAsync` is invoked exactly once with the updated dates.
- When the action has no `OutlookEventId`, `IOutlookCalendarSync.CreateEventAsync`/`UpdateEventAsync` are never invoked.
- A `SaveChangesAsync` failure returns `ErrorCodes.DatabaseError` and does not throw past the handler.

### FR-3: Controller endpoint
Add to `backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs`:

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

Route: `PATCH /api/MarketingCalendar/{id}/move`. Same `[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]` gate as `UpdateMarketingAction` and `DeleteMarketingAction` — no new permission is introduced.

**Acceptance criteria:**
- Endpoint is reachable at `PATCH /api/MarketingCalendar/{id}/move` and requires `Marketing_MarketingCalendar` write access (returns 401/403 consistent with the existing `PUT {id}` behavior when unauthorized).
- The auto-generated OpenAPI/TypeScript client exposes a `marketingCalendar_MoveMarketingAction(id, request)` method after `npm run build` regenerates the client (per `docs/development/api-client-generation.md`).

### FR-4: Frontend — calendar drag/resize calls the new endpoint
In `frontend/src/api/hooks/useMarketingCalendar.ts`, add a `useMoveMarketingAction` hook mirroring `useUpdateMarketingAction`'s structure (same query-invalidation targets: `actions`, `calendar`, and `action/{id}`), calling `client.marketingCalendar_MoveMarketingAction(id, new MoveMarketingActionRequest({ id, startDate, endDate }))`.

In `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx`:
- Replace the `updateMutation` call inside `handleEventMove` (lines 207–223) with the new `useMoveMarketingAction` mutation, passing only `{ id, startDate: new Date(dateFrom), endDate: new Date(dateTo) }`.
- `handleEventResize` continues to delegate to `handleEventMove` unchanged (it already does, line 225–230).
- The existing `useUpdateMarketingAction`/`updateMutation` remains in place for the full edit modal path (`MarketingActionModal`) — this spec does not touch that flow.

**Acceptance criteria:**
- After a drag or resize, the network call observed is `PATCH /api/MarketingCalendar/{id}/move` with a body containing only `id`, `startDate`, `endDate` — no `title`, `associatedProducts`, or `folderLinks` keys.
- `UpdateMarketingActionPayload`/`updateMutation` is no longer referenced from `handleEventMove`/`handleEventResize`.
- An action with existing folder links, dragged to a new date on the calendar, retains all folder links and product associations when subsequently opened in the edit modal.
- Calendar view still reflects the new date range immediately (existing optimistic/query-invalidation behavior preserved via `useMoveMarketingAction`'s `onSuccess` invalidating the same query keys as today).

### FR-5: Regression test coverage for the original bug
Add/extend backend tests to lock in the fix and prevent recurrence:
- A handler-level test (xUnit, matching existing `UpdateMarketingActionHandler` test conventions if present under `backend/test/.../Marketing/UseCases/`) asserting `MoveMarketingActionHandler` leaves `FolderLinks` untouched when the request has no folder-link fields at all (there are none on the type, but assert against a seeded action with 2+ folder links pre-move).
- A test asserting `UpdateMarketingActionHandler`'s existing behavior is unchanged (full replace semantics still apply there) — this spec does not alter `UpdateMarketingActionHandler`.

**Acceptance criteria:**
- New tests fail against the pre-fix code path (i.e., they would have caught this bug) and pass after the fix.
- No existing test for `UpdateMarketingActionHandler` regresses.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected — the new endpoint does strictly less work than `UpdateMarketingAction` (no collection diffing/replacement). Response time should be at or below the existing `PUT {id}` endpoint's baseline for the same action. No new indexes or queries are introduced; `GetByIdAsync`/`UpdateAsync`/`SaveChangesAsync` reuse existing repository methods.

### NFR-2: Security
- The new endpoint is gated by the same `[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]` policy as the existing write endpoints — no new permission surface, no privilege escalation.
- `MoveMarketingActionRequest.StartDate` is `[Required]`; model validation via `[ApiController]`'s automatic `ModelState` handling rejects malformed/missing dates with a 400 before the handler runs, consistent with other endpoints in this controller.
- No new PII or sensitive data is introduced by this change; the request/response shapes are a strict subset of the existing `UpdateMarketingActionRequest`/`Response`.

## Data Model
No schema changes. `MarketingAction` (`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`) is unchanged — `UpdateDetails` (lines 235–253) is reused as-is; it already mutates only `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`, and never touches `ProductAssociations` or `FolderLinks`. `MarketingActionFolderLink` and `MarketingActionProduct` collections are unaffected by the new use case by construction, since `MoveMarketingActionHandler` never calls `ReplaceProductAssociations`/`ReplaceFolderLinks`.

## API / Interface Design

**New endpoint:**
- `PATCH /api/MarketingCalendar/{id}/move`
  - Request body: `{ "id": number, "startDate": "ISO-8601 date", "endDate": "ISO-8601 date | null" }` (`id` in body is overwritten by the route `{id}`, matching the existing `PUT {id}` pattern)
  - Response: `{ "id": number, "modifiedAt": "ISO-8601 datetime", "message": string, "success": bool, "errorCode": ..., "parameters": {...} }` (standard `BaseResponse` shape)
  - Errors: `401 Unauthorized` (unauthenticated/unauthorized), `404 Not Found` (unknown id), `403 Forbidden` (Outlook access denied during sync), `500`-equivalent `DatabaseError` code with `200` envelope (matching existing `HandleResponse` convention — verify against `BaseApiController.HandleResponse` for exact status mapping).

**Unchanged endpoint:** `PUT /api/MarketingCalendar/{id}` (`UpdateMarketingAction`) continues to require full payload including `associatedProducts` and `folderLinks` for the edit-modal flow; behavior is not modified by this spec.

**Frontend flow:**
- Drag/resize on `MarketingMonthCalendar` → `handleEventMove`/`handleEventResize` in `MarketingCalendarPage.tsx` → `useMoveMarketingAction` mutation → `PATCH {id}/move` → on success, invalidate `actions`, `calendar`, `action/{id}` query keys (same as today).
- Edit modal save → unchanged `useUpdateMarketingAction` → `PUT {id}`.

## Dependencies
- Existing `IMarketingActionRepository` (`GetByIdAsync`, `UpdateAsync`, `SaveChangesAsync`) — no new repository methods required, per the brief.
- Existing `IOutlookCalendarSync.UpdateEventAsync` — reused for date-change propagation to already-synced Outlook events.
- Existing `ICurrentUserService`, `IOptionsMonitor<MarketingCalendarOptions>`, `ILogger<T>` — same DI shape as `UpdateMarketingActionHandler`.
- OpenAPI/TypeScript client generation pipeline (`docs/development/api-client-generation.md`) must be re-run (automatic on build) to produce `marketingCalendar_MoveMarketingAction` and `MoveMarketingActionRequest` in `frontend/src/api/generated/api-client`.
- `Feature.Marketing_MarketingCalendar` authorization feature flag/permission — already exists, reused unchanged.

## Out of Scope
- Any change to `UpdateMarketingActionHandler`'s full-replacement semantics — the edit modal continues to require complete `associatedProducts`/`folderLinks` payloads.
- Adding `folderLinks`/`associatedProducts` to `MarketingActionCalendarDto` or `GetMarketingCalendarHandler` — the calendar view intentionally stays lightweight.
- Any UI affordance for editing folder links or products directly from the calendar drag/resize interaction.
- Multi-day bulk move / recurring-event move operations.
- Client-side or server-side optimistic-locking/concurrency-token changes (e.g., `ModifiedAt`-based conflict detection) beyond what already exists for `UpdateMarketingAction`.
- Retroactive repair of folder links already deleted by the pre-fix bug (a separate data-recovery/backfill concern, not a code change).

## Open Questions
None.

## Status: COMPLETE
