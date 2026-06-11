# Specification: Encapsulate Collection Replacement in MarketingAction Domain Entity

## Summary
Refactor `UpdateMarketingActionHandler` to stop directly mutating EF Core navigation collections on the `MarketingAction` aggregate. Introduce domain-owned `ReplaceProductAssociations` and `ReplaceFolderLinks` methods on `MarketingAction` so the Application layer is decoupled from EF change-tracker behaviour and the entity retains full control over its invariants.

## Background
The current `UpdateMarketingActionHandler` (lines 95–110 of `backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/UpdateMarketingAction/UpdateMarketingActionHandler.cs`) leaks persistence concerns into the Application layer:

- It calls `action.ProductAssociations.Clear()` and `action.FolderLinks.Clear()` directly on `virtual ICollection<>` navigation properties.
- It then re-populates them via the encapsulated domain methods `AssociateWithProduct` and `LinkToFolder`.

The `MarketingAction` aggregate exposes encapsulated *add* methods (with deduplication guards) but no equivalent *replace* or *clear* operations. The handler compensates by reaching into the raw collection, which:

1. Breaks aggregate encapsulation — removal-side invariants cannot be enforced or evolved (e.g. future audit logging, minimum-association guards).
2. Implicitly couples the Application layer to EF Core change-tracking. The same code would silently no-op against an in-memory or non-EF repository.
3. Violates SRP — collection-state mutation is a domain concern, not a handler concern.

This refactor was identified by the daily architecture-review routine on 2026-06-07.

## Functional Requirements

### FR-1: Add `ReplaceProductAssociations` to `MarketingAction`
The `MarketingAction` domain entity must expose a method that atomically replaces the full set of product associations with a new set.

**Signature (illustrative):**
```csharp
public void ReplaceProductAssociations(IEnumerable<string> productCodes, DateTime utcNow)
```

**Behaviour:**
- Accepts a sequence of raw product-code prefixes plus a `utcNow` timestamp supplied by the caller (no `DateTime.UtcNow` inside the domain).
- Normalises each code with `Trim()` and `ToUpperInvariant()` (matching the existing `AssociateWithProduct` normalisation).
- Deduplicates the normalised codes.
- Clears the existing `ProductAssociations` collection and repopulates it with new `MarketingActionProduct` entries (`MarketingActionId`, `ProductCodePrefix`, `CreatedAt = utcNow`).
- A `null` sequence is treated as an empty sequence (i.e. clears all associations).
- An empty input set leaves the collection empty.

**Acceptance criteria:**
- Calling `ReplaceProductAssociations` with `["abc", "ABC", " abc "]` results in exactly one association with `ProductCodePrefix = "ABC"`.
- Calling with `null` or `[]` empties the collection.
- Existing associations not present in the new set are removed.
- New associations carry the supplied `utcNow` value as `CreatedAt`.
- Unit test covers: empty input, null input, duplicate input, mixed-case input, whitespace input, and a delta scenario (some kept, some added, some removed).

### FR-2: Add `ReplaceFolderLinks` to `MarketingAction`
The `MarketingAction` domain entity must expose a method that atomically replaces the full set of folder links.

**Signature (illustrative):**
```csharp
public void ReplaceFolderLinks(
    IEnumerable<(string folderKey, MarketingFolderType folderType)> links,
    DateTime utcNow)
```

**Behaviour:**
- Accepts a sequence of `(folderKey, folderType)` pairs plus a `utcNow` timestamp.
- Normalises `folderKey` with `Trim()` (matching the existing `LinkToFolder` normalisation).
- Deduplicates by the composite key `(folderKey, folderType)`.
- Clears existing `FolderLinks` and repopulates with new `MarketingActionFolderLink` entries (`MarketingActionId`, `FolderKey`, `FolderType`, `CreatedAt = utcNow`).
- A `null` sequence is treated as an empty sequence.
- Rejects entries where `folderKey` is null, empty, or whitespace by throwing the same exception type the existing `LinkToFolder` uses for invalid input (or, if `LinkToFolder` silently accepts, match that behaviour — see Open Questions).

**Acceptance criteria:**
- Calling `ReplaceFolderLinks` with two pairs differing only in `folderType` keeps both.
- Calling with `null` or `[]` empties the collection.
- Whitespace in `folderKey` is trimmed before persistence.
- Unit test covers: empty input, null input, duplicate input (same key+type), distinct-type-same-key input, whitespace input, and a delta scenario.

### FR-3: Refactor `UpdateMarketingActionHandler` to Use New Methods
The handler must stop touching `action.ProductAssociations` and `action.FolderLinks` directly and instead delegate to the new domain methods.

**Behaviour:**
- Replace lines 95–110 of `UpdateMarketingActionHandler.cs` so the handler:
  - Calls `action.ReplaceProductAssociations(request.AssociatedProducts ?? Enumerable.Empty<string>(), utcNow)`.
  - Calls `action.ReplaceFolderLinks(request.FolderLinks?.Select(l => (l.FolderKey, l.FolderType)) ?? Enumerable.Empty<(string, MarketingFolderType)>(), utcNow)`.
- `utcNow` must be obtained from the existing `IDateTimeProvider` / time abstraction already used in this handler (or, if none is currently injected, fall through to `DateTime.UtcNow` at the call site — see Open Questions).
- No `.Clear()` call on EF navigation properties remains anywhere in the Application layer for this aggregate.

**Acceptance criteria:**
- `UpdateMarketingActionHandler.cs` contains no direct collection mutation (`Clear`, `Add`, `Remove`) against `MarketingAction` navigation properties.
- Existing integration test(s) for `UpdateMarketingAction` continue to pass without modification (the externally observable behaviour is identical).
- A new integration test verifies that an update which removes all product associations actually clears them in the database.
- A new integration test verifies that an update which changes folder-link composition (mix of added, removed, retained) results in the expected final state.

### FR-4: Preserve Backwards-Compatible External Behaviour
The end-to-end semantics of `UpdateMarketingActionCommand` must not change for any caller.

**Acceptance criteria:**
- A request with `AssociatedProducts = null` clears all product associations (matches current behaviour — the existing handler only re-adds when `request.AssociatedProducts?.Any() == true`, but `Clear()` always runs).
- A request with `FolderLinks = null` clears all folder links (same reasoning).
- A request with the same sets as currently persisted is a no-op from the caller's perspective (final state matches input).
- All existing unit and integration tests for the Marketing module continue to pass with no test modifications other than additions.

## Non-Functional Requirements

### NFR-1: Performance
- No regression in handler throughput. The replacement is bounded by the input list size, identical to the prior implementation.
- No additional database round trips: the replace methods operate on in-memory tracked collections; EF Core change tracking continues to emit the same `DELETE`/`INSERT` SQL as before.

### NFR-2: Security
- No new attack surface. Input validation (trimming, case normalisation, dedup) remains where it was, but is now enforced uniformly inside the domain entity.

### NFR-3: Maintainability
- After the refactor, the Application layer must contain zero direct mutations of `MarketingAction`'s navigation collections.
- The domain entity becomes the single source of truth for both *adding* and *replacing* product associations and folder links.

### NFR-4: Testability
- The new domain methods must be unit-testable without EF Core, an in-memory database, or any infrastructure dependency. Pure POCO construction + method call + collection assertion.

## Data Model
No schema changes. Affected types (existing):

- `MarketingAction` (aggregate root) — gains two methods, no new state.
- `MarketingActionProduct` (child entity) — unchanged.
- `MarketingActionFolderLink` (child entity) — unchanged.
- `MarketingFolderType` (enum) — unchanged.

Existing relationships:
- `MarketingAction 1..* MarketingActionProduct` via `ProductAssociations` navigation collection.
- `MarketingAction 1..* MarketingActionFolderLink` via `FolderLinks` navigation collection.

## API / Interface Design

### Public command / endpoint
`UpdateMarketingActionCommand` request DTO and endpoint contract are unchanged. No OpenAPI regeneration required for the FE.

### Domain entity surface (new)
```csharp
public class MarketingAction
{
    // existing members unchanged

    public void ReplaceProductAssociations(
        IEnumerable<string>? productCodes,
        DateTime utcNow);

    public void ReplaceFolderLinks(
        IEnumerable<(string folderKey, MarketingFolderType folderType)>? links,
        DateTime utcNow);
}
```

### Handler surface (refactored)
`UpdateMarketingActionHandler.Handle` keeps its current signature, dependencies, and return type. Internal block at lines 95–110 collapses to two delegated calls.

## Dependencies
- **Entity Framework Core** — continues to handle change tracking of the cleared/repopulated collections (no change to EF configuration).
- **Time abstraction** — the handler must supply a `DateTime` to the domain methods. Use whatever abstraction the surrounding code already uses; if none is present in this handler, accept `DateTime.UtcNow` at the call site (see Open Questions).
- No new NuGet packages, no new infrastructure.

## Out of Scope
- Renaming or restructuring `AssociateWithProduct` / `LinkToFolder` — they remain as-is for callers that need to add a single item.
- Introducing a generic "child collection replace" pattern across other aggregates (e.g. orders, manufacturing) — this spec covers only `MarketingAction`.
- Adding audit logging, soft-delete, or domain events for association removal — flagged as future opportunities only.
- Changes to the `UpdateMarketingActionCommand` request shape or the corresponding FE client.
- Database schema changes or EF migrations.
- Replacing EF Core change tracking with explicit repository-managed deletes.

## Open Questions
None.

## Status: COMPLETE