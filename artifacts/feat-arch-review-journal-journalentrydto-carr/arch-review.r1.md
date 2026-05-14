I have enough context to produce the architecture review.

# Architecture Review: Split `JournalEntryDto` into list/detail and search variants

## Skip Design: true

## Architectural Fit Assessment

This is a pure contract-narrowing change confined to the Journal vertical slice and its corresponding frontend module. It is fully aligned with the project's stated architecture:

- **Vertical Slice respect**: all backend changes live in `backend/src/Anela.Heblo.Application/Features/Journal/{Contracts,Mapping,UseCases}/`. No cross-module impact.
- **Contracts ownership**: the new DTO belongs in `Features/Journal/Contracts/` — the same folder that already houses `JournalEntryDto`, `SearchJournalEntriesResponse`, and `JournalEntryTagDto`. This matches the rule "DTO objects for API live in `contracts/` of the specific module".
- **Project DTO rule (CRITICAL)**: the spec correctly mandates `class`, not `record`. The codebase confirms this — `JournalEntryDto`, `SearchJournalEntriesResponse`, `JournalEntryTagDto` are all classes with public setters.
- **Auto-generated client**: the OpenAPI/TS client regenerates on backend build, so frontend type churn is automatic — `JournalList.tsx` simply consumes whatever the new schema produces.

The spec's explicit rejection of inheritance (`SearchJournalEntryDto : JournalEntryDto`) is correct and aligns with how NSwag generates `allOf` schemas. The brief's original suggestion (inheritance) would have silently shipped `content` to the wire via the base type — defeating the bandwidth goal.

The only structural concern: the search handler currently mutates DTOs in-place (`dto.ContentPreview = …`). The new design must move preview computation to the construction site to keep the handler aligned with the project's general immutability preference (still fine to use object initializers — DTOs are mutable by necessity, but no post-construction reassignment is needed).

## Proposed Architecture

### Component Overview

```
backend/src/Anela.Heblo.Application/Features/Journal/
├── Contracts/
│   ├── JournalEntryDto.cs          (modified: drop ContentPreview, HighlightedTerms)
│   ├── SearchJournalEntryDto.cs    (NEW: flat DTO, no Content field)
│   ├── SearchJournalEntriesResponse.cs   (modified: Entries → List<SearchJournalEntryDto>)
│   └── …unchanged…
├── Mapping/
│   └── JournalEntryMapper.cs       (modified: add ToSearchDto, keep ToDto)
└── UseCases/
    └── SearchJournalEntries/
        └── SearchJournalEntriesHandler.cs   (modified: use ToSearchDto + compute preview from domain.Content)

frontend/src/
├── api/generated/api-client.ts     (regenerated)
└── components/pages/Journal/
    └── JournalList.tsx             (modified: branch on isSearchMode for type narrowing, not on a nullable field)
```

Read paths after the change:

```
GET /api/journal            ─► GetJournalEntriesHandler   ─► JournalEntryDto[]        (with full Content)
GET /api/journal/{id}       ─► GetJournalEntryHandler     ─► JournalEntryDto          (with full Content)
POST/GET search             ─► SearchJournalEntriesHandler─► SearchJournalEntryDto[]  (no Content, has ContentPreview)
```

### Key Design Decisions

#### Decision 1: Two flat DTOs, no inheritance
**Options considered:**
- (A) `SearchJournalEntryDto : JournalEntryDto` adding preview/terms (brief's suggestion).
- (B) Two flat DTOs, each owning exactly the fields it needs (spec's choice).
- (C) Single DTO with `JsonIgnore` conditional serialization on search-only fields.

**Chosen approach:** (B) — two flat DTOs.

**Rationale:** NSwag emits `allOf` for class inheritance, and the generated TypeScript client carries every inherited member. Option (A) would force `content?: string` onto `SearchJournalEntryDto` and keep the bandwidth waste invisible in the C# code while still shipping over the wire. Option (C) hides the contract in attributes and breaks OpenAPI schema cleanliness. Option (B) makes the contract explicit in both directions and is consistent with how the rest of the codebase models distinct read contracts.

#### Decision 2: Compute preview from the domain entity, not the DTO
**Options considered:**
- (A) Map full DTO first, then derive preview from `dto.Content`, then null-out `Content` (current shape).
- (B) Add `ToSearchDto(JournalEntry)` that omits `Content`; handler reads `entry.Content` directly for preview computation.

**Chosen approach:** (B).

**Rationale:** (A) requires a "set then unset" anti-pattern and still allocates the full string into a DTO field. (B) keeps the DTO immutable-after-construction in spirit, eliminates the wasted allocation, and makes the mapper's responsibility crisp: build the wire shape; the handler owns search-specific enrichment.

#### Decision 3: Empty-search fallback for `ContentPreview`
**Options considered:**
- (A) Keep `ContentPreview` nullable (no fallback when search text is empty).
- (B) Compute a truncated preview (≤200 chars) from `entry.Content` even when search text is empty (spec's choice).

**Chosen approach:** (B) — non-nullable, always populated.

**Rationale:** Non-nullable on the contract eliminates the only remaining reason for a frontend to defensively branch on field presence. The existing `CreateContentPreview` method already handles the empty-search case correctly (returns leading slice + ellipsis when truncation occurred). Reuse it.

#### Decision 4: Handler-level preview enrichment stays in the handler
**Options considered:**
- (A) Push `CreateContentPreview` / `ExtractHighlightTerms` into the mapper as overloads.
- (B) Keep them as `private static` helpers in `SearchJournalEntriesHandler` (current).

**Chosen approach:** (B).

**Rationale:** Preview generation is search-policy (depends on `request.SearchText`), not DTO-shape policy. Mapper should not take a search-text parameter. Keep the seam clean.

## Implementation Guidance

### Directory / Module Structure

**Create**
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntryDto.cs` — new class DTO per FR-1.

**Modify**
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs` — remove `ContentPreview` and `HighlightedTerms` properties (lines 22–24).
- `backend/src/Anela.Heblo.Application/Features/Journal/Contracts/SearchJournalEntriesResponse.cs` — change `Entries` type to `List<SearchJournalEntryDto>` (line 7).
- `backend/src/Anela.Heblo.Application/Features/Journal/Mapping/JournalEntryMapper.cs` — add `ToSearchDto(JournalEntry)`; reuse the same projections (`Title`, `EntryDate`, etc.); do **not** copy `Content`.
- `backend/src/Anela.Heblo.Application/Features/Journal/UseCases/SearchJournalEntries/SearchJournalEntriesHandler.cs` — replace `.Select(JournalEntryMapper.ToDto)` with a per-item projection that calls `ToSearchDto`, then sets `ContentPreview` and `HighlightedTerms` from `entry.Content` and `request.SearchText`. Always populate `ContentPreview` (use truncated fallback when `request.SearchText` is empty).
- `backend/test/Anela.Heblo.Tests/Features/Journal/JournalEntryMapperTests.cs` — drop the two tests asserting `ContentPreview.Should().BeNull()` and `HighlightedTerms.Should().NotBeNull().And.BeEmpty()` (lines 181–205); add equivalent coverage for `ToSearchDto` (no `Content` field even exists; preview/terms are defaults).
- `backend/test/Anela.Heblo.Tests/Features/Journal/SearchJournalEntriesHandlerTests.cs` — re-target assertions at `SearchJournalEntryDto`; cover the new empty-search fallback path.
- `frontend/src/components/pages/Journal/JournalList.tsx` — replace the `isSearchMode && entry.contentPreview` branch (line 336) with a typed split: either render two table-row components keyed on mode, or map `entries` through a discriminated local helper that returns `{ preview: string }` based on mode.

**Leave unchanged**
- `backend/src/Anela.Heblo.API/Controllers/JournalController.cs` — the controller forwards MediatR responses by type; no shape rewiring needed.
- `frontend/src/components/catalog/detail/tabs/JournalTab.tsx`, `JournalEntryForm.tsx`, `JournalEntryModal.tsx`, `CatalogDetail.tsx` — these consume `JournalEntryDto` for detail/list use only; removing the two unused fields cannot break them. Verify with `tsc --noEmit` post-regeneration.

### Interfaces and Contracts

```csharp
// New file: Features/Journal/Contracts/SearchJournalEntryDto.cs
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

// Mapper addition
internal static class JournalEntryMapper
{
    public static JournalEntryDto ToDto(JournalEntry entry) { /* unchanged */ }

    public static SearchJournalEntryDto ToSearchDto(JournalEntry entry)
    {
        return new SearchJournalEntryDto
        {
            Id = entry.Id,
            Title = entry.Title,
            EntryDate = entry.EntryDate,
            CreatedAt = entry.CreatedAt,
            ModifiedAt = entry.ModifiedAt,
            CreatedByUserId = entry.CreatedByUserId,
            CreatedByUsername = entry.CreatedByUsername,
            ModifiedByUserId = entry.ModifiedByUserId,
            ModifiedByUsername = entry.ModifiedByUsername,
            AssociatedProducts = entry.ProductAssociations
                .Select(pa => pa.ProductCodePrefix).Distinct().ToList(),
            Tags = entry.TagAssignments
                .Where(ta => ta.Tag != null)
                .Select(ta => new JournalEntryTagDto
                {
                    Id = ta.Tag.Id, Name = ta.Tag.Name, Color = ta.Tag.Color
                }).ToList()
            // ContentPreview and HighlightedTerms left at defaults — handler populates them
        };
    }
}
```

`SearchJournalEntriesResponse.Entries: List<SearchJournalEntryDto>` is the only response-type change.

### Data Flow

**Search path (post-change):**
1. Controller receives request → MediatR dispatches → `SearchJournalEntriesHandler.Handle`.
2. Handler builds `JournalSearchCriteria` (unchanged), calls `_journalRepository.SearchEntriesAsync` (unchanged), receives `result.Items: IEnumerable<JournalEntry>`.
3. Handler projects each domain entry: `var dto = JournalEntryMapper.ToSearchDto(entry); dto.ContentPreview = CreateContentPreview(entry.Content, request.SearchText); dto.HighlightedTerms = string.IsNullOrEmpty(request.SearchText) ? new() : ExtractHighlightTerms(request.SearchText);`.
4. Handler returns `SearchJournalEntriesResponse { Entries = […] , … }`. Full `Content` never leaves the domain.

**List/detail paths:** unchanged. Both still use `ToDto` and ship full `Content`.

**Frontend:**
- `useJournalEntries` → `GetJournalEntriesResponse.entries: JournalEntryDto[]` (full `content`).
- `useSearchJournalEntries` → `SearchJournalEntriesResponse.entries: SearchJournalEntryDto[]` (no `content`, required `contentPreview`).
- `JournalList` renders the active dataset by mode; the previous `entry.contentPreview ?? truncateContent(entry.content!, 150)` runtime branch is replaced by a compile-time type split.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `JournalList.tsx` is currently typed `entries.map((entry: JournalEntryDto) …)` (line 313). After the change, `entries` is `JournalEntryDto[] \| SearchJournalEntryDto[]` — a union that doesn't narrow naturally inside `.map`. | Medium | Split the row-render JSX into two `.map` branches under `isSearchMode`, or extract a `<JournalRow>` / `<SearchRow>` component pair. Do not use `as` casts — they will mask future drift. |
| Generated TS client may not pick up new types until a fresh build of the backend OpenAPI document is produced. | Low | Per `docs/development/api-client-generation.md`, the TS client regenerates on build. Run `dotnet build` before `npm run build`. Verify `api-client.ts` contains `SearchJournalEntryDto` after regeneration. |
| Existing snapshot/contract tests for the OpenAPI document (if any) will fail. | Low | FR-6 already calls this out; regenerate snapshots as part of the change. |
| `JournalTab` catalog widget consumes `JournalEntryDto` and could silently rely on the now-removed search-only fields. | Low | Verified via grep — `JournalTab.tsx` only imports the type; no field access on `contentPreview`/`highlightedTerms`. `tsc --noEmit` is the final guard. |
| Handler still mutates DTOs in-place when assigning `ContentPreview`/`HighlightedTerms`. | Low | Acceptable: DTOs in this codebase are mutable by convention (public setters everywhere). The mutation happens in the same expression that constructs the DTO, so no shared-state concern. Could be tightened later via an init-only constructor if the broader codebase migrates. |
| The 200-char preview window can land mid-multibyte for non-ASCII content (existing latent bug, not introduced here). | Low | Out of scope per spec. Note for future cleanup. |

## Specification Amendments

1. **FR-3, empty-search fallback wording**: the spec already commits to "non-null truncated preview" when `request.SearchText` is empty. Add explicit guidance that `HighlightedTerms` MUST remain an empty list when search text is empty (not derived from anywhere). The current `if (!string.IsNullOrEmpty(request.SearchText))` block conflates both fields; after the change, only `ContentPreview` is always populated.

2. **FR-4, mapper signature clarification**: the spec says "leaves [`ContentPreview` and `HighlightedTerms`] at their defaults". `ContentPreview` is declared non-nullable (`= null!;`), so "default" here means the sentinel `null` from `null!`. The handler MUST overwrite it before returning; otherwise the API ships a null in a non-nullable field. Recommend either:
   - Construct the DTO with `ContentPreview = ""` in the mapper, then overwrite (defensive), OR
   - Move preview computation into a handler-level factory method that returns a fully populated DTO and never exposes a half-built instance.

   The second is cleaner. Either is acceptable.

3. **FR-6, additional test**: add a contract test that loads the generated OpenAPI document and asserts `JournalEntryDto.properties` does not contain `contentPreview` or `highlightedTerms`, and that `SearchJournalEntryDto.required` contains `contentPreview`. If no OpenAPI contract test exists today, a single xUnit test that parses `swagger.json` from the running test host (`WebApplicationFactory`) is sufficient.

4. **FR-5, frontend pattern**: prescribe the split-rendering approach (two `.map` branches or a typed row component) rather than leaving it to interpretation. Casts (`as SearchJournalEntryDto`) inside the map should be explicitly disallowed.

## Prerequisites

None. This change requires:
- No database migrations (domain model untouched).
- No infrastructure changes.
- No configuration changes.
- No new packages.
- No coordinated external-consumer rollout — the project ships a single Docker image with the auto-generated TS client; backend and frontend deploy together (spec NFR-2 confirms).

Build sequence to follow: `dotnet build` (regenerates OpenAPI) → `npm run build` (regenerates TS client and compiles UI) → run backend + frontend tests. This is the standard workflow per `docs/development/api-client-generation.md`.