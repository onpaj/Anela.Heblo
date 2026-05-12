# Specification: Consolidate JournalEntry → JournalEntryDto projection into a shared mapper

## Summary
The `JournalEntry` → `JournalEntryDto` projection is duplicated across three MediatR handlers (`GetJournalEntriesHandler`, `SearchJournalEntriesHandler`, `GetJournalEntryHandler`), with a subtle behavioral divergence in the tag mapping that causes two of the handlers to throw `NullReferenceException` on orphaned tag assignments. This spec defines the extraction of a single shared mapper that unifies the projection, applies the null-safe tag guard consistently, and removes the duplication so future DTO changes are made in one place.

## Background
The Journal module exposes three read endpoints that return `JournalEntryDto`:

- `GET /api/journal` → `GetJournalEntriesHandler` (paged list)
- `POST /api/journal/search` → `SearchJournalEntriesHandler` (filtered list, with `ContentPreview` / `HighlightedTerms` populated when `SearchText` is present)
- `GET /api/journal/{id}` → `GetJournalEntryHandler` (single entry)

Each handler constructs a `JournalEntryDto` inline with the same ~22-line LINQ projection of scalar fields, `AssociatedProducts` (distinct `ProductCodePrefix` values from `ProductAssociations`), and `Tags` (mapped from `TagAssignments`). The handlers diverge on one line:

- `GetJournalEntryHandler.cs:49` applies `.Where(ta => ta.Tag != null)` before reading `ta.Tag.Id/Name/Color`.
- `GetJournalEntriesHandler.cs` and `SearchJournalEntriesHandler.cs` skip the guard and access `ta.Tag.*` directly.

If a `JournalEntryTagAssignment` row references a deleted `JournalEntryTag` (no cascade) or is loaded without the navigation populated, the unguarded handlers will throw `NullReferenceException` at runtime, while the single-entry endpoint will silently drop the orphan. The inconsistency makes neither behavior verifiably correct and adds a latent crash to the list and search endpoints.

This is an internal refactor with a small but real correctness fix. No public API contract changes; no UI changes; no schema changes.

## Functional Requirements

### FR-1: Single source of truth for `JournalEntry` → `JournalEntryDto` projection
All three read handlers must produce `JournalEntryDto` via a single shared mapping unit that lives outside the handlers. The handlers must not contain any field-by-field projection of `JournalEntry` themselves.

**Acceptance criteria:**
- A shared static mapping API exists under `Anela.Heblo.Application.Features.Journal` (e.g. `JournalEntryMapper.ToDto(JournalEntry entry)` returning a new `JournalEntryDto`).
- `GetJournalEntriesHandler`, `SearchJournalEntriesHandler`, and `GetJournalEntryHandler` each call the shared mapper exactly once per entry; none of them assign `JournalEntryDto` fields directly except for search-specific enrichment (`ContentPreview`, `HighlightedTerms`).
- Removing or renaming a field on `JournalEntryDto` requires changing exactly one production code location (the mapper).
- The mapper is pure: it takes a `JournalEntry` and returns a fully-populated `JournalEntryDto`, with no I/O, no DI, no side effects.

### FR-2: Consistent null-safe tag projection
The tag projection must skip `JournalEntryTagAssignment` rows whose `Tag` navigation is `null`, regardless of which handler invokes the mapper. This unifies the three handlers on the safer of the two existing behaviors.

**Acceptance criteria:**
- For an input `JournalEntry` whose `TagAssignments` contains one or more entries with `Tag == null`, the resulting `Tags` collection in `JournalEntryDto` contains only the entries with a non-null tag, and no exception is thrown.
- For an input `JournalEntry` whose `TagAssignments` is empty, `Tags` is an empty list (not null).
- The behavior is identical across the list, search, and single-entry endpoints.

### FR-3: Preserve existing DTO shape and field semantics
The mapper must produce a `JournalEntryDto` that is byte-for-byte equivalent to today's output for inputs where all `Tag` navigations are populated, so consumers (frontend, generated OpenAPI clients) see no change.

**Acceptance criteria:**
- For any `JournalEntry` with all tag navigations populated, the DTO produced by the mapper has identical values to the DTO produced by the current inline projection: same scalar fields, same `AssociatedProducts` (distinct `ProductCodePrefix` values, original ordering), same `Tags` list (same order, same `Id`/`Name`/`Color`).
- The OpenAPI schema for `JournalEntryDto` is unchanged.
- `ContentPreview` and `HighlightedTerms` remain default (null / empty list) when the mapper is called; the search handler continues to populate them after mapping.

### FR-4: Search-specific enrichment remains in the search handler
`ContentPreview` and `HighlightedTerms` are search-only concerns and must not move into the shared mapper.

**Acceptance criteria:**
- The mapper does not reference `SearchText` or any search criteria.
- `SearchJournalEntriesHandler` continues to populate `ContentPreview` and `HighlightedTerms` on each DTO after mapping, only when `request.SearchText` is non-empty, using the existing `CreateContentPreview` and `ExtractHighlightTerms` private methods.
- Behavior for an empty `SearchText` is unchanged: `ContentPreview` stays null, `HighlightedTerms` stays empty.

### FR-5: Mapper placement and visibility
The mapper lives in the Application layer alongside the other Journal use-case code; it is not exposed across module boundaries.

**Acceptance criteria:**
- The mapper type is declared in the `Anela.Heblo.Application.Features.Journal` namespace (a sibling of `Contracts/` and `UseCases/`, e.g. `Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs`).
- The mapper is declared `internal static` so it is reachable from any handler in the Journal feature but not from other features or the Domain/Infrastructure layers.
- No domain types leak into the Contracts layer beyond what is already there today.

### FR-6: Test coverage for the mapper
The mapper has direct unit tests that lock in the projection contract and the null-tag guard.

**Acceptance criteria:**
- A new xUnit test class (e.g. `JournalEntryMapperTests` under `backend/test/Anela.Heblo.Tests/Features/Journal/`) covers:
  - All scalar fields are copied through (one assertion-per-field test using FluentAssertions, or a single `Should().BeEquivalentTo(...)` against an expected DTO).
  - `AssociatedProducts` contains the distinct `ProductCodePrefix` values from `ProductAssociations`, with duplicates collapsed.
  - `AssociatedProducts` is an empty list when `ProductAssociations` is empty.
  - `Tags` is populated from `TagAssignments` with `Id`/`Name`/`Color` mapped through.
  - `Tags` skips assignments whose `Tag` is null and does not throw.
  - `Tags` is an empty list when `TagAssignments` is empty.
  - `ContentPreview` is null and `HighlightedTerms` is empty after mapping (mapper does not populate them).
- The tests use AAA structure and behavior-named tests per the project test conventions.
- All three handlers' existing tests (if any) continue to pass without modification to their assertions.

### FR-7: Handler refactor is behavior-preserving
Each of the three handlers is updated to call the mapper, and no other handler behavior changes.

**Acceptance criteria:**
- `GetJournalEntriesHandler.Handle` continues to return the same `GetJournalEntriesResponse` shape with the same paging math (`TotalPages`, `HasNextPage`, `HasPreviousPage`).
- `SearchJournalEntriesHandler.Handle` continues to apply content-preview enrichment exactly when `request.SearchText` is non-empty.
- `GetJournalEntryHandler.Handle` continues to return `ErrorCodes.JournalEntryNotFound` with the `entryId` payload when the repository returns null.
- No new exceptions are introduced; no exceptions previously caught are swallowed.
- `dotnet build` succeeds with zero new warnings.
- `dotnet format` produces no diff after the refactor.

## Non-Functional Requirements

### NFR-1: Performance
The refactor is allocation- and CPU-neutral. The mapper performs the same materialization as the inline projection (the same `.Select(...).ToList()` calls), so per-entry cost is unchanged within measurement noise.

- No additional database round-trips.
- No additional materialization of navigation properties beyond what the repository already loads.
- Mapper is a pure synchronous method; no `async` overhead.
- For the paged list endpoint with `PageSize = 100`, end-to-end latency must remain within ±5 % of the pre-refactor baseline on a local benchmark.

### NFR-2: Security
No security surface change.

- No new inputs are accepted from clients.
- No new data is exposed; the DTO shape is identical.
- No logging of new fields, including no logging of tag or content data.
- Authorization continues to be enforced at the controller layer (unchanged).

### NFR-3: Maintainability
The change reduces the surface area for DTO drift.

- Adding a new field to `JournalEntryDto` requires editing exactly one production code location after this change (the mapper) plus the DTO class itself.
- The mapper has no dependencies that would require mocking in handler tests.
- Code style follows the project's C# conventions (nullable reference types enabled, `internal static`, expression-bodied when readable, no records for DTOs per project rule).

### NFR-4: Backwards compatibility
The OpenAPI-generated TypeScript client and any frontend consumers continue to work without regeneration or code changes, because the wire shape of `JournalEntryDto` is unchanged.

## Data Model
No schema changes. Existing types are unchanged:

- `JournalEntry` (domain entity) — source of the projection.
  - `ProductAssociations: ICollection<JournalEntryProductAssociation>` — each has `ProductCodePrefix`.
  - `TagAssignments: ICollection<JournalEntryTagAssignment>` — each has a `Tag` navigation (may be null in degenerate data) with `Id`, `Name`, `Color`.
- `JournalEntryDto` (Application/Contracts) — projection target; class, not record, per project DTO rule.
- `JournalEntryTagDto` (Application/Contracts) — tag projection target.

The mapper introduces no new types and does not alter existing ones.

## API / Interface Design

### New internal API
```csharp
namespace Anela.Heblo.Application.Features.Journal.Mapping;

internal static class JournalEntryMapper
{
    public static JournalEntryDto ToDto(JournalEntry entry);
}
```

- Single public method on the static class.
- Returns a freshly-allocated `JournalEntryDto` per call (no caching, no reuse).
- Caller is responsible for any post-mapping enrichment (e.g. search preview).

### Handler call sites after refactor
- `GetJournalEntriesHandler`: replace the inline `result.Items.Select(entry => new JournalEntryDto { ... }).ToList()` with `result.Items.Select(JournalEntryMapper.ToDto).ToList()`.
- `SearchJournalEntriesHandler`: same as above, followed by the existing `foreach` that sets `ContentPreview` / `HighlightedTerms`.
- `GetJournalEntryHandler`: replace the inline `new JournalEntryDto { ... }` with `JournalEntryMapper.ToDto(entry)` inside the success-branch `GetJournalEntryResponse`.

### Public HTTP API
Unchanged. No controller, route, request, or response shapes are modified.

## Dependencies
- `Anela.Heblo.Domain.Features.Journal` (existing) — source entity types.
- `Anela.Heblo.Application.Features.Journal.Contracts` (existing) — `JournalEntryDto`, `JournalEntryTagDto`.
- xUnit + FluentAssertions (existing test deps) — for the new mapper unit tests.
- No new NuGet packages.

## Out of Scope
- Adding cascade behavior or referential-integrity fixes for `JournalEntryTagAssignment` → `JournalEntryTag`. Orphan rows continue to be tolerated by the mapper, not prevented at the database layer.
- Logging or telemetry for orphaned tag assignments. (Captured as an Open Question.)
- Touching the other journal handlers (`CreateJournalEntry`, `UpdateJournalEntry`, `DeleteJournalEntry`, `CreateJournalTag`, `GetJournalTags`) — they do not duplicate the read-side projection.
- Introducing AutoMapper, Mapster, or any third-party mapping library. A hand-written static method is sufficient and avoids a new dependency.
- Refactoring the search preview / highlight logic in `SearchJournalEntriesHandler`. It stays where it is.
- Changing the `JournalEntryDto` shape (e.g. exposing whether a tag was dropped, or adding a flag for orphaned assignments).
- Frontend changes; the OpenAPI contract is unchanged.

## Open Questions
None.

## Status: COMPLETE