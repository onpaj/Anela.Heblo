## Module
Journal

## Finding
`DeleteJournalEntryHandler.Handle` calls `GetByIdAsync` to check existence (line 39), then immediately calls `DeleteSoftAsync` (line 51), which calls `GetByIdAsync` a second time internally:

```csharp
// DeleteJournalEntryHandler.cs:39
var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
// ...
// DeleteJournalEntryHandler.cs:51
await _journalRepository.DeleteSoftAsync(request.Id, currentUser.Id, currentUser.Name, cancellationToken);

// JournalRepository.cs:27–35  (DeleteSoftAsync body)
public async Task DeleteSoftAsync(int id, ...)
{
    var entry = await GetByIdAsync(id, cancellationToken);   // ← second fetch
    if (entry != null)
    {
        entry.SoftDelete(userId, username);
        await UpdateAsync(entry, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }
}
```

The `GetByIdAsync` override (lines 18–25) performs two `Include` joins (`ProductAssociations`, `TagAssignments.Tag`), so each call executes a multi-join SQL query. The not-found result in `DeleteSoftAsync` is silently swallowed, meaning the second fetch's null check is also redundant — the handler already returned early on null.

## Why it matters
Every delete operation makes two database round-trips where one is sufficient, each loading full eager-loaded graphs. For small data this is a latency smell; it also makes the handler harder to reason about because the `entry` variable fetched on line 39 is not the entity that gets modified.

## Suggested fix
Have the handler call `SoftDelete` directly on the fetched entity and delegate persistence to the repository, bypassing the ID-based `DeleteSoftAsync`:

```csharp
var entry = await _journalRepository.GetByIdAsync(request.Id, cancellationToken);
if (entry == null) return new DeleteJournalEntryResponse(ErrorCodes.JournalEntryNotFound, ...);

entry.SoftDelete(currentUser.Id, currentUser.Name);
await _journalRepository.UpdateAsync(entry, cancellationToken);
await _journalRepository.SaveChangesAsync(cancellationToken);
```

Alternatively, add an overload of `DeleteSoftAsync` that accepts a `JournalEntry` directly.

---
_Filed by daily arch-review routine on 2026-05-12._