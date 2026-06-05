## Module
Journal

## Finding
`JournalEntry` has a well-formed domain method for soft-delete that encapsulates audit-trail bookkeeping:

```csharp
// backend/src/Anela.Heblo.Domain/Features/Journal/JournalEntry.cs:153
public void SoftDelete(string userId, string username)
{
    IsDeleted = true;
    DeletedAt = DateTime.UtcNow;
    DeletedByUserId = userId;
    DeletedByUsername = username;
    ModifiedAt = DateTime.UtcNow;
    ModifiedByUserId = userId;
    ModifiedByUsername = username;
}
```

The `UpdateJournalEntryHandler` does not follow the same pattern. It mutates `ModifiedAt`, `ModifiedByUserId`, `ModifiedByUsername`, `Title`, `Content`, and `EntryDate` as direct property assignments from the application layer:

```csharp
// backend/src/Anela.Heblo.Application/Features/Journal/UseCases/UpdateJournalEntry/UpdateJournalEntryHandler.cs:51-59
var now = DateTime.UtcNow;
entry.Title = request.Title?.Trim();
entry.Content = request.Content.Trim();
entry.EntryDate = request.EntryDate.Date;
entry.ModifiedAt = now;
entry.ModifiedByUserId = currentUser.Id;
entry.ModifiedByUsername = currentUser.Name ?? "Unknown User";
```

## Why it matters
The entity owns the rule "who sets ModifiedAt and when" for deletion, but the handler owns that rule for updates. This split means:
- The audit trail logic (`ModifiedAt`, `ModifiedBy*`) for updates lives in the wrong layer.
- A future update rule (e.g. "only the original author may edit") or a new invariant (e.g. "content may not be blanked") would need to be added to the handler rather than the entity — the exact opposite of the pattern already established by `SoftDelete`.
- Content trimming (`request.Title?.Trim()`) is also applied in the handler instead of being a domain-level normalisation.

## Suggested fix
Add an `Update(string title, string content, DateTime entryDate, string userId, string username)` method to `JournalEntry` that mirrors `SoftDelete`:

```csharp
public void Update(string? title, string content, DateTime entryDate, string userId, string username)
{
    Title = title?.Trim();
    Content = content.Trim();
    EntryDate = entryDate.Date;
    ModifiedAt = DateTime.UtcNow;
    ModifiedByUserId = userId;
    ModifiedByUsername = username;
}
```

Replace the direct field assignments in `UpdateJournalEntryHandler` with a single call to `entry.Update(...)`. This makes the entity consistently responsible for its own audit trail and puts future invariants in the right place.

---
_Filed by daily arch-review routine on 2026-06-04._