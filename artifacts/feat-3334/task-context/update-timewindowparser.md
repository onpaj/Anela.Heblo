### task: update-timewindowparser


Convert `TimeWindowParser` from a static class to an injectable instance class that receives `TimeProvider` and replaces the silent fallback with `ArgumentException`.

**File to modify:**
`backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`

**Current content (full file):**
```csharp
namespace Anela.Heblo.Application.Features.Analytics.Services;

public static class TimeWindowParser
{
    public static (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = DateTime.Today;

        return timeWindow switch
        {
            "current-year" => (new DateTime(today.Year, 1, 1), today),
            "current-and-previous-year" => (new DateTime(today.Year - 1, 1, 1), today),
            "last-6-months" => (today.AddMonths(-6), today),
            "last-12-months" => (today.AddMonths(-12), today),
            "last-24-months" => (today.AddMonths(-24), today),
            _ => (new DateTime(today.Year, 1, 1), today)
        };
    }
}
```

**Replace with:**
```csharp
namespace Anela.Heblo.Application.Features.Analytics.Services;

public class TimeWindowParser
{
    private readonly TimeProvider _timeProvider;

    public TimeWindowParser(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)
    {
        var today = _timeProvider.GetLocalNow().Date;

        return timeWindow switch
        {
            "current-year" => (new DateTime(today.Year, 1, 1), today),
            "current-and-previous-year" => (new DateTime(today.Year - 1, 1, 1), today),
            "last-6-months" => (today.AddMonths(-6), today),
            "last-12-months" => (today.AddMonths(-12), today),
            "last-24-months" => (today.AddMonths(-24), today),
            _ => throw new ArgumentException($"Unknown time window value: '{timeWindow}'", nameof(timeWindow))
        };
    }
}
```

Key changes:
- `static class` → `class`
- Constructor accepts `TimeProvider timeProvider`, stored as `_timeProvider`
- `DateTime.Today` → `_timeProvider.GetLocalNow().Date`
- Static method becomes instance method (remove `static` keyword)
- Silent fallback `_ => (new DateTime(today.Year, 1, 1), today)` → `ArgumentException`

**Verify the build compiles (expect two errors for the two remaining files that still need updating — that is fine at this stage):**
```bash
cd /home/user/worktrees/feature-3334-Arch-Review-Analytics-Timewindowparser-Uses-Dateti/backend
dotnet build 2>&1 | grep -E "error|warning" | head -20
```

Expected: errors in `GetProductMarginSummaryHandler.cs` about static call on non-static type. That is the compile signal that you must now fix the handler.

**Commit:**
```
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs
git commit -m "refactor: convert TimeWindowParser to injectable instance class with TimeProvider"
```

---

