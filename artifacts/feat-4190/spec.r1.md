# Specification: Dedicated `Reschedule` domain method for `MarketingAction`

## Summary
`MoveMarketingActionHandler` currently reschedules a `MarketingAction` by calling the general-purpose `UpdateDetails(...)` method, passing back the entity's own `Title`, `Description`, and `ActionType` unchanged just to satisfy that method's signature. This spec adds a focused `Reschedule` domain method to `MarketingAction` that only touches date/audit fields, and updates the handler to call it instead — making the "move" operation explicit in the domain API and removing the fragile read-then-write-unchanged pattern.

## Background
This originates from an arch-review finding (issue #4190) against `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` (lines 60–68) and `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`.

Today, `MoveMarketingActionHandler.Handle` does:

```csharp
action.UpdateDetails(
    title: action.Title,
    description: action.Description,
    actionType: action.ActionType,
    startDate: request.StartDate,
    endDate: request.EndDate,
    modifiedByUserId: currentUser.Id,
    modifiedByUsername: currentUser.Name,
    utcNow: now);
```

`UpdateDetails` re-runs `NormalizeTitle`/`NormalizeDescription` on values that are already normalized and unchanged, which is harmless today but silently couples the "move" use case to whatever validation `UpdateDetails` gains in the future (e.g. a non-empty-description rule would be inappropriate to enforce during a pure reschedule). It also forces a reader of the handler to figure out why `Title`/`Description`/`ActionType` are being read and written back unchanged. The codebase already follows a pattern of small, intention-revealing domain methods for other single-purpose entity operations (`SoftDelete`, `Restore`, `MarkOutlookSynced`, `ClearOutlookLink`, `AssociateWithProduct`, `LinkToFolder`) — `UpdateDetails` is the odd one out being reused for a narrower operation it wasn't designed for.

## Functional Requirements

### FR-1: Add a `Reschedule` domain method to `MarketingAction`
Add a new public instance method on `MarketingAction` (in `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs`) that updates only `StartDate`, `EndDate`, and the standard modification-audit fields (`ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`). It must not touch `Title`, `Description`, or `ActionType`, and must not call `NormalizeTitle`/`NormalizeDescription`.

Signature (matching the issue's suggested fix and the existing `UpdateDetails`/`SoftDelete`/`Restore` parameter conventions):

```csharp
public void Reschedule(
    DateTime startDate,
    DateTime? endDate,
    string modifiedByUserId,
    string? modifiedByUsername,
    DateTime utcNow)
{
    StartDate = startDate;
    EndDate = endDate;
    ModifiedAt = utcNow;
    ModifiedByUserId = modifiedByUserId;
    ModifiedByUsername = modifiedByUsername ?? "Unknown User";
}
```

**Acceptance criteria:**
- `MarketingAction.Reschedule(startDate, endDate, modifiedByUserId, modifiedByUsername, utcNow)` exists and is public.
- After calling `Reschedule`, `StartDate` and `EndDate` equal the values passed in.
- After calling `Reschedule`, `Title`, `Description`, and `ActionType` are unchanged from their pre-call values.
- After calling `Reschedule`, `ModifiedAt == utcNow`, `ModifiedByUserId` equals the passed-in value, and `ModifiedByUsername` equals the passed-in value, or `"Unknown User"` when `modifiedByUsername` is `null` (mirroring `UpdateDetails`'s existing fallback behavior).
- `Reschedule` does not call `NormalizeTitle` or `NormalizeDescription` and does not modify `CreatedAt`, `CreatedByUserId`, `CreatedByUsername`, `IsDeleted`, `DeletedAt`, or any Outlook-sync field, or the `ProductAssociations`/`FolderLinks` collections.

### FR-2: `MoveMarketingActionHandler` calls `Reschedule` instead of `UpdateDetails`
Update `MoveMarketingActionHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs`, lines 60–68) to call the new `action.Reschedule(...)` method instead of `action.UpdateDetails(...)`, passing `request.StartDate`, `request.EndDate`, `currentUser.Id`, `currentUser.Name`, and `now`. No other logic in the handler changes (authorization check, not-found check, Outlook sync push, error handling, repository save, logging, response shape all stay exactly as they are).

**Acceptance criteria:**
- The handler no longer references `action.UpdateDetails` anywhere.
- The handler no longer reads `action.Title`, `action.Description`, or `action.ActionType` as inputs to the reschedule call.
- Given a valid authenticated request for an existing action, the resulting `action.StartDate`/`action.EndDate` match the request, and `action.Title`/`action.Description`/`action.ActionType` are unchanged from before the move — same externally observable behavior as today.
- All existing behavior downstream of the reschedule call (Outlook push when enabled and an `OutlookEventId` is present, `OutlookCalendarSyncException` handling and error codes, DB save error handling, success response with `Id`/`ModifiedAt`) is unchanged.

### FR-3: Test coverage mirrors the existing domain-method test pattern
Add a domain-level test file for `Reschedule` (e.g. `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRescheduleTests.cs`), following the structure of the existing sibling files (`MarketingActionUpdateDetailsTests.cs`, `MarketingActionSoftDeleteTests.cs`, `MarketingActionRestoreTests.cs`) and using `MarketingActionTestBuilder`.

**Acceptance criteria:**
- A test asserts `StartDate`/`EndDate` are updated to the passed-in values.
- A test asserts `Title`, `Description`, and `ActionType` are unchanged after `Reschedule`.
- A test asserts `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername` are updated correctly, including the `null` → `"Unknown User"` fallback for `modifiedByUsername`.
- The existing `MoveMarketingActionHandlerTests.cs` suite continues to pass unmodified in behavior (assertions about `StartDate`/`EndDate`/`Title`/`Description`/`ActionType`/Outlook sync/error handling all still hold) after the handler is switched to call `Reschedule`. If any existing test in that file directly asserts on `UpdateDetails`-specific behavior that no longer applies, it is updated to reflect the call to `Reschedule`, not deleted.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact is expected or required; this is a like-for-like field-assignment refactor with no new I/O, allocations, or loops.

### NFR-2: Security
No change to authentication/authorization behavior. The handler's existing authenticated-user check and audit-field population (`modifiedByUserId`, `modifiedByUsername`) are preserved exactly, just routed through the new method.

## Data Model
No schema changes. `Reschedule` operates on the existing `MarketingAction` entity's existing fields (`StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`) — no new columns, no migration.

## API / Interface Design
No change to any public HTTP/MediatR contract. `MoveMarketingActionRequest` and `MoveMarketingActionResponse` are unchanged. The only interface addition is the new public method `MarketingAction.Reschedule(DateTime, DateTime?, string, string?, DateTime)` on the domain entity, which is internal to the backend domain/application layers (not exposed via any API contract or generated client).

## Dependencies
None beyond the existing Marketing module code already in the repository (`MarketingAction`, `MoveMarketingActionHandler`, `IMarketingActionRepository`, `IOutlookCalendarSync`, `ICurrentUserService`, and their existing test doubles/builders).

## Out of Scope
- Any change to `UpdateDetails` itself (it continues to exist and is used by `UpdateMarketingActionHandler` and covered by its own existing tests) — this spec does not touch or restrict its current callers.
- Any change to `MoveMarketingActionRequest`/`MoveMarketingActionResponse` shapes, MediatR request/response contracts, or frontend/API-client code — none of these change since the handler's externally observable behavior is unchanged.
- Renaming or otherwise refactoring the other single-purpose domain methods (`SoftDelete`, `Restore`, `MarkOutlookSynced`, `ClearOutlookLink`, `AssociateWithProduct`, `LinkToFolder`, `ReplaceProductAssociations`, `ReplaceFolderLinks`) — out of scope, mentioned only as the existing pattern this change follows.
- Any broader audit of other handlers in the Marketing module (or elsewhere) that might have a similar `UpdateDetails`-for-a-narrow-operation smell — this spec is scoped to the one finding in issue #4190.
- Database migration — none is needed; no persisted schema changes.

## Open Questions

None.

## Status: COMPLETE
