# Code Review: fix-sync-service-map-methods

## Summary
All four `Map()` methods have been correctly migrated from `.ToUniversalTime()` to `TimeZoneInfo.ConvertTimeToUtc(..., TimeZoneInfo.Local)` and changed from `private static` to `internal static`. The developer correctly identified that Ledger and Contact DTOs carry `DateTimeOffset?` (not `DateTime?`) and adapted the fix accordingly. The Department and AccountingTemplate pattern using `DateTime.SpecifyKind(..., Unspecified)` before `ConvertTimeToUtc` is sound and matches the existing `UnspecifiedDateTimeConverter` convention in the codebase.

## Review Result: PASS

### task: fix-sync-service-map-methods
**Status:** PASS

## Overall Notes

- No remaining `.ToUniversalTime()` calls exist in any of the four files. The one hit from a grep of the Analytics folder is a comment in `FlexiAnalyticsSyncOptions.cs` (not one of the four changed files, and not a live call).
- **LedgerSyncService / ContactSyncService**: `.Value.DateTime` on a `DateTimeOffset?` correctly extracts the raw `DateTime` ticks with `Kind=Unspecified`, which is exactly what `ConvertTimeToUtc` expects when the timezone argument is `TimeZoneInfo.Local`. No data loss — `DateTimeOffset.DateTime` preserves the wall-clock value without shifting.
- **DepartmentSyncService / AccountingTemplateSyncService**: `DateTime.SpecifyKind(dto.LastUpdate, DateTimeKind.Unspecified)` before `ConvertTimeToUtc` is the correct guard. `TimeZoneInfo.ConvertTimeToUtc` throws `ArgumentException` when passed a `DateTime` with `Kind=Utc`; forcing `Unspecified` avoids that while being semantically correct given the SDK convention. The `== default` null guard for the non-nullable field is also correct.
- The `(DateTimeOffset?)` cast on the result of `ConvertTimeToUtc` is valid — `ConvertTimeToUtc` returns `DateTime` with `Kind=Utc`, and the implicit conversion to `DateTimeOffset?` is well-defined.
- All four `Map()` methods are now `internal static`, consistent with the stated goal and compatible with the existing `InternalsVisibleTo` assembly attribute for the test project.
- Code comments in all four files accurately explain the rationale (`Kind=Unspecified`, `UnspecifiedDateTimeConverter` pattern), which is useful context for future maintainers.
- One minor observation: the `(DateTimeOffset?)` cast appears on both the Ledger/Contact path (as explicit cast wrapping the whole expression) and the Department/AccountingTemplate path (as inline cast). Both are functionally equivalent; no action needed.
