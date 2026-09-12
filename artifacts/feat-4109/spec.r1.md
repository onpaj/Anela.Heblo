# Specification: JournalEntry.Create() Factory Method for Domain-Owned Normalization

## Summary
`JournalEntry.Update()` is the single source of truth for how `Title`, `Content`, and `EntryDate` are normalized (trim + `.Date`), but the creation path (`CreateJournalEntryHandler`) duplicates this normalization logic inline instead of delegating to the domain. This spec adds a static factory method, `JournalEntry.Create(...)`, that owns construction-time normalization and audit-field assignment, and updates `CreateJournalEntryHandler` to call it instead of using an object initializer. This closes an Open/Closed violation: any future creation path can no longer bypass the invariant by omission.

## Background
`JournalEntry` (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`) already encapsulates two invariant-bearing domain operations:
- `Update()` (line 153) trims `Title`/`Content` and normalizes `EntryDate` to `.Date`, then stamps modification audit fields.
- `SoftDelete()` (line 164) stamps deletion + modification audit fields.

`CreateJournalEntryHandler.Handle()` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs`, lines 49–58) constructs a new `JournalEntry` via an object initializer, manually re-implementing the same trim/`.Date` normalization that `Update()` already encapsulates:

```csharp
var entry = new JournalEntry
{
    Title = request.Title.Trim(),
    Content = request.Content.Trim(),
    EntryDate = request.EntryDate.Date,
    CreatedAt = now,
    ModifiedAt = now,
    CreatedByUserId = userId,
    CreatedByUsername = currentUser.Name ?? "Unknown User"
};
```

This works today only because the handler author happened to mirror `Update()`'s logic by convention. There is no compiler- or domain-enforced guarantee that a new creation path (a batch importer, a v2 API, a test factory, a data-migration script) will do the same. If it doesn't, entries can be persisted with untrimmed `Title`/`Content` or an `EntryDate` carrying a time component — silently violating the same invariant `Update()` protects on every edit.

The fix mirrors an existing convention already used elsewhere in the domain layer (e.g. `MarginLevel.Create(...)`, `InvoiceDqtResult.Create(...)`): a static factory method on the entity that is the *only* supported way to construct a fully-initialized instance, so the normalization rule lives in exactly one place regardless of how many callers create entries.

## Functional Requirements

### FR-1: `JournalEntry.Create()` static factory method
Add a static factory method to `JournalEntry`:

```csharp
public static JournalEntry Create(
    string title, string content, DateTime entryDate,
    string userId, string username, DateTime now)
```

It constructs and returns a new `JournalEntry` with:
- `Title` = `title.Trim()`
- `Content` = `content.Trim()`
- `EntryDate` = `entryDate.Date`
- `CreatedAt` = `now`
- `ModifiedAt` = `now`
- `CreatedByUserId` = `userId`
- `CreatedByUsername` = `username`

All other properties (`Id`, `IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername`, `ModifiedByUserId`, `ModifiedByUsername`, `ProductAssociations`, `TagAssignments`) retain their type defaults (`Id = 0`, `IsDeleted = false`, collections empty, nullable audit fields `null`) — `Create()` does not set them explicitly.

**Acceptance criteria:**
- Given `title = "  My Title  "`, the returned entry's `Title` is `"My Title"`.
- Given `content = "  Body  "`, the returned entry's `Content` is `"Body"`.
- Given `entryDate = new DateTime(2026, 6, 4, 14, 30, 0)`, the returned entry's `EntryDate` is `new DateTime(2026, 6, 4)` (`TimeOfDay == TimeSpan.Zero`).
- The returned entry's `CreatedAt` and `ModifiedAt` both equal the `now` argument exactly (no internal `DateTime.UtcNow` call — the caller supplies the timestamp, matching how `CreateJournalEntryHandler` already computes `now` once and reuses it for both fields).
- The returned entry's `CreatedByUserId` equals `userId` and `CreatedByUsername` equals `username`, unmodified (no trim — matches current handler behavior, which does not trim these).
- The returned entry's `ModifiedByUserId`, `ModifiedByUsername`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername` are all `null`.
- The returned entry's `IsDeleted` is `false`.
- The returned entry's `ProductAssociations` and `TagAssignments` collections are non-null and empty.
- `Create()` does not itself call `_journalRepository` or any persistence API — it is a pure in-memory construction, consistent with `Update()` and `SoftDelete()`.

### FR-2: `CreateJournalEntryHandler` delegates to the factory
`CreateJournalEntryHandler.Handle()` must construct the new entity via `JournalEntry.Create(...)` instead of an object initializer.

**Acceptance criteria:**
- The object-initializer block (current lines 49–58) is replaced by a call: `var entry = JournalEntry.Create(request.Title, request.Content, request.EntryDate, userId, currentUser.Name ?? "Unknown User", now);`.
- The handler no longer contains `.Trim()` or `.Date` calls for these fields — that logic lives exclusively in `JournalEntry.Create()`.
- All existing handler behavior is preserved byte-for-byte from the caller's perspective: unauthenticated-user check, blank-title validation, product association loop, tag assignment loop, repository `AddAsync`/`SaveChangesAsync` calls, logging, and the response shape are all unchanged.
- All pre-existing tests in `CreateJournalEntryHandlerTests.cs` continue to pass without modification to their assertions (only internal construction changes, not observable behavior).

### FR-3: Domain-level test coverage for `Create()`
Add unit tests for `JournalEntry.Create()` directly (not just through the handler), in `JournalEntryTests.cs`, following the existing `Update_*` test naming/structure in that file.

**Acceptance criteria:**
- A test asserts all fields are trimmed/normalized/assigned per FR-1's acceptance criteria.
- A test asserts default/empty state of fields not set by `Create()` (collections empty, nullable audit fields null, `IsDeleted == false`).
- Tests follow Arrange/Act/Assert style and FluentAssertions, matching the rest of the file.

## Non-Functional Requirements

### NFR-1: Behavioral equivalence
This is a pure refactor. No change in persisted data, API contract, response shape, or error handling for `CreateJournalEntryRequest`/`CreateJournalEntryResponse` is permitted. The change is invisible to API consumers and to the frontend.

### NFR-2: Test isolation
New/modified tests must not depend on wall-clock time in a way that introduces flakiness — where a timestamp needs verification, pass an explicit `now` value (as `Create()`'s signature already requires) rather than asserting against `DateTime.UtcNow` captured inside the test.

## Data Model

**`JournalEntry`** (existing entity, `backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`) — no schema/column changes. This spec adds one static method; no new properties, no migration.

No other entities are affected. `JournalEntryProduct` and `JournalEntryTagAssignment` (populated after construction via `AssociateWithProduct`/`AssignTag`, unchanged) are out of scope for the factory itself.

## API / Interface Design

No public API surface changes. `CreateJournalEntryRequest` / `CreateJournalEntryResponse` (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs`) are unchanged. The only new interface element is the domain-internal static method `JournalEntry.Create(string title, string content, DateTime entryDate, string userId, string username, DateTime now) : JournalEntry`.

## Dependencies

- `JournalEntry` domain entity (existing)
- `CreateJournalEntryHandler` (existing, modified)
- `CreateJournalEntryHandlerTests.cs`, `JournalEntryTests.cs` (existing test files, extended)
- No new NuGet packages, no new project references, no DI changes.

## Out of Scope

- `Update()` and `SoftDelete()` are already domain-owned and are not modified by this spec.
- No change to `UpdateJournalEntryHandler` or `DeleteJournalEntryHandler` — they already call domain methods correctly (per the issue, only the creation path bypasses normalization).
- No new validation rules (e.g. max length enforcement) beyond what already exists via data annotations and the handler's blank-title check.
- No introduction of an `ITimeProvider`/clock abstraction — `Create()` takes `now` as a parameter exactly as the issue's suggested fix specifies, and the handler continues to compute `now = DateTime.UtcNow` itself.
- No batch-import or v2 API creation path is being added; this spec only ensures any *future* one would be forced through the same normalization by construction.

## Open Questions

None.

## Status: COMPLETE
