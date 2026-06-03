# Inject TimeProvider into GetProductMarginsHandler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded `DateTime.Now` call in `GetProductMarginsHandler.MapToMarginDto` (line 189) with a constructor-injected `TimeProvider`, restoring testability, UTC semantics, and consistency with sibling Catalog handlers — and add the first unit test class for this handler covering the 13-month UTC window.

**Architecture:** Single-handler refactor + new test file. Constructor adds a `TimeProvider` parameter (already DI-registered at `ServiceCollectionExtensions.cs:128`), stored in a private readonly field; the `DateTime.Now.AddMonths(-13)` call inside `MapToMarginDto` is swapped for `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`. New `GetProductMarginsHandlerTests.cs` mirrors the existing `GetCatalogDetailHandlerTests` pattern: `Mock<TimeProvider>` via Moq with `.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(...))`. Two test cases — boundary inclusion at 13 months and a UTC+1 day-boundary case — pin the new contract.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions, MediatR. No new dependencies — `Microsoft.Extensions.TimeProvider.Testing` is intentionally NOT introduced; see arch-review amendment #1.

---

## File Structure

**Modified:**

- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`
  - Add `private readonly TimeProvider _timeProvider;` field.
  - Add `TimeProvider timeProvider` parameter to the constructor (between `ICatalogRepository` and `ILogger`).
  - Replace `DateTime.Now.AddMonths(-13)` at line 189 with `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`.

**Created:**

- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`
  - First unit-test class for this handler.
  - Two test methods: one deterministic 13-month-boundary inclusion/exclusion test, one UTC-vs-local-time day-boundary test.
  - Uses `Mock<TimeProvider>` / `Mock<ICatalogRepository>` / `Mock<ILogger<...>>`, FluentAssertions for asserts.

**Untouched (verified during review):**

- `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` — already registers `TimeProvider.System` as a singleton at line 128. **No DI changes.**
- `GetProductMarginsRequest.cs`, `GetProductMarginsResponse.cs`, `ProductMarginDto`, `MonthlyMarginDto`, MediatR contract, HTTP route — all unchanged.

---

## Task 1: Refactor handler to inject TimeProvider (TDD cycle)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`

### Context for the implementer

The handler currently has this constructor (lines 14-24 of `GetProductMarginsHandler.cs`):

```csharp
private readonly ICatalogRepository _catalogRepository;
private readonly ILogger<GetProductMarginsHandler> _logger;

public GetProductMarginsHandler(
    ICatalogRepository catalogRepository,
    ILogger<GetProductMarginsHandler> logger
    )
{
    _catalogRepository = catalogRepository;
    _logger = logger;
}
```

And this filtering expression at line 189 inside `MapToMarginDto`:

```csharp
var dateFrom = DateTime.Now.AddMonths(-13);
var filteredMonthlyData = marginHistory.MonthlyData
    .Where(m => m.Key >= dateFrom)
    .ToList();
```

`marginHistory.MonthlyData` is a `Dictionary<DateTime, MarginData>` on `MonthlyMarginHistory` (see `backend/src/Anela.Heblo.Domain/Features/Catalog/MonthlyMarginHistory.cs`). `MarginData` has `M0`, `M1_A`, `M1_B`, `M2` of type `MarginLevel`, each defaulting to `MarginLevel.Zero`.

The sibling test class to mirror is `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerTests.cs` — specifically the `Mock<TimeProvider>` setup at lines 23, 31, 57.

### Steps

- [ ] **Step 1.1: Create the new test file with the first failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` with the following content:

```csharp
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductMargins;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetProductMarginsHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetProductMarginsHandler _handler;

    public GetProductMarginsHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        var loggerMock = new Mock<ILogger<GetProductMarginsHandler>>();
        _handler = new GetProductMarginsHandler(
            _catalogRepositoryMock.Object,
            _timeProviderMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task Handle_FiltersMonthlyMarginsTo13MonthsBeforeInjectedUtcNow()
    {
        // Arrange
        var utcNow = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var expectedDateFrom = utcNow.AddMonths(-13); // 2025-05-15T12:00:00
        _timeProviderMock
            .Setup(tp => tp.GetUtcNow())
            .Returns(new DateTimeOffset(utcNow, TimeSpan.Zero));

        var atBoundaryKey = expectedDateFrom;                 // included (>=)
        var justBeforeBoundaryKey = expectedDateFrom.AddTicks(-1); // excluded
        var wellWithinKey = new DateTime(2026, 1, 1);          // included

        var aggregate = BuildAggregate(
            productCode: "TEST001",
            monthlyKeys: new[] { atBoundaryKey, justBeforeBoundaryKey, wellWithinKey });

        _catalogRepositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { aggregate });

        var request = new GetProductMarginsRequest();

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(1);

        var monthlyHistory = response.Items[0].MonthlyHistory;
        monthlyHistory.Select(m => m.Month).Should().BeEquivalentTo(new[] { atBoundaryKey, wellWithinKey });
        monthlyHistory.Select(m => m.Month).Should().NotContain(justBeforeBoundaryKey);
    }

    private static CatalogAggregate BuildAggregate(string productCode, IEnumerable<DateTime> monthlyKeys)
    {
        var aggregate = new CatalogAggregate
        {
            Id = productCode,
            ProductName = "Test Product",
            Type = ProductType.Product
        };

        foreach (var key in monthlyKeys)
        {
            aggregate.Margins.MonthlyData[key] = new MarginData();
        }

        return aggregate;
    }
}
```

> Note for implementer: if `CatalogAggregate.Type` has a different property name (e.g. `ProductType` instead of `Type`), check the line `filtered = filtered.Where(x => x.Type == ProductType.Product || x.Type == ProductType.Goods);` in `GetProductMarginsHandler.cs` (around line 115) and use the same name. Likewise, if `Margins` is settable rather than a default-initialized property, instantiate `new MonthlyMarginHistory()` before populating `MonthlyData`. Do not change the handler's filter line — only mirror it accurately in the test fixture.

- [ ] **Step 1.2: Run the test to verify it fails (compile error)**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests" --no-restore
```

Expected: BUILD FAILURE. The compiler reports that `GetProductMarginsHandler` has no constructor accepting `(ICatalogRepository, TimeProvider, ILogger<GetProductMarginsHandler>)` — the current constructor takes only `(ICatalogRepository, ILogger<GetProductMarginsHandler>)`.

If the build instead fails for an unrelated reason (e.g. `CatalogAggregate.Type` name mismatch), fix the test setup to match the aggregate's real property names before continuing — do NOT modify the handler yet.

- [ ] **Step 1.3: Modify the handler to inject TimeProvider**

Edit `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs`.

Replace the field block (lines 14-15):

```csharp
private readonly ICatalogRepository _catalogRepository;
private readonly ILogger<GetProductMarginsHandler> _logger;
```

with:

```csharp
private readonly ICatalogRepository _catalogRepository;
private readonly TimeProvider _timeProvider;
private readonly ILogger<GetProductMarginsHandler> _logger;
```

Replace the constructor (lines 17-24):

```csharp
public GetProductMarginsHandler(
    ICatalogRepository catalogRepository,
    ILogger<GetProductMarginsHandler> logger
    )
{
    _catalogRepository = catalogRepository;
    _logger = logger;
}
```

with:

```csharp
public GetProductMarginsHandler(
    ICatalogRepository catalogRepository,
    TimeProvider timeProvider,
    ILogger<GetProductMarginsHandler> logger)
{
    _catalogRepository = catalogRepository;
    _timeProvider = timeProvider;
    _logger = logger;
}
```

Replace line 189 inside `MapToMarginDto`:

```csharp
var dateFrom = DateTime.Now.AddMonths(-13);
```

with:

```csharp
var dateFrom = _timeProvider.GetUtcNow().DateTime.AddMonths(-13);
```

Do NOT touch any other line in the file. Do NOT add `using System;` — `TimeProvider` lives in `System` which is already in the implicit-usings set for the project.

- [ ] **Step 1.4: Run the test to verify it passes**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests" --no-restore
```

Expected: 1 test passed (`Handle_FiltersMonthlyMarginsTo13MonthsBeforeInjectedUtcNow`).

- [ ] **Step 1.5: Add the timezone-boundary test**

Append the following `[Fact]` to the test class, immediately after `Handle_FiltersMonthlyMarginsTo13MonthsBeforeInjectedUtcNow`:

```csharp
[Fact]
public async Task Handle_UsesUtcNotLocalTime_AtDayBoundary()
{
    // Arrange — UTC time 2025-12-31T23:30:00Z. In a UTC+1 zone this is 2026-01-01T00:30:00.
    // Correct (UTC) dateFrom = 2024-11-30T23:30:00.
    // Bug-mode (local-time) dateFrom would be 2024-12-01T00:30:00 — one month later.
    // An entry keyed at 2024-12-01T00:00:00 is >= the UTC dateFrom but < the local-time dateFrom.
    // Asserting it is INCLUDED demonstrates the new code uses UTC.
    var utcNow = new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc);
    _timeProviderMock
        .Setup(tp => tp.GetUtcNow())
        .Returns(new DateTimeOffset(utcNow, TimeSpan.Zero));

    var discriminatingKey = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc);
    // Included under UTC semantics (2024-11-30T23:30 <= 2024-12-01T00:00),
    // excluded under local-time UTC+1 semantics (2024-12-01T00:30 > 2024-12-01T00:00).

    var aggregate = BuildAggregate(
        productCode: "TEST002",
        monthlyKeys: new[] { discriminatingKey });

    _catalogRepositoryMock
        .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(new[] { aggregate });

    var request = new GetProductMarginsRequest();

    // Act
    var response = await _handler.Handle(request, CancellationToken.None);

    // Assert
    response.Success.Should().BeTrue();
    response.Items.Should().HaveCount(1);
    response.Items[0].MonthlyHistory.Select(m => m.Month)
        .Should().ContainSingle()
        .Which.Should().Be(discriminatingKey);
}
```

- [ ] **Step 1.6: Run both tests to verify they pass**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetProductMarginsHandlerTests" --no-restore
```

Expected: 2 tests passed.

- [ ] **Step 1.7: Run the full Catalog test slice to confirm no regression**

Run:

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Catalog" --no-restore
```

Expected: all tests pass. If anything in the Catalog suite fails, investigate — the only behavior changed is the source of `dateFrom` inside `MapToMarginDto`. No other consumer's contract has changed.

- [ ] **Step 1.8: Format**

Run:

```bash
cd backend && dotnet format
```

Expected: clean exit. No remaining unstaged formatting changes outside the two files touched.

- [ ] **Step 1.9: Build the solution**

Run:

```bash
cd backend && dotnet build
```

Expected: 0 errors, 0 new warnings.

- [ ] **Step 1.10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetProductMargins/GetProductMarginsHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs
git commit -m "$(cat <<'EOF'
refactor: inject TimeProvider into GetProductMarginsHandler

Replace hardcoded DateTime.Now in MapToMarginDto with the injected
TimeProvider abstraction, matching the convention used by sibling
Catalog handlers. Restores testability and fixes a UTC-vs-local-time
discrepancy that could shift the 13-month history window by one month
around midnight in a non-UTC timezone.

Adds the first unit test class for this handler, covering the 13-month
boundary inclusion/exclusion and the UTC-at-day-boundary correctness.

Resolves arch-review finding 2026-05-30.
EOF
)"
```

---

## Self-Review

**1. Spec coverage:**

| Spec requirement | Implementing task / step |
|---|---|
| FR-1 (inject TimeProvider into ctor; private readonly field; no other ctor param changes) | Task 1, Step 1.3 |
| FR-1 (DI container resolves TimeProvider without changes) | Verified: `ServiceCollectionExtensions.cs:128` already registers `TimeProvider.System`. No DI step needed in this plan. |
| FR-2 (line 189 uses `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`; no other `DateTime.Now`/`UtcNow` introduced) | Task 1, Step 1.3 |
| FR-3 (preserves `AddMonths(-13)`, DTO shape, call sites) | Task 1, Step 1.3 changes only one expression; Step 1.7 verifies no regressions |
| FR-4 (deterministic boundary unit test) | Task 1, Step 1.1 (`Handle_FiltersMonthlyMarginsTo13MonthsBeforeInjectedUtcNow`) |
| FR-4 (timezone-sensitive case) | Task 1, Step 1.5 (`Handle_UsesUtcNotLocalTime_AtDayBoundary`) — uses arch-review amendment #2 phrasing |
| FR-4 (AAA pattern, no `FakeTimeProvider` dependency) | Step 1.1 / 1.5 follow Arrange-Act-Assert and use `Mock<TimeProvider>` per arch amendment #1 |
| NFR-4 (`dotnet build` + `dotnet format` pass) | Steps 1.8 and 1.9 |
| Arch-review prerequisite: create test file at exact path | Step 1.1 creates `backend/test/Anela.Heblo.Tests/Features/Catalog/GetProductMarginsHandlerTests.cs` per amendment #4 |

All spec requirements and arch amendments mapped to concrete steps.

**2. Placeholder scan:** No TBDs, no "implement later", no "similar to Task N", no missing code blocks. Every code edit shows literal before/after text and exact file path.

**3. Type consistency:**
- Constructor parameter order in test (`catalogRepository, timeProviderMock.Object, loggerMock.Object`) matches the constructor order shown in Step 1.3.
- Mock setup `tp => tp.GetUtcNow()` matches the call site `_timeProvider.GetUtcNow()` used in Step 1.3.
- `DateTimeOffset(utcNow, TimeSpan.Zero)` returned by the mock produces a UTC offset; `.DateTime` on that value yields the UTC clock reading — consistent with the expression `_timeProvider.GetUtcNow().DateTime.AddMonths(-13)`.
- Test fixture writes to `aggregate.Margins.MonthlyData` which is the `Dictionary<DateTime, MarginData>` that the handler reads at line 190 (`marginHistory.MonthlyData`). Keys and comparison semantics align.
- `ProductType.Product` matches the filter condition in the handler (line ~115).

No drift detected.
