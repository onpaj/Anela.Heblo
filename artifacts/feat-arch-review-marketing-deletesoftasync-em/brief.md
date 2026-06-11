## Module
Marketing

## Finding

`IMarketingActionRepository.DeleteSoftAsync` commits to the database internally, which is inconsistent with every other repository operation in this module (and the project). This also forces `DeleteMarketingActionHandler` to load the entity twice.

**Repository implementation** (`backend/src/Anela.Heblo.Persistence/Marketing/MarketingActionRepository.cs`, lines 27–35):

```csharp
public async Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default)
{
    var entity = await GetByIdAsync(id, cancellationToken);   // DB load #2 (see below)
    if (entity != null)
    {
        entity.SoftDelete(userId, username);
        await UpdateAsync(entity, cancellationToken);
        await SaveChangesAsync(cancellationToken);             // ← save embedded here
    }
}
```

**Handler pattern for all other operations** — callers control the save:
```csharp
// CreateMarketingActionHandler (lines 83–86)
await _repository.AddAsync(action, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken);

// UpdateMarketingActionHandler (lines 115–116)
await _repository.UpdateAsync(action, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken);
```

**Double DB load in `DeleteMarketingActionHandler`** (`backend/src/Anela.Heblo.Application/Features/Marketing/UseCases/DeleteMarketingAction/DeleteMarketingActionHandler.cs`):
- Line 47: `await _repository.GetByIdAsync(request.Id, ...)` — loads entity to read `action.OutlookEventId`
- Line 77: `await _repository.DeleteSoftAsync(request.Id, ...)` — calls `GetByIdAsync` again internally

The entity is therefore fetched from the database twice per delete request.

## Why it matters

- **Hidden side effect**: the interface signature (`DeleteSoftAsync`) gives no indication it commits to the DB. A caller wrapping multiple operations in a logical unit of work cannot include a soft-delete without an unexpected mid-unit commit.
- **Inconsistency**: three other handler patterns in the same module all follow caller-controlled `SaveChangesAsync`. The delete is the odd one out, which creates a maintenance trap.
- **Wasted I/O**: the double load is unnecessary. The handler already has the entity in memory when it calls `DeleteSoftAsync`.

## Suggested fix

Remove `SaveChangesAsync` from `DeleteSoftAsync` (or remove `DeleteSoftAsync` entirely from the interface and let the handler own the full sequence):

```csharp
// Option A — keep DeleteSoftAsync but remove the embedded save
public async Task DeleteSoftAsync(int id, string userId, string username, CancellationToken ct = default)
{
    var entity = await GetByIdAsync(id, ct);
    if (entity == null) return;
    entity.SoftDelete(userId, username, utcNow: DateTime.UtcNow);
    await UpdateAsync(entity, ct);
    // caller is responsible for SaveChangesAsync
}
```

Or (preferred, avoids double load):

```csharp
// Option B — handler owns full lifecycle, no DeleteSoftAsync needed
// In DeleteMarketingActionHandler, after Outlook delete:
action.SoftDelete(currentUser.Id, currentUser.Name ?? "Unknown User", now);
await _repository.UpdateAsync(action, cancellationToken);
await _repository.SaveChangesAsync(cancellationToken);
```

Option B also eliminates `IMarketingActionRepository.DeleteSoftAsync` from the domain interface, keeping the repository interface lean.

---
_Filed by daily arch-review routine on 2026-06-07._