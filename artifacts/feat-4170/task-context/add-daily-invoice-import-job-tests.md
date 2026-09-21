### task: add-daily-invoice-import-job-tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBaseTests.cs`
- Read (no changes): `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBase.cs`
- Read (no changes): `backend/src/Anela.Heblo.Application/Features/Invoices/Services/IInvoiceImportService.cs`
- Read (no changes): `backend/src/Anela.Heblo.Application/Features/Invoices/Contracts/ImportResultDto.cs`
- Read (no changes): `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobStatusChecker.cs`
- Reference pattern: `backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs`
- Reference pattern (logger-mock verification): `backend/test/Anela.Heblo.Tests/Adapters/ShoptetApi/ShippingMethodMapperTests.cs`

- [ ] **Step 1: Create the test file skeleton with the test-double class and shared fixtures**

Create `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBaseTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.Invoices.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure.Jobs;

public sealed class DailyInvoiceImportJobBaseTests
{
    private const string TestJobName = "test-daily-invoice-import-job";
    private const string TestCurrency = "EUR";

    private readonly Mock<IInvoiceImportService> _importService = new();
    private readonly Mock<IRecurringJobStatusChecker> _statusChecker = new();

    public DailyInvoiceImportJobBaseTests()
    {
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(true);
        _importService
            .Setup(s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResultDto());
    }

    private TestDailyInvoiceImportJob CreateJob(ILoggerFactory? loggerFactory = null) => new(
        _importService.Object,
        loggerFactory ?? NullLoggerFactory.Instance,
        _statusChecker.Object,
        TestJobName,
        TestCurrency);

    private sealed class TestDailyInvoiceImportJob : DailyInvoiceImportJobBase
    {
        public TestDailyInvoiceImportJob(
            IInvoiceImportService invoiceImportService,
            ILoggerFactory loggerFactory,
            IRecurringJobStatusChecker statusChecker,
            string jobName,
            string currency)
            : base(invoiceImportService, loggerFactory, statusChecker)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = "Test Daily Invoice Import Job",
                Description = "Test job for DailyInvoiceImportJobBase",
                CronExpression = "0 0 * * *",
                DefaultIsEnabled = true,
            };
            Currency = currency;
        }

        public override RecurringJobMetadata Metadata { get; }
        protected override string Currency { get; }
    }
}
```

This compiles and has zero `[Fact]` methods yet — it's the scaffold the remaining steps build on.

- [ ] **Step 2: Run the build to confirm the skeleton compiles**

Run: `cd backend && dotnet build`
Expected: Build succeeds (0 errors). If `IssuedInvoiceSourceQuery`'s fully-qualified name causes a compile error, add `using Anela.Heblo.Domain.Features.Invoices;` and use the short name instead of the fully-qualified reference in the `Setup` call.

- [ ] **Step 3: Write and run the disabled-job short-circuit test (FR-1)**

Add inside `DailyInvoiceImportJobBaseTests`:

```csharp
    [Fact]
    public async Task ExecuteAsync_ReturnsEarly_WhenJobIsDisabled()
    {
        _statusChecker
            .Setup(c => c.IsJobEnabledAsync(TestJobName, It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(false);

        await CreateJob().ExecuteAsync(CancellationToken.None);

        _importService.Verify(
            s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DailyInvoiceImportJobBaseTests.ExecuteAsync_ReturnsEarly_WhenJobIsDisabled"`
Expected: PASS (1 test passed).

- [ ] **Step 4: Write and run the partial-failure warning test (FR-2)**

Add inside `DailyInvoiceImportJobBaseTests`:

```csharp
    [Fact]
    public async Task ExecuteAsync_LogsWarning_AndCompletes_WhenSomeInvoicesFail()
    {
        _importService
            .Setup(s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportResultDto
            {
                Succeeded = new List<string> { "INV-001" },
                Failed = new List<string> { "INV-002" }
            });

        var loggerMock = new Mock<ILogger>();
        var loggerFactoryMock = new Mock<ILoggerFactory>();
        loggerFactoryMock.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(loggerMock.Object);

        var act = () => CreateJob(loggerFactoryMock.Object).ExecuteAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
```

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DailyInvoiceImportJobBaseTests.ExecuteAsync_LogsWarning_AndCompletes_WhenSomeInvoicesFail"`
Expected: PASS (1 test passed). If the `Log` verification fails to match, double check `LogLevel.Warning` is the only warning-level call in the run (the base class's `LogInformation` calls are `LogLevel.Information` and won't match `Times.Once` against `Warning`, so this should pass as written — if it doesn't, inspect whether `ImportInvoicesAsync`'s mocked description string argument matters, which it doesn't for this assertion).

- [ ] **Step 5: Write and run the exception-rethrow test (FR-3)**

Add inside `DailyInvoiceImportJobBaseTests`:

```csharp
    [Fact]
    public async Task ExecuteAsync_Rethrows_WhenImportServiceThrows()
    {
        _importService
            .Setup(s => s.ImportInvoicesAsync(It.IsAny<string>(), It.IsAny<Anela.Heblo.Domain.Features.Invoices.IssuedInvoiceSourceQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("import service unavailable"));

        var act = () => CreateJob().ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("import service unavailable");
    }
```

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DailyInvoiceImportJobBaseTests.ExecuteAsync_Rethrows_WhenImportServiceThrows"`
Expected: PASS (1 test passed).

- [ ] **Step 6: Run the full new test class together**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DailyInvoiceImportJobBaseTests"`
Expected: PASS (3 tests passed, 0 failed).

- [ ] **Step 7: Run the full backend test suite to confirm no regressions**

Run: `cd backend && dotnet build && dotnet format --verify-no-changes`
Expected: Build succeeds; `dotnet format` reports no changes needed (if it reports formatting issues, run `dotnet format` without `--verify-no-changes` to apply them, then re-verify).

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: All tests pass, including the 3 new ones and every pre-existing test (no regressions).

- [ ] **Step 8: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/Jobs/DailyInvoiceImportJobBaseTests.cs
git commit -m "test(invoices): cover DailyInvoiceImportJobBase disabled/partial-failure/rethrow paths

Closes #4170"
```

## Self-Review

**Spec coverage:**
- FR-1 (disabled short-circuit) → Step 3 test `ExecuteAsync_ReturnsEarly_WhenJobIsDisabled`.
- FR-2 (partial-failure warning) → Step 4 test `ExecuteAsync_LogsWarning_AndCompletes_WhenSomeInvoicesFail`.
- FR-3 (exception rethrow) → Step 5 test `ExecuteAsync_Rethrows_WhenImportServiceThrows`.
- FR-4 (test double for the abstract base class) → Step 1's `TestDailyInvoiceImportJob`.
- Out of Scope items (no production code changes, no DI/Hangfire wiring tests, no currency-specific job tests) — respected; nothing in this plan touches any file outside the one new test file.

**Placeholder scan:** No "TBD"/"TODO"/"implement later" markers; every step has complete, runnable code and exact commands with expected output.

**Type consistency:** `TestDailyInvoiceImportJob`'s constructor signature (Step 1) is used identically by `CreateJob(...)` and by every test (Steps 3-5) — no drift. `TestJobName`/`TestCurrency` constants are defined once (Step 1) and reused everywhere rather than re-typed as literals.
