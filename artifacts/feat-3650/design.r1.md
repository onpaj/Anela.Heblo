# Design: Marketing Calendar Drag/Resize Silently Deletes Folder Links

## Component Design

### `MoveMarketingActionRequest` / `MoveMarketingActionResponse`
`backend/src/Anela.Heblo.Application/Features/Marketing/Contracts/MoveMarketingActionRequest.cs` (new file).

- `MoveMarketingActionRequest : IRequest<MoveMarketingActionResponse>` — class DTO (never a record), carrying only `Id` (int, overwritten from route), `StartDate` (`DateTime`, `[Required]`), `EndDate` (`DateTime?`). No `Title`, `Description`, `ActionType`, `AssociatedProducts`, or `FolderLinks` members exist on the type — this is the structural guarantee that the endpoint cannot be used to clear collections.
- `MoveMarketingActionResponse : BaseResponse` — carries `Id` (int), `ModifiedAt` (`DateTime`), `Message` (string, defaults to `"Marketing action moved successfully"`), plus the inherited `Success`/`ErrorCode`/`Parameters` from `BaseResponse`. Same two constructors (`()` and `(ErrorCodes, Dictionary<string,string>?)`) as every other response in this module.

### `MoveMarketingActionHandler`
`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` (new file). Implements `IRequestHandler<MoveMarketingActionRequest, MoveMarketingActionResponse>`.

**Responsibility:** date-only reschedule of a `MarketingAction`. Never reads or writes `ProductAssociations`/`FolderLinks`.

**Dependencies (constructor-injected, same shape as `UpdateMarketingActionHandler`):**
- `IMarketingActionRepository`
- `ICurrentUserService`
- `IOutlookCalendarSync`
- `IOptionsMonitor<MarketingCalendarOptions>`
- `ILogger<MoveMarketingActionHandler>`

**`Handle` control flow:**
1. Resolve current user via `ICurrentUserService`. Not authenticated → return `MoveMarketingActionResponse(ErrorCodes.UnauthorizedMarketingAccess)` without touching the repository.
2. `_repository.GetByIdAsync(request.Id)`. Not found → return `MoveMarketingActionResponse(ErrorCodes.MarketingActionNotFound)`.
3. `action.UpdateDetails(title: action.Title, description: action.Description, actionType: action.ActionType, startDate: request.StartDate, endDate: request.EndDate, modifiedByUserId: currentUser.Id, modifiedByUsername: currentUser.Name, utcNow: now)`. Existing title/description/actionType are re-supplied unchanged; only dates move. `ReplaceProductAssociations`/`ReplaceFolderLinks` are **never called** — no branch, no code path reaches them.
4. If `_options.CurrentValue.PushEnabled` and `action.OutlookEventId` is already set: call `IOutlookCalendarSync.UpdateEventAsync(action, ct)`, then `action.MarkOutlookSynced(...)`. If `OutlookEventId` is absent, skip Outlook sync entirely — do not call `CreateEventAsync`. On `OutlookCalendarSyncException`, map to `MarketingCalendarAccessDenied`/`MarketingCalendarSyncFailed` exactly as `UpdateMarketingActionHandler` does.
5. `_repository.UpdateAsync(action)` + `SaveChangesAsync()`. On failure, catch and return `ErrorCodes.DatabaseError` (no throw past the handler); log matches `UpdateMarketingActionHandler`'s catch block.
6. Log `Information`: `"MarketingAction {ActionId} moved by user {UserId}"`.
7. Return `MoveMarketingActionResponse { Id = action.Id, ModifiedAt = action.ModifiedAt }`.

No new domain method is introduced on `MarketingAction` — `UpdateDetails` is reused as-is because it already leaves `ProductAssociations`/`FolderLinks` untouched; the bug lived only in the handler layer, not the domain.

### `MarketingCalendarController.MoveMarketingAction`
`backend/src/Anela.Heblo.API/Controllers/MarketingCalendarController.cs` (modified — new action added, nothing existing removed).

- Route: `[HttpPatch("{id:int}/move")]`.
- Guard: `[FeatureAuthorize(Feature.Marketing_MarketingCalendar, AccessLevel.Write)]` — same gate as `PUT {id}` and `DELETE {id}`, no new permission surface.
- Signature: `Task<ActionResult<MoveMarketingActionResponse>> MoveMarketingAction(int id, [FromBody] MoveMarketingActionRequest request)`. Sets `request.Id = id` from the route (route wins over any body value, matching the `PUT {id}` convention), sends via `_mediator.Send(request)`, returns `HandleResponse(response)`.
- `MediatR` handler registration is automatic (assembly scan) — no change to `MarketingModule.AddMarketingModule`.

### Frontend: `useMoveMarketingAction` hook
`frontend/src/api/hooks/useMarketingCalendar.ts` (modified — new hook added).

- Mirrors `useUpdateMarketingAction`'s structure: `useMutation` wrapping `client.marketingCalendar_MoveMarketingAction(id, new MoveMarketingActionRequest({ id, startDate, endDate }))`, built from `${apiClient.baseUrl}` per the project's absolute-URL hook convention.
- `onSuccess` invalidates the same three query keys as `useUpdateMarketingAction`: `[...QUERY_KEYS.marketingCalendar, "actions"]`, `[...QUERY_KEYS.marketingCalendar, "calendar"]`, `[...QUERY_KEYS.marketingCalendar, "action", id]`.
- `MoveMarketingActionRequest` (TS class) and `marketingCalendar_MoveMarketingAction` (client method) are auto-generated from the OpenAPI spec on build — not hand-written.

### Frontend: `MarketingCalendarPage.tsx` — `handleEventMove`/`handleEventResize`
`frontend/src/components/marketing/pages/MarketingCalendarPage.tsx` (modified — call site swapped, no new component).

- `handleEventMove` (currently lines 207–223) replaces its `updateMutation`/`UpdateMarketingActionRequest` call with `useMoveMarketingAction().mutate({ id, startDate: new Date(dateFrom), endDate: new Date(dateTo) })`.
- The `calendarEvents.find(...)` lookup used today to read `title`/`actionType`/`associatedProducts` off the in-memory `CalendarEvent` (needed only to satisfy `UpdateMarketingActionRequest`'s required fields) is removed as dead code, since the new payload doesn't carry those fields. `useCallback` dependencies drop `calendarEvents` accordingly.
- `handleEventResize` (lines 225–230) is unchanged — it already delegates to `handleEventMove`.
- `useUpdateMarketingAction`/`updateMutation` remains wired to the full edit modal (`MarketingActionModal`) exactly as today; this component is not touched by this change.

No new UI component, screen, or visual state is introduced — same call site, same downstream props into `MarketingMonthCalendar`, only the network call underneath changes.

## Data Schemas

### `MoveMarketingActionRequest` (C# / generated TS)

```csharp
public class MoveMarketingActionRequest : IRequest<MoveMarketingActionResponse>
{
    public int Id { get; set; }

    [Required]
    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }
}
```

### `MoveMarketingActionResponse` (C# / generated TS)

```csharp
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

### HTTP contract

**Request:** `PATCH /api/MarketingCalendar/{id}/move`

```json
{
  "id": 123,
  "startDate": "2026-07-20T00:00:00Z",
  "endDate": "2026-07-21T00:00:00Z"
}
```
`id` in the body is overwritten by the route `{id}` (matches existing `PUT {id}` behavior). `endDate` may be `null`.

**Response (200):**

```json
{
  "id": 123,
  "modifiedAt": "2026-07-15T10:32:00Z",
  "message": "Marketing action moved successfully",
  "success": true,
  "errorCode": null,
  "parameters": {}
}
```

**Error responses** (standard `BaseResponse` envelope via `HandleResponse`):
- `401 Unauthorized` — `ErrorCodes.UnauthorizedMarketingAccess` (not authenticated).
- `404 Not Found` — `ErrorCodes.MarketingActionNotFound` (unknown `id`).
- `403 Forbidden` — `ErrorCodes.MarketingCalendarAccessDenied` (Outlook access denied during sync).
- `200` with `success: false`, `errorCode: DatabaseError` — persistence failure (matches existing `HandleResponse` convention for this error code).
- `400 Bad Request` — automatic `ModelState` validation failure when `startDate` is missing/malformed (`[ApiController]` behavior, no handler code involved).

No database schema changes. `MarketingAction`, `MarketingActionFolderLink`, and `MarketingActionProduct` entities and their persisted shapes are unchanged; the new use case only ever reads/writes `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`, and (conditionally) `OutlookEventId`/Outlook sync metadata via `MarkOutlookSynced`.
