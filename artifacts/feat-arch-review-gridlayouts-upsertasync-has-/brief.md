## Module
GridLayouts

## Finding
`GridLayoutRepository.UpsertAsync` uses a select-then-insert/update pattern:

```csharp
// backend/src/Anela.Heblo.Persistence/GridLayouts/GridLayoutRepository.cs:40-59
var existing = await _context.GridLayouts
    .FirstOrDefaultAsync(x => x.UserId == userId && x.GridKey == gridKey, cancellationToken);

if (existing is not null)
{
    existing.LayoutJson = layoutJson;
    existing.LastModified = _timeProvider.GetUtcNow().DateTime;
}
else
{
    _context.GridLayouts.Add(new GridLayout { ... });
}
await _context.SaveChangesAsync(cancellationToken);
```

Under concurrent saves from the same user on the same grid (e.g. rapid column resize events where the debounce fires twice before the first completes), two requests can both read `null` (no existing row), both attempt to insert, and the second insert hits the unique index on `(UserId, GridKey)`. The PostgreSQL exception is caught by `PostgresExceptionTranslator.TryTranslateGridLayout`, rethrown as `GridLayoutPersistenceException`, logged as `LogError`, and returned to the caller as `ErrorCodes.DatabaseError` — meaning the user's layout silently fails to save.

`DeleteAsync` (lines 72-93) has the same read-then-delete structure, though the race there is benign (deleting something already deleted is a no-op).

## Why it matters
The unique index exists precisely to prevent duplicates, but the application doesn't exploit it — it treats the constraint violation as an error instead of a successful idempotent upsert. The user loses their layout change, and an error is logged for what is essentially a normal concurrent-access scenario.

## Suggested fix
Replace the EF Core select-then-write with a raw SQL PostgreSQL upsert, which is atomic and handles the race at the database level:

```csharp
await _context.Database.ExecuteSqlRawAsync("""
    INSERT INTO public."GridLayouts" ("UserId", "GridKey", "LayoutJson", "LastModified")
    VALUES ({0}, {1}, {2}, {3})
    ON CONFLICT ("UserId", "GridKey") DO UPDATE
        SET "LayoutJson" = EXCLUDED."LayoutJson",
            "LastModified" = EXCLUDED."LastModified"
    """, userId, gridKey, layoutJson, now, cancellationToken);
```

This eliminates the round-trip and makes concurrent saves idempotent (last write wins), which matches the intended semantics.

---
_Filed by daily arch-review routine on 2026-06-07._