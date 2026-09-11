## Module
Journal

## Finding
`JournalEntry` has domain methods that encapsulate field normalization:
- `Update()` (`backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs`, line 153) trims `title` and `content` and applies `.Date` to `entryDate`.
- `SoftDelete()` (line 164) sets audit fields.

But the creation path in `CreateJournalEntryHandler` (`backend/src/Anela.Heblo.Application/Features/Journal/UseCases/CreateJournalEntry/CreateJournalEntryHandler.cs`, lines 49–58) constructs the entity by setting properties directly:

```csharp
var entry = new JournalEntry
{
    Title = request.Title.Trim(),   // handler applies trim
    Content = request.Content.Trim(), // handler applies trim
    EntryDate = request.EntryDate.Date, // handler applies .Date
    CreatedAt = now,
    ModifiedAt = now,
    CreatedByUserId = userId,
    CreatedByUsername = currentUser.Name ?? "Unknown User"
};
```

The trimming and `.Date` normalization are correct here — but only because the handler developer manually mirrored the same logic that `Update()` already encapsulates. There is no domain-owned factory method or constructor that enforces these invariants for new entries. Any future creation path (batch import, API v2, test factory) must independently remember to apply the same normalizations.

## Why it matters
The `Update()` domain method is the single source of truth for how `Title`, `Content`, and `EntryDate` are normalized. Creation bypasses this contract, meaning the invariant is enforced by convention rather than by the domain model. This is an Open/Closed violation: adding a new creation path requires modifying the caller rather than extending the domain.

## Suggested fix
Add a static factory method to `JournalEntry`:

```csharp
public static JournalEntry Create(
    string title, string content, DateTime entryDate,
    string userId, string username, DateTime now)
{
    var entry = new JournalEntry
    {
        CreatedAt = now,
        ModifiedAt = now,
        CreatedByUserId = userId,
        CreatedByUsername = username
    };
    // Reuse the same normalization as Update()
    entry.Title = title.Trim();
    entry.Content = content.Trim();
    entry.EntryDate = entryDate.Date;
    return entry;
}
```

`CreateJournalEntryHandler` then calls `JournalEntry.Create(...)` instead of constructing the object inline. The handler no longer needs to know about trimming rules; they live exclusively in the domain.

---
_Filed by daily arch-review routine on 2026-09-08._