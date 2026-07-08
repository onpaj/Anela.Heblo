# Task Plan: Inject TimeProvider into GetInvoiceImportStatisticsHandler

## Overview
Replace the direct `DateTime.UtcNow` call in `GetInvoiceImportStatisticsHandler` with an injected `TimeProvider`, matching the pattern already used by `GetBankStatementImportStatisticsHandler`, and update the handler's unit tests to supply a mocked, fixed `TimeProvider` instead of relying on wall-clock time.

### task: inject-timeprovider-into-handler-and-update-tests

**Goal:** `GetInvoiceImportStatisticsHandler` computes `endDate` from an injected `TimeProvider` instead of `DateTime.UtcNow`, and its unit tests are updated to construct the handler with a mocked `TimeProvider` and assert against a fixed, deterministic date.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs`

**Context for the engineer:**
- The repo root is `/home/user/worktrees/feature-3488-Arch-Review-Analytics-Getinvoiceimportstatisticsha` (this is the working directory for all commands below).
- The solution file is `Anela.Heblo.sln` at the repo root.
- `TimeProvider` (BCL, .NET 8) is already registered in DI as a singleton (`services.AddSingleton(TimeProvider.System)` in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`), and is already consumed the same way by the sibling handler `GetBankStatementImportStatisticsHandler` (`backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs`) and its test `GetBankStatementImportStatisticsHandlerTests.cs`. No DI registration changes are needed in this task.
- A build-wide grep confirms there are exactly 4 call sites constructing `GetInvoiceImportStatisticsHandler` directly (`new GetInvoiceImportStatisticsHandler(`), and all 4 are in the test file `GetInvoiceImportStatisticsHandlerTests.cs` (lines 23, 69, 94, 153 as of this writing). MediatR resolves the handler itself via assembly scanning, so there is no other production call site to update.

- [ ] Step 1: Open `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs`. Its current full contents are:
```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;

/// <summary>
/// Handler for getting invoice import statistics for monitoring purposes
/// </summary>
public class GetInvoiceImportStatisticsHandler : IRequestHandler<GetInvoiceImportStatisticsRequest, GetInvoiceImportStatisticsResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly InvoiceImportOptions _options;

    public GetInvoiceImportStatisticsHandler(
        IAnalyticsRepository analyticsRepository,
        IOptions<InvoiceImportOptions> invoiceImportOptions)
    {
        _analyticsRepository = analyticsRepository;
        _options = invoiceImportOptions.Value;
    }

    public async Task<GetInvoiceImportStatisticsResponse> Handle(
        GetInvoiceImportStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        // Get configuration values
        var minimumThreshold = _options.MinimumDailyThreshold;
        var defaultDaysBack = _options.DefaultDaysBack;

        // Use provided days back or default from configuration
        var daysBack = request.DaysBack ?? defaultDaysBack;

        // Calculate date range - work with UTC dates for consistency
        // Repository will handle conversion to Unspecified for PostgreSQL timestamp without time zone
        var endDate = DateTime.UtcNow.Date;
        endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var startDate = endDate.AddDays(-daysBack);
        startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

        // Get daily invoice counts from repository
        var dailyCounts = await _analyticsRepository.GetInvoiceImportStatisticsAsync(
            startDate,
            endDate,
            request.DateType,
            cancellationToken);

        // Project to DTOs, marking days below threshold as problematic
        var data = dailyCounts.Select(c => new DailyInvoiceCountDto
        {
            Date = c.Date,
            Count = c.Count,
            IsBelowThreshold = c.Count < minimumThreshold
        }).ToList();

        return new GetInvoiceImportStatisticsResponse
        {
            Data = data,
            MinimumThreshold = minimumThreshold
        };
    }
}
```
Replace the whole file with:
```csharp
using Anela.Heblo.Application.Features.Analytics.Contracts;
using Anela.Heblo.Domain.Features.Analytics;
using MediatR;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;

/// <summary>
/// Handler for getting invoice import statistics for monitoring purposes
/// </summary>
public class GetInvoiceImportStatisticsHandler : IRequestHandler<GetInvoiceImportStatisticsRequest, GetInvoiceImportStatisticsResponse>
{
    private readonly IAnalyticsRepository _analyticsRepository;
    private readonly InvoiceImportOptions _options;
    private readonly TimeProvider _timeProvider;

    public GetInvoiceImportStatisticsHandler(
        IAnalyticsRepository analyticsRepository,
        IOptions<InvoiceImportOptions> invoiceImportOptions,
        TimeProvider timeProvider)
    {
        _analyticsRepository = analyticsRepository;
        _options = invoiceImportOptions.Value;
        _timeProvider = timeProvider;
    }

    public async Task<GetInvoiceImportStatisticsResponse> Handle(
        GetInvoiceImportStatisticsRequest request,
        CancellationToken cancellationToken)
    {
        // Get configuration values
        var minimumThreshold = _options.MinimumDailyThreshold;
        var defaultDaysBack = _options.DefaultDaysBack;

        // Use provided days back or default from configuration
        var daysBack = request.DaysBack ?? defaultDaysBack;

        // Calculate date range - work with UTC dates for consistency
        // Repository will handle conversion to Unspecified for PostgreSQL timestamp without time zone
        var endDate = _timeProvider.GetUtcNow().Date;
        endDate = DateTime.SpecifyKind(endDate, DateTimeKind.Utc);

        var startDate = endDate.AddDays(-daysBack);
        startDate = DateTime.SpecifyKind(startDate, DateTimeKind.Utc);

        // Get daily invoice counts from repository
        var dailyCounts = await _analyticsRepository.GetInvoiceImportStatisticsAsync(
            startDate,
            endDate,
            request.DateType,
            cancellationToken);

        // Project to DTOs, marking days below threshold as problematic
        var data = dailyCounts.Select(c => new DailyInvoiceCountDto
        {
            Date = c.Date,
            Count = c.Count,
            IsBelowThreshold = c.Count < minimumThreshold
        }).ToList();

        return new GetInvoiceImportStatisticsResponse
        {
            Data = data,
            MinimumThreshold = minimumThreshold
        };
    }
}
```
The only functional changes are: the new `private readonly TimeProvider _timeProvider;` field, the new third constructor parameter `TimeProvider timeProvider` with its assignment `_timeProvider = timeProvider;`, and replacing `var endDate = DateTime.UtcNow.Date;` with `var endDate = _timeProvider.GetUtcNow().Date;`. Everything else is byte-for-byte identical.

- [ ] Step 2: Confirm the build now fails only in the test project (expected, since the constructor signature changed but the test file hasn't been updated yet). From the repo root, run:
```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: build fails with CS7036 ("There is no argument given that corresponds to the required formal parameter 'timeProvider'") at the 4 call sites in `GetInvoiceImportStatisticsHandlerTests.cs` (lines 23, 69, 94, 153). This confirms the constructor change took effect and that these are the only call sites needing updates.

- [ ] Step 3: Open `backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs`. Replace its entire contents with:
```csharp
using Anela.Heblo.Application.Features.Analytics;
using Anela.Heblo.Application.Features.Analytics.UseCases.GetInvoiceImportStatistics;
using Anela.Heblo.Domain.Features.Analytics;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Analytics;

public class GetInvoiceImportStatisticsHandlerTests
{
    private readonly Mock<IAnalyticsRepository> _mockRepository;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetInvoiceImportStatisticsHandler _handler;
    private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 14, 10, 0, 0, DateTimeKind.Utc);

    public GetInvoiceImportStatisticsHandlerTests()
    {
        _mockRepository = new Mock<IAnalyticsRepository>();
        _timeProviderMock = new Mock<TimeProvider>();
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);
        var options = Options.Create(new InvoiceImportOptions
        {
            MinimumDailyThreshold = 10,
            DefaultDaysBack = 14
        });
        _handler = new GetInvoiceImportStatisticsHandler(_mockRepository.Object, options, _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_ShouldReturnStatisticsWithMinimumThreshold()
    {
        // Arrange
        var request = new GetInvoiceImportStatisticsRequest
        {
            DateType = ImportDateType.InvoiceDate,
            DaysBack = 14
        };

        var expectedThreshold = 10;
        var baseDate = _fixedDateTime.Date;
        var expectedData = new List<DailyInvoiceCount>
        {
            new() { Date = DateTime.SpecifyKind(baseDate.AddDays(-1), DateTimeKind.Utc), Count = 15 },
            new() { Date = DateTime.SpecifyKind(baseDate, DateTimeKind.Utc), Count = 5 } // IsBelowThreshold will be set by handler
        };

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                ImportDateType.InvoiceDate,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(expectedThreshold, result.MinimumThreshold);
        Assert.Equal(2, result.Data.Count);

        // Verify threshold logic is applied
        Assert.False(result.Data[0].IsBelowThreshold); // 15 >= 10
        Assert.True(result.Data[1].IsBelowThreshold);  // 5 < 10
    }

    [Fact]
    public async Task Handle_ShouldUseDefaultThresholdWhenNotConfigured()
    {
        // Arrange - Create handler with empty configuration
        var handlerWithEmptyConfig = new GetInvoiceImportStatisticsHandler(
            _mockRepository.Object,
            Options.Create(new InvoiceImportOptions()),
            _timeProviderMock.Object);

        var request = new GetInvoiceImportStatisticsRequest();
        var expectedData = new List<DailyInvoiceCount>();

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ImportDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await handlerWithEmptyConfig.Handle(request, CancellationToken.None);

        // Assert
        Assert.Equal(10, result.MinimumThreshold);
    }

    [Fact]
    public async Task Handle_ShouldUseConfigurableDefaultDaysBack()
    {
        // Arrange - Create handler with custom default days back
        var handlerWithCustomConfig = new GetInvoiceImportStatisticsHandler(
            _mockRepository.Object,
            Options.Create(new InvoiceImportOptions { DefaultDaysBack = 30 }),
            _timeProviderMock.Object);

        var request = new GetInvoiceImportStatisticsRequest(); // DaysBack = null to use default
        var expectedData = new List<DailyInvoiceCount>();

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ImportDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedData);

        // Act
        var result = await handlerWithCustomConfig.Handle(request, CancellationToken.None);

        // Assert - Verify that 30 days range was used by checking repository call
        // Implementation uses: startDate = endDate.AddDays(-daysBack), endDate = _timeProvider.GetUtcNow().Date
        var expectedEndDate = _fixedDateTime.Date;
        var expectedStartDate = expectedEndDate.AddDays(-30);

        _mockRepository.Verify(r => r.GetInvoiceImportStatisticsAsync(
            It.Is<DateTime>(d => d.Date == expectedStartDate), // Should be 30 days before the fixed date
            It.Is<DateTime>(d => d.Date == expectedEndDate), // Should be the fixed date
            It.IsAny<ImportDateType>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(ImportDateType.InvoiceDate)]
    [InlineData(ImportDateType.LastSyncTime)]
    public async Task Handle_ShouldPassCorrectDateTypeToRepository(ImportDateType dateType)
    {
        // Arrange
        var request = new GetInvoiceImportStatisticsRequest { DateType = dateType };

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                dateType,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyInvoiceCount>());

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _mockRepository.Verify(r => r.GetInvoiceImportStatisticsAsync(
            It.IsAny<DateTime>(),
            It.IsAny<DateTime>(),
            dateType,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless()
    {
        // Arrange
        var handlerWithDefaults = new GetInvoiceImportStatisticsHandler(
            _mockRepository.Object,
            Options.Create(new InvoiceImportOptions()),
            _timeProviderMock.Object);

        _mockRepository.Setup(r => r.GetInvoiceImportStatisticsAsync(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<ImportDateType>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyInvoiceCount>());

        var request = new GetInvoiceImportStatisticsRequest();

        // Act
        var result = await handlerWithDefaults.Handle(request, CancellationToken.None);

        // Assert - defaults are 10 and 14
        Assert.Equal(10, result.MinimumThreshold);
        _mockRepository.Verify(r => r.GetInvoiceImportStatisticsAsync(
            It.Is<DateTime>(d => d.Date == _fixedDateTime.Date.AddDays(-14)),
            It.IsAny<DateTime>(),
            It.IsAny<ImportDateType>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
```
Summary of changes made in this step (for review purposes):
- Added `private readonly Mock<TimeProvider> _timeProviderMock;` and `private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 14, 10, 0, 0, DateTimeKind.Utc);` fields.
- In the constructor, initialize `_timeProviderMock` and stub `GetUtcNow()` to return `_fixedDateTime`, then pass `_timeProviderMock.Object` as the third argument to `_handler`'s construction.
- Pass `_timeProviderMock.Object` as the third argument at the other 3 ad-hoc construction sites (`Handle_ShouldUseDefaultThresholdWhenNotConfigured`, `Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`).
- In `Handle_ShouldReturnStatisticsWithMinimumThreshold`, `baseDate` now comes from `_fixedDateTime.Date` instead of `DateTime.UtcNow.Date` (this test doesn't assert on the exact dates passed to the repository, but removing the wall-clock read keeps the test fully deterministic).
- In `Handle_ShouldUseConfigurableDefaultDaysBack`, `expectedEndDate` now comes from `_fixedDateTime.Date` instead of `DateTime.UtcNow.Date`.
- In `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`, the `Verify` assertion now checks `d.Date == _fixedDateTime.Date.AddDays(-14)` instead of `d.Date == DateTime.UtcNow.Date.AddDays(-14)`.

- [ ] Step 4: Confirm there is no remaining reference to `DateTime.UtcNow` or `DateTime.Now` in either file. From the repo root, run:
```bash
grep -n "DateTime.UtcNow\|DateTime.Now" backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs
```
Expected: no output (no matches) from either file.

- [ ] Step 5: Build the backend solution. From the repo root, run:
```bash
dotnet build Anela.Heblo.sln
```
Expected: `Build succeeded.` with 0 errors.

- [ ] Step 6: Run the updated test file. From the repo root, run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests"
```
Expected: all 6 tests pass (`Handle_ShouldReturnStatisticsWithMinimumThreshold`, `Handle_ShouldUseDefaultThresholdWhenNotConfigured`, `Handle_ShouldUseConfigurableDefaultDaysBack`, `Handle_ShouldPassCorrectDateTypeToRepository` x2 theory cases, `Handle_ShouldUseDefaultValuesWhenOptionsAreParameterless`), 0 failed.

- [ ] Step 7: Run the full test project to confirm no other test was broken by the constructor change. From the repo root, run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```
Expected: all tests pass, 0 failed.

- [ ] Step 8: Format the code to match repository conventions. From the repo root, run:
```bash
dotnet format Anela.Heblo.sln
```
Expected: exits successfully; if it modifies either of the two files, re-run Step 5 and Step 6 to confirm the build and tests still pass after formatting.

- [ ] Step 9: Stage and commit the change. From the repo root, run:
```bash
git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetInvoiceImportStatistics/GetInvoiceImportStatisticsHandler.cs backend/test/Anela.Heblo.Tests/Features/Analytics/GetInvoiceImportStatisticsHandlerTests.cs
git commit -m "Inject TimeProvider into GetInvoiceImportStatisticsHandler

Replaces the direct DateTime.UtcNow call with the module's standard
injected TimeProvider, matching GetBankStatementImportStatisticsHandler,
TimeWindowParser, and InvoiceImportStatisticsTile. Updates the handler's
unit tests to use a mocked, fixed TimeProvider instead of asserting
against wall-clock time."
```
Expected: commit succeeds; `git status` shows a clean working tree (no unstaged changes) for these two files.
