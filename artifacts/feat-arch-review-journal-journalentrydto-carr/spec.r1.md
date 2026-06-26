# Specification: Split `JournalEntryDto` into list/detail and search variants

## Summary
Extract a search-specific DTO (`SearchJournalEntryDto`) so that the search-only fields `ContentPreview` and `HighlightedTerms` no longer leak into the list and detail responses of the Journal module. Drop the full `Content` payload from search results in favor of the truncated preview to eliminate the per-row bandwidth waste. The change is contract-narrowing on the read side only; create/update/delete operations and the persistence layer are untouched.

## Background
`JournalEntryDto` (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs`) is the single response type returned by three distinct read operations:

- `GetJournalEntriesHandler` — paged list for the Journal page
- `GetJournalEntryHandler` — single-entry detail view
- `SearchJournalEntriesHandler` — full-text search with previews and highlights
- `JournalTab` catalog widget — embedded list of entries in another module's UI

Two fields on the DTO — `ContentPreview` and `HighlightedTerms` — are only populated by `SearchJournalEntriesHandler` (lines 42–46). Every other call returns `null` / empty for them but still serializes them. In addition, `SearchJournalEntriesHandler` maps the full `Content` string (up to 10 000 characters per entry) into the DTO via `JournalEntryMapper.ToDto`, then computes a 200-character `ContentPreview` on top of it. Both representations are sent over the wire for every search hit.

The frontend has already grown a workaround for this: `JournalList.tsx:336` branches on `entry.contentPreview` to detect whether it is rendering a search result vs. a list row, effectively using the nullability of a search-only field as an out-of-band mode signal.

Issues this creates:

1. **SRP violation on the contract.** A single DTO answers three different questions. Adding a search-only field silently widens the contract of unrelated endpoints.
2. **Bandwidth waste.** Search responses ship both `Content` (≤10 KB) and `ContentPreview` (≤200 B) per hit. For default page sizes this multiplies response size unnecessarily.
3. **Dead fields on the wire.** List and detail responses always carry `contentPreview: null` and `highlightedTerms: []` — visible noise in API payloads, OpenAPI schema, and the generated TypeScript client.
4. **Hidden coupling.** Frontend mode detection piggybacks on a nullable field rather than on a typed contract.

## Functional Requirements

### FR-1: Introduce `SearchJournalEntryDto`
Create a new DTO that represents a single search hit. It carries the same identification/metadata fields as `JournalEntryDto` plus the search-specific fields.

```csharp
public class SearchJournalEntryDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public DateTime EntryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string CreatedByUserId { get; set; } = null!;
    public string? CreatedByUsername { get; set; }
    public string? ModifiedByUserId { get; set; }
    public string? ModifiedByUsername { get; set; }
    public List<string> AssociatedProducts { get; set; } = new();
    public List<JournalEntryTagDto> Tags { get; set; } = new();

    public string ContentPreview { get; set; } = null!;
    public List<string> HighlightedTerms { get; set; } = new();
}
```

**Acceptance criteria:**
- `SearchJournalEntryDto` lives in `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/` alongside the other Journal contracts.
- `ContentPreview` is non-nullable on this type.
- The full `Content` field is **not** present on `SearchJournalEntryDto`.
- The DTO is a `class`, not a `record` (project rule: DTOs are classes because the OpenAPI generator mishandles record parameter order).
- The type is exposed via the OpenAPI document and round-trips through the generated TypeScript client.

### FR-2: Remove search-only fields from `JournalEntryDto`
`ContentPreview` and `HighlightedTerms` are removed from `JournalEntryDto`. The DTO becomes the contract for list, detail, and catalog-widget reads.

**Acceptance criteria:**
- `JournalEntryDto` no longer declares `ContentPreview` or `HighlightedTerms`.
- The `Content` field remains on `JournalEntryDto` (list and detail responses still need it as they do today).
- `GetJournalEntriesHandler`, `GetJournalEntryHandler`, and any catalog/`JournalTab` code paths continue to compile and return `JournalEntryDto` unchanged in semantics.
- The OpenAPI schema for `JournalEntryDto` no longer contains `contentPreview` or `highlightedTerms`.

### FR-3: Switch search response to `SearchJournalEntryDto`
`SearchJournalEntriesResponse.Entries` changes to `List<SearchJournalEntryDto>`. `SearchJournalEntriesHandler` is updated accordingly.

**Acceptance criteria:**
- `SearchJournalEntriesResponse.Entries` is typed `List<SearchJournalEntryDto>`.
- `SearchJournalEntriesHandler` produces `SearchJournalEntryDto` instances (via a new mapper method — see FR-4).
- Pagination metadata on `SearchJournalEntriesResponse` (`TotalCount`, `PageNumber`, `PageSize`, `TotalPages`, `HasNextPage`, `HasPreviousPage`) is unchanged.
- Preview computation (`CreateContentPreview`, `ExtractHighlightTerms`) keeps its current behavior, including the 200-character preview window and the >2-character term filter.
- When `request.SearchText` is null/empty, `ContentPreview` falls back to a non-null truncated preview of the entry's content (≤200 chars, ellipsis suffix when truncated) rather than the previous behavior of leaving the field null. (See Open Questions if a different fallback is desired.)

### FR-4: Add a search mapper to avoid copying the full `Content`
Extend `JournalEntryMapper` (or add a sibling) with a method that maps a domain `JournalEntry` directly to `SearchJournalEntryDto` **without** projecting the full `Content` field. The preview is set by the handler after mapping, using the original domain `entry.Content` as the source.

**Acceptance criteria:**
- A method such as `JournalEntryMapper.ToSearchDto(JournalEntry entry)` returns a `SearchJournalEntryDto` with `ContentPreview` and `HighlightedTerms` left at their defaults.
- The full `Content` string is **not** copied onto the DTO at any point in the search flow.
- `SearchJournalEntriesHandler` passes the domain `entry.Content` (not a DTO field) into `CreateContentPreview` when computing the preview.
- The existing `JournalEntryMapper.ToDto` method continues to populate `Content` for non-search paths.

### FR-5: Update the frontend to use the typed search contract
The OpenAPI TypeScript client is auto-generated on build; the new `SearchJournalEntryDto` type will appear automatically. `JournalList.tsx` is updated so that the row-rendering logic uses the appropriate type for the active mode instead of branching on a nullable field.

**Acceptance criteria:**
- After regenerating `frontend/src/api/generated/api-client.ts`, `JournalEntryDto` no longer exposes `contentPreview` / `highlightedTerms`, and `SearchJournalEntryDto` exposes both as required-on-server-side fields.
- `JournalList.tsx` renders search rows from the search response type (using `contentPreview` directly) and list rows from the list response type (using `content` truncated to 150 chars), without using a nullable field as a discriminator.
- The component compiles under `tsc --noEmit` and passes `npm run lint`.
- No other frontend file references the removed `JournalEntryDto.contentPreview` / `JournalEntryDto.highlightedTerms` after the change.

### FR-6: Update tests
All tests that referenced the removed fields or that asserted on the search response shape are updated.

**Acceptance criteria:**
- Backend unit tests for `SearchJournalEntriesHandler` assert against `SearchJournalEntryDto`, including: preview length ≤ 200, ellipsis prefix/suffix where appropriate, highlight terms filtered by `length > 2`.
- Backend unit tests for `GetJournalEntriesHandler` and `GetJournalEntryHandler` assert that the returned DTO is `JournalEntryDto` and that no search-only fields appear on the serialized payload.
- Any test snapshot or contract test for the OpenAPI document is regenerated to reflect the new schema.
- Frontend test(s) covering `JournalList` continue to pass; rendering of search and list modes is exercised.

## Non-Functional Requirements

### NFR-1: Performance / bandwidth
- Each search hit must omit the full `Content` string from the wire payload. Expected reduction per hit: `len(Content) − len(ContentPreview)` bytes (typically 10 KB → ≤200 B).
- No additional database round-trips are introduced. The search query continues to fetch the same domain entities; only the mapping changes.

### NFR-2: API compatibility
- This is an intentional breaking change to three response shapes (list, detail, search).
- The project ships a single Docker image with the auto-generated TypeScript client; backend and frontend are deployed together. No external/third-party consumer of these endpoints is known.
- The OpenAPI document version should be regenerated as part of the standard build; no manual versioning step required.

### NFR-3: Security
- No new auth, authorization, or data-sensitivity surface. Existing endpoint-level auth on `Journal*` controllers/handlers is unchanged.
- Removing fields strictly reduces the data returned to the client; no PII or sensitive field is added.

### NFR-4: Maintainability
- After the change, adding a future search-only field (e.g., a relevance score) must require **only** a change to `SearchJournalEntryDto` and the search mapper/handler — never to `JournalEntryDto`.
- `JournalEntryDto` retains a single responsibility: representing a journal entry as returned by non-search read paths.

## Data Model
No domain model changes. `Domain.Features.Journal.JournalEntry` remains the source of truth and is unaffected.

Contract layer after the change:

| DTO                       | Used by                                                                                  | Notable fields                                                                 |
| ------------------------- | ---------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------ |
| `JournalEntryDto`         | `GetJournalEntriesHandler`, `GetJournalEntryHandler`, `JournalTab` catalog widget        | `Content` (full), no search fields                                             |
| `SearchJournalEntryDto`   | `SearchJournalEntriesHandler` (via `SearchJournalEntriesResponse`)                       | No `Content`; `ContentPreview` (≤200 chars), `HighlightedTerms`                |
| `JournalEntryTagDto`      | Both of the above (shared, unchanged)                                                    | `Id`, `Name`, `Color`                                                          |

`SearchJournalEntryDto` does **not** inherit from `JournalEntryDto`. The brief proposed inheritance, but inheritance would cause the OpenAPI generator to emit an `allOf` schema and the TypeScript client would model `SearchJournalEntryDto` as carrying `content`. Two flat DTOs keep the contract explicit and the wire payload minimal.

## API / Interface Design

### Backend (HTTP contract)

- `GET /api/journal` — `GetJournalEntriesHandler` → `JournalEntryDto` items (full `content`, no `contentPreview`, no `highlightedTerms`).
- `GET /api/journal/{id}` — `GetJournalEntryHandler` → `JournalEntryDto`.
- Search route → `SearchJournalEntriesHandler` → `SearchJournalEntriesResponse` with `Entries: List<SearchJournalEntryDto>` (no `content`).

Handler/route paths themselves do not change.

### Frontend (UI flow)

- `JournalList.tsx` already maintains an `isSearchMode` flag. After the change:
  - When `isSearchMode` is true, the component consumes `SearchJournalEntryDto[]` and renders `entry.contentPreview` directly.
  - Otherwise it consumes `JournalEntryDto[]` and renders `truncateContent(entry.content!, 150)`.
- No new UI elements are introduced.

## Dependencies
- `backend/src/Anela.Heblo.Application/Features/Journal/**` (Contracts, Mapping, UseCases).
- Backend Journal handler tests.
- `frontend/src/api/generated/api-client.ts` (auto-regenerated on backend build).
- `frontend/src/components/pages/Journal/JournalList.tsx`.
- No third-party library additions; no database schema changes; no migrations.

## Out of Scope
- Changes to `CreateJournalEntryRequest`, `UpdateJournalEntryRequest`, `DeleteJournalEntryRequest`, `CreateJournalTagRequest`, `GetJournalTagsRequest`, or any tag-related DTO.
- Changes to `JournalEntry`, tag entities, or any domain type.
- Repository / persistence changes — `IJournalRepository.SearchEntriesAsync` still returns the same projection.
- Server-side highlight markup (e.g., `<mark>` wrapping). Current "raw preview text + list of terms" behavior is retained.
- Pagination, sort-field, or search-criteria changes.
- Versioned API support / deprecation shims for the removed fields.
- Caching, ETags, or other response-size optimizations beyond dropping `Content` from search hits.

## Open Questions
None.

## Status: COMPLETE