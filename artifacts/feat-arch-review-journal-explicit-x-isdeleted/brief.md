## Module
Journal

## Finding
`JournalEntryConfiguration` at `backend/src/Anela.Heblo.Persistence/Catalog/Journal/JournalEntryConfiguration.cs:53` sets a global EF Core query filter:

```csharp
builder.HasQueryFilter(x => !x.IsDeleted);
```

Despite this, every query in `JournalRepository.cs` also adds an explicit `!x.IsDeleted` predicate:

- `GetByIdAsync` (line 26): `FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ...)`
- `GetEntriesAsync` (line 41): `.Where(x => !x.IsDeleted)`
- `SearchEntriesAsync` (line 87): `.Where(x => !x.IsDeleted)`
- `GetEntriesByProductAsync` (line 172): `.Where(x => !x.IsDeleted && ...)`
- `GetJournalIndicatorsAsync` (line 188): `Context.Set<JournalEntry>().Where(je => !je.IsDeleted)`

The global filter makes all five explicit predicates redundant — EF Core already appends the soft-delete condition to every query against `JournalEntry`.

## Why it matters
1. **Readability**: readers cannot tell whether the global filter is intentional protection or whether the explicit checks are the real guard — the code implies both are needed.
2. **Maintenance trap**: if anyone needs to call `IgnoreQueryFilters()` for a legitimate admin scenario, they'll encounter the explicit guards unexpectedly and have to clean them up separately.
3. **Silent inconsistency**: `GetJournalIndicatorsAsync` wraps the entry set in a LINQ join — the global filter still applies there, so the extra `.Where(je => !je.IsDeleted)` is doubly redundant and can mislead future developers working on query-shape changes.

## Suggested fix
Remove the five redundant `.Where(x => !x.IsDeleted)` conditions from `JournalRepository.cs`. The global query filter in `JournalEntryConfiguration` is sufficient and already enforced. If an admin bypass is ever needed, it will be done intentionally with `IgnoreQueryFilters()`.

---
_Filed by daily arch-review routine on 2026-06-04._