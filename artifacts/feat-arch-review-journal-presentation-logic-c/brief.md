## Module
Journal

## Finding
`SearchJournalEntriesHandler` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs`, lines 62–88) contains two private methods that compute display strings for the UI:

- `CreateContentPreview(string content, string searchText, int maxLength = 200)` — truncates entry content to 200 chars with ellipsis, centering the window on the search term.
- `ExtractHighlightTerms(string searchText)` — splits the search text into a word list for the frontend to highlight.

These methods exist solely to shape how results are rendered in the browser. They populate `dto.ContentPreview` and `dto.HighlightedTerms` on `SearchJournalEntryDto`, fields whose only consumer is the `JournalList` component.

The problem is compounded by a parallel implementation in the frontend: `JournalList.tsx:261` has its own `truncateContent(content, maxLength = 150)` helper for the non-search list view. The two paths use **different truncation lengths** (backend: 200, frontend: 150), so search results and browse results display previews at different widths.

## Why it matters
**SRP violation**: the handler's job is to execute the search query and return the data. Deciding how many characters to show and which words to bold is a rendering concern that belongs in the frontend. Any change to how search results are visually presented (different preview length, richer highlighting, markdown-aware truncation) requires touching a backend handler instead of a component.

**Inconsistency**: The two truncation implementations drifted apart (200 vs 150 chars). Future maintainers must keep them in sync, and there is no mechanism to catch divergence.

## Suggested fix
Move `CreateContentPreview` and `ExtractHighlightTerms` to the frontend. The handler should return raw `Content` (already present on the full `JournalEntryDto`). `SearchJournalEntryDto` can drop `ContentPreview` and `HighlightedTerms` — or keep them as optional fields computed client-side — and a single `truncateContent` helper in the frontend handles both list and search views at one consistent length.

---
_Filed by daily arch-review routine on 2026-06-10._