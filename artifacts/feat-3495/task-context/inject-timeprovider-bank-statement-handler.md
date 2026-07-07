### task: inject-timeprovider-bank-statement-handler

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs:9-23`
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

  Create `backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs` with the following complete content:

  ```csharp
  using Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;
  using Anela.Heblo.Domain.Features.Analytics;
  using FluentAssertions;
  using Moq;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Analytics;

  public class GetBankStatementImportStatisticsHandlerTests
  {
      private readonly Mock<IAnalyticsRepository> _mockRepository;
      private readonly Mock<TimeProvider> _timeProviderMock;
      private readonly GetBankStatementImportStatisticsHandler _handler;
      private readonly DateTime _fixedDateTime = new DateTime(2025, 10, 14, 10, 0, 0, DateTimeKind.Utc);

      public GetBankStatementImportStatisticsHandlerTests()
      {
          _mockRepository = new Mock<IAnalyticsRepository>();
          _timeProviderMock = new Mock<TimeProvider>();
          _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(_fixedDateTime);
          _handler = new GetBankStatementImportStatisticsHandler(_mockRepository.Object, _timeProviderMock.Object);
      }

      [Fact]
      public async Task Handle_WithNoDatesProvided_UsesInjectedTimeProviderForDefaultRange()
      {
          // Arrange
          var request = new GetBankStatementImportStatisticsRequest();
          var expectedEndDate = DateTime.SpecifyKind(_fixedDateTime.Date, DateTimeKind.Utc);
          var expectedStartDate = DateTime.SpecifyKind(_fixedDateTime.Date.AddDays(-30), DateTimeKind.Utc);

          _mockRepository
              .Setup(r => r.GetBankStatementImportStatisticsAsync(
                  It.IsAny<DateTime>(),
                  It.IsAny<DateTime>(),
                  It.IsAny<BankStatementDateType>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<DailyBankStatementStatistics>());

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Should().NotBeNull();
          result.Success.Should().BeTrue();

          _timeProviderMock.Verify(x => x.GetUtcNow(), Times.Once);
          _mockRepository.Verify(r => r.GetBankStatementImportStatisticsAsync(
              expectedStartDate,
              expectedEndDate,
              request.DateType,
              It.IsAny<CancellationToken>()), Times.Once);
      }

      [Fact]
      public async Task Handle_WithExplicitDatesProvided_DoesNotConsultTimeProvider()
      {
          // Arrange
          var suppliedStartDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
          var suppliedEndDate = new DateTime(2024, 1, 31, 0, 0, 0, DateTimeKind.Utc);
          var request = new GetBankStatementImportStatisticsRequest
          {
              StartDate = suppliedStartDate,
              EndDate = suppliedEndDate,
              DateType = BankStatementDateType.ImportDate
          };

          _mockRepository
              .Setup(r => r.GetBankStatementImportStatisticsAsync(
                  It.IsAny<DateTime>(),
                  It.IsAny<DateTime>(),
                  It.IsAny<BankStatementDateType>(),
                  It.IsAny<CancellationToken>()))
              .ReturnsAsync(new List<DailyBankStatementStatistics>());

          // Act
          var result = await _handler.Handle(request, CancellationToken.None);

          // Assert
          result.Should().NotBeNull();
          result.Success.Should().BeTrue();

          _timeProviderMock.Verify(x => x.GetUtcNow(), Times.Never);
          _mockRepository.Verify(r => r.GetBankStatementImportStatisticsAsync(
              suppliedStartDate,
              suppliedEndDate,
              BankStatementDateType.ImportDate,
              It.IsAny<CancellationToken>()), Times.Once);
      }
  }
  ```

  This will not compile/pass yet: `GetBankStatementImportStatisticsHandler` does not currently have a constructor overload accepting `TimeProvider`, so `new GetBankStatementImportStatisticsHandler(_mockRepository.Object, _timeProviderMock.Object)` fails to compile against the current single-argument constructor.

- [ ] **Step 2: Confirm the test fails**

  Run:
  ```
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankStatementImportStatisticsHandlerTests"
  ```
  Expected outcome: build failure (CS1729 or similar — "does not contain a constructor that takes 2 arguments"), confirming the test currently fails because the production code has not yet been changed.

- [ ] **Step 3: Modify `GetBankStatementImportStatisticsHandler.cs`**

  In `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs`, replace lines 9-23:

  ```csharp
  public class GetBankStatementImportStatisticsHandler : IRequestHandler<GetBankStatementImportStatisticsRequest, GetBankStatementImportStatisticsResponse>
  {
      private readonly IAnalyticsRepository _analyticsRepository;

      public GetBankStatementImportStatisticsHandler(IAnalyticsRepository analyticsRepository)
      {
          _analyticsRepository = analyticsRepository;
      }

      public async Task<GetBankStatementImportStatisticsResponse> Handle(
          GetBankStatementImportStatisticsRequest request,
          CancellationToken cancellationToken)
      {
          // Set default date range if not provided (last 30 days)
          var endDate = request.EndDate ?? DateTime.UtcNow.Date;
  ```

  with:

  ```csharp
  public class GetBankStatementImportStatisticsHandler : IRequestHandler<GetBankStatementImportStatisticsRequest, GetBankStatementImportStatisticsResponse>
  {
      private readonly IAnalyticsRepository _analyticsRepository;
      private readonly TimeProvider _timeProvider;

      public GetBankStatementImportStatisticsHandler(IAnalyticsRepository analyticsRepository, TimeProvider timeProvider)
      {
          _analyticsRepository = analyticsRepository;
          _timeProvider = timeProvider;
      }

      public async Task<GetBankStatementImportStatisticsResponse> Handle(
          GetBankStatementImportStatisticsRequest request,
          CancellationToken cancellationToken)
      {
          // Set default date range if not provided (last 30 days)
          var endDate = request.EndDate ?? _timeProvider.GetUtcNow().Date;
  ```

  The rest of the file (lines 24-44: `startDate` derivation, the `DateTimeKind` normalization block, the repository call, and the response construction) remains exactly as-is — no other lines change. The full resulting file is:

  ```csharp
  using Anela.Heblo.Domain.Features.Analytics;
  using MediatR;

  namespace Anela.Heblo.Application.Features.Analytics.UseCases.GetBankStatementImportStatistics;

  /// <summary>
  /// Handler for getting bank statement import statistics for monitoring purposes
  /// </summary>
  public class GetBankStatementImportStatisticsHandler : IRequestHandler<GetBankStatementImportStatisticsRequest, GetBankStatementImportStatisticsResponse>
  {
      private readonly IAnalyticsRepository _analyticsRepository;
      private readonly TimeProvider _timeProvider;

      public GetBankStatementImportStatisticsHandler(IAnalyticsRepository analyticsRepository, TimeProvider timeProvider)
      {
          _analyticsRepository = analyticsRepository;
          _timeProvider = timeProvider;
      }

      public async Task<GetBankStatementImportStatisticsResponse> Handle(
          GetBankStatementImportStatisticsRequest request,
          CancellationToken cancellationToken)
      {
          // Set default date range if not provided (last 30 days)
          var endDate = request.EndDate ?? _timeProvider.GetUtcNow().Date;
          var startDate = request.StartDate ?? endDate.AddDays(-30);

          // Ensure dates are UTC for consistency
          if (startDate.Kind != DateTimeKind.Utc)
              startDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
          if (endDate.Kind != DateTimeKind.Utc)
              endDate = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

          // Get daily bank statement statistics from repository
          var dailyStatistics = await _analyticsRepository.GetBankStatementImportStatisticsAsync(
              startDate,
              endDate,
              request.DateType,
              cancellationToken);

          return new GetBankStatementImportStatisticsResponse
          {
              Statistics = dailyStatistics
          };
      }
  }
  ```

- [ ] **Step 4: Confirm the tests pass**

  Run:
  ```
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetBankStatementImportStatisticsHandlerTests"
  ```
  Expected outcome: both `Handle_WithNoDatesProvided_UsesInjectedTimeProviderForDefaultRange` and `Handle_WithExplicitDatesProvided_DoesNotConsultTimeProvider` pass.

  Also run the full Analytics test folder to confirm no regressions among sibling tests (e.g. `GetInvoiceImportStatisticsHandlerTests`, `InvoiceImportStatisticsTileTests`):
  ```
  dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Analytics"
  ```

- [ ] **Step 5: Final validation**

  Run, from `backend/`:
  ```
  dotnet build
  dotnet format
  ```
  Confirm `dotnet build` succeeds with no new errors/warnings, and `dotnet format` reports no unformatted files (or applies only whitespace formatting with no semantic changes) in the two touched files.

- [ ] **Step 6: Commit**

  Stage exactly the two touched files and commit:
  ```
  git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetBankStatementImportStatistics/GetBankStatementImportStatisticsHandler.cs backend/test/Anela.Heblo.Tests/Features/Analytics/GetBankStatementImportStatisticsHandlerTests.cs
  git commit -m "Inject TimeProvider into GetBankStatementImportStatisticsHandler

  Replaces the direct DateTime.UtcNow.Date call with the module's
  standard injected TimeProvider abstraction, matching the pattern
  already used by InvoiceImportStatisticsTile and TimeWindowParser.
  No behavior change in production; adds unit test coverage for the
  default-date branch.

  Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
  ```

## Spec Coverage Self-Review

- FR-1 (constructor takes `TimeProvider`, private readonly field, no DI registration changes) — covered by Step 3; DI registration untouched (no changes made to `ServiceCollectionExtensions.cs`).
- FR-2 (line 23 default-date swap only, `DateTimeKind` normalization and `startDate` derivation untouched) — covered by Step 3; diff is limited to the constructor and the single `endDate` assignment line.
- FR-3 (new test file, `Mock<TimeProvider>` pattern, fixed-date + pass-through test cases, all Analytics tests pass) — covered by Step 1 (test creation) and Step 4 (running the new tests plus the full Analytics folder).
- NFR-1/NFR-2/NFR-3 (no perf/security impact, behavior-preserving in production) — no additional code introduced beyond the swap; `TimeProvider.System` wraps the real clock, so production behavior is identical.
- Data Model / API sections (no changes) — confirmed no changes made to `GetBankStatementImportStatisticsRequest`, `GetBankStatementImportStatisticsResponse`, `DailyBankStatementStatistics`, or the controller.
- Out of Scope items (`GetInvoiceImportStatisticsHandler`, 30-day window, normalization logic, repository signature, `TimeWindowParser`/`InvoiceImportStatisticsTile`, DI registration, frontend) — none touched by this plan.
