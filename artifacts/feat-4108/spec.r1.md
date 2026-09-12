# Specification: Journal default sort key ("entryDate") triggers spurious "Unknown sort key" warning

## Summary
`JournalRepository.ApplySort()` logs an "Unknown sort key" warning on every default-sorted Journal request, because its switch statement has explicit arms for `"title"` and `"createdbyusername"` but not for `"entrydate"` — the lowercased form of the default `SortBy` value (`"EntryDate"`). The fix adds an explicit `"entrydate"` arm that delegates to the existing `ApplyDefaultSort` helper without logging. No behavioral change to sort order or results — this is a log-noise fix only.

## Background
`GetJournalEntriesRequest.SortBy` and `SearchJournalEntriesRequest.SortBy` both default to `"EntryDate"`. Both `GetEntriesAsync` and `SearchEntriesAsync` pass `sortBy` straight into `ApplySort()`, which lowercases it and switches on the result. The switch's default arm (`_`) calls `ApplyDefaultSortWithWarning`, which logs a warning ("Unknown sort key {SortBy} requested on {Repository}") before falling back to `ApplyDefaultSort`. Because no explicit `"entrydate"` arm exists, every default page load — i.e. every Journal request that doesn't override `sortBy` — falls into this default arm and logs a warning, even though the resulting sort (by `EntryDate`) is exactly what was intended.

This is the same class of issue previously fixed for `"createdByUsername"` in #2502, where an explicit `"createdbyusername"` arm was added. That fix was not extended to cover `"entrydate"` at the time, even though `"entrydate"` is the far more common case since it's the default for both request types. The result is that an "Unknown sort key" warning — meant to flag genuinely invalid/unsupported sort keys from the frontend or API callers — fires constantly for correct, expected traffic, degrading its usefulness as a monitoring/alerting signal (log noise, false-positive alerts in Application Insights).

This was surfaced by the automated arch-review routine on 2026-09-08 (see `artifacts/feat-4108/brief.md`).

## Functional Requirements

### FR-1: Add explicit "entrydate" sort-key arm
`ApplySort()` in `backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs` must handle the lowercased sort key `"entrydate"` as an explicit switch arm, delegating to `ApplyDefaultSort(query, ascending)` — the same behavior currently produced via the warning fallback path, minus the warning.

Current switch (lines 152–163):
```csharp
return sortBy.ToLowerInvariant() switch
{
    "title" => ascending
        ? query.OrderBy(x => x.Title)
        : query.OrderByDescending(x => x.Title),

    "createdbyusername" => ascending
        ? query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)
        : query.OrderByDescending(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate),

    _ => ApplyDefaultSortWithWarning(query, ascending, sortBy, logger),
};
```

Required change: add an `"entrydate"` arm before the default (`_`) arm, calling `ApplyDefaultSort(query, ascending)` directly (no warning):
```csharp
return sortBy.ToLowerInvariant() switch
{
    "entrydate" => ApplyDefaultSort(query, ascending),

    "title" => ascending
        ? query.OrderBy(x => x.Title)
        : query.OrderByDescending(x => x.Title),

    "createdbyusername" => ascending
        ? query.OrderBy(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate)
        : query.OrderByDescending(x => x.CreatedByUsername).ThenByDescending(x => x.EntryDate),

    _ => ApplyDefaultSortWithWarning(query, ascending, sortBy, logger),
};
```

**Acceptance criteria:**
- A request with `sortBy = "EntryDate"` (any casing, matching the default) produces the same `IQueryable` ordering as before (ascending: `OrderBy(x => x.EntryDate)`; descending: `OrderByDescending(x => x.EntryDate)`).
- A request with `sortBy = "EntryDate"` does **not** call `ILogger.LogWarning` (verifiable via a mocked/substituted `ILogger<JournalRepository>` asserting no warning-level log entry is written).
- `sortBy = "title"` and `sortBy = "createdByUsername"` (any casing) continue to behave exactly as before — no regression to those two existing arms.
- An actually-unknown sort key (e.g. `sortBy = "bogus"`) still falls through to `ApplyDefaultSortWithWarning` and still logs the "Unknown sort key" warning — the warning path itself is not removed, only no longer spuriously triggered by the default key.
- `sortBy = null` / empty / whitespace continues to short-circuit to `ApplyDefaultSort` before the switch is even reached (line 147–150 is unchanged) and does not log a warning (already correct today; no regression).

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact. The change adds one `switch` pattern-match arm; there is no additional query execution, allocation, or I/O. Existing query shape (`OrderBy`/`OrderByDescending` translated to SQL `ORDER BY`) is unchanged for the `entrydate` case.

### NFR-2: Security
None. This is a pure logging/control-flow fix in an internal repository method; no auth, input validation, or data-exposure surface changes.

## Data Model
No data model changes. No entity, DTO, or persistence schema is touched.

## API / Interface Design
No public API, endpoint, or contract changes. `GetJournalEntriesRequest.SortBy` and `SearchJournalEntriesRequest.SortBy` (both defaulting to `"EntryDate"`) are unchanged. `IJournalRepository`'s public method signatures (`GetEntriesAsync`, `SearchEntriesAsync`) are unchanged. The only change is internal to the private `ApplySort()` helper's switch statement in `JournalRepository`.

## Dependencies
None beyond the existing `Microsoft.Extensions.Logging` (`ILogger<JournalRepository>`) already used by the class. No new packages, services, or feature flags.

## Out of Scope
- Any broader refactor of `ApplySort()`, `ApplyDefaultSort()`, or `ApplyDefaultSortWithWarning()` beyond adding the single `"entrydate"` arm.
- Changing the default value of `SortBy` on `GetJournalEntriesRequest` / `SearchJournalEntriesRequest`.
- Adding new sortable fields or sort keys not already supported (`title`, `createdByUsername`, `entryDate`).
- Reworking the "unknown sort key" warning mechanism itself (e.g. making it configurable, changing log level, or validating `sortBy` at the API/request layer) — the warning path remains for genuinely unrecognized keys.
- Auditing other repositories for the same pattern (e.g. `IssuedInvoiceRepository`, which also contains an `ApplySort`-style method per the grep above) — out of scope for this fix; may warrant a separate arch-review finding if the same bug exists there.
- Adding a unit test file if none currently exists for `JournalRepository` — the acceptance criteria above describe testable behavior, but whether a new test file is created or the fix is verified via existing/integration coverage is left to the implementer, consistent with "surgical, one-switch-arm" scope.

## Open Questions
None.

## Status: COMPLETE
