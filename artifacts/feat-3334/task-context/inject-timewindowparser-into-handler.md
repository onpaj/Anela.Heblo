### task: inject-timewindowparser-into-handler


Replace the static `TimeWindowParser.ParseTimeWindow(...)` call in `GetProductMarginSummaryHandler` with constructor-injected instance usage.

**File to modify:**
`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`

**Step 1 — add field and update constructor.**

Current constructor (lines 18–26):
```csharp
    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
    }
```

Current field declarations (lines 14–16):
```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
```

Replace both with:
```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly TimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        TimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _timeWindowParser = timeWindowParser;
    }
```

**Step 2 — replace static call on line 31.**

Current:
```csharp
        var (fromDate, toDate) = TimeWindowParser.ParseTimeWindow(request.TimeWindow);
```

Replace with:
```csharp
        var (fromDate, toDate) = _timeWindowParser.ParseTimeWindow(request.TimeWindow);
```

No new `using` directives are needed — `TimeWindowParser` is already imported via the existing `using Anela.Heblo.Application.Features.Analytics.Services;` on line 3.

**Verify the solution now builds cleanly:**
```bash
cd /home/user/worktrees/feature-3334-Arch-Review-Analytics-Timewindowparser-Uses-Dateti/backend
dotnet build
```

Expected output: `Build succeeded.` with zero errors.

**Commit:**
```
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs
git commit -m "feat: inject TimeWindowParser into GetProductMarginSummaryHandler (FR-3)"
```

---

