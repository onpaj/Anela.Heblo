# Unit Test Coverage for GetIssuedInvoiceSyncStatsHandler Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Raise `GetIssuedInvoiceSyncStatsHandler` from 19.4% line coverage to full-branch coverage by adding a unit test class that pins date-range defaulting, explicit-date pass-through, the exception-to-structured-failure path, and happy-path field mapping — test-only, no production code changes.

**Architecture:** One new xUnit test class, `GetIssuedInvoiceSyncStatsHandlerTests`, in the existing `backend/test/Anela.Heblo.Tests/Features/Invoices/` folder, following the exact Moq + FluentAssertions pattern already used by the sibling `GetIssuedInvoiceDetailHandlerTests.cs`. Because the handler's production behavior is already correct and is not being changed, each task below writes a test asserting the handler's **current, correct** behavior and runs it to confirm it **passes** immediately (the usual "run to see it fail first" TDD step does not apply to a test-only coverage task — there is no new production code to make the test go from red to green). Each task adds one `[Fact]` method to the same growing file.

**Tech Stack:** .NET 8, xUnit, Moq, FluentAssertions — no new packages.

---

### task: setup-test-file

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs`

- [ ] **Step 1: Create the test file skeleton**

Create the file with the class scaffold, matching the constructor shape confirmed in the architecture review (2 dependencies only — `IIssuedInvoiceRepository` and `ILogger<GetIssuedInvoiceSyncStatsHandler>`):

```csharp
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoiceSyncStats;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class GetIssuedInvoiceSyncStatsHandlerTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repositoryMock;
    private readonly GetIssuedInvoiceSyncStatsHandler _handler;

    public GetIssuedInvoiceSyncStatsHandlerTests()
    {
        _repositoryMock = new Mock<IIssuedInvoiceRepository>();

        _handler = new GetIssuedInvoiceSyncStatsHandler(
            _repositoryMock.Object,
            Mock.Of<ILogger<GetIssuedInvoiceSyncStatsHandler>>());
    }
}
```

- [ ] **Step 2: Build to confirm the skeleton compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: `Build succeeded.` (0 errors) — an empty test class with no `[Fact]` methods is valid.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): scaffold GetIssuedInvoiceSyncStatsHandlerTests"
```

---

### task: date-defaulting-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body, after the constructor)

Covers spec FR-1 — this is the test that directly guards against the coverage-gap issue's stated risk (a sign flip or wrong date source silently shifting the reported window). Uses an exact `It.Is<DateTime>` predicate on both arguments, comparing `.Date` only, per arch-review Decision 1/2.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_BothDatesNull_DefaultsToTrailing30DayWindow()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = null,
            ToDate = null
        };
        var expectedFrom = DateTime.Now.Date.AddDays(-30);
        var expectedTo = DateTime.Now.Date;

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(
                It.Is<DateTime>(d => d.Date == expectedFrom),
                It.Is<DateTime>(d => d.Date == expectedTo),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedInvoiceSyncStats());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.GetSyncStatsAsync(
                It.Is<DateTime>(d => d.Date == expectedFrom),
                It.Is<DateTime>(d => d.Date == expectedTo),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_BothDatesNull_DefaultsToTrailing30DayWindow"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler date-range defaulting"
```

---

### task: explicit-dates-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body)

Covers spec FR-2 — confirms explicit request dates are passed through unchanged, not overwritten by the 30-day default.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_ExplicitDates_PassesThemThroughUnchanged()
    {
        // Arrange
        var explicitFrom = new DateTime(2026, 1, 5);
        var explicitTo = new DateTime(2026, 1, 20);
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = explicitFrom,
            ToDate = explicitTo
        };

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(explicitFrom, explicitTo, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IssuedInvoiceSyncStats());

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        _repositoryMock.Verify(
            r => r.GetSyncStatsAsync(explicitFrom, explicitTo, It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_ExplicitDates_PassesThemThroughUnchanged"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler explicit date pass-through"
```

---

### task: exception-path-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body)

Covers spec FR-3 — asserts the full structured-failure response shape, not just `Success == false`, including the exact `Params["ErrorMessage"]` Czech message, and confirms the handler does not rethrow.

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_RepositoryThrows_ReturnsStructuredFailure()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest();

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("repository failure"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.Exception);
        response.Params.Should().NotBeNull();
        response.Params.Should().ContainKey("ErrorMessage")
            .WhoseValue.Should().Be("Chyba při načítání statistik synchronizace faktur");
        response.TotalInvoices.Should().Be(0);
        response.SyncedInvoices.Should().Be(0);
        response.UnsyncedInvoices.Should().Be(0);
        response.InvoicesWithErrors.Should().Be(0);
        response.CriticalErrors.Should().Be(0);
        response.LastSyncTime.Should().BeNull();
        response.SyncSuccessRate.Should().Be(0);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_RepositoryThrows_ReturnsStructuredFailure"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler exception path"
```

---

### task: happy-path-mapping-test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs` (append inside the class body)

Covers spec FR-4 — asserts every response field is mapped one-to-one from the repository's `IssuedInvoiceSyncStats`, including the computed `SyncSuccessRate` (which has no setter on the domain type — `TotalInvoices`/`SyncedInvoices` are chosen so the computed rate is distinctive and easy to assert, per arch-review Risk row 3).

- [ ] **Step 1: Write the test**

Add this `[Fact]` inside the class body:

```csharp
    [Fact]
    public async Task Handle_RepositoryReturnsStats_MapsAllFieldsOntoResponse()
    {
        // Arrange
        var request = new GetIssuedInvoiceSyncStatsRequest
        {
            FromDate = new DateTime(2026, 2, 1),
            ToDate = new DateTime(2026, 2, 28)
        };
        var lastSync = new DateTime(2026, 2, 27, 14, 30, 0);
        var stats = new IssuedInvoiceSyncStats
        {
            TotalInvoices = 200,
            SyncedInvoices = 150,   // SyncSuccessRate = 150/200*100 = 75
            UnsyncedInvoices = 50,
            InvoicesWithErrors = 12,
            CriticalErrors = 3,
            LastSyncTime = lastSync
        };

        _repositoryMock
            .Setup(r => r.GetSyncStatsAsync(request.FromDate.Value, request.ToDate.Value, It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.TotalInvoices.Should().Be(200);
        response.SyncedInvoices.Should().Be(150);
        response.UnsyncedInvoices.Should().Be(50);
        response.InvoicesWithErrors.Should().Be(12);
        response.CriticalErrors.Should().Be(3);
        response.LastSyncTime.Should().Be(lastSync);
        response.SyncSuccessRate.Should().Be(75m);
    }
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests.Handle_RepositoryReturnsStats_MapsAllFieldsOntoResponse"`
Expected: `Passed! - Failed: 0, Passed: 1`

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/GetIssuedInvoiceSyncStatsHandlerTests.cs
git commit -m "test(invoices): cover GetIssuedInvoiceSyncStatsHandler happy-path field mapping"
```

---

### task: full-suite-verification

**Files:**
- None (verification only)

- [ ] **Step 1: Run the whole new test class together**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetIssuedInvoiceSyncStatsHandlerTests"`
Expected: `Passed! - Failed: 0, Passed: 4` (all four `[Fact]`s from the tasks above)

- [ ] **Step 2: Run `dotnet format` and the full backend build, per repo validation requirements**

Run:
```bash
cd backend
dotnet format --verify-no-changes || dotnet format
dotnet build
```
Expected: `Build succeeded.` with 0 errors; `dotnet format` reports no remaining changes needed (or applies them cleanly).

- [ ] **Step 3: Run the full backend test suite to confirm no regressions elsewhere**

Run: `cd backend && dotnet test`
Expected: all suites pass, including the four new `GetIssuedInvoiceSyncStatsHandlerTests` facts and the pre-existing `GetIssuedInvoiceDetailHandlerTests` / `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests` suites (unaffected, confirming no accidental production-code drift).
