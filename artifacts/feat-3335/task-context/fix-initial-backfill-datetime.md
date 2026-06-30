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
