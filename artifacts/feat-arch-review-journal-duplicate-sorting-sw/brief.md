## Module
Journal

## Finding
The sorting switch statement is duplicated verbatim in two repository methods inside `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalRepository.cs`:

- `GetEntriesAsync` — lines 44–55
- `SearchEntriesAsync` — lines 135–146

Both blocks are character-for-character identical:

```csharp
query = sortBy?.ToLower() switch
{
    "title" => sortDirection == "ASC"
        ? query.OrderBy(x => x.Title)
        : query.OrderByDescending(x => x.Title),
    "createdat" => sortDirection == "ASC"
        ? query.OrderBy(x => x.CreatedAt)
        : query.OrderByDescending(x => x.CreatedAt),
    _ => sortDirection == "ASC"
        ? query.OrderBy(x => x.EntryDate)
        : query.OrderByDescending(x => x.EntryDate)
};
```

## Why it matters
KISS / DRY violation. Any future sort column addition or fix must be applied in two places; one will inevitably be missed. The duplication also obscures the relationship between the two methods.

## Suggested fix
Extract a private static method:

```csharp
private static IQueryable<JournalEntry> ApplySorting(
    IQueryable<JournalEntry> query, string? sortBy, string sortDirection) =>
    sortBy?.ToLower() switch
    {
        "title"     => sortDirection == "ASC" ? query.OrderBy(x => x.Title)     : query.OrderByDescending(x => x.Title),
        "createdat" => sortDirection == "ASC" ? query.OrderBy(x => x.CreatedAt) : query.OrderByDescending(x => x.CreatedAt),
        _           => sortDirection == "ASC" ? query.OrderBy(x => x.EntryDate) : query.OrderByDescending(x => x.EntryDate),
    };
```

Replace both duplicated blocks with a single call: `query = ApplySorting(query, sortBy, sortDirection);`

---
_Filed by daily arch-review routine on 2026-06-04._