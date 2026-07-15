## Module
Marketing

## Finding

Dragging or resizing a marketing action in the calendar view calls `UpdateMarketingAction` with a payload that omits `folderLinks`, which causes `ReplaceFolderLinks(null, …)` to delete every folder link on that action silently.

**Trace:**

1. `frontend/src/components/marketing/pages/MarketingCalendarPage.tsx:207–222` — `handleEventMove` / `handleEventResize` build the update payload from the in-memory `CalendarEvent` object, which comes from the lightweight `MarketingActionCalendarDto`. That DTO has no `folderLinks` field, so the payload omits it entirely.

2. `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs:95–98` — the handler always calls `action.ReplaceFolderLinks(request.FolderLinks?.Select(…), now)`. When `request.FolderLinks` is null (absent from the JSON body), the expression evaluates to `null`.

3. `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs:174–203` — `ReplaceFolderLinks` treats a `null` argument as "empty set": it clears `FolderLinks` and adds nothing back. The deletion is then committed in the same `SaveChangesAsync` call.

Any drag or resize of a calendar event therefore permanently removes all OneDrive / file-storage folder links attached to that action.

## Why it matters

`UpdateMarketingAction` uses full-replacement semantics for both collections (product associations and folder links), which is appropriate for the edit modal where the user explicitly manages those lists. The calendar drag/resize path, however, is a **date-only move** — the user did not intend to change folder links at all. Applying full-replacement semantics on a data set that was never fetched in the calendar view is a hidden destructive side-effect.

This violates Single Responsibility: the same handler is being used for two conceptually different operations (full edit vs. date-only move), and there is no way at the handler level to distinguish them.

## Suggested fix

Introduce a dedicated `MoveMarketingAction` use case (handler + request/response + controller action) that accepts only `{ id, startDate, endDate }` and calls `action.UpdateDetails` with only date fields — no `ReplaceProductAssociations`, no `ReplaceFolderLinks`. The calendar page calls this new endpoint on drag/resize instead of `UpdateMarketingAction`.

```csharp
// New: MoveMarketingActionRequest.cs
public class MoveMarketingActionRequest : IRequest
{
    public int Id { get; set; }
    [Required] public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
```

```csharp
// Handler: load entity, call action.UpdateDetails(existing title/type/desc, new dates, userId, now), save.
// No collection replacement calls.
```

No new repository methods are required. The controller adds `PATCH {id}/move`. The frontend `handleEventMove` calls the generated `marketingCalendar_MoveMarketingAction` instead of `marketingCalendar_UpdateMarketingAction`.

---
_Filed by daily arch-review routine on 2026-07-14._
