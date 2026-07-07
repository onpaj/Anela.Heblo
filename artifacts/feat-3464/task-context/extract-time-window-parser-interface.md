### task: extract-time-window-parser-interface

## Context

`GetProductMarginSummaryHandler` currently depends on the concrete class `TimeWindowParser` instead of an interface, unlike every other collaborator in the Analytics module (`IProductFilterService`, `IReportBuilderService`, `IMarginCalculator`, `IMonthlyBreakdownGenerator`). Fix: add `ITimeWindowParser`, implement it on `TimeWindowParser`, update DI registration, update the handler's constructor/field. Pure refactor — no parsing logic, test assertions, or public contracts change.

Exactly three production/test files reference `TimeWindowParser` today (confirmed in spec/arch-review): the implementation file, the DI module, and the handler. A fourth file — the existing test — constructs `TimeWindowParser` directly via `new` and will keep compiling unchanged because `TimeWindowParser` will still exist as a concrete class implementing the new interface.

Files touched (all exist already; no new files):
1. `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`
2. `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`
3. `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`

Not modified: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs` — it already compiles and passes unchanged once `TimeWindowParser : ITimeWindowParser` exists, since `TimeWindowParser` remains a valid argument everywhere `ITimeWindowParser` is now expected. This is the smallest-diff option explicitly permitted by spec FR-4 and design's "Component Design" section for `GetProductMarginSummaryHandlerTests`.

Working directory for all commands below: repository root (`/home/user/worktrees/feature-3464-Arch-Review-Analytics-Timewindowparser-Injected-As`).

---

## Step 0 — Baseline: confirm the existing test suite is green before touching anything

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected output: build succeeds, all 6 tests in `GetProductMarginSummaryHandlerTests` pass (`Passed!` summary line, `Failed: 0`).

If this fails before any change is made, stop — the baseline is broken for reasons unrelated to this task and must be investigated first (do not proceed).

---

## Step 1 — Add `ITimeWindowParser` and implement it on `TimeWindowParser`

File: `backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs`

Current full content:

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

Replace the entire file content with:

```csharp
namespace Anela.Heblo.Application.Features.Analytics.Services;

public interface ITimeWindowParser
{
    (DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow);
}

public class TimeWindowParser : ITimeWindowParser
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

Changes: inserted the `ITimeWindowParser` interface above the class; changed `public class TimeWindowParser` to `public class TimeWindowParser : ITimeWindowParser`. Constructor and `ParseTimeWindow` body are byte-for-byte unchanged.

Verify — build only (interface exists, nothing consumes it as `ITimeWindowParser` yet, so this must compile cleanly on its own):

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected output: `Build succeeded.` with 0 errors.

---

## Step 2 — Update DI registration in `AnalyticsModule.cs`

File: `backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs`

Find this line (currently line 47):

```csharp
        services.AddScoped<TimeWindowParser>();
```

Replace with:

```csharp
        services.AddScoped<ITimeWindowParser, TimeWindowParser>();
```

No other line in this file changes. The surrounding context (for exact-match confirmation) is:

```csharp
        services.AddScoped<ITimeWindowParser, TimeWindowParser>();
        services.AddScoped<IMarginCalculator, MarginCalculator>();
        services.AddScoped<IMonthlyBreakdownGenerator, MonthlyBreakdownGenerator>();
```

Verify — build only:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected output: `Build succeeded.` with 0 errors (the handler still declares its constructor parameter as concrete `TimeWindowParser` at this point, and DI registers `TimeWindowParser` as its own implementation type too via the interface mapping — the concrete type resolution via constructor injection still works because ASP.NET Core DI does not require the constructor parameter type to be the same as the registered service type for this to compile; it is a source-level check only at this stage, actual DI graph resolution happens at runtime and is unaffected by this intermediate state). No runtime/integration test needs to pass at this intermediate step — build success is the only gate here.

---

## Step 3 — Update `GetProductMarginSummaryHandler` to depend on `ITimeWindowParser`

File: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs`

Find:

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

Replace with:

```csharp
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly IMarginCalculator _marginCalculator;
    private readonly IMonthlyBreakdownGenerator _monthlyBreakdownGenerator;
    private readonly ITimeWindowParser _timeWindowParser;

    public GetProductMarginSummaryHandler(
        IAnalyticsRepository analyticsRepository,
        IMarginCalculator marginCalculator,
        IMonthlyBreakdownGenerator monthlyBreakdownGenerator,
        ITimeWindowParser timeWindowParser)
    {
        _analyticsRepository = analyticsRepository;
        _marginCalculator = marginCalculator;
        _monthlyBreakdownGenerator = monthlyBreakdownGenerator;
        _timeWindowParser = timeWindowParser;
    }
```

No other part of this file changes — `Handle`, `GenerateTopProducts`, `CalculateGroupMarginData`, `ApplySorting`, `CalculateTotalMarginForLevel`, and the `GroupMarginData` helper class are untouched. The `using Anela.Heblo.Application.Features.Analytics.Services;` import at the top of the file already covers `ITimeWindowParser` (same namespace as `TimeWindowParser`) — no new `using` needed.

Verify — build only:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected output: `Build succeeded.` with 0 errors.

---

## Step 4 — Confirm the existing test file compiles and passes unmodified

No edit is made to `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs`. Its constructor does:

```csharp
var timeProvider = new FakeTimeProvider(FrozenNow);
_timeWindowParser = new TimeWindowParser(timeProvider);
_handler = new GetProductMarginSummaryHandler(
    _analyticsRepositoryMock.Object,
    _marginCalculator,
    _monthlyBreakdownGenerator,
    _timeWindowParser);
```

`_timeWindowParser` is declared as `private readonly TimeWindowParser _timeWindowParser;`. Since `TimeWindowParser` now implements `ITimeWindowParser`, passing it into `GetProductMarginSummaryHandler`'s constructor (which now expects `ITimeWindowParser`) compiles without any change to the test file.

Verify — full test run for this class:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginSummaryHandlerTests"
```

Expected output: build succeeds, all 6 tests pass:
- `Handle_ValidRequest_ReturnsCorrectResponse`
- `Handle_DifferentTimeWindows_ParsesCorrectly` (3 theory cases: `current-year`, `last-6-months`, `last-12-months`)
- `Handle_EmptyProductList_ReturnsZeroMargin`
- `Handle_WithMockedDependencies_InvokesCalculatorAndBreakdownGenerator`
- `GetMarginAmountForLevel_WithUndefinedEnumValue_ThrowsArgumentOutOfRangeException`
- `ParseTimeWindow_UnknownValue_ThrowsArgumentException`

`Passed!` summary line, `Failed: 0`.

---

## Step 5 — Repo-wide confirmation of zero remaining concrete-type references outside the implementation file

Run:

```bash
grep -rn "TimeWindowParser" backend --include="*.cs" | grep -v "Services/TimeWindowParser.cs"
```

Expected output: exactly two matches, both compiling references to the concrete type that are expected to remain (DI registration's second type argument, and the test's `new TimeWindowParser(...)` construction):

```
backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs:        services.AddScoped<ITimeWindowParser, TimeWindowParser>();
backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs:    private readonly TimeWindowParser _timeWindowParser;
backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginSummaryHandlerTests.cs:        _timeWindowParser = new TimeWindowParser(timeProvider);
```

If any other file appears (e.g. another handler or module still declaring a `TimeWindowParser`-typed field/parameter), stop and investigate — the spec's usage-site inventory (spec.r1.md, Background section) would be incomplete and the fix is not done.

---

## Step 6 — Full solution build and format check

```bash
dotnet build
```

Expected output: `Build succeeded.` with 0 errors, 0 new warnings introduced by this change.

```bash
dotnet format --verify-no-changes
```

Expected output: exit code 0, no files reported as needing formatting changes. If `TimeWindowParser.cs` (or any other touched file) is reported, run `dotnet format` (without `--verify-no-changes`) once, review the diff to confirm it only reformats whitespace/ordering (no logic change), and re-run `dotnet format --verify-no-changes` to confirm it now passes.

---

## Step 7 — Full backend test suite

```bash
dotnet test
```

Expected output: `Build succeeded.` followed by a `Passed!` summary with `Failed: 0` across the whole solution (no test outside `GetProductMarginSummaryHandlerTests` references `TimeWindowParser`, so no other test class is affected).

---

## Step 8 — Commit

Stage exactly the three modified files (do not use `git add -A`):

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Analytics/Services/TimeWindowParser.cs \
  backend/src/Anela.Heblo.Application/Features/Analytics/AnalyticsModule.cs \
  backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginSummary/GetProductMarginSummaryHandler.cs
```

Confirm exactly these three files are staged and nothing else:

```bash
git status --short
```

Expected output: three lines, each starting with `M ` (modified), matching the three paths above, and no other entries.

Commit:

```bash
git commit -m "#3464: Extract ITimeWindowParser interface for DIP compliance in Analytics module

TimeWindowParser was the sole concrete-type dependency injected into
GetProductMarginSummaryHandler, inconsistent with every sibling
collaborator (IProductFilterService, IReportBuilderService,
IMarginCalculator, IMonthlyBreakdownGenerator) which are all
interface-based. Extract ITimeWindowParser colocated in
TimeWindowParser.cs, update DI registration to
AddScoped<ITimeWindowParser, TimeWindowParser>(), and update the
handler's constructor/field to depend on the interface. Pure
refactor: no parsing logic, contracts, or test assertions change."
```

Verify:

```bash
git log --oneline -1
git status --short
```

Expected output: the new commit appears at HEAD with the message above; `git status --short` shows a clean working tree for the three files (no longer listed as modified — untouched files, if any remain from unrelated in-flight work, are out of scope for this task).

---

## Done criteria for this task

- `ITimeWindowParser` exists in `Anela.Heblo.Application.Features.Analytics.Services`, colocated with `TimeWindowParser` in `TimeWindowParser.cs`, with the exact signature `(DateTime fromDate, DateTime toDate) ParseTimeWindow(string timeWindow)`.
- `TimeWindowParser : ITimeWindowParser`; constructor and `ParseTimeWindow` body unchanged.
- `AnalyticsModule.cs` registers `services.AddScoped<ITimeWindowParser, TimeWindowParser>();` (Scoped lifetime preserved).
- `GetProductMarginSummaryHandler` declares `private readonly ITimeWindowParser _timeWindowParser;` and takes `ITimeWindowParser timeWindowParser` in its constructor.
- `GetProductMarginSummaryHandlerTests.cs` is unmodified and all 6 of its tests pass.
- `dotnet build`, `dotnet format --verify-no-changes`, and `dotnet test` all pass across the full solution.
- Exactly one commit contains exactly the three intended file changes.
