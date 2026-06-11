# Specification: Centralize MarketingAction scalar field updates via domain method

## Summary
Add an `UpdateDetails` domain method to the `MarketingAction` entity that owns normalization (trimming) and assignment of scalar fields (title, description, action type, dates, modification audit). Refactor all three mutation paths — the create handler, update handler, and Outlook import mapper — to use this method instead of mutating properties directly, eliminating the current inconsistency where Outlook-imported titles retain leading/trailing whitespace.

## Background
`MarketingAction.Title` and `Description` are currently mutated from multiple call sites with diverging normalization. The API path (`CreateMarketingActionHandler`, `UpdateMarketingActionHandler`) trims both fields. The Outlook import path (`OutlookEventImportMapper.BuildAction` and `ApplyChanges`) only incidentally trims descriptions (via `WhitespaceRegex` in `ParseDescription`) and does not trim titles at all. As a result, two records representing the same logical action can differ by surrounding whitespace depending on which code path last touched them, breaking exact-match search, deduplication, and title comparison.

The entity already encapsulates other mutations (`SoftDelete`, `MarkOutlookSynced`, `AssociateWithProduct`, `LinkToFolder`); scalar-field update is the only operation still exposed as raw property assignment. Centralizing this mutation in a domain method removes the invariant from being duplicated across call sites and gives a single location to add future normalization rules (e.g. stripping control characters, normalizing smart quotes from Outlook content).

## Functional Requirements

### FR-1: Add `UpdateDetails` domain method to `MarketingAction`
Introduce a public instance method on `MarketingAction` that accepts the scalar fields a caller can change (title, description, action type, start/end dates) plus modification audit fields (user id, username, UTC timestamp). The method normalizes inputs and assigns them to backing properties atomically.

**Acceptance criteria:**
- Method signature accepts: `string title`, `string? description`, `MarketingActionType actionType`, `DateTime startDate`, `DateTime? endDate`, `string modifiedByUserId`, `string? modifiedByUsername`, `DateTime utcNow`.
- `Title` is set to `(title ?? string.Empty).Trim()`.
- `Description` is set to `description?.Trim()` (null preserved).
- `ActionType`, `StartDate`, `EndDate` are assigned directly.
- `ModifiedAt` is set to `utcNow`.
- `ModifiedByUserId` is assigned directly.
- `ModifiedByUsername` defaults to `"Unknown User"` when null (matches existing handler behavior).
- Property setters for `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername` are restricted (private set or internal) so callers cannot bypass `UpdateDetails`.

### FR-2: Refactor `UpdateMarketingActionHandler` to use `UpdateDetails`
Replace the direct property assignments at `UpdateMarketingActionHandler.cs:61–68` (or whichever lines hold the mutation block) with a single call to `action.UpdateDetails(...)`.

**Acceptance criteria:**
- No direct assignment to `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, or `ModifiedByUsername` remains in the handler.
- Behavior is unchanged for the API path: existing inputs produce identical persisted state.
- Existing tests for `UpdateMarketingActionHandler` continue to pass.

### FR-3: Refactor `OutlookEventImportMapper.ApplyChanges` to use `UpdateDetails`
Replace the direct property assignments at `OutlookEventImportMapper.cs:66–73` with a call to `existing.UpdateDetails(...)`, passing the parsed title (post-`ParseTitle`) and parsed description (post-`ParseDescription`).

**Acceptance criteria:**
- Titles imported or updated from Outlook are trimmed (this is the intentional behavior change).
- Descriptions remain effectively trimmed (no regression — `ParseDescription` already returns a trimmed string; calling `Trim()` again is a no-op).
- Audit fields (`ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`) are populated from the Outlook import context using the same convention the mapper currently uses (e.g. system/sync user identity).

### FR-4: Refactor `OutlookEventImportMapper.BuildAction` to use `UpdateDetails` (or analogous constructor)
The `BuildAction` path constructs a new `MarketingAction`. Either route construction through a constructor that delegates normalization to the same logic as `UpdateDetails`, or instantiate with safe defaults and immediately call `UpdateDetails`. Choose whichever keeps invariants in a single place.

**Acceptance criteria:**
- Newly built actions from Outlook have trimmed titles.
- All scalar fields are normalized through one code path shared with `UpdateDetails`.
- No raw property assignment remains in `BuildAction` for `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`.

### FR-5: Refactor `CreateMarketingActionHandler` to share normalization
`CreateMarketingActionHandler` currently initializes a new entity (via constructor or property assignment) and then trims `Title` / `Description`. Update it to share the same normalization path used by FR-4 — either a constructor that normalizes, or instantiation followed by `UpdateDetails`.

**Acceptance criteria:**
- No raw `.Trim()` calls on `Title`/`Description` remain in the handler — normalization is delegated to the entity.
- Behavior is unchanged for the create path: existing inputs produce identical persisted state.
- Existing tests for `CreateMarketingActionHandler` continue to pass.

### FR-6: Unit tests for `UpdateDetails`
Add unit tests on `MarketingAction.UpdateDetails` covering normalization rules independently of any handler.

**Acceptance criteria:**
- Test: title with leading/trailing whitespace is trimmed.
- Test: null title throws or is replaced with empty string (per implementation choice in FR-1).
- Test: null description remains null (not converted to empty string).
- Test: description with whitespace is trimmed.
- Test: `ModifiedByUsername` defaults to `"Unknown User"` when null.
- Test: `ModifiedAt` is set to the provided `utcNow`.
- Test: all other scalar fields are assigned exactly as passed.

### FR-7: Integration tests for the Outlook import behavior change
Add or extend tests for `OutlookEventImportMapper` that confirm titles from Outlook events are trimmed in both `BuildAction` and `ApplyChanges` paths.

**Acceptance criteria:**
- Test: importing a new Outlook event with a leading/trailing whitespace subject produces a `MarketingAction` with a trimmed `Title`.
- Test: re-importing (updating) an existing `MarketingAction` from an Outlook event with whitespace in subject results in a trimmed `Title`.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. `UpdateDetails` is a synchronous in-memory method call replacing inline property assignments — same allocation and CPU profile.

### NFR-2: Security
No new security surface. Field normalization is defensive (trimming) and reduces the chance of whitespace-based collisions in lookups; it does not introduce new validation gaps.

### NFR-3: Backward compatibility (data)
Existing rows with untrimmed titles are not retroactively cleaned. A row stays as-is until the next time it passes through `UpdateDetails` (any API update or Outlook re-sync), at which point its title is trimmed. No data migration is included in this work (see Out of Scope).

### NFR-4: Encapsulation
After this change, no code outside `MarketingAction` may directly assign `Title`, `Description`, `ActionType`, `StartDate`, `EndDate`, `ModifiedAt`, `ModifiedByUserId`, or `ModifiedByUsername`. This is enforced via setter accessibility (private or internal as appropriate for EF Core materialization).

## Data Model
No schema changes. The `MarketingAction` entity and its persisted columns are unchanged. Only the API surface of the entity changes:
- Setters on the affected scalar properties are tightened (private/internal).
- A new public method `UpdateDetails(...)` is added.

EF Core materialization must continue to work — typically achieved by leaving private setters that EF can populate via its proxy/backing-field conventions.

## API / Interface Design

### Internal entity method
```csharp
public void UpdateDetails(
    string title,
    string? description,
    MarketingActionType actionType,
    DateTime startDate,
    DateTime? endDate,
    string modifiedByUserId,
    string? modifiedByUsername,
    DateTime utcNow)
```

### External (HTTP/MediatR) API
No changes. The existing `CreateMarketingActionCommand` and `UpdateMarketingActionCommand` request/response contracts remain identical. The OpenAPI surface is unchanged.

### UI
No UI changes.

## Dependencies
- No new external services, libraries, or packages.
- Depends on the existing `MarketingAction` entity, `MarketingActionType` enum, and the three call sites listed in the brief.
- Existing `OutlookEventImportMapper.ParseTitle` and `ParseDescription` helpers continue to be used to pre-process raw Outlook content before it reaches `UpdateDetails`.

## Out of Scope
- Data backfill / migration to trim whitespace in already-persisted `MarketingAction.Title` values. (Recommended as a follow-up if duplicate-by-whitespace is observed in production data.)
- Additional normalization rules beyond trimming (e.g. stripping control characters, normalizing smart quotes, Unicode normalization). The new method is the right home for these, but they are not included in this change.
- Refactoring other domain entities that may have analogous raw-property mutation patterns.
- Changes to `ParseTitle` / `ParseDescription` themselves. They continue to perform Outlook-specific pre-processing (HTML strip, length clip); trimming is layered on by `UpdateDetails`.
- Audit-log changes or events emitted on update — existing behavior preserved.

## Open Questions
None.

## Status: COMPLETE