# Specification: Remove Redundant Soft-Delete Predicates in `JournalRepository`

## Summary
The `JournalEntry` entity already has a global EF Core query filter (`HasQueryFilter(x => !x.IsDeleted)`) defined in `JournalEntryConfiguration.cs:53`, yet `JournalRepository.cs` adds an explicit `!x.IsDeleted` predicate to all five of its query methods. This specification covers removing those redundant predicates so the global filter is the single source of truth for soft-delete enforcement.

## Background
Soft delete on `JournalEntry` is enforced by `HasQueryFilter(x => !x.IsDeleted)` in `JournalEntryConfiguration.cs:53`. EF Core automatically appends this predicate to every LINQ query targeting `JournalEntry` (and to any join that pulls from `DbSet<JournalEntry>`). The five explicit `!x.IsDeleted` checks in `JournalRepository.cs` therefore produce duplicated SQL predicates and create three concrete problems flagged by the daily architecture review on 2026-06-04:

1. **Readability** — readers cannot tell whether the global filter is the actual guard or whether the explicit checks are required. Defense-in-depth implications are misleading.
2. **Maintenance trap** — a future admin scenario that needs `IgnoreQueryFilters()` would be silently broken by the leftover explicit guards, forcing a second cleanup pass.
3. **Silent inconsistency** — `GetJournalIndicatorsAsync` joins `JournalEntryProduct` with `Context.Set<JournalEntry>().Where(je => !je.IsDeleted)`. The global filter already applies to the inner `DbSet<JournalEntry>`, so the predicate is doubly redundant and obscures the actual query shape.

The fix is a small, behavior-preserving refactor. No schema changes, no API changes, no new behavior.

## Functional Requirements

### FR-1: Remove redundant predicate in `GetByIdAsync`
Drop `&& !x.IsDeleted` from the `FirstOrDefaultAsync` predicate at `JournalRepository.cs:26`. The final predicate must be `x.Id == id`.

**Acceptance criteria:**
- `JournalRepository.cs:26` reads `.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);`.
- Existing repository unit and integration tests for `GetByIdAsync` continue to pass.
- A test that calls `GetByIdAsync` for a soft-deleted entry still returns `null` (proving the global filter is in effect).

### FR-2: Remove redundant predicate in `GetEntriesAsync`
Drop the `.Where(x => !x.IsDeleted)` clause at `JournalRepository.cs:40`.

**Acceptance criteria:**
- The query chain in `GetEntriesAsync` no longer contains `.Where(x => !x.IsDeleted)`.
- Pagination, sorting, and total counts return the same values as before for datasets containing both deleted and non-deleted entries.

### FR-3: Remove redundant predicate in `SearchEntriesAsync`
Drop the `.Where(x => !x.IsDeleted)` clause at `JournalRepository.cs:90`.

**Acceptance criteria:**
- The base query in `SearchEntriesAsync` starts without an explicit `IsDeleted` filter.
- All other filters (search text, date range, product code prefix, tag, user) continue to compose correctly on top of the global filter.
- Search results exclude soft-deleted entries.

### FR-4: Remove redundant predicate in `GetEntriesByProductAsync`
Rewrite the predicate at `JournalRepository.cs:172` to keep only the product-association check: `x.ProductAssociations.Any(pa => productCode.StartsWith(pa.ProductCodePrefix))`.

**Acceptance criteria:**
- The `.Where(...)` clause at `JournalRepository.cs:172` contains only the product-association predicate.
- Results for a given product code remain identical to current behavior on a mixed dataset (deleted + non-deleted entries).

### FR-5: Remove redundant predicate in `GetJournalIndicatorsAsync`
Replace `Context.Set<JournalEntry>().Where(je => !je.IsDeleted)` at `JournalRepository.cs:188` with `Context.Set<JournalEntry>()`. The global query filter on `JournalEntry` still applies to the join source.

**Acceptance criteria:**
- The join source is `Context.Set<JournalEntry>()` with no `.Where(je => !je.IsDeleted)`.
- Indicator aggregates (`DirectEntries`, `LastEntryDate`, `HasRecentEntries`) are unchanged for the same dataset.
- Soft-deleted journal entries are still excluded from indicator counts.

### FR-6: Behavior parity verification
The change must be observably non-functional. No callers should see a different result for any input.

**Acceptance criteria:**
- The full backend test suite passes (`dotnet test`).
- `dotnet build` and `dotnet format` complete without warnings or formatting changes beyond the edited lines.
- Generated SQL for each touched method still contains exactly one `IsDeleted` predicate (the one EF appends from the global filter). Spot-check by enabling EF Core query logging for one method in a test, or via reviewer inspection — not a required automated assertion.

## Non-Functional Requirements

### NFR-1: Performance
Removing duplicate predicates may marginally simplify generated SQL but is not expected to produce a measurable performance change. The existing index `IX_JournalEntries_IsDeleted_EntryDate` (`JournalEntryConfiguration.cs:62`) continues to serve the predicate appended by the global filter. No performance regression is acceptable; no specific improvement is required.

### NFR-2: Security
No security impact. Soft-delete enforcement is preserved through the global query filter. There is no path through the repository methods after the change that would expose soft-deleted entries to callers.

### NFR-3: Maintainability
After the change, any future need to query soft-deleted entries (e.g., admin/audit views) can be implemented via `IgnoreQueryFilters()` in a single place per query, without needing to also strip per-method predicates.

### NFR-4: Backward compatibility
Public method signatures on `IJournalRepository` are unchanged. The change is purely internal.

## Data Model
No schema changes. Relevant existing model elements:

- `JournalEntry` (entity) — has `IsDeleted` (bool), `DeletedAt` (nullable DateTime), `DeletedByUserId` (nullable string).
- `JournalEntryConfiguration` defines the global query filter `!x.IsDeleted` and a composite index `(IsDeleted, EntryDate)`.
- `JournalEntryProduct` — joined entity in `GetJournalIndicatorsAsync`; no `IsDeleted` of its own. Its parent `JournalEntry` is filtered by the global filter through the join.

## API / Interface Design
No public API or contract changes. No new endpoints, events, DTOs, or UI changes. All edits are confined to method bodies inside `JournalRepository.cs`.

## Dependencies
- No new libraries.
- Relies on existing EF Core global query filter behavior. EF Core applies global filters to:
  - Direct queries on `DbSet<JournalEntry>` (covers FR-1 through FR-4).
  - Sub-queries and joins that originate from `DbSet<JournalEntry>` (covers FR-5).
- No dependency on other in-flight features.

## Out of Scope
- Audit of other entities/repositories for the same redundancy pattern. (Mention in PR description if encountered, do not fix here.)
- Introducing an admin/audit query path that uses `IgnoreQueryFilters()`. Not needed by current product behavior.
- Adding new test coverage beyond ensuring existing tests still pass and the soft-delete behavior is exercised at least once per method. No coverage uplift targets.
- Refactoring sort logic, paging logic, search-term handling (e.g., culture-aware `ToLower()`), or include strategies. These are pre-existing and out of scope for this surgical cleanup.
- Migrating the `(IsDeleted, EntryDate)` index — keep as is; the global filter still uses `IsDeleted`.

## Open Questions
None.

## Status: COMPLETE