# Design: JournalEntry.Create() Factory Method for Domain-Owned Normalization

This feature has no user-facing component — it is a domain-layer refactor invisible to the API contract, the frontend, and end users (per `arch-review.r1.md`, Skip Design: true). UX/UI sections are omitted per the designer's own format for backend-only features.

## Component Design

### `JournalEntry` (domain entity — modified)

**Responsibility addition:** owns construction-time normalization for new entries, mirroring the update-time normalization it already owns via `Update()`.

**New member:**

```csharp
public static JournalEntry Create(
    string title,
    string content,
    DateTime entryDate,
    string userId,
    string username,
    DateTime now)
{
    var entry = new JournalEntry
    {
        CreatedAt = now,
        ModifiedAt = now,
        CreatedByUserId = userId,
        CreatedByUsername = username
    };

    entry.Title = title.Trim();
    entry.Content = content.Trim();
    entry.EntryDate = entryDate.Date;

    return entry;
}
```

Placed in the `// Domain methods` region of `JournalEntry.cs`, directly above `Update()` — grouping all invariant-owning operations (`AssociateWithProduct`, `ReplaceProductAssociations`, `AssignTag`, `ReplaceTagAssignments`, `Create`, `Update`, `SoftDelete`) together, per `arch-review.r1.md`'s Implementation Guidance.

**Contract:**
- Pure, side-effect-free construction — no repository or I/O access, consistent with every other domain method on this entity.
- Fields not listed above (`Id`, `IsDeleted`, `DeletedAt`, `DeletedByUserId`, `DeletedByUsername`, `ModifiedByUserId`, `ModifiedByUsername`, `ProductAssociations`, `TagAssignments`) are left at their declared defaults — `Create()` does not assign them.
- Caller supplies `now` explicitly (not `DateTime.UtcNow` internally) so `CreatedAt` and `ModifiedAt` are guaranteed to be the identical instant — see `arch-review.r1.md`, Decision 2.

### `CreateJournalEntryHandler` (application handler — modified)

**Responsibility change:** no longer performs field-level normalization (`.Trim()`, `.Date`); delegates entirely to `JournalEntry.Create()`.

**Before:**
```csharp
var entry = new JournalEntry
{
    Title = request.Title.Trim(),
    Content = request.Content.Trim(),
    EntryDate = request.EntryDate.Date,
    CreatedAt = now,
    ModifiedAt = now,
    CreatedByUserId = userId,
    CreatedByUsername = currentUser.Name ?? "Unknown User"
};
```

**After:**
```csharp
var entry = JournalEntry.Create(
    request.Title,
    request.Content,
    request.EntryDate,
    userId,
    currentUser.Name ?? "Unknown User",
    now);
```

Everything else in `Handle()` (authentication check, blank-title validation, `now = DateTime.UtcNow` computation, product-association loop, tag-assignment loop, repository calls, logging, response construction) is unchanged.

## Data Schemas

No database schema change. `JournalEntry` maps to the same columns via the existing `JournalEntryConfiguration` (`backend/src/Anela.Heblo.Persistence/Journal/JournalEntryConfiguration.cs`), which configures properties, not construction paths — EF Core materializes rows via the parameterless constructor and property setters regardless of whether application code constructs new instances via `Create()` or an object initializer.

No API request/response shape change. `CreateJournalEntryRequest` / `CreateJournalEntryResponse` (`backend/src/Anela.Heblo.Application/Features/Journal/Contracts/CreateJournalEntryRequest.cs`) are byte-for-byte unchanged — this refactor is entirely internal to how the handler turns a validated request into a persisted entity.
