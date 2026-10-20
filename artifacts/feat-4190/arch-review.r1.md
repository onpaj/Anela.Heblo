# Architecture Review: Dedicated `Reschedule` domain method for `MarketingAction`

## Skip Design: true

## Architectural Fit Assessment
This is a small, backend-only domain refactor with no new API surface, no schema change, and no UI impact. It fits squarely into the codebase's existing "rich domain model" convention: `docs/architecture/development_guidelines.md` explicitly states *"Don't create anemic domain models - Put behavior in entities"* as a project-wide practice, and `MarketingAction` already follows that convention everywhere else — `SoftDelete`, `Restore`, `MarkOutlookSynced`, `ClearOutlookLink`, `AssociateWithProduct`, `LinkToFolder`, `ReplaceProductAssociations`, and `ReplaceFolderLinks` are all narrow, intention-revealing methods that mutate only the fields relevant to that one operation. `UpdateDetails` is the entity's one "full update" method, meant for the general edit use case (`UpdateMarketingActionHandler`). Reusing it from `MoveMarketingActionHandler` for a date-only reschedule is the outlier, not the norm — this change brings the Move use case back in line with the rest of the entity's API and with ADR-adjacent guidance already written down in this repo. There is no architectural risk or open design question here; this is a same-layer, same-pattern addition.

## Proposed Architecture

### Component Overview
No new components. One new method on an existing domain entity, one call-site swap in an existing handler:

```
MoveMarketingActionRequest
        │
        ▼
MoveMarketingActionHandler.Handle()          (Application layer — unchanged except one call)
        │
        │  action.Reschedule(startDate, endDate, userId, username, utcNow)   ← NEW call
        ▼
MarketingAction (Domain entity)
        │  Reschedule(...)   ← NEW method, sits alongside UpdateDetails/SoftDelete/Restore/...
        ▼
StartDate, EndDate, ModifiedAt, ModifiedByUserId, ModifiedByUsername updated
```

`UpdateDetails` remains on `MarketingAction`, untouched, still used by `UpdateMarketingActionHandler`.

### Key Design Decisions

#### Decision 1: Add a new `Reschedule` method rather than modifying `UpdateDetails`
**Options considered:**
- (a) Add optional/nullable parameters to `UpdateDetails` so callers can omit title/description/actionType.
- (b) Split `UpdateDetails` into smaller composable methods (e.g. a private core + two public overloads).
- (c) Add a new, separate `Reschedule` method with its own narrow signature (the issue's suggested fix).

**Chosen approach:** (c) — new `Reschedule` method, `UpdateDetails` left exactly as-is.

**Rationale:** `UpdateDetails` is a stable, tested, actively-used method (`UpdateMarketingActionHandlerTests.cs`, `MarketingActionUpdateDetailsTests.cs`). Options (a) and (b) both touch its signature or internals and risk regressing the general-edit use case for the sake of a narrower one — exactly the coupling this finding is trying to remove. Option (c) is the minimal, additive change: it introduces one new method whose entire body is five field assignments, touches no existing method, and requires no changes to `UpdateMarketingActionHandler` or its tests. It also matches the exact signature/shape the issue itself proposes, which was clearly modeled on the existing `SoftDelete`/`Restore` methods' parameter conventions (`(..., string modifiedByUserId, string? modifiedByUsername, DateTime utcNow)`), so it needs no further justification against house style.

#### Decision 2: `Reschedule`'s `modifiedByUsername` fallback matches `UpdateDetails`, not `SoftDelete`/`Restore`
**Options considered:**
- (a) `modifiedByUsername ?? "Unknown User"` (matches `UpdateDetails`'s existing fallback).
- (b) Require non-null `modifiedByUsername` (matches `SoftDelete`/`Restore`, which take `string username` not `string? modifiedByUsername`).

**Chosen approach:** (a).

**Rationale:** The call site (`MoveMarketingActionHandler`) passes `currentUser.Name`, exactly as it does today into `UpdateDetails`'s `modifiedByUsername` parameter — so `Reschedule` must accept the same nullable type and apply the same fallback to preserve today's observable behavior for a null/empty display name. Changing the parameter to non-nullable `string` would be a stricter contract than the call site currently guarantees and is out of scope for a refactor that must be behavior-preserving.

## Implementation Guidance

### Directory / Module Structure
No new files or directories beyond one new test file. All changes are in existing files:

- `backend/src/Anela.Heblo.Domain/Features/Marketing/MarketingAction.cs` — add the `Reschedule` method. Place it near `UpdateDetails` (immediately before or after it, at the "Domain methods" section) so the two full/partial-update methods read together.
- `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/MoveMarketingAction/MoveMarketingActionHandler.cs` — replace the `action.UpdateDetails(...)` call (lines 60–68) with `action.Reschedule(...)`. No other lines in this file change.
- `backend/test/Anela.Heblo.Tests/Domain/Marketing/MarketingActionRescheduleTests.cs` — new test file, sibling to `MarketingActionUpdateDetailsTests.cs`/`MarketingActionSoftDeleteTests.cs`/`MarketingActionRestoreTests.cs`, using the existing `MarketingActionTestBuilder`.
- `backend/test/Anela.Heblo.Tests/Application/Marketing/MoveMarketingActionHandlerTests.cs` — existing file, review only; its assertions are on observable `MarketingAction` state (`StartDate`, `EndDate`, `Title`, `Description`, `ActionType`, Outlook sync calls, error codes) so it should keep passing unmodified once the handler calls `Reschedule`. No test in this file should need to change unless one specifically mocks/asserts on `UpdateDetails` being called (verified: it does not — the existing tests build a real `MarketingAction` via `MarketingActionTestBuilder` and assert on its resulting property values, not on which entity method was invoked).

### Interfaces and Contracts
New public method on `MarketingAction`:

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

No MediatR request/response contract changes. No DTO changes (n/a to the "DTOs are classes, not records" rule since nothing here is a DTO). No public API surface change — `MarketingAction` is a domain entity, not exposed through any generated OpenAPI client.

### Data Flow
Identical to today's flow, with one substitution:

1. `MoveMarketingActionHandler.Handle` authenticates the current user and loads the `MarketingAction` by id (unchanged).
2. **Was:** `action.UpdateDetails(title: action.Title, description: action.Description, actionType: action.ActionType, startDate: request.StartDate, endDate: request.EndDate, modifiedByUserId: currentUser.Id, modifiedByUsername: currentUser.Name, utcNow: now)`.
   **Now:** `action.Reschedule(startDate: request.StartDate, endDate: request.EndDate, modifiedByUserId: currentUser.Id, modifiedByUsername: currentUser.Name, utcNow: now)`.
3. Outlook push (if `PushEnabled` and `OutlookEventId` set), repository save, logging, and response construction are all unchanged and run against the same post-call entity state as before.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| A future change to `Reschedule` and a future change to `UpdateDetails` silently drift apart on the audit-field fallback logic (`?? "Unknown User"`) | Low | Both methods keep the identical one-line fallback; FR-3 test coverage asserts the fallback explicitly for `Reschedule` so any future drift is caught by a failing test, not by inspection |
| Existing `MoveMarketingActionHandlerTests.cs` has a test that incidentally depends on `UpdateDetails`-specific behavior (e.g. an implicit re-trim of title) | Low | Run the full existing test file after the change (spec FR-3); since `Reschedule` never touches `Title`/`Description`, no test asserting those fields as unchanged is at risk of newly failing — only a test asserting they get re-normalized would break, and none should exist for a *move* handler |
| A reviewer expects `UpdateDetails` to be deprecated/removed as part of "the domain richness fix" | Low | Spec's Out of Scope section and this review both explicitly state `UpdateDetails` is untouched and remains the general-edit method for `UpdateMarketingActionHandler` |

## Specification Amendments
None. The specification (`spec.r1.md`) as written is directly implementable; this review found no gaps, ambiguities, or conflicts with existing code or documented conventions.

## Prerequisites
None. No migration, no config, no infrastructure change, no new dependency. Implementation can start immediately against the current `main`/feature branch state.
