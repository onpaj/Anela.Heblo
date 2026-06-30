# Fix DateTime Kind=Unspecified in FlexiAnalyticsSync Pipeline — Implementation Plan

**Goal:** Fix nightly `flexi-analytics-sync` crash by replacing `DateTime.ToUniversalTime()` with `TimeZoneInfo.ConvertTimeToUtc()` in all four sync service Map() methods and fixing `GetInitialBackfillDateTime()`.

**Architecture:** Backend-only fix in 5 source files. No schema changes, no interface changes, no API changes.

**Tech Stack:** .NET 8, C#, Npgsql, Hangfire, xUnit

---

### task: fix-initial-backfill-datetime

**Goal:** Fix `GetInitialBackfillDateTime()` in `FlexiAnalyticsSyncOptions` to drop `.Date` (which strips `DateTimeKind` back to `Unspecified`) and return correct UTC midnight via `DateTimeOffset.Parse(...).UtcDateTime`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncOptions.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/FlexiAnalyticsSyncOptionsTests.cs`

**Background:**

The current code (introduced by #3243) has a subtle secondary bug:

```csharp
// CURRENT (buggy) — line 17-20 of FlexiAnalyticsSyncOptions.cs
public DateTime GetInitialBackfillDateTime() =>
    DateTime.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
            .Date
            .ToUniversalTime();
```

`DateTime.Parse(..., AssumeUniversal)` returns `Kind=Utc`. Calling `.Date` on a `Kind=Utc` value returns `Kind=Unspecified` (a .NET documented quirk). Then `.ToUniversalTime()` on `Kind=Unspecified` treats the value as the server's local timezone (Prague = UTC+2 in CEST), so `"2020-01-01"` becomes `2019-12-31T23:00:00Z` — one hour off in summer, two hours off in winter.

The fix uses `DateTimeOffset.Parse` (which carries explicit offset) and `.UtcDateTime` (which always returns `Kind=Utc`), matching the watermark pattern already used in `LedgerSyncService.SyncAsync()`.

**Steps:**

- [ ] Open `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/FlexiAnalyticsSyncOptionsTests.cs`. The existing test asserts only `Kind=Utc`. Extend it and add a second fact. Replace the entire file with:

```csharp
using Anela.Heblo.Adapters.Flexi.Analytics;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class FlexiAnalyticsSyncOptionsTests
{
    [Fact]
    public void GetInitialBackfillDateTime_ReturnsUtcKind()
    {
        var options = new FlexiAnalyticsSyncOptions { InitialBackfillFrom = "2024-01-01" };
        var result = options.GetInitialBackfillDateTime();
        Assert.Equal(DateTimeKind.Utc, result.Kind);
    }

    [Fact]
    public void GetInitialBackfillDateTime_DateComponentMatchesConfiguredDateAsUtcMidnight()
    {
        // Regression for the .Date bug introduced in #3243:
        // .Date on Kind=Utc returns Kind=Unspecified, then .ToUniversalTime() shifts
        // by the local offset (Prague UTC+2 CEST), producing 2019-12-31T23:00:00Z.
        // The correct result is 2020-01-01T00:00:00Z.
        var options = new FlexiAnalyticsSyncOptions { InitialBackfillFrom = "2020-01-01" };
        var result = options.GetInitialBackfillDateTime();
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), result);
    }
}
```

- [ ] Run the new test to confirm it fails with the current buggy implementation:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~FlexiAnalyticsSyncOptionsTests" \
  --no-build 2>&1 | tail -20
```

  Expected: `GetInitialBackfillDateTime_DateComponentMatchesConfiguredDateAsUtcMidnight` fails (result is `2019-12-31T23:00:00Z`, not `2020-01-01T00:00:00Z`).

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncOptions.cs`. Replace lines 14–20 (the comment + method body):

```csharp
    // Npgsql 6+ rejects DateTime with Kind != Utc on 'timestamptz' columns.
    // Parse via DateTimeOffset (which carries explicit offset) so .UtcDateTime
    // always returns Kind=Utc regardless of server local timezone.
    // Do NOT use DateTime.Parse(...).Date.ToUniversalTime(): .Date strips Kind back
    // to Unspecified, causing a timezone shift on Prague-TZ containers (#3243 regression).
    public DateTime GetInitialBackfillDateTime() =>
        DateTimeOffset.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
                      .UtcDateTime;
```

  Full file after edit:

```csharp
namespace Anela.Heblo.Adapters.Flexi.Analytics;

public class FlexiAnalyticsSyncOptions
{
    public const string ConfigurationKey = "FlexiAnalyticsSync";

    public bool Enabled { get; set; } = true;
    public string CronExpression { get; set; } = "0 3 * * *";
    public string TimeZone { get; set; } = "Europe/Prague";
    public int BatchSize { get; set; } = 500;
    public string InitialBackfillFrom { get; set; } = "2020-01-01";
    public int RequestTimeoutSeconds { get; set; } = 120;

    // Npgsql 6+ rejects DateTime with Kind != Utc on 'timestamptz' columns.
    // Parse via DateTimeOffset (which carries explicit offset) so .UtcDateTime
    // always returns Kind=Utc regardless of server local timezone.
    // Do NOT use DateTime.Parse(...).Date.ToUniversalTime(): .Date strips Kind back
    // to Unspecified, causing a timezone shift on Prague-TZ containers (#3243 regression).
    public DateTime GetInitialBackfillDateTime() =>
        DateTimeOffset.Parse(InitialBackfillFrom, null, System.Globalization.DateTimeStyles.AssumeUniversal)
                      .UtcDateTime;
}
```

- [ ] Run the tests again to confirm both pass:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~FlexiAnalyticsSyncOptionsTests"
```

  Expected: both facts green.

- [ ] Build to confirm no compile errors:

```
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

- [ ] Commit:

```
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/FlexiAnalyticsSyncOptions.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/FlexiAnalyticsSyncOptionsTests.cs
git commit -m "fix: GetInitialBackfillDateTime drops .Date to prevent Kind=Unspecified shift (#3335)"
```

---

### task: fix-sync-service-map-methods

**Goal:** Fix all four sync service `Map()` methods to use `TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)` instead of `.ToUniversalTime()`, eliminating the ambient-timezone dependency and matching the canonical pattern in `UnspecifiedDateTimeConverter.cs`.

**Files:**
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs`
- Modify: `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs`

**Background:**

The FlexiBee SDK returns `DateTime` with `Kind=Unspecified` representing Prague local time. All four services call `.ToUniversalTime()` which works only if the container's `TZ` is `Europe/Prague`, but this is an implicit ambient dependency. `TimeZoneInfo.ConvertTimeToUtc(value, TimeZoneInfo.Local)` makes the intent explicit (matches `UnspecifiedDateTimeConverter.cs` and `DateTimeLocalKindConverter.cs` already in the codebase).

Two structural patterns exist across the four services:
- **`LedgerSyncService` and `ContactSyncService`**: `dto.LastUpdate` is `DateTime?` (nullable). Current: `dto.LastUpdate?.ToUniversalTime()`. Fix: use the `HasValue` guard.
- **`DepartmentSyncService` and `AccountingTemplateSyncService`**: `dto.LastUpdate` is `DateTime` (non-nullable). Current: `dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime()`. Fix: replace only the conversion call, keep the `== default` guard.

Also change each `Map()` from `private static` to `internal static` so regression tests in the next task can call them directly without going through `SyncAsync`. `InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")` is already configured in both `AssemblyInfo.cs` and the `.csproj`.

**Steps:**

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs`.

  Change line 140 from `private static LedgerEntry Map(...)` to `internal static LedgerEntry Map(...)`.

  Change line 151 from:
  ```csharp
          LastModified = dto.LastUpdate?.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          LastModified = dto.LastUpdate.HasValue
              ? TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)
              : (DateTime?)null,
  ```

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs`.

  Change line 137 from `private static Contact Map(...)` to `internal static Contact Map(...)`.

  Change line 144 from:
  ```csharp
          LastModified = dto.LastUpdate?.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          LastModified = dto.LastUpdate.HasValue
              ? TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate.Value, TimeZoneInfo.Local)
              : (DateTime?)null,
  ```

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs`.

  Change line 111 from `private static Department Map(...)` to `internal static Department Map(...)`.

  Change line 116 from:
  ```csharp
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          // dto.LastUpdate is non-nullable DateTime; == default guard is correct here (no nullable operator).
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate, TimeZoneInfo.Local),
  ```

- [ ] Edit `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs`.

  Change line 112 from `private static AccountingTemplate Map(...)` to `internal static AccountingTemplate Map(...)`.

  Change line 118 from:
  ```csharp
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)dto.LastUpdate.ToUniversalTime(),
  ```
  To:
  ```csharp
          // FlexiBee SDK returns Kind=Unspecified representing Prague local time.
          // ConvertTimeToUtc with TimeZoneInfo.Local matches UnspecifiedDateTimeConverter pattern.
          // dto.LastUpdate is non-nullable DateTime; == default guard is correct here (no nullable operator).
          LastModified = dto.LastUpdate == default ? null : (DateTimeOffset?)TimeZoneInfo.ConvertTimeToUtc(dto.LastUpdate, TimeZoneInfo.Local),
  ```

- [ ] Build to confirm all four compile cleanly:

```
cd backend && dotnet build src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

- [ ] Run the full test suite for the adapter to confirm no regressions:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests
```

  All previously passing tests should remain green.

- [ ] Run `dotnet format` on the project:

```
cd backend && dotnet format src/Adapters/Anela.Heblo.Adapters.Flexi/Anela.Heblo.Adapters.Flexi.csproj
```

- [ ] Commit:

```
git add backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/LedgerSyncService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/ContactSyncService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/DepartmentSyncService.cs \
        backend/src/Adapters/Anela.Heblo.Adapters.Flexi/Analytics/AccountingTemplateSyncService.cs
git commit -m "fix: use ConvertTimeToUtc in all four sync service Map() methods (#3335)"
```

---

### task: add-datetime-regression-tests

**Goal:** Add unit tests that exercise `Kind=Unspecified` inputs — the exact crash path — in all four sync service `Map()` methods and verify `GetInitialBackfillDateTime()` returns correct UTC midnight, so regressions are caught before they reach Npgsql.

**Files:**
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs`
- Test: `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs`

**Background:**

The existing `MakeXxxDto` helpers all pass `DateTimeKind.Utc` inputs, so the tests pass today whether or not the `Map()` methods use `ToUniversalTime()` or `ConvertTimeToUtc`. The crash only occurs with `Kind=Unspecified` inputs (what the SDK actually produces). Each new test passes `Kind=Unspecified` directly to the `Map()` method (now `internal static` after the previous task) and asserts the output is `Kind=Utc` and equals the value that `TimeZoneInfo.ConvertTimeToUtc` would produce.

`InternalsVisibleTo("Anela.Heblo.Adapters.Flexi.Tests")` is already declared in `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs` — no new assembly attribute is needed.

The test uses `TimeZoneInfo.ConvertTimeToUtc(input, TimeZoneInfo.Local)` as the expected value, which means the test is environment-sensitive (it will produce a different absolute UTC value depending on what `TimeZoneInfo.Local` is on the test runner). This is intentional and correct: the test verifies that the `Map()` method applies the same conversion as the reference pattern, not a hardcoded UTC offset. The Kind=Utc assertion is the primary regression guard; the value equality assertion confirms the correct offset was applied.

**Steps:**

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatusAndKeepsWatermarkUnchanged` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // Map() must call ConvertTimeToUtc, not ToUniversalTime().
        // Kind=Unspecified + ToUniversalTime() works only if container TZ=Europe/Prague;
        // ConvertTimeToUtc(value, TimeZoneInfo.Local) is explicit and testable.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new LedgerItemFlexiDto
        {
            Id = 99,
            AccountingDate = unspecified,
            LastUpdate = unspecified,
            AmountLocal = 100.0,
            ParSymbol = "CODE99",
            DebitAccountShowAs = "501000",
            CreditAccountShowAs = "221000",
            CurrencyRef = "code:CZK",
            Description = "Regression test entry",
        };

        var entry = LedgerSyncService.Map(dto);

        Assert.NotNull(entry.LastModified);
        Assert.Equal(DateTimeKind.Utc, entry.LastModified!.Value.Kind);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, entry.LastModified.Value);
    }
```

- [ ] Run just this new test to confirm it compiles and passes:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~LedgerSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatus` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new ContactFlexiDto
        {
            Id = 99L,
            Code = "TEST99",
            Name = "Test Contact",
            CIN = "CIN00000099",
            VATIN = "CZ00000099",
            LastUpdate = unspecified,
        };

        var contact = ContactSyncService.Map(dto);

        Assert.NotNull(contact.LastModified);
        Assert.Equal(DateTimeKind.Utc, contact.LastModified!.Value.Kind);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, contact.LastModified.Value);
    }
```

- [ ] Run just this new test:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~ContactSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatus` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // DepartmentFlexiDto.LastUpdate is non-nullable DateTime; default guard applies.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new DepartmentFlexiDto
        {
            Id = 99,
            Code = "TEST",
            Name = "Test Department",
            LastUpdate = unspecified,
        };

        var department = DepartmentSyncService.Map(dto);

        Assert.NotNull(department.LastModified);
        // Entity.LastModified is DateTimeOffset? for Department; UtcDateTime gives Kind=Utc
        Assert.Equal(TimeSpan.Zero, department.LastModified!.Value.Offset);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, department.LastModified.Value.UtcDateTime);
    }
```

  Note: `DepartmentSyncService.Map()` returns a `Department` whose `LastModified` is `DateTimeOffset?` (the cast in the `== default` branch produces `DateTimeOffset`). The test asserts `Offset == TimeSpan.Zero` (UTC) rather than `Kind=Utc`, because `DateTimeOffset` does not have a `Kind` property.

- [ ] Run just this new test:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~DepartmentSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Add the following fact to `backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs`. Insert it after the `SyncAsync_OnClientError_RecordsFailedStatus` test, before the closing `}` of the class:

```csharp
    [Fact]
    public void Map_WhenLastUpdateIsUnspecifiedKind_ReturnsKindUtcLastModified()
    {
        // Regression test: SDK returns Kind=Unspecified representing Prague local time.
        // AccountingTemplateFlexiDto.LastUpdate is non-nullable DateTime; default guard applies.
        var unspecified = new DateTime(2025, 6, 19, 10, 0, 0, DateTimeKind.Unspecified);
        var dto = new AccountingTemplateFlexiDto
        {
            Id = 99,
            Code = "TEST",
            Name = "Test Template",
            Description = "Regression test",
            LastUpdate = unspecified,
        };

        var template = AccountingTemplateSyncService.Map(dto);

        Assert.NotNull(template.LastModified);
        // Entity.LastModified is DateTimeOffset? for AccountingTemplate; Offset=0 confirms UTC.
        Assert.Equal(TimeSpan.Zero, template.LastModified!.Value.Offset);
        var expected = TimeZoneInfo.ConvertTimeToUtc(unspecified, TimeZoneInfo.Local);
        Assert.Equal(expected, template.LastModified.Value.UtcDateTime);
    }
```

- [ ] Run just this new test:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests \
  --filter "FullyQualifiedName~AccountingTemplateSyncServiceTests.Map_WhenLastUpdateIsUnspecifiedKind"
```

- [ ] Run the full test suite for the adapter to confirm everything is green:

```
cd backend && dotnet test test/Anela.Heblo.Adapters.Flexi.Tests
```

- [ ] Run the full backend build and format:

```
cd backend && dotnet build && dotnet format
```

- [ ] Commit:

```
git add backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/LedgerSyncServiceTests.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/ContactSyncServiceTests.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/DepartmentSyncServiceTests.cs \
        backend/test/Anela.Heblo.Adapters.Flexi.Tests/Analytics/AccountingTemplateSyncServiceTests.cs
git commit -m "test: add Kind=Unspecified regression tests for all four sync service Map() methods (#3335)"
```

---

## Notes for the implementer

**Type differences between services:**

| Service | `dto.LastUpdate` type | Entity `LastModified` type | Guard pattern |
|---|---|---|---|
| `LedgerSyncService` | `DateTime?` | `DateTime?` | `.HasValue` + `ConvertTimeToUtc(dto.LastUpdate.Value, ...)` |
| `ContactSyncService` | `DateTime?` | `DateTime?` | `.HasValue` + `ConvertTimeToUtc(dto.LastUpdate.Value, ...)` |
| `DepartmentSyncService` | `DateTime` (non-nullable) | `DateTimeOffset?` | `== default ? null : (DateTimeOffset?)ConvertTimeToUtc(dto.LastUpdate, ...)` |
| `AccountingTemplateSyncService` | `DateTime` (non-nullable) | `DateTimeOffset?` | `== default ? null : (DateTimeOffset?)ConvertTimeToUtc(dto.LastUpdate, ...)` |

`Department.LastModified` and `AccountingTemplate.LastModified` are `DateTimeOffset?` even though the source comes from a `DateTime` conversion. The cast `(DateTimeOffset?)someDateTime` creates a `DateTimeOffset` with `Offset=TimeSpan.Zero` when `Kind=Utc`, which is correct for Npgsql `timestamptz` writes.

**Do not** change `LedgerSyncService` or `ContactSyncService` to return `DateTimeOffset?` — their entity properties are `DateTime?` and changing them would require an EF Core mapping change and a migration (out of scope).

**`InternalsVisibleTo` is already in place** at `backend/src/Adapters/Anela.Heblo.Adapters.Flexi/AssemblyInfo.cs` — no assembly attribute changes needed in this task.

**Verifying the fix is deployed** (out of scope for code tasks, but critical): after these commits merge to `main`, approve the `deploy-production` environment gate in the `ci-main-branch.yml` Actions tab. Verify the Docker image tag in Azure Portal matches the new build. The nightly job at 02:00 UTC should complete without `ArgumentException` on the following morning.
