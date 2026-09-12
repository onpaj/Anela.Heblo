## Module
Journal

## Finding
`JournalRepository.ApplySort()` (`backend/src/Anela.Heblo.Persistence/Journal/JournalRepository.cs`, lines 152–163) lowercases the `sortBy` parameter and switches on it, but the switch only has explicit arms for `"title"` and `"createdbyusername"`. The primary sort key — `"entryDate"` (the default value set in `GetJournalEntriesRequest.SortBy` at line 12 and `SearchJournalEntriesRequest.SortBy` at line 25) — lowercases to `"entrydate"`, which falls through to `ApplyDefaultSortWithWarning`. That helper logs:

```
Unknown sort key entryDate requested on JournalRepository
```

on **every single** default `GET /api/journal` or `GET /api/journal/search` request. The result is still correct (default sort is by EntryDate), but the log entry is wrong and pollutes monitoring dashboards with false positives.

A similar issue for `"createdByUsername"` was filed as #2502 and fixed by adding the `"createdbyusername"` arm; `"entrydate"` was not added at the same time.

## Why it matters
Every default page load emits an "Unknown sort key" warning, making the warning meaningless for actual unknown keys. It degrades the signal-to-noise ratio in log monitoring and Application Insights alerts.

## Suggested fix
Add `"entrydate"` as an explicit arm in the switch that delegates to `ApplyDefaultSort` without logging a warning:

```csharp
"entrydate" => ApplyDefaultSort(query, ascending),
"title" => ascending ? ... : ...,
"createdbyusername" => ascending ? ... : ...,
_ => ApplyDefaultSortWithWarning(query, ascending, sortBy, logger),
```

No other change needed.

---
_Filed by daily arch-review routine on 2026-09-08._
