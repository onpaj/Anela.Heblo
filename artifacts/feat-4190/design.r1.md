# Design: Dedicated `Reschedule` domain method for `MarketingAction`

## Component Design

### `MarketingAction` (domain entity)
`backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`

Add one new public instance method, `Reschedule`, alongside the entity's existing narrow-purpose mutators (`SoftDelete`, `Restore`, `MarkOutlookSynced`, `ClearOutlookLink`, `AssociateWithProduct`, `LinkToFolder`). Responsibility: update only the scheduling fields (`StartDate`, `EndDate`) and the standard modification-audit fields (`ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`) of an existing action. It does not validate, normalize, or touch `Title`, `Description`, `ActionType`, `CreatedAt`/`CreatedBy*`, deletion state, Outlook-sync state, or the `ProductAssociations`/`FolderLinks` collections. `UpdateDetails` is unchanged and keeps its existing responsibility (full-field edit) for `UpdateMarketingActionHandler`.

Interface:

```csharp
public void Reschedule(
    DateTime startDate,
    DateTime? endDate,
    string modifiedByUserId,
    string? modifiedByUsername,
    DateTime utcNow)
```

### `MoveMarketingActionHandler` (application handler)
`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs`

Responsibility is unchanged: authenticate the current user, load the `MarketingAction`, apply the requested date change, push to Outlook when configured, persist, and return the result. The only change is which entity method it calls to apply the date change — `action.Reschedule(...)` instead of `action.UpdateDetails(...)`. Its dependencies (`IMarketingActionRepository`, `ICurrentUserService`, `ILogger<MoveMarketingActionHandler>`, `IOutlookCalendarSync`, `IOptionsMonitor<MarketingCalendarOptions>`), control flow, and error handling are all unchanged.

## Data Schemas

No database schema change — `Reschedule` writes to the same existing `MarketingAction` table columns (`StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`) that `UpdateDetails` already writes to.

No API request/response shape change — `MoveMarketingActionRequest` and `MoveMarketingActionResponse` are unchanged; the MediatR contract between the controller and `MoveMarketingActionHandler` is untouched. No OpenAPI/generated-client regeneration is triggered by this change.

No event payload change — this feature introduces no new events.
