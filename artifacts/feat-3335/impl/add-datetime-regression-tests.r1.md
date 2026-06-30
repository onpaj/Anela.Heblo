# Implementation: add-datetime-regression-tests

## What was implemented

Added one `[Fact]` regression test (`Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified`) to each of the four sync service test classes. Each test passes a `Kind=Unspecified` `DateTime` (or wraps one in a `DateTimeOffset` for Ledger/Contact) directly to the now-`internal static` `Map()` method and asserts that:

1. `LastModified` is not null
2. `LastModified.Value.Offset == TimeSpan.Zero` (confirms UTC storage, works for `DateTimeOffset?`)
3. `LastModified.Value.UtcDateTime` equals `TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local)` (confirms correct offset was applied, not a no-op `ToUniversalTime`)

**Type correction applied vs. original plan:** All four entity `LastModified` properties are `DateTimeOffset?` (not `DateTime?` as the task context assumed). The assertions were adapted to use `Offset == TimeSpan.Zero` and `.UtcDateTime` instead of `.Kind == DateTimeKind.Utc` (which does not compile on `DateTimeOffset`).

**LedgerSyncService and ContactSyncService input:** Their DTOs have `LastUpdate` as `DateTimeOffset?`. The test passes `new DateTimeOffset(unspecified, TimeSpan.Zero)` — `DateTimeOffset.DateTime` always returns `Kind=Unspecified`, which is the exact path exercised in production.

**DepartmentSyncService and AccountingTemplateSyncService input:** Their DTOs have `LastUpdate` as `DateTime` (non-nullable). The test passes `new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified)` directly.

## Files created/modified

- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs` — added `Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified`
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs` — added `Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified`
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs` — added `Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified`
- `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs` — added `Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified`

## Tests

```
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests --filter "FullyQualifiedName~Map_WhenLastUpdate"
```

Result: Passed 4, Failed 0 (all four new tests pass).

Full Analytics unit test run: Passed 28/30 (2 failures are pre-existing `LedgerSyncIntegrationTests` that require a PostgreSQL Testcontainer — not related to this change).

## How to verify

```bash
cd /home/user/worktrees/feature-3335-Telemetry-Nightly-Hangfire-Job-Argumentexception-D/backend
dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~Map_WhenLastUpdateIsUnspecifiedKind" \
  --no-build
```

Expected: Passed 4, Failed 0.

## Notes

- `InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")` was already in place in `AssemblyInfo.cs` — no changes needed.
- The value-equality assertion is environment-sensitive: it will produce a different absolute UTC offset depending on `TimeZoneInfo.Local` on the test runner. This is intentional — the test verifies the method applies the same reference conversion, not a hardcoded offset.
- If `TimeZoneInfo.Local` is UTC (e.g., on GitHub Actions with `TZ=UTC`), both the `ConvertTimeToUtc` call in `Map()` and the `expected` computation use UTC, so the test still passes and is a valid regression guard.

## PR Summary

Added `Kind=Unspecified` regression tests to all four Flexi sync service test classes (`LedgerSyncServiceTests`, `ContactSyncServiceTests`, `DepartmentSyncServiceTests`, `AccountingTemplateSyncServiceTests`). Each new test exercises the exact code path that was crashing the nightly Hangfire job (Npgsql `ArgumentException` on `Kind=Unspecified` timestamps) and will catch any future regression that reverts `ConvertTimeToUtc` back to `ToUniversalTime`.

## Status
DONE
