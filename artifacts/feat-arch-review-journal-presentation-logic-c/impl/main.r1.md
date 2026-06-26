The implementation is complete and committed. Here's the output summary:

---

# Implementation: Move Journal Search Presentation Logic to Frontend

## What was implemented

Removed `CreateContentPreview` and `ExtractHighlightTerms` from `SearchJournalEntriesHandler`, making the handler return raw content. Created a new `journalPreview.ts` frontend helper that consolidates preview truncation for both browse and search views, replacing two divergent implementations.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` — removed `ContentPreview`/`HighlightedTerms`; added `Content`
- `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs` — `ToSearchDto` now sets `Content = entry.Content`; removed handler-overwrite comment
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — deleted `CreateContentPreview` and `ExtractHighlightTerms`; reduced to `result.Items.Select(JournalEntryMapper.ToSearchDto).ToList()`
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — removed 3 presentation-logic tests; added `Handle_ReturnsRawContentFromEntry`
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs` — rewrote `ToSearchDto` tests to assert on `Content`; removed `ContentPreview`/`HighlightedTerms` assertions
- `frontend/src/components/pages/Journal/journalPreview.ts` — NEW: `MAX_PREVIEW_LENGTH = 200`, `truncateContent(content, options?)` with search-aware window centering
- `frontend/src/components/pages/Journal/JournalList.tsx` — removed inline `truncateContent`; both browse and search paths use the shared helper
- `frontend/src/components/catalog/detail/tabs/JournalTab.tsx` — replaced `entry.contentPreview` with `truncateContent(entry.content!)`
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — added `useScreenView` mock; no contentPreview/highlightedTerms refs to migrate
- `frontend/src/components/pages/Journal/__tests__/journalPreview.test.ts` — NEW: 11 unit tests covering all truncation paths

## Tests

- **Backend**: 96 journal tests pass (`dotnet test --filter Journal`)
- **Frontend**: 33 tests pass (18 JournalList + 15 journalPreview)

## How to verify

```bash
# Backend
dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~Journal" --no-build

# Frontend (from worktree/frontend with node_modules symlinked)
npm test -- --testPathPattern="journalPreview|JournalList" --watchAll=false
```

## Notes

- **Highlighting dropped intentionally** (arch-review Decision 2): `HighlightedTerms` was populated on the wire but never rendered in any consumer. FR-6 is omitted rather than introducing net-new `<mark>` rendering.
- **Browse preview length grows 150→200 chars**: intentional convergence per FR-7; both views now use `MAX_PREVIEW_LENGTH = 200`.
- **`JournalTab.tsx` migrated in the same change**: both `SearchJournalEntryDto` consumers updated together to avoid TS compile errors after client regeneration.
- Frontend `node_modules` was symlinked from the main repo for test execution (worktree doesn't carry its own installation).

## PR Summary

Removes the SRP violation in `SearchJournalEntriesHandler` where presentation logic (200-char content preview centering on matched terms, term extraction for highlighting) was embedded in a use-case handler. The handler now delegates directly to `JournalEntryMapper.ToSearchDto` and returns raw content.

A new `journalPreview.ts` module consolidates preview rendering: `truncateContent(content, { searchQuery? })` mirrors the prior backend window-centering algorithm and is used by both `JournalList.tsx` (browse + search rows) and `JournalTab.tsx` (catalog product-detail). Browse previews grow from 150 to 200 chars, matching the search preview length and eliminating the visual inconsistency between the two views.

`HighlightedTerms` is removed without replacement — it was computed, transported over the wire, and never rendered by either frontend consumer.

### Changes
- `SearchJournalEntryDto.cs` — removed `ContentPreview`/`HighlightedTerms`; added `Content`
- `JournalEntryMapper.cs` — `ToSearchDto` maps `Content = entry.Content`
- `SearchJournalEntriesHandler.cs` — deleted two private presentation methods; handler is now a thin delegation to the mapper
- `SearchJournalEntriesHandlerTests.cs` — removed 3 stale tests; added `Handle_ReturnsRawContentFromEntry`
- `JournalEntryMapperTests.cs` — rewrote two `ToSearchDto` tests to assert on `Content`
- `journalPreview.ts` — new helper module with `MAX_PREVIEW_LENGTH = 200` and `truncateContent`
- `JournalList.tsx` — both render branches use the shared helper; inline function removed
- `JournalTab.tsx` — uses `truncateContent(entry.content!)` instead of `entry.contentPreview`
- `journalPreview.test.ts` — 11 new unit tests for the helper

## Status
DONE