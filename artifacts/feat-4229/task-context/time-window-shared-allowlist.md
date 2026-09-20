### task: time-window-shared-allowlist

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs` (new file — no existing test file for this class was found; create it)

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Analytics.Services;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics.Services;

public class TimeWindowParserTests
{
    [Fact]
    public void SupportedTimeWindows_ContainsExactlyTheFiveKnownValues()
    {
        TimeWindowParser.SupportedTimeWindows.Should().BeEquivalentTo(new[]
        {
            "current-year",
            "current-and-previous-year",
            "last-6-months",
            "last-12-months",
            "last-24-months"
        });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TimeWindowParserTests"`
Expected: FAIL — build error, `TimeWindowParser` has no member `SupportedTimeWindows`.

- [ ] **Step 3: Write minimal implementation**

In `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`, add the static field to the existing class (do not change `ParseTimeWindow`'s body or its `ArgumentException` fallback):

```csharp
public class TimeWindowParser : ITimeWindowParser
{
    public static readonly string[] SupportedTimeWindows =
        ["current-year", "current-and-previous-year", "last-6-months", "last-12-months", "last-24-months"];

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

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~TimeWindowParserTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs backend/test/Anela.Heblo.Tests/Features/Analytics/Services/TimeWindowParserTests.cs
git commit -m "feat(analytics): expose TimeWindowParser.SupportedTimeWindows as shared allow-list"
```

---
