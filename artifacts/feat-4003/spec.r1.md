# Specification: Remove duplicate SearchJournalEntryDto in favor of JournalEntryDto

## Summary
`SearchJournalEntryDto` is a field-for-field duplicate of `JournalEntryDto`, and `JournalEntryMapper.ToSearchDto()` is a near-character-for-character duplicate of `ToDto()`. This is pure duplication with no behavioral or structural justification. This change removes the duplicate type and its mapping method, consolidating both the entry-detail and entry-search endpoints on a single `JournalEntryDto`, with no observable change to API responses or frontend behavior.

## Background
The Journal module exposes two read paths for journal entries: a single-entry/list path using `JournalEntryDto` (produced by `JournalEntryMapper.ToDto()`), and a search path (`SearchJournalEntriesResponse.Entries`) using `SearchJournalEntryDto` (produced by `JournalEntryMapper.ToSearchDto()`). Both DTOs expose the identical eleven properties (`Id`, `Title`, `Content`, `EntryDate`, `CreatedAt`, `ModifiedAt`, `CreatedByUserId`, `CreatedByUsername`, `ModifiedByUserId`, `ModifiedByUsername`, `AssociatedProducts`, `Tags`), and both mapper methods build them from a `JournalEntry` domain entity using identical logic. There is no current or foreseeable divergence between the two shapes — the split was speculative (YAGNI). Any future field added to one DTO must be manually duplicated into the other and both mapper methods, or the two endpoints silently drift out of sync. This was flagged by the daily arch-review routine (2026-08-31) as a duplication-cleanup finding, filed as issue #4003.

## Functional Requirements

### FR-1: Remove `SearchJournalEntryDto`
Delete `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` in its entirety. `JournalEntryTagDto` (also referenced by `JournalEntryDto.cs`) is unaffected — it must remain available from `JournalEntryDto.cs` where it is already declared.

**Acceptance criteria:**
- `SearchJournalEntryDto.cs` no longer exists in the repository.
- No remaining reference to the `SearchJournalEntryDto` type anywhere in `backend/` or `frontend/`.
- The solution builds successfully after removal.

### FR-2: Point `SearchJournalEntriesResponse.Entries` at `JournalEntryDto`
Change the `Entries` property on `SearchJournalEntriesResponse` (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs`) from `List<SearchJournalEntryDto>` to `List<JournalEntryDto>`.

**Acceptance criteria:**
- `SearchJournalEntriesResponse.Entries` is declared as `List<JournalEntryDto>`.
- The JSON shape of `SearchJournalEntriesResponse` is unchanged (same property names, same property types per entry) since `JournalEntryDto` and `SearchJournalEntryDto` were structurally identical — this is a pure type-identity change, not a contract change.

### FR-3: Remove `JournalEntryMapper.ToSearchDto()` and route through `ToDto()`
Delete the `ToSearchDto(JournalEntry entry)` method from `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs`. Update `SearchJournalEntriesHandler.Handle()` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`, line 34) to call `JournalEntryMapper.ToDto` instead of `JournalEntryMapper.ToSearchDto` when projecting `result.Items`.

**Acceptance criteria:**
- `JournalEntryMapper` contains only `ToDto(JournalEntry entry)`; `ToSearchDto` no longer exists.
- `SearchJournalEntriesHandler` builds `entryDtos` via `result.Items.Select(JournalEntryMapper.ToDto).ToList()`.
- Existing tests in `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` continue to pass unmodified in behavior (assertions against DTO property values remain valid, since the values are computed identically to before).

### FR-4: Update generated OpenAPI client and frontend import
Regenerate the OpenAPI TypeScript client (per `docs/development/api-client-generation.md`) so `SearchJournalEntryDto` is no longer emitted and `SearchJournalEntriesResponse.entries` is typed as `JournalEntryDto[]`. Update the frontend import in `JournalList.tsx` (line 22) to remove the now-nonexistent `SearchJournalEntryDto` import and use `JournalEntryDto` wherever `SearchJournalEntryDto` was previously used as a type annotation.

**Acceptance criteria:**
- No frontend source file imports or references `SearchJournalEntryDto`.
- `JournalList.tsx` (and any other consumer found during implementation) compiles against `JournalEntryDto` with no type errors.
- `npm run build` and `npm run lint` pass in `frontend/`.

## Non-Functional Requirements

### NFR-1: No behavior change
This is a pure refactor. The search endpoint (`SearchJournalEntries`) must return byte-for-byte identical JSON payloads before and after the change, for the same underlying data. No new fields, no removed fields, no renamed fields.

### NFR-2: Backward compatibility of the wire contract
Because `JournalEntryDto` and `SearchJournalEntryDto` were structurally identical (same property names and types), consumers of the API (including any external clients, if applicable) see no schema change in the JSON response — only the OpenAPI schema's *type name* for the `entries` array items changes internally (from a `SearchJournalEntryDto` schema reference to a `JournalEntryDto` schema reference in the generated spec), which does not affect runtime JSON shape.

## Data Model
No database or domain model changes. `JournalEntry` (domain entity) is unchanged. The only model-level change is the removal of one Application-layer DTO class (`SearchJournalEntryDto`) and its consolidation with an existing one (`JournalEntryDto`).

## API / Interface Design
- `GET`/search endpoint backing `SearchJournalEntriesRequest` → `SearchJournalEntriesResponse`: the `entries` property's item schema changes from `SearchJournalEntryDto` to `JournalEntryDto` in the OpenAPI spec. Field names/types per entry are unchanged, so this is a non-breaking schema change for any client relying on structural shape (TypeScript structural typing, JSON deserialization by field name).
- No new endpoints, no removed endpoints, no changed request parameters.

## Dependencies
- OpenAPI client generation tooling (`docs/development/api-client-generation.md`) must be re-run as part of implementation so the frontend's generated types reflect the DTO consolidation.
- No external service dependencies.

## Out of Scope
- Any change to `JournalEntry` domain entity, `IJournalRepository`, or search/filter logic.
- Any change to the `JournalEntryTagDto` type.
- Any change to other Journal endpoints (e.g. get-by-id, create, update) beyond the search path.
- Broader DTO-duplication cleanup elsewhere in the codebase (this issue is scoped to Journal only).

## Open Questions
None.

## Status: COMPLETE
