# Inject TimeProvider into Manufacture Module Handlers (GetManufactureOutput & CalculateBatchPlan) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every `DateTime.Now` call in `GetManufactureOutputHandler` and `CalculateBatchPlanHandler` with `_timeProvider.GetUtcNow().DateTime` so the two handlers no longer drift with the server's wall clock and become deterministic in tests.

**Architecture:** Append `TimeProvider` as the final constructor parameter on both handlers, store it in `private readonly TimeProvider _timeProvider;`, and snapshot the clock once per `Handle` call into a local variable. Test fixtures use `Mock<TimeProvider>` via the already-referenced Moq package — **not** `FakeTimeProvider` (the testing package is not referenced and the project convention forbids adding it). DI registration is already in place (`TimeProvider.System` is a singleton at `ServiceCollectionExtensions.cs:127`); no wiring change is required.

**Tech Stack:** .NET 8, C#, MediatR, Moq, FluentAssertions, xUnit. Files touched live under `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/` and `backend/test/Anela.Heblo.Tests/Features/Manufacture/`.

---

## File Structure

**Modified (production):**
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs` — add `TimeProvider _timeProvider`, snapshot `now` once, replace 3 `DateTime.Now` reads.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs` — add `TimeProvider _timeProvider`, replace 1 `DateTime.Now` read in `ResolveSalesRanges`.

**Modified (tests):**
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOutputHandlerTests.cs` — add `Mock<TimeProvider>` field, pass to constructor, add two time-shift tests.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs` — add `Mock<TimeProvider>` field, pass to constructor, add a fallback-path time-shift test.

**Untouched (verified):**
- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:127` — `services.AddSingleton(TimeProvider.System);` is already registered.
- All MediatR request/response DTOs in both use-case folders.
- `IManufactureHistoryClient`, `DateRange` (`record DateRange(DateTime From, DateTime To)`), `ITimePeriodResolver`, `IBatchPlanningService`.
- The MVC controllers that dispatch these handlers.

**Verified ground truth before writing this plan:**
- `GetManufactureOutputHandler` currently has 3 `DateTime.Now` reads (line 31 once, lines 128–129 twice via `.Year`/`.Month`). No `TimeProvider` injected.
- `CalculateBatchPlanHandler` currently has 1 `DateTime.Now` read (line 58 in `ResolveSalesRanges`). No `TimeProvider` injected.
- `GetManufactureOutputHandlerTests.cs:25–28` constructs the handler with three arguments — will fail to compile after the change unless updated.
- `CalculateBatchPlanHandlerTests.cs:26–30` constructs the handler with four arguments — will fail to compile after the change unless updated.
- `CreateManufactureOrderHandler` (the sibling pattern) appends `TimeProvider` last and uses `private readonly TimeProvider _timeProvider;` (lines 17, 23–24, 30). Mirror this.
- `DateRange` has properties `From`/`To` — **not** `Start`/`End`.

---

## Task 1: Inject `TimeProvider` into `GetManufactureOutputHandler` and snapshot the clock once

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs`

- [ ] **Step 1: Add the `_timeProvider` field and constructor parameter**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs`, find this block (lines 12–24):

```csharp
public class GetManufactureOutputHandler : IRequestHandler<GetManufactureOutputRequest, GetManufactureOutputResponse>
{
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly IManufactureCatalogSource _catalogSource;
    private readonly ILogger<GetManufactureOutputHandler> _logger;

    public GetManufactureOutputHandler(
        IManufactureHistoryClient manufactureHistoryClient,
        IManufactureCatalogSource catalogSource,
        ILogger<GetManufactureOutputHandler> logger)
    {
        _manufactureHistoryClient = manufactureHistoryClient;
        _catalogSource = catalogSource;
        _logger = logger;
    }
```

Replace it with:

```csharp
public class GetManufactureOutputHandler : IRequestHandler<GetManufactureOutputRequest, GetManufactureOutputResponse>
{
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly IManufactureCatalogSource _catalogSource;
    private readonly ILogger<GetManufactureOutputHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public GetManufactureOutputHandler(
        IManufactureHistoryClient manufactureHistoryClient,
        IManufactureCatalogSource catalogSource,
        ILogger<GetManufactureOutputHandler> logger,
        TimeProvider timeProvider)
    {
        _manufactureHistoryClient = manufactureHistoryClient;
        _catalogSource = catalogSource;
        _logger = logger;
        _timeProvider = timeProvider;
    }
```

- [ ] **Step 2: Replace the `toDate` read (line 31) with a single snapshot**

In the same file, find this line (line 31):

```csharp
        var toDate = DateTime.Now;
```

Replace it with:

```csharp
        var now = _timeProvider.GetUtcNow().DateTime;
        var toDate = now;
```

Rationale: we will reuse `now` later (Step 3) to keep one clock read per `Handle` call.

- [ ] **Step 3: Replace the gap-filling loop bounds (lines 128–129) to use the snapshot**

In the same file, find this block (lines 128–129):

```csharp
        var currentDate = new DateTime(fromDate.Year, fromDate.Month, 1);
        var endDate = new DateTime(toDate.Year, toDate.Month, 1);
```

Replace it with:

```csharp
        var currentDate = new DateTime(fromDate.Year, fromDate.Month, 1);
        var endDate = new DateTime(now.Year, now.Month, 1);
```

Rationale: the spec calls for deriving `Year`/`Month` from a single snapshot (FR-1, lines 128–129). `toDate == now` here so `toDate.Year`/`toDate.Month` would work too, but using `now` makes the snapshot's role explicit.

- [ ] **Step 4: Verify no `DateTime.Now`/`DateTime.UtcNow`/`DateTime.Today` remains**

Run: `grep -nE 'DateTime\.(Now|UtcNow|Today)' backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs`
Expected: no output (empty result).

- [ ] **Step 5: Build the backend to confirm the production code compiles in isolation**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds. (Test project will be broken at this point — that is normal and handled in Task 3.)

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs
git commit -m "refactor(manufacture): inject TimeProvider into GetManufactureOutputHandler"
```

---

## Task 2: Inject `TimeProvider` into `CalculateBatchPlanHandler`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`

- [ ] **Step 1: Add the `_timeProvider` field and constructor parameter**

In `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`, find this block (lines 14–29):

```csharp
    private readonly IBatchPlanningService _batchPlanningService;
    private readonly IManufactureCatalogSource _catalogSource;
    private readonly IManufactureClient _manufactureClient;
    private readonly ITimePeriodResolver _timePeriodResolver;

    public CalculateBatchPlanHandler(
        IBatchPlanningService batchPlanningService,
        IManufactureCatalogSource catalogSource,
        IManufactureClient manufactureClient,
        ITimePeriodResolver timePeriodResolver)
    {
        _batchPlanningService = batchPlanningService;
        _catalogSource = catalogSource;
        _manufactureClient = manufactureClient;
        _timePeriodResolver = timePeriodResolver;
    }
```

Replace it with:

```csharp
    private readonly IBatchPlanningService _batchPlanningService;
    private readonly IManufactureCatalogSource _catalogSource;
    private readonly IManufactureClient _manufactureClient;
    private readonly ITimePeriodResolver _timePeriodResolver;
    private readonly TimeProvider _timeProvider;

    public CalculateBatchPlanHandler(
        IBatchPlanningService batchPlanningService,
        IManufactureCatalogSource catalogSource,
        IManufactureClient manufactureClient,
        ITimePeriodResolver timePeriodResolver,
        TimeProvider timeProvider)
    {
        _batchPlanningService = batchPlanningService;
        _catalogSource = catalogSource;
        _manufactureClient = manufactureClient;
        _timePeriodResolver = timePeriodResolver;
        _timeProvider = timeProvider;
    }
```

- [ ] **Step 2: Replace the `DateTime.Now` fallback in `ResolveSalesRanges` (line 58)**

In the same file, find this line (line 58):

```csharp
        var endDate = request.ToDate ?? DateTime.Now;
```

Replace it with:

```csharp
        var endDate = request.ToDate ?? _timeProvider.GetUtcNow().DateTime;
```

- [ ] **Step 3: Verify no `DateTime.Now`/`DateTime.UtcNow`/`DateTime.Today` remains**

Run: `grep -nE 'DateTime\.(Now|UtcNow|Today)' backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs`
Expected: no output.

- [ ] **Step 4: Build the backend application project to confirm production code compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds. (The test project is still broken — see Task 3.)

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs
git commit -m "refactor(manufacture): inject TimeProvider into CalculateBatchPlanHandler"
```

---

## Task 3: Update `GetManufactureOutputHandlerTests` fixture and add time-shift tests

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOutputHandlerTests.cs`

- [ ] **Step 1: Confirm the test project currently fails to compile (red baseline)**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | grep -E "GetManufactureOutputHandler|CalculateBatchPlanHandler" | head`
Expected: at least one error indicating the constructor arity mismatch (e.g., "does not contain a constructor that takes 3 arguments" / "4 arguments"). This confirms the existing fixtures break, as the arch-review predicted.

- [ ] **Step 2: Update the fixture to inject a `Mock<TimeProvider>`**

In `backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOutputHandlerTests.cs`, find this block (lines 14–29):

```csharp
public class GetManufactureOutputHandlerTests
{
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryClientMock;
    private readonly Mock<IManufactureCatalogSource> _catalogRepositoryMock;
    private readonly Mock<ILogger<GetManufactureOutputHandler>> _loggerMock;
    private readonly GetManufactureOutputHandler _handler;

    public GetManufactureOutputHandlerTests()
    {
        _manufactureHistoryClientMock = new Mock<IManufactureHistoryClient>();
        _catalogRepositoryMock = new Mock<IManufactureCatalogSource>();
        _loggerMock = new Mock<ILogger<GetManufactureOutputHandler>>();

        _handler = new GetManufactureOutputHandler(
            _manufactureHistoryClientMock.Object,
            _catalogRepositoryMock.Object,
            _loggerMock.Object);
    }
```

Replace it with:

```csharp
public class GetManufactureOutputHandlerTests
{
    private static readonly DateTimeOffset FixedClock =
        new DateTimeOffset(2026, 03, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryClientMock;
    private readonly Mock<IManufactureCatalogSource> _catalogRepositoryMock;
    private readonly Mock<ILogger<GetManufactureOutputHandler>> _loggerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetManufactureOutputHandler _handler;

    public GetManufactureOutputHandlerTests()
    {
        _manufactureHistoryClientMock = new Mock<IManufactureHistoryClient>();
        _catalogRepositoryMock = new Mock<IManufactureCatalogSource>();
        _loggerMock = new Mock<ILogger<GetManufactureOutputHandler>>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(FixedClock);

        _handler = new GetManufactureOutputHandler(
            _manufactureHistoryClientMock.Object,
            _catalogRepositoryMock.Object,
            _loggerMock.Object,
            _timeProviderMock.Object);
    }
```

- [ ] **Step 3: Build the test project to confirm existing tests compile again**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj 2>&1 | grep -E "error CS" | grep -i "GetManufactureOutput" | head`
Expected: no errors mentioning `GetManufactureOutputHandler` (errors for `CalculateBatchPlanHandler` are still expected — that fixture is fixed in Task 4).

- [ ] **Step 4: Add a failing test that pins the upper bound passed to `GetHistoryAsync`**

In the same file, append this test method inside the class (after the existing `Handle_NullHistoryFromClient_ReturnsSuccessfulResponseWithEmptyData` test, before `CreateTestCatalogItems`):

```csharp
    [Fact]
    public async Task Handle_UsesInjectedClock_ForDateRangeUpperBound()
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = 3 };

        DateTime? capturedTo = null;
        DateTime? capturedFrom = null;
        _manufactureHistoryClientMock
            .Setup(x => x.GetHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<DateTime, DateTime, string?, CancellationToken>((from, to, _, _) =>
            {
                capturedFrom = from;
                capturedTo = to;
            })
            .ReturnsAsync(new List<ManufactureHistoryRecord>());

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCatalogItems());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        capturedTo.Should().Be(FixedClock.DateTime);
        capturedFrom.Should().Be(FixedClock.DateTime.AddMonths(-3));
    }
```

- [ ] **Step 5: Add a failing test that pins the gap-filling loop terminus to the injected clock**

In the same file, append this test method after the one added in Step 4:

```csharp
    [Fact]
    public async Task Handle_GapFillingLoop_TerminatesAtMonthOfInjectedClock()
    {
        // Arrange
        var request = new GetManufactureOutputRequest { MonthsBack = 2 };

        _manufactureHistoryClientMock
            .Setup(x => x.GetHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());

        _catalogRepositoryMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateTestCatalogItems());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        // FixedClock is 2026-03-15 → range start month = 2026-01, end month = 2026-03 → 3 buckets.
        response.Months.Should().HaveCount(3);
        response.Months.Select(m => m.Month).Should().ContainInOrder("2026-01", "2026-02", "2026-03");
    }
```

- [ ] **Step 6: Run the test project — new tests must pass, no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetManufactureOutputHandlerTests"`
Expected: all tests in the class pass — including the two newly added ones. The existing three tests remain green because their assertions did not depend on the wall clock.

- [ ] **Step 7: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/GetManufactureOutputHandlerTests.cs
git commit -m "test(manufacture): time-shift tests for GetManufactureOutputHandler"
```

---

## Task 4: Update `CalculateBatchPlanHandlerTests` fixture and add a fallback-path time-shift test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs`

- [ ] **Step 1: Update the fixture to inject a `Mock<TimeProvider>`**

In `backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs`, find this block (lines 12–31):

```csharp
public class CalculateBatchPlanHandlerTests
{
    private readonly Mock<IBatchPlanningService> _batchPlanningServiceMock;
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<IManufactureCatalogSource> _catalogRepositoryMock;
    private readonly Mock<ITimePeriodResolver> _timePeriodResolverMock;
    private readonly CalculateBatchPlanHandler _handler;

    public CalculateBatchPlanHandlerTests()
    {
        _batchPlanningServiceMock = new Mock<IBatchPlanningService>();
        _catalogRepositoryMock = new Mock<IManufactureCatalogSource>();
        _manufactureClientMock = new Mock<IManufactureClient>();
        _timePeriodResolverMock = new Mock<ITimePeriodResolver>();
        _handler = new CalculateBatchPlanHandler(
            _batchPlanningServiceMock.Object,
            _catalogRepositoryMock.Object,
            _manufactureClientMock.Object,
            _timePeriodResolverMock.Object);
    }
```

Replace it with:

```csharp
public class CalculateBatchPlanHandlerTests
{
    private static readonly DateTimeOffset FixedClock =
        new DateTimeOffset(2026, 03, 15, 10, 0, 0, TimeSpan.Zero);

    private readonly Mock<IBatchPlanningService> _batchPlanningServiceMock;
    private readonly Mock<IManufactureClient> _manufactureClientMock;
    private readonly Mock<IManufactureCatalogSource> _catalogRepositoryMock;
    private readonly Mock<ITimePeriodResolver> _timePeriodResolverMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly CalculateBatchPlanHandler _handler;

    public CalculateBatchPlanHandlerTests()
    {
        _batchPlanningServiceMock = new Mock<IBatchPlanningService>();
        _catalogRepositoryMock = new Mock<IManufactureCatalogSource>();
        _manufactureClientMock = new Mock<IManufactureClient>();
        _timePeriodResolverMock = new Mock<ITimePeriodResolver>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(FixedClock);

        _handler = new CalculateBatchPlanHandler(
            _batchPlanningServiceMock.Object,
            _catalogRepositoryMock.Object,
            _manufactureClientMock.Object,
            _timePeriodResolverMock.Object,
            _timeProviderMock.Object);
    }
```

- [ ] **Step 2: Build the test project to confirm everything now compiles**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: build succeeds with no errors.

- [ ] **Step 3: Add a failing test that exercises the `DateTime.Now` fallback path**

In the same file, append this test method inside the class (after `Handle_TimePeriodNull_UsesSingleRangeFromFromDateToDate`):

```csharp
    [Fact]
    public async Task Handle_TimePeriodNullAndToDateNull_UsesInjectedClockAsEndDate()
    {
        // Arrange
        var request = new CalculateBatchPlanRequest
        {
            ProductCode = "SEMI001",
            ControlMode = BatchPlanControlMode.MmqMultiplier,
            MmqMultiplier = 1.0,
            FromDate = null,
            ToDate = null,
            TimePeriod = null
        };

        IReadOnlyList<DateRange>? capturedRanges = null;
        _batchPlanningServiceMock
            .Setup(x => x.CalculateBatchPlan(request, It.IsAny<IReadOnlyList<DateRange>>(), It.IsAny<CancellationToken>()))
            .Callback<CalculateBatchPlanRequest, IReadOnlyList<DateRange>, CancellationToken>((_, ranges, _) => capturedRanges = ranges)
            .ReturnsAsync(new CalculateBatchPlanResponse { Success = true });

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(capturedRanges);
        Assert.Single(capturedRanges!);
        Assert.Equal(FixedClock.DateTime, capturedRanges![0].To);
        // Default fallback window is 30 days behind the end date.
        Assert.Equal(FixedClock.DateTime.AddDays(-30), capturedRanges[0].From);
        _timePeriodResolverMock.Verify(
            x => x.Resolve(It.IsAny<TimePeriod>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()),
            Times.Never);
    }
```

Rationale: this is the only branch in `ResolveSalesRanges` that consults the clock. The other two branches (`TimePeriod` set, explicit `ToDate`) are already covered by existing tests and remain green.

- [ ] **Step 4: Run the test project — new test passes, no regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CalculateBatchPlanHandlerTests"`
Expected: all four existing tests pass + the new `Handle_TimePeriodNullAndToDateNull_UsesInjectedClockAsEndDate` test passes.

- [ ] **Step 5: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/CalculateBatchPlanHandlerTests.cs
git commit -m "test(manufacture): time-shift test for CalculateBatchPlanHandler fallback path"
```

---

## Task 5: Full validation — build, format, full test pass

**Files:** none modified — verification only.

- [ ] **Step 1: Format the solution to satisfy the repo style gate**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: exits 0; if any files were reformatted, include them in the final commit (Step 5).

- [ ] **Step 2: Build the entire backend solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: `Build succeeded. 0 Error(s)` (warnings unchanged from baseline).

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln --no-build`
Expected: all tests pass; no new failures introduced beyond any pre-existing failures unrelated to this work.

- [ ] **Step 4: Audit confirms no `DateTime.Now` / `DateTime.UtcNow` / `DateTime.Today` remains in either handler**

Run:
```bash
grep -nE 'DateTime\.(Now|UtcNow|Today)' \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureOutput/GetManufactureOutputHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/CalculateBatchPlan/CalculateBatchPlanHandler.cs
```
Expected: no output. This satisfies FR-1 / FR-2 acceptance criteria.

- [ ] **Step 5: Commit any formatter changes (skip if `dotnet format` made none)**

```bash
git status --short
# if anything is listed, stage and commit:
git add -A
git commit -m "chore: dotnet format after TimeProvider injection"
```

If `git status --short` shows nothing, this step is a no-op — skip the `git add` / `git commit` commands.

---

## Acceptance Cross-Check

Map of spec acceptance criteria → tasks/steps that satisfy them:

| Spec criterion | Where satisfied |
|---|---|
| FR-1: `_timeProvider` field + constructor parameter on `GetManufactureOutputHandler` | Task 1, Step 1 |
| FR-1: `DateTime.Now` removed from line 31 of `GetManufactureOutputHandler` | Task 1, Step 2 |
| FR-1: gap-filling loop uses single snapshot (no double-read) | Task 1, Step 3 + Step 4 audit |
| FR-1: no `DateTime.Now`/`DateTime.UtcNow`/`DateTime.Today` left in `GetManufactureOutputHandler` | Task 1, Step 4 + Task 5, Step 4 |
| FR-2: `_timeProvider` field + constructor parameter on `CalculateBatchPlanHandler` | Task 2, Step 1 |
| FR-2: `DateTime.Now` removed from line 58 in `ResolveSalesRanges` | Task 2, Step 2 |
| FR-2: explicit `ToDate` / `TimePeriod` paths unchanged | Existing tests `Handle_TimePeriodSet_ResolvesViaTimePeriodResolver` and `Handle_TimePeriodNull_UsesSingleRangeFromFromDateToDate` continue passing (Task 5, Step 3) |
| FR-3 (arch-amended): use `Mock<TimeProvider>`, not `FakeTimeProvider` | Task 3, Step 2 and Task 4, Step 1 |
| FR-3: pins upper bound to injected clock for `GetManufactureOutputHandler` | Task 3, Step 4 |
| FR-3: pins gap-filling loop terminus to injected clock | Task 3, Step 5 |
| FR-3 (arch-amended): asserts `DateRange.To` (not `End`) equals injected instant | Task 4, Step 3 |
| FR-3 (arch-amended): existing tests updated to pass `TimeProvider` to constructor | Task 3, Step 2 + Task 4, Step 1 |
| FR-3: all new tests pass, existing tests stay green | Task 3, Step 6 + Task 4, Step 4 + Task 5, Step 3 |
| FR-4: `DateTimeKind` audit — downstream consumers do not depend on `Kind.Local` | Arch-review confirmed: `IManufactureHistoryClient.GetHistoryAsync` and `DateRange` are `Kind`-agnostic; no remediation needed. The PR description should note this audit was performed. |
| NFR-3: no DI registration change, no public API change | Task 1 / Task 2 leave `ServiceCollectionExtensions.cs` and all DTOs untouched (verified in File Structure section). |
| NFR-4: constructor signature matches sibling pattern (append `TimeProvider` last, `_timeProvider` field) | Task 1, Step 1 and Task 2, Step 1 mirror `CreateManufactureOrderHandler.cs:17–31`. |

## Status: COMPLETE
