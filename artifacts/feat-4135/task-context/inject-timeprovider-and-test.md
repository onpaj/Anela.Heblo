### task: inject-timeprovider-and-test

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs:12-15` (fields), `:17-27` (constructor), `:33-34` (Handle body)
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs:15-26` (fields + constructor), insert new test after line 176

- [ ] **Step 1: Add `TimeProvider` field to `GetPurchaseStockAnalysisHandler`**

In `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`, change (lines 12-15):

```csharp
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IStockSeverityCalculator _stockSeverityCalculator;
    private readonly IStockAnalysisCalculator _stockAnalysisCalculator;
    private readonly ILogger<GetPurchaseStockAnalysisHandler> _logger;
```

to:

```csharp
    private readonly IMaterialCatalogService _materialCatalog;
    private readonly IStockSeverityCalculator _stockSeverityCalculator;
    private readonly IStockAnalysisCalculator _stockAnalysisCalculator;
    private readonly ILogger<GetPurchaseStockAnalysisHandler> _logger;
    private readonly TimeProvider _timeProvider;
```

- [ ] **Step 2: Add `TimeProvider` constructor parameter**

In the same file, change the constructor (lines 17-27):

```csharp
    public GetPurchaseStockAnalysisHandler(
        IMaterialCatalogService materialCatalog,
        IStockSeverityCalculator stockSeverityCalculator,
        IStockAnalysisCalculator stockAnalysisCalculator,
        ILogger<GetPurchaseStockAnalysisHandler> logger)
    {
        _materialCatalog = materialCatalog;
        _stockSeverityCalculator = stockSeverityCalculator;
        _stockAnalysisCalculator = stockAnalysisCalculator;
        _logger = logger;
    }
```

to:

```csharp
    public GetPurchaseStockAnalysisHandler(
        IMaterialCatalogService materialCatalog,
        IStockSeverityCalculator stockSeverityCalculator,
        IStockAnalysisCalculator stockAnalysisCalculator,
        ILogger<GetPurchaseStockAnalysisHandler> logger,
        TimeProvider timeProvider)
    {
        _materialCatalog = materialCatalog;
        _stockSeverityCalculator = stockSeverityCalculator;
        _stockAnalysisCalculator = stockAnalysisCalculator;
        _logger = logger;
        _timeProvider = timeProvider;
    }
```

- [ ] **Step 3: Replace `DateTime.UtcNow` calls in `Handle`**

In the same file, change (lines 33-34):

```csharp
        var fromDate = request.FromDate ?? DateTime.UtcNow.AddYears(-1);
        var toDate = request.ToDate ?? DateTime.UtcNow;
```

to:

```csharp
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var fromDate = request.FromDate ?? now.AddYears(-1);
        var toDate = request.ToDate ?? now;
```

Verify no other `DateTime.UtcNow`/`DateTime.Now` reference remains in this file:

```bash
grep -n "DateTime.UtcNow\|DateTime.Now" backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs
```

Expected output: no lines printed (exit code 1).

- [ ] **Step 4: Update `GetPurchaseStockAnalysisHandlerTests` to inject a mocked `TimeProvider`**

In `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs`, change (lines 15-26):

```csharp
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    public GetPurchaseStockAnalysisHandlerTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, _stockSeverityCalculatorMock.Object, new StockAnalysisCalculator(), _loggerMock.Object);
    }
```

to:

```csharp
    private readonly Mock<IMaterialCatalogService> _materialCatalogMock;
    private readonly Mock<IStockSeverityCalculator> _stockSeverityCalculatorMock;
    private readonly Mock<ILogger<GetPurchaseStockAnalysisHandler>> _loggerMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetPurchaseStockAnalysisHandler _handler;

    private static readonly DateTimeOffset FixedNow = new(2024, 8, 2, 14, 30, 22, TimeSpan.Zero);

    public GetPurchaseStockAnalysisHandlerTests()
    {
        _materialCatalogMock = new Mock<IMaterialCatalogService>();
        _stockSeverityCalculatorMock = new Mock<IStockSeverityCalculator>();
        _loggerMock = new Mock<ILogger<GetPurchaseStockAnalysisHandler>>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(FixedNow);
        _handler = new GetPurchaseStockAnalysisHandler(_materialCatalogMock.Object, _stockSeverityCalculatorMock.Object, new StockAnalysisCalculator(), _loggerMock.Object, _timeProviderMock.Object);
    }
```

This is the single call site that constructs `_handler` (confirmed by reading the file — no per-test `new GetPurchaseStockAnalysisHandler(...)` calls exist elsewhere), so every existing test picks up the new dependency automatically without further edits.

- [ ] **Step 5: Add the default-fallback test**

In the same file, insert a new test immediately after `Handle_InvalidDateRange_ReturnsError` (which ends at line 176, right before `Handle_Pagination_ReturnsCorrectPage` at line 178):

```csharp
    [Fact]
    public async Task Handle_NullDates_DefaultsToOneYearWindowFromTimeProvider()
    {
        var snapshots = CreateTestSnapshots();
        _materialCatalogMock
            .Setup(x => x.GetStockAnalysisSnapshotsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);
        _stockSeverityCalculatorMock.Setup(x => x.DetermineStockSeverity(
            It.IsAny<double>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>(), It.IsAny<bool>()))
            .Returns(StockSeverity.Optimal);

        var request = new GetPurchaseStockAnalysisRequest { PageNumber = 1, PageSize = 10 };

        var response = await _handler.Handle(request, CancellationToken.None);

        response.Summary.AnalysisPeriodStart.Should().Be(FixedNow.UtcDateTime.AddYears(-1));
        response.Summary.AnalysisPeriodEnd.Should().Be(FixedNow.UtcDateTime);
    }

```

Place it as its own method between the closing brace of `Handle_InvalidDateRange_ReturnsError` (line 176) and the `[Fact]` attribute of `Handle_Pagination_ReturnsCorrectPage` (line 178), keeping the existing blank line separating test methods.

- [ ] **Step 6: Build**

```bash
cd backend && dotnet build
```

Expected output ends with:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

(pre-existing warning counts, if any, are unaffected by this change — the key signal is `0 Error(s)` and no new warnings referencing `GetPurchaseStockAnalysisHandler.cs` or its test file)

- [ ] **Step 7: Format**

```bash
cd backend && dotnet format --verify-no-changes
```

If this reports changes needed, run `cd backend && dotnet format` and re-verify. Expected final output: no formatting violations reported (exit code 0).

- [ ] **Step 8: Run the affected test class**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPurchaseStockAnalysisHandlerTests"
```

Expected output: all tests pass, e.g.:

```
Passed!  - Failed:     0, Passed:    16, Skipped:     0, Total:    16
```

(16 = the 15 existing tests in `GetPurchaseStockAnalysisHandlerTests.cs` + 1 new `Handle_NullDates_DefaultsToOneYearWindowFromTimeProvider` test; confirm the actual total matches "existing count + 1" from the file as edited)

- [ ] **Step 9: Run the full test project to confirm no regressions**

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected output: `Passed!` with `Failed: 0`.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs
git commit -m "$(cat <<'EOF'
Inject TimeProvider into GetPurchaseStockAnalysisHandler

Replaces the two direct DateTime.UtcNow calls used for the default
date-range fallback with an injected TimeProvider, matching the
pattern already used by CreatePurchaseOrderHandler. Makes the
fallback path deterministic under test.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01XVuVzAr9Ens3DDzsK2UuN8
EOF
)"
```

Expected output: a new commit created on the current branch; `git status` shows a clean working tree for these two files.

---

## Self-Review

**Spec coverage:**
- FR-1 (inject `TimeProvider` constructor dependency, private readonly field, no other params/order/assignments changed) → Step 1 (field) + Step 2 (constructor param appended last, all other params/assignments untouched).
- FR-2 (replace both `DateTime.UtcNow` calls with `now = _timeProvider.GetUtcNow().UtcDateTime`, no remaining direct `DateTime.UtcNow`/`DateTime.Now` reference, rest of `Handle` unchanged) → Step 3 (exact replacement + grep verification); no other lines in `Handle` touched.
- FR-3 (test class uses `Mock<TimeProvider>` with fixed `DateTimeOffset`, existing tests pass unmodified in behavior with only the construction call site changed, new test asserts `Summary.AnalysisPeriodStart`/`AnalysisPeriodEnd` against `fixedNow` deterministically) → Step 4 (single shared constructor edit, matching `CreatePurchaseOrderHandlerTests`'s `Mock<TimeProvider>` + `FixedNow` pattern) + Step 5 (new test, no real-wall-clock dependency).
- NFR-1 (no performance impact) → satisfied by construction; no additional step needed (trivial call substitution).
- NFR-2 (no security impact) → satisfied by construction; no additional step needed.
- Out-of-scope items (CreatePurchaseOrderHandler, other handlers, DTOs, controller/endpoint, validation/filtering/sorting/pagination logic) → not touched by any step; only the two named files are modified.
- Build/format/test validation required by CLAUDE.md → Steps 6-9.

**Placeholder scan:** No "TBD", no "add appropriate error handling", no "similar to Task N" placeholders anywhere in the steps above — every step contains the literal before/after code, exact file paths with line numbers, and exact shell commands with expected output.

**Type consistency:** `TimeProvider timeProvider` parameter name and `_timeProvider` field name match between handler (Step 1-2) and are referenced identically in `Handle` (Step 3). Test field `_timeProviderMock` of type `Mock<TimeProvider>` and `FixedNow` of type `DateTimeOffset` match `CreatePurchaseOrderHandlerTests`'s existing declarations exactly (same type, same mock setup call `Setup(x => x.GetUtcNow()).Returns(FixedNow)`). The new test's assertions (`FixedNow.UtcDateTime.AddYears(-1)` / `FixedNow.UtcDateTime`) match the handler's computation (`now.AddYears(-1)` / `now` where `now = _timeProvider.GetUtcNow().UtcDateTime`) exactly. Constructor call site in Step 4 passes exactly 5 arguments in the same order as the Step 2 constructor signature (`materialCatalog, stockSeverityCalculator, stockAnalysisCalculator, logger, timeProvider`).
