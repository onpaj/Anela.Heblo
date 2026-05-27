# Specification: Encapsulate Replace Semantics for JournalEntry Collections

## Summary
Replace the raw `.Clear()` + per-item add pattern in `UpdateJournalEntryHandler` with two new domain methods on `JournalEntry`: `ReplaceProductAssociations(IEnumerable<string>)` and `ReplaceTagAssignments(IEnumerable<int>)`. This restores tell-don't-ask encapsulation, eliminates the inconsistency between the create and update paths, and centralizes invariant enforcement for both add and remove flows.

## Background
`UpdateJournalEntryHandler.Handle` (lines 62–80) directly mutates the `ProductAssociations` and `TagAssignments` navigation collections by calling `.Clear()` on them before re-adding items through the existing `AssociateWithProduct`/`AssignTag` domain methods. `CreateJournalEntryHandler` routes 100% of mutations through domain methods. The asymmetry means:

- Any future removal invariant (audit, validation, side effect) would need to be applied in both the handler and the entity.
- The handler violates tell-don't-ask by reaching into the aggregate's internal collections.
- EF Core change-tracking for owned/related collections is brittle when cleared directly — future cascade or tracking changes could surface as silent data bugs.

The fix is small, surgical, and confined to the Journal aggregate and one handler. The domain entity already has the templates (`AssociateWithProduct`, `AssignTag`) that the new methods mirror.

## Functional Requirements

### FR-1: Add `ReplaceProductAssociations` domain method to `JournalEntry`
A new public instance method on `Anela.Heblo.Domain.Features.Journal.JournalEntry` that accepts a collection of product identifiers and atomically updates `ProductAssociations` to exactly match that collection.

**Signature:**
```csharp
public void ReplaceProductAssociations(IEnumerable<string>? productCodes)
```

**Behavior:**
- A `null` or empty input clears all existing associations.
- Input is de-duplicated (case-insensitive, after trim/upper) before comparison.
- Each non-null, non-whitespace product code is normalized with `Trim().ToUpperInvariant()`, matching the existing rule in `AssociateWithProduct`.
- Whitespace-only or empty codes trigger `ArgumentException`, matching the per-item guard in `AssociateWithProduct`.
- Associations not present in the new set are removed.
- Associations already present (matched by `ProductCodePrefix` after normalization) are left in place — no churn on unchanged items, so `CreatedAt` is preserved and EF tracking is not unnecessarily disturbed.
- New associations are added via the same construction path used by `AssociateWithProduct` (sets `JournalEntryId` and normalized `ProductCodePrefix`).

**Acceptance criteria:**
- Calling with `null` empties `ProductAssociations`.
- Calling with `[]` empties `ProductAssociations`.
- Calling with `["AB-1", "ab-1", " AB-1 "]` results in exactly one association with `ProductCodePrefix = "AB-1"`.
- Calling with `["X"]` on an entry that already has `["X", "Y"]` removes `Y`, retains the existing `X` instance (object reference preserved), and adds no duplicate.
- Calling with `[" "]` throws `ArgumentException`.

### FR-2: Add `ReplaceTagAssignments` domain method to `JournalEntry`
A new public instance method that accepts a collection of tag IDs and atomically updates `TagAssignments` to exactly match that collection.

**Signature:**
```csharp
public void ReplaceTagAssignments(IEnumerable<int>? tagIds)
```

**Behavior:**
- A `null` or empty input clears all existing tag assignments.
- Input is de-duplicated.
- Assignments not present in the new set are removed.
- Assignments already present (matched by `TagId`) are retained (instance preserved).
- New assignments are constructed via the same path as `AssignTag` (sets `JournalEntryId` and `TagId`).

**Acceptance criteria:**
- Calling with `null` empties `TagAssignments`.
- Calling with `[1, 1, 2]` leaves assignments for exactly `{1, 2}`.
- Calling with `[1]` on an entry with `{1, 2}` removes the assignment for `2` and retains the existing `TagId == 1` instance.
- No validation on tag-existence is performed in the domain method (consistent with the existing `AssignTag`, which also does not verify the tag row exists).

### FR-3: Update `UpdateJournalEntryHandler` to use the new domain methods
Replace lines 61–80 of `UpdateJournalEntryHandler.cs` with two calls:

```csharp
entry.ReplaceProductAssociations(request.AssociatedProducts);
entry.ReplaceTagAssignments(request.TagIds);
```

The handler no longer touches `ProductAssociations` or `TagAssignments` directly.

**Acceptance criteria:**
- The handler file no longer references `.Clear()` on `ProductAssociations` or `TagAssignments`.
- The handler file no longer iterates `request.AssociatedProducts` or `request.TagIds` directly.
- Behavior observed via the existing PUT endpoint (`PUT /api/journal/{id}` or equivalent) is unchanged for typical inputs: passing a list replaces the set; passing `null` clears it.

### FR-4: Unit-test coverage for the new domain methods
Add xUnit tests in `backend/test/Anela.Heblo.Tests/Features/Journal/` (new file, e.g. `JournalEntryDomainTests.cs`) covering both methods.

**Acceptance criteria — `ReplaceProductAssociations`:**
- Replacing with `null` clears existing associations.
- Replacing with empty collection clears existing associations.
- Replacing with a superset adds the new codes.
- Replacing with a disjoint set removes the old codes.
- Replacing with overlapping codes preserves the existing instance reference for unchanged items.
- De-duplication is case-insensitive and whitespace-insensitive.
- Whitespace-only entries throw `ArgumentException`.

**Acceptance criteria — `ReplaceTagAssignments`:**
- Replacing with `null` clears existing assignments.
- Replacing with empty collection clears existing assignments.
- Replacing with a superset adds the new IDs.
- Replacing with a disjoint set removes the old IDs.
- Replacing with overlapping IDs preserves the existing instance reference.
- Duplicate IDs in input are collapsed.

### FR-5: Handler test coverage
If unit tests for `UpdateJournalEntryHandler` exist, update them to verify behavior via the new domain methods (i.e. set up an entry with prior associations and assert replacement semantics through the handler). If none exist, do not introduce a new test project — handler behavior is exercised indirectly by the domain tests.

**Acceptance criteria:**
- Existing tests still pass.
- No new test infrastructure is introduced.

## Non-Functional Requirements

### NFR-1: Performance
- Replace operations are O(n + m) where n = existing count, m = incoming count. Journal entries have a small number of associations and tags in practice (single-digit to low-double-digit), so set-difference using `HashSet<T>` is acceptable and preferred over nested loops.
- No additional database round-trips are introduced. EF change tracking should detect removal of the unwanted child rows and insertion of new ones via the existing `UpdateAsync`/`SaveChangesAsync` flow.

### NFR-2: Security
- No change to the authentication/authorization model. The existing `currentUser.IsAuthenticated` check in the handler remains.
- No new user input is accepted; the contract of `UpdateJournalEntryRequest` is unchanged.

### NFR-3: Backwards compatibility
- Public API contract (`UpdateJournalEntryRequest`/`UpdateJournalEntryResponse`) is unchanged.
- Database schema is unchanged.
- Observable behavior of `PUT` is unchanged for all valid inputs already accepted today.

### NFR-4: Code quality
- Domain methods must follow the same coding style as existing `AssociateWithProduct`/`AssignTag`: `[`guard clauses → existence check → mutation`]`.
- `dotnet build` and `dotnet format` must pass.
- All journal-related tests must pass.

## Data Model

No schema changes. Affected entities:

- `JournalEntry` (1) — gains two methods, no property/field changes.
- `JournalEntryProduct` (n) — owned child; lifecycle continues to be managed via parent's navigation collection.
- `JournalEntryTagAssignment` (n) — owned child; same.

Relationships and EF mappings are unchanged.

## API / Interface Design

### Domain (new public methods on `JournalEntry`)
```csharp
public void ReplaceProductAssociations(IEnumerable<string>? productCodes);
public void ReplaceTagAssignments(IEnumerable<int>? tagIds);
```

### Application handler (changed body, unchanged signature)
`UpdateJournalEntryHandler.Handle` retains its signature; the replace blocks become two method calls.

### HTTP API
Unchanged. `PUT /api/journal/{id}` (or whichever route maps to `UpdateJournalEntryRequest`) continues to accept the same payload.

## Dependencies

- `Anela.Heblo.Domain.Features.Journal.JournalEntry`
- `Anela.Heblo.Application.Features.Journal.UseCases.UpdateJournalEntry.UpdateJournalEntryHandler`
- `Anela.Heblo.Tests` project for new unit tests
- No new NuGet packages, no new abstractions, no new DI registrations.

## Out of Scope

- Refactoring `AssociateWithProduct` or `AssignTag` themselves.
- Validating that supplied `tagId` values exist in the `JournalEntryTag` table (current `AssignTag` does not validate either; behavioral parity is intentional).
- Adding audit-trail entries for removed associations/assignments (no existing audit mechanism on these child entities).
- Introducing a domain event for replace operations.
- Changing the `CreateJournalEntryHandler` (already correctly delegates to domain methods).
- Restricting edit permissions to the original author (explicitly left as a future concern in the handler comment).
- Front-end changes.

## Open Questions

None.

## Status: COMPLETE