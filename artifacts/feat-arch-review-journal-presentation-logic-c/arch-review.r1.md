# Architecture Review: Move Journal Search Presentation Logic to Frontend

## Skip Design: true

Backend-only contract change plus a presentation-logic move to the frontend. No new screens, no new visual components, no design decisions. The single visual outcome — preview rows render at a consistent length across browse and search views — is a regression-elimination, not new design.

## Architectural Fit Assessment

The change aligns cleanly with three existing project conventions:

- **Vertical-slice ownership** (`docs/architecture/development_guidelines.md`): the search handler in `Features/Journal/UseCases/SearchJournalEntries/` is the right and only place this presentation logic lives today. Removing `CreateContentPreview` / `ExtractHighlightTerms` narrows the slice without crossing module boundaries.
- **Auto-regenerated TypeScript client** (`docs/development/api-client-generation.md`): dropping `ContentPreview` / `HighlightedTerms` from `SearchJournalEntryDto` will surface as compile errors on the frontend the moment the OpenAPI client regenerates. This is the project's intended migration mechanism for breaking DTO changes.
- **Solo-dev, no external consumers** (CLAUDE.md): the DTO shape can break without coordination.

**Integration points worth flagging that the spec does not address:**

1. **`SearchJournalEntryDto` has two consumers, not one.** `frontend/src/components/catalog/detail/tabs/JournalTab.tsx:107` reads `entry.contentPreview` to render journal previews on the catalog product-detail page. It uses `useJournalEntriesByProduct` (`frontend/src/api/hooks/useJournal.ts:191`), which calls the **same** `journal_SearchJournalEntries` endpoint with a product-code prefix. The spec only names `JournalList.tsx`. Any helper must serve both, and `JournalTab.tsx` must be migrated in the same change or the catalog detail page breaks.
2. **No highlight rendering exists today.** A `grep` for `mark`, `<strong>`, `font-bold`, or any term-highlighting JSX in `JournalList.tsx` and `JournalTab.tsx` returns nothing relevant. `HighlightedTerms` is populated, transported over the wire, and dropped on the floor. FR-6's acceptance criterion "Highlight visual output matches the prior behavior (same terms bolded)" is therefore underspecified — prior behavior is **no highlighting**. The team must explicitly decide: (a) drop highlighting entirely (matches current rendered behavior), or (b) introduce highlighting now as a new capability (adds scope).
3. **`JournalEntryMapper.ToSearchDto`** (`backend/.../Mapping/JournalEntryMapper.cs:38`) currently omits `Content` and has a comment ("`The search handler overwrites these with real values before returning`") tying the mapper to the handler. The spec's FR-1/FR-2 imply the mapper needs to be updated, but it is never mentioned by name. This file must be edited alongside the handler.
4. **`JournalEntryMapperTests.cs:220`** asserts on `ContentPreview` / `HighlightedTerms` defaults. FR-8 names only `SearchJournalEntriesHandler` tests; the mapper test is missed.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────┐
│ Backend (Application/Features/Journal/UseCases/SearchJournal...)   │
│                                                                    │
│   SearchJournalEntriesHandler                                      │
│     • Calls IJournalRepository.SearchEntriesAsync(...)             │
│     • Maps via JournalEntryMapper.ToSearchDto(entry)               │
│     • Returns SearchJournalEntriesResponse — NO display strings    │
│                                                                    │
│   JournalEntryMapper.ToSearchDto                                   │
│     • Now populates Content (raw)                                  │
│                                                                    │
│   SearchJournalEntryDto                                            │
│     • Loses: ContentPreview, HighlightedTerms                      │
│     • Gains: Content (string, raw)                                 │
└────────────────────────────────────────────────────────────────────┘
                              │ OpenAPI / NSwag
                              ▼
┌────────────────────────────────────────────────────────────────────┐
│ Frontend                                                           │
│                                                                    │
│   components/pages/Journal/journalPreview.ts        (NEW)          │
│     • MAX_PREVIEW_LENGTH = 200                                     │
│     • truncateContent(content, { searchQuery? }): string           │
│       └─ centered window when searchQuery hits in content          │
│       └─ head-truncate fallback otherwise                          │
│                                                                    │
│   ┌──────────────────────────────┐    ┌──────────────────────────┐ │
│   │ pages/Journal/JournalList    │    │ catalog/detail/tabs/      │ │
│   │   uses truncateContent for   │    │   JournalTab              │ │
│   │   both browse & search       │    │   uses truncateContent    │ │
│   │   (single code path)         │    │   on entry.content        │ │
│   └──────────────────────────────┘    └──────────────────────────┘ │
└────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Where the helper lives

**Options considered:**
- (A) Inline `truncateContent` inside `JournalList.tsx` as-is, duplicate into `JournalTab.tsx`.
- (B) Extract to `frontend/src/components/pages/Journal/journalPreview.ts` and import from both consumers.
- (C) Extract to a global utility (e.g. `frontend/src/utils/text.ts`).

**Chosen approach:** (B) — co-locate with the Journal page module, but as its own file (`journalPreview.ts`) exporting `truncateContent` and `MAX_PREVIEW_LENGTH`.

**Rationale:** (A) re-creates the exact divergence this refactor exists to eliminate; the brief calls that out as the root failure mode. (C) over-generalizes for a single domain concept and violates the "extract when repetition is real, not speculative" rule — only Journal needs this. (B) keeps cohesion with the feature (`JournalEntryModal`, `JournalList`, journal preview rendering all sit together) while serving the cross-module `JournalTab` consumer.

#### Decision 2: Highlight rendering scope

**Options considered:**
- (A) Drop highlighting outright — match today's actual rendered behavior, delete `HighlightedTerms` from the DTO, do not introduce `<mark>` / `<strong>` rendering.
- (B) Introduce client-side highlight rendering now, deriving terms from `searchTextFilter`.

**Chosen approach:** (A), unless the spec is explicitly amended to add a UI capability.

**Rationale:** Neither `JournalList.tsx` nor `JournalTab.tsx` currently renders term highlights. The backend was producing `HighlightedTerms` that nothing consumed visually. FR-6 reads as a like-for-like move but is in fact net-new behavior if implemented. The brief frames the change as cleanup of an SRP violation, not a feature addition. Adding `<mark>` styling now expands scope, drags in CSS / accessibility considerations, and contradicts CLAUDE.md's "surgical changes" rule. If the user wants highlighting, treat it as a follow-up.

#### Decision 3: Window-centering helper signature

**Options considered:**
- (A) Single function with options object: `truncateContent(content, { searchQuery?, maxLength? })`.
- (B) Two functions: `truncatePreview(content, max)` for browse, `truncateAroundMatch(content, query, max)` for search.

**Chosen approach:** (A).

**Rationale:** Matches the spec's API design section. One call-site at the row level keeps `JournalList`'s render branches symmetric. Internally the function delegates to a private head-truncate path when `searchQuery` is absent or unmatched, satisfying FR-5's fallback requirement without exposing two surfaces.

#### Decision 4: Word-length filter on term extraction

**Status:** Behavior change requiring explicit decision.

The current backend `ExtractHighlightTerms` filters `term.Length > 2`. The spec's FR-6 proposes `query.trim().split(/\s+/).filter(Boolean)` with no length filter. Per Decision 2, if highlighting is dropped, this is moot. If highlighting is kept, the client must mirror the `> 2` filter or the spec must amend to drop it explicitly.

## Implementation Guidance

### Directory / Module Structure

**New file:**
- `frontend/src/components/pages/Journal/journalPreview.ts` — exports `MAX_PREVIEW_LENGTH` constant and `truncateContent` function. ~40–60 lines. Pure, no React.

**Modified files:**

Backend:
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — delete `CreateContentPreview`, `ExtractHighlightTerms`, the `searchText`/`hasSearchText` locals (lines 34–35), and the lambda that assigns `ContentPreview` / `HighlightedTerms` (lines 41–45). Reduce to `.Select(JournalEntryMapper.ToSearchDto).ToList()`.
- `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs` — add `Content = entry.Content` to `ToSearchDto`. Remove the trailing comment about the handler overwriting.
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` — remove `ContentPreview` and `HighlightedTerms`; add `public string Content { get; set; } = null!;`.

Backend tests:
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — rewrite the three tests at lines 186, 234, 291, 345 to assert against `Content` (raw, untouched) and entry ordering/paging only.
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs:220` — rewrite to assert `ToSearchDto` populates `Content` from `entry.Content`.

Frontend:
- `frontend/src/components/pages/Journal/JournalList.tsx` — delete the inline `truncateContent` (lines 261–265). Replace the search branch (line 443–455) to pass `truncateContent(entry.content!, { searchQuery: searchTextFilter })`. Replace the browse branch (line 456–468) to pass `truncateContent(entry.content!)`. Both paths now share one call.
- `frontend/src/components/catalog/detail/tabs/JournalTab.tsx:107` — replace `{entry.contentPreview}` with `{truncateContent(entry.content!)}` (no `searchQuery` — JournalTab does not run a text search, only filters by `productCodePrefix`).

Frontend tests:
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — if any test mocks `contentPreview` or `highlightedTerms` (must verify after regenerated client compiles), migrate to `content`. Add unit tests covering `journalPreview.ts` directly (centering, no-match fallback, max length, ellipsis boundary cases).

Generated:
- `frontend/src/api/generated/api-client.ts` — regenerated by build; do not edit manually.

### Interfaces and Contracts

**Backend (`SearchJournalEntryDto`):**
```csharp
public class SearchJournalEntryDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = null!;
    public DateTime EntryDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public string CreatedByUserId { get; set; } = null!;
    public string? CreatedByUsername { get; set; }
    public string? ModifiedByUserId { get; set; }
    public string? ModifiedByUsername { get; set; }
    public List<string> AssociatedProducts { get; set; } = new();
    public List<JournalEntryTagDto> Tags { get; set; } = new();
    // ContentPreview and HighlightedTerms removed
}
```

**Frontend (`journalPreview.ts`):**
```ts
export const MAX_PREVIEW_LENGTH = 200;

export interface TruncateOptions {
  searchQuery?: string;
  maxLength?: number;
}

export function truncateContent(content: string, options?: TruncateOptions): string;
```

Behavior contract:
- Empty / null content → empty string.
- No `searchQuery` (or whitespace-only) → head-truncate to `maxLength`, append `"..."` when truncated.
- `searchQuery` present and matches in content (case-insensitive, first occurrence) → window of `maxLength` centered on the match; prepend `"..."` if window starts > 0; append `"..."` if window ends before content length.
- `searchQuery` present but no match in content → head-truncate fallback (same as no-query path). This handles matches that landed in the title or tags, not the body.

### Data Flow

**Search use case (with text query):**
1. `JournalList` calls `useSearchJournalEntries({ searchText, ... }, isSearchMode=true)`.
2. Backend handler runs repo query, maps each entry via `ToSearchDto` (now includes raw `Content`), returns `SearchJournalEntriesResponse`.
3. `JournalList` row render calls `truncateContent(entry.content, { searchQuery: searchTextFilter })`.
4. Preview renders identical 200-char window across browse and search.

**Browse use case:**
1. `JournalList` calls `useJournalEntries(...)` → returns `JournalEntryDto[]` (already exposes `Content`).
2. Row render calls `truncateContent(entry.content)` — no `searchQuery`, head-truncate at 200.

**Catalog product detail:**
1. `JournalTab` calls `useJournalEntriesByProduct(productCode)` → hits `journal_SearchJournalEntries` with `productCodePrefix`, **no** `searchText`.
2. Tab renders `truncateContent(entry.content)` — no text search, plain head-truncate at 200.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `JournalTab.tsx` (catalog detail) overlooked — TS compile fail or runtime undefined preview after client regen | High | Explicit acceptance criterion: `JournalTab.tsx:107` is migrated in the same PR. Add a smoke test or visual check on the product-detail Journal tab. |
| Highlighting "matches prior behavior" interpreted as new feature work | High | Decision 2 above: drop the field, do not introduce `<mark>` rendering, unless spec is amended. If amended, also re-introduce the `> 2` char filter or explicitly waive it. |
| Mapper test `JournalEntryMapperTests.cs:220` left asserting against removed fields → red CI | Medium | Add to FR-8 acceptance list: the mapper test must be rewritten alongside handler tests. |
| `JournalEntryMapper.ToSearchDto` still missing `Content`, handler relies on field that defaults to null | Medium | Add `Content = entry.Content` in `ToSearchDto`. Verify via unit test that round-trips raw content. |
| Payload size growth on busy search queries (100 long entries × full body) | Low | Confirmed via NFR-1 — current entries are short prose. Revisit only if real telemetry shows regression. No mitigation now. |
| Search match in title or tags (no body hit) renders awkward head-truncated preview | Low | Already covered by FR-5 fallback. Acceptable — matches current backend behavior. |
| Browse preview length silently changes from 150 → 200 chars | Low (intended) | This is the intentional convergence. Note in PR description; CLAUDE.md design system has no opinion on line-clamp width. |

## Specification Amendments

1. **FR-1 / FR-2 — extend to the mapper.** Add: `JournalEntryMapper.ToSearchDto` populates `Content = entry.Content` and removes the comment about the handler overwriting fields. The handler then reduces to `result.Items.Select(JournalEntryMapper.ToSearchDto).ToList()`.

2. **New FR — migrate the second consumer.** Add an acceptance criterion: `frontend/src/components/catalog/detail/tabs/JournalTab.tsx` uses the shared `truncateContent` helper on `entry.content` and no longer references `entry.contentPreview`. The spec must explicitly own this consumer; otherwise the catalog product-detail Journal tab breaks at TS compile.

3. **FR-6 — resolve the highlight ambiguity.** Choose explicitly:
   - **(Recommended) Drop highlighting** — delete FR-6 entirely; `HighlightedTerms` simply goes away. This matches today's actual rendered output (no highlighting exists).
   - **Or — introduce highlighting** — call out that this PR adds a new visible capability, specify the markup (e.g. `<mark>` or `<strong>`), the styling, whether to keep the `> 2` term-length filter from the old backend, and add render-assertion tests for the bolded substrings. Flip **Skip Design** to `false` if pursued.

4. **FR-7 — confirm "200" is intentional.** The spec already picks 200 over the frontend's prior 150. Worth stating in the PR description that browse previews grow from 150 → 200 chars, since this is a small but visible UI change.

5. **FR-8 — extend test scope.** Add `JournalEntryMapperTests.ToSearchDto_LeavesContentPreviewEmpty_AndHighlightedTermsEmpty` (line 220) to the list of tests that must be rewritten. Replace with assertion that `Content` is populated.

6. **FR-9 — extend to `JournalTab` tests.** No dedicated `JournalTab.tsx` test file exists today. Either add a minimal render test that verifies the helper is invoked, or document why we are not adding one (low blast radius, covered by E2E catalog-detail flows).

## Prerequisites

None blocking. All of the following are already in place:

- `JournalEntry.Content` exists on the domain entity and is already projected onto `JournalEntryDto.Content` — no DB schema or repository change needed.
- OpenAPI client regeneration is wired into the backend build (`docs/development/api-client-generation.md`); the TS client will reflect the DTO change automatically.
- No new package, no feature flag, no migration, no infra change.

Run order during implementation:
1. Backend DTO + mapper + handler + handler tests + mapper tests → `dotnet build` + `dotnet format` + `dotnet test` (Journal slice).
2. Frontend build regenerates the TS client → expect compile errors in `JournalList.tsx` and `JournalTab.tsx` referencing `contentPreview` / `highlightedTerms`. Those errors are the migration checklist.
3. Implement `journalPreview.ts`, wire into both consumers, update tests → `npm run build` + `npm run lint` + `npm test`.