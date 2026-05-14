## Module
Journal

## Finding
`JournalEntryDto` is the single DTO type returned by all Journal read operations — list, detail, and search. It contains two fields that are only meaningful for search results:

```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/Contracts/JournalEntryDto.cs
// Lines 23–24
public string? ContentPreview { get; set; }     // only set in SearchJournalEntriesHandler
public List<string> HighlightedTerms { get; set; } = new();  // only set in SearchJournalEntriesHandler
```

These fields are populated exclusively inside `SearchJournalEntriesHandler` (lines 42–46). For every other use — `GetJournalEntriesHandler`, `GetJournalEntryHandler`, and the `JournalTab` catalog widget — they are `null`/empty but still serialised into every JSON response.

Additionally, `SearchJournalEntriesHandler` maps the full `Content` (up to 10 000 chars per entry) into the DTO via `JournalEntryMapper.ToDto`, then computes a 200-char `ContentPreview` on top of it (line 44). The full content travels across the wire for search results even though only the preview is displayed.

## Why it matters
- **SRP violation**: the DTO conflates the contract for three distinct operations (list, detail, search). Adding a search-only field silently changes the shape of list and detail responses too.
- **Bandwidth waste**: list responses for search include the full `Content` string and two unused fields for every row.
- **Hidden coupling**: `JournalList.tsx` already branches on `entry.contentPreview` (line 336) to decide which text to render, making the frontend aware of the server-side search mode through a nullable field rather than a proper type distinction.

## Suggested fix
Extract a `SearchJournalEntryDto` that adds `ContentPreview` and `HighlightedTerms` (and omits or truncates `Content`):

```csharp
public class SearchJournalEntryDto : JournalEntryDto
{
    public string ContentPreview { get; set; } = null!;
    public List<string> HighlightedTerms { get; set; } = new();
}
```

Use it in `SearchJournalEntriesResponse` and `SearchJournalEntriesHandler`. Remove the search-specific fields from the base `JournalEntryDto`. The mapper for search results can then omit the full `Content` field to avoid the bandwidth waste.

---
_Filed by daily arch-review routine on 2026-05-12._