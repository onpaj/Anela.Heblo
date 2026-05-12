## Module
Journal

## Finding
The full `JournalEntry` → `JournalEntryDto` LINQ projection is copy-pasted across three handlers:

- `GetJournalEntriesHandler.cs` lines 30–54
- `SearchJournalEntriesHandler.cs` lines 37–61
- `GetJournalEntryHandler.cs` lines 32–58

All three project `ProductAssociations` and `TagAssignments` with nearly identical code (~22 lines each). There is a behavioral divergence on the tag mapping: `GetJournalEntryHandler` (line 49) adds `.Where(ta => ta.Tag != null)` before accessing `ta.Tag.Id/Name/Color`, but the other two handlers do not. If an orphaned `JournalEntryTagAssignment` row exists (e.g. a tag was deleted without cascading), `GetJournalEntriesHandler` and `SearchJournalEntriesHandler` will throw a `NullReferenceException` at runtime while `GetJournalEntryHandler` handles it silently.

## Why it matters
- **Duplication**: any change to the DTO shape (adding a field, changing projection logic) must be made in three places independently — a classic SRP violation.
- **Latent bug**: the missing null guard in two of the three handlers is a runtime crash waiting for data inconsistency to trigger it. The inconsistency across sibling handlers also makes it hard to trust which behavior is intentional.

## Suggested fix
Extract a private static (or internal) mapper method, e.g.:

```csharp
// In a shared JournalEntryMapper static class or on JournalEntryDto itself
internal static JournalEntryDto FromEntity(JournalEntry entry) => new()
{
    Id = entry.Id,
    Title = entry.Title,
    Content = entry.Content,
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
        .Where(ta => ta.Tag != null)   // consistent null guard
        .Select(ta => new JournalEntryTagDto { Id = ta.Tag.Id, Name = ta.Tag.Name, Color = ta.Tag.Color })
        .ToList()
};
```

All three handlers call `JournalEntryMapper.FromEntity(entry)`.

---
_Filed by daily arch-review routine on 2026-05-12._