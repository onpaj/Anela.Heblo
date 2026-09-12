# Design: Journal default sort key ("entryDate") triggers spurious warning

## Component Design

### `JournalRepository.ApplySort()` (`backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`)
Private helper that lowercases the incoming `sortBy` string and switches on it to build the `IQueryable<JournalEntry>` ordering. Add one new switch arm, matched before the `_` default arm:

- `"entrydate"` → delegates directly to `ApplyDefaultSort(query, ascending)` — no logging.

All other arms are unchanged:
- `"title"` → explicit `OrderBy`/`OrderByDescending` on `Title`.
- `"createdbyusername"` → explicit `OrderBy`/`OrderByDescending` on `CreatedByUsername` with a `ThenByDescending(EntryDate)` tiebreak.
- `_` (anything else) → `ApplyDefaultSortWithWarning(query, ascending, sortBy, logger)`, which logs the "Unknown sort key" warning and then falls back to `ApplyDefaultSort`.

`ApplyDefaultSort(query, ascending)` and `ApplyDefaultSortWithWarning(...)` keep their existing signatures and behavior; they are reused as-is, not modified. `ApplySort`'s own signature is unchanged, and its callers (`GetEntriesAsync`, `SearchEntriesAsync`) require no changes.

Net effect: for `sortBy = "entrydate"` (any casing, matching the default), the resulting `IQueryable` ordering is byte-for-byte identical to today's output — only the `LogWarning` call is skipped. Every other, genuinely unrecognized `sortBy` value still falls through to `_` and still logs the warning.

## Data Schemas

No data schema changes. No entity, DTO, database, or API request/response shape is touched — this is a control-flow-only change inside one private method. `IJournalRepository`'s public method signatures and the `SortBy` field on `GetJournalEntriesRequest` / `SearchJournalEntriesRequest` are unaffected.
