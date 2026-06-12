# Specification: Move Journal Search Presentation Logic to Frontend

## Summary
Move content preview truncation and search-term highlighting from `SearchJournalEntriesHandler` to the frontend `JournalList` component, eliminating an SRP violation in the backend and consolidating two divergent truncation implementations (200 vs 150 chars) into a single client-side helper used by both browse and search views.

## Background
The Journal module's search handler currently shapes data for rendering: it truncates entry content to a 200-character preview window centered on the matched term, and splits the search query into a list of words for the UI to bold. Both behaviors exist only to feed the `JournalList` React component.

The frontend already has its own `truncateContent(content, maxLength = 150)` helper for the non-search (browse) list view. The two implementations have drifted: search results display 200-char previews while browsed entries display 150-char previews, producing visually inconsistent rows in the same UI surface.

Beyond inconsistency, the current arrangement means presentation changes (different length, markdown-aware truncation, richer highlighting like fuzzy-term matching) require backend changes. The handler should return raw query results; the component should decide how to display them.

## Functional Requirements

### FR-1: Remove presentation logic from search handler
The methods `CreateContentPreview` and `ExtractHighlightTerms` in `SearchJournalEntriesHandler` (backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs, lines 62–88) must be deleted. The handler must no longer compute display strings.

**Acceptance criteria:**
- `CreateContentPreview` is removed from the handler file.
- `ExtractHighlightTerms` is removed from the handler file.
- The handler no longer references string truncation, ellipsis insertion, or term-list construction.
- No other backend code calls these methods (confirmed by build + symbol search).

### FR-2: Return raw content from the search endpoint
The search response must return the entry's full `Content` (already available on `JournalEntryDto`). The frontend will derive the preview from this raw value.

**Acceptance criteria:**
- `SearchJournalEntryDto` exposes `Content` (raw, untruncated).
- The handler populates `Content` directly from the persisted journal entry.
- No length capping happens server-side for search results.

### FR-3: Remove `ContentPreview` and `HighlightedTerms` from the search DTO
Both fields exist solely for the `JournalList` component. They are removed from `SearchJournalEntryDto` rather than kept as optional fields, eliminating dead-on-arrival contract surface and forcing the OpenAPI-generated TypeScript client to reflect the simpler shape.

**Acceptance criteria:**
- `ContentPreview` property is deleted from `SearchJournalEntryDto`.
- `HighlightedTerms` property is deleted from `SearchJournalEntryDto`.
- Backend builds cleanly after removal (`dotnet build`).
- Regenerated TypeScript client no longer exposes these fields.

### FR-4: Consolidate frontend truncation into one helper
The existing `truncateContent` helper in `JournalList.tsx:261` becomes the single source of truth for journal preview rendering. It is used by both the browse list and the search results list.

**Acceptance criteria:**
- One `truncateContent(content, maxLength)` function exists in the frontend (in `JournalList.tsx` or extracted to a shared utility within the same feature folder if reused).
- Both the search results path and the browse path call this helper.
- No duplicate truncation logic remains in the journal frontend codebase.

### FR-5: Search-aware preview window (client-side)
When rendering search results, the preview window is centered on the first occurrence of any search term in the content, matching the prior backend behavior. When no term matches in the content (e.g., match was on title or tags), the preview starts at character 0.

**Acceptance criteria:**
- A frontend helper computes a substring window around the first matched term occurrence.
- Ellipsis (`…` or `...`, matching prior backend style) is prepended/appended when the window is not at the start/end of the content.
- For browse-mode rendering (no search query), the helper falls back to a head-of-content truncation.
- Case-insensitive matching is preserved (consistent with the prior backend implementation).

### FR-6: Client-side highlight term extraction
The search query is split into a list of terms client-side for highlighting in the preview. The existing rendering logic that consumed `HighlightedTerms` is updated to derive the term list from the current search query in React state instead of from the DTO.

**Acceptance criteria:**
- The component splits the active search query into terms (whitespace-separated, empty entries filtered) at render time.
- The highlight render path uses this locally computed list.
- Highlight visual output matches the prior behavior (same terms bolded).

### FR-7: Consistent truncation length across both views
A single `MAX_PREVIEW_LENGTH` constant is used for both browse and search rendering. Value: **200** (matches the prior backend behavior, which provided more useful context around matches). This is exposed as a named constant rather than a magic number.

**Acceptance criteria:**
- A single constant defines preview length.
- Both browse and search views render previews at the same length.
- The constant lives alongside the truncation helper.

### FR-8: Update backend tests
Tests that asserted on `ContentPreview` or `HighlightedTerms` in handler output are removed or rewritten to assert on raw `Content` only. The handler's responsibility narrows accordingly.

**Acceptance criteria:**
- All handler unit tests for `SearchJournalEntriesHandler` compile and pass.
- No test references `ContentPreview` or `HighlightedTerms`.
- Tests covering search-result correctness (entries returned, ordering, paging) remain intact.

### FR-9: Update frontend tests
Frontend tests that mocked `ContentPreview`/`HighlightedTerms` in search responses are updated to use raw `Content`. Render assertions verify that truncated previews and highlighted terms appear as expected.

**Acceptance criteria:**
- All `JournalList` tests pass after the refactor.
- New or updated tests cover: window-centering on matched term, fallback to head-truncation when no match in body, term highlighting from current query state, identical preview length in browse and search rows.

## Non-Functional Requirements

### NFR-1: Performance
Moving truncation to the client adds negligible work per row (≤ a few hundred journal entries per page expected). Raw `Content` payload size increases per row, but typical journal entries are short prose; no payload-size mitigation is required at this stage. Search latency end-to-end must not regress.

### NFR-2: Security
No change to authentication, authorization, or data sensitivity. Raw `Content` was already accessible via the existing journal detail and list endpoints — exposing it on the search response does not widen access.

### NFR-3: Maintainability
The refactor must eliminate (not duplicate) the existing inconsistency. After this change, any future preview-rendering change is a single-file edit in the frontend.

### NFR-4: Backward compatibility
Solo-developer project with no external API consumers. Breaking the search DTO shape is acceptable; the TypeScript client is regenerated on build (per `docs/development/api-client-generation.md`), so frontend code referencing the old fields will fail to compile and force the migration.

## Data Model
No persisted-data changes. The change is purely in the contract surface of the search use case.

**Affected DTOs:**
- `SearchJournalEntryDto` — loses `ContentPreview` and `HighlightedTerms`; gains (or already exposes) `Content`.

**Unaffected:**
- `JournalEntry` entity, persistence schema, repository contracts.

## API / Interface Design

### Backend
Endpoint unchanged: `GET /api/journal/search?query=...` (or equivalent existing route).

**Response shape change** (`SearchJournalEntryDto`):
- Remove: `contentPreview: string`
- Remove: `highlightedTerms: string[]`
- Confirm present: `content: string` (raw, full content)

**Handler change** (`SearchJournalEntriesHandler`):
- Delete `CreateContentPreview`.
- Delete `ExtractHighlightTerms`.
- Map repository results directly to DTOs without preview/term-list computation.

### Frontend
**`JournalList.tsx` (and any shared helper module within the journal feature folder):**
- Single `truncateContent(content: string, options?: { searchQuery?: string; maxLength?: number }): string` helper that:
  - When `searchQuery` is provided and matches in `content`, centers a window of `maxLength` chars on the first match, with leading/trailing ellipses as appropriate.
  - When `searchQuery` is empty or does not match in `content`, returns the head-truncated string with trailing ellipsis if truncated.
- A single `MAX_PREVIEW_LENGTH = 200` constant.
- Term extraction at render time: `query.trim().split(/\s+/).filter(Boolean)`.
- Existing highlight render path (component that bolds matched substrings) is fed by the locally computed term list instead of the removed DTO field.

## Dependencies
- OpenAPI TypeScript client regeneration (automatic on backend build per `docs/development/api-client-generation.md`).
- No new libraries.
- No new feature flags.
- No database migration.

## Out of Scope
- Changes to non-search journal list endpoints or DTOs.
- Markdown-aware truncation (preserving word boundaries beyond the existing whitespace heuristic, handling markdown syntax).
- Fuzzy or stemming-based highlight matching.
- Server-side full-text-search engine changes (Elastic, Postgres FTS configuration).
- Pagination, sorting, or filter behavior changes.
- UI restyling of preview rows beyond making them visually consistent.
- Migrating any other handlers that compute display strings.

## Open Questions
None.

## Status: COMPLETE