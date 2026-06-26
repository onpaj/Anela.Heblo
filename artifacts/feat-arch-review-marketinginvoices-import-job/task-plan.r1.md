# Refactor MarketingInvoices Import Jobs to Use MediatR and DI — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce `MetaAdsInvoiceImportJob` and `GoogleAdsInvoiceImportJob` to thin MediatR dispatchers backed by a new `ImportMarketingInvoicesCommand` use case, with `MarketingInvoiceImportService` and the transaction sources fully DI-managed.

**Architecture:** A new Application-layer slice (`ImportMarketingInvoices`) holds a MediatR request/response/handler. The handler injects `IEnumerable<IMarketingTransactionSource>`, selects the source whose `Platform` matches the request, and delegates to the now DI-registered `MarketingInvoiceImportService`. The service stops taking the source in its constructor — the source is passed per-call to `ImportAsync`. Each adapter forwards its concrete source to the `IMarketingTransactionSource` interface so the handler's enumerable resolves correctly. Both jobs drop their service-construction logic and inject only `IMediator`, `IRecurringJobStatusChecker`, and a logger — matching `DailyConsumptionJob`.

**Tech Stack:** .NET 8, C#, MediatR (auto-scanned by `ApplicationModule`), `Microsoft.Extensions.DependencyInjection`, xUnit + Moq for tests.

---

## Design Notes (read before starting)

These constraints come from the architecture review and are non-negotiable:

- **The handler must NOT swallow import exceptions.** Do not copy `ProcessDailyConsumptionHandler`'s catch-all idiom. Exceptions from `ImportAsync` must propagate so the job's `catch → log → throw` re-surfaces them to Hangfire for retry. Wrapping them into `Success = false` is a behavior regression.
- **Unknown / duplicate platform → throw.** `ArgumentException` for zero matches, `InvalidOperationException` for more than one. Throw before any import work.
- **The service takes the source per-call, not per-construction.** Two implementations are registered against `IMarketingTransactionSource`; a constructor-injected source would resolve ambiguously. The handler knows the platform, so the handler selects the source and hands it to `ImportAsync`.
- **Platform literal is single-sourced.** Each `*TransactionSource` exposes `public const string PlatformName`, and `Platform => PlatformName`. The job references the const; it never constructs or injects the source.
- `ImportMarketingInvoicesResponse` inherits `BaseResponse` per project convention, but the returned path is always `Success = true` — failures are exceptions.
- DTOs are classes, never records (project rule).

## File Structure

**Created:**
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesRequest.cs` — MediatR request DTO (`Platform`, `From`, `To`).
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesResponse.cs` — response DTO inheriting `BaseResponse`.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs` — selects source, runs import, maps result.
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs` — handler tests.
- `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsInvoiceImportJobTests.cs` — Meta job tests.
- `backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsInvoiceImportJobTests.cs` — Google job tests.

**Modified:**
- `backend/src/Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs` — add `PlatformName` const.
- `backend/src/Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs` — add `PlatformName` const.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — source moves from ctor to `ImportAsync` parameter.
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — adapt to new service signature.
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs` — register `MarketingInvoiceImportService` as scoped.
- `backend/src/Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs` — forward `IMarketingTransactionSource`.
- `backend/src/Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs` — forward `IMarketingTransactionSource`.
- `backend/src/Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs` — reduce to MediatR dispatcher.
- `backend/src/Anela.Heblo.Adapters.GoogleAds/GoogleAdsInvoiceImportJob.cs` — reduce to MediatR dispatcher.

> **Note on paths:** The repository may root adapter projects either at `backend/src/Anela.Heblo.Adapters.MetaAds/` or `backend/src/Adapters/Anela.Heblo.Adapters.MetaAds/`. Confirm the actual location once (`ls backend/src` / `ls backend/src/Adapters`) and use it consistently. The test-project `ProjectReference` entries point at `..\..\src\Adapters\Anela.Heblo.Adapters.MetaAds\`, so the `Adapters/` subfolder is the likely root for source projects.

---

## Task 1: Add `PlatformName` const to both transaction sources

Single-sources the platform literal so the jobs can reference a compile-time constant without constructing the source.

**Files:**
- Modify: `backend/src/.../Anela.Heblo.Adapters.MetaAds/MetaAdsTransactionSource.cs`
- Modify: `backend/src/.../Anela.Heblo.Adapters.GoogleAds/GoogleAdsTransactionSource.cs`

- [ ] **Step 1: Add the const to `MetaAdsTransactionSource`**

Replace the existing line:

```csharp
    public string Platform => "MetaAds";
```

with:

```csharp
    public const string PlatformName = "MetaAds";

    public string Platform => PlatformName;
```

- [ ] **Step 2: Add the const to `GoogleAdsTransactionSource`**

Replace the existing line:

```csharp
    public string Platform => "GoogleAds";
```

with:

```csharp
    public const string PlatformName = "GoogleAds";

    public string Platform => PlatformName;
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded (use the actual solution path; substitute if the solution file name differs).

- [ ] **Step 4: Run the existing source tests to confirm `Platform` behavior is unchanged**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~TransactionSourceTests"`
Expected: PASS — `MetaAdsTransactionSourceTests` and `GoogleAdsTransactionSourceTests` still green.

- [ ] **Step 5: Commit**

```bash
git add backend/src
git commit -m "refactor: single-source platform literal on marketing transaction sources"
```

---

## Task 2: Move the transaction source from the service constructor to `ImportAsync`

The service becomes a stateless, DI-friendly scoped service. The source flows as a method argument because the handler — not DI — knows which platform was requested.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

- [ ] **Step 1: Update the existing tests to the new signature (RED)**

Replace the full contents of `MarketingInvoiceImportServiceTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class MarketingInvoiceImportServiceTests
{
    private readonly Mock<IMarketingTransactionSource> _mockSource;
    private readonly Mock<IImportedMarketingTransactionRepository> _mockRepository;
    private readonly Mock<ILogger<MarketingInvoiceImportService>> _mockLogger;
    private readonly MarketingInvoiceImportService _service;

    public MarketingInvoiceImportServiceTests()
    {
        _mockSource = new Mock<IMarketingTransactionSource>();
        _mockRepository = new Mock<IImportedMarketingTransactionRepository>();
        _mockLogger = new Mock<ILogger<MarketingInvoiceImportService>>();

        _mockSource.Setup(x => x.Platform).Returns("TestPlatform");

        _service = new MarketingInvoiceImportService(
            _mockRepository.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task ImportAsync_NewTransactions_ArePersistedAndCounted()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Platform = "TestPlatform", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ImportAsync_DuplicateTransaction_IsSkipped()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", "TX-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Platform = "TestPlatform", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Platform = "TestPlatform", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-001"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-002"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB write failed"));

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"`
Expected: BUILD FAILURE — `MarketingInvoiceImportService` constructor still takes 3 args; `ImportAsync` still takes 2 args.

- [ ] **Step 3: Rewrite `MarketingInvoiceImportService` to take the source per-call**

Replace the full contents of `MarketingInvoiceImportService.cs` with:

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public class MarketingInvoiceImportService
{
    private readonly IImportedMarketingTransactionRepository _repository;
    private readonly ILogger<MarketingInvoiceImportService> _logger;

    public MarketingInvoiceImportService(
        IImportedMarketingTransactionRepository repository,
        ILogger<MarketingInvoiceImportService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<MarketingImportResult> ImportAsync(
        IMarketingTransactionSource source,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting marketing invoice import for platform {Platform} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            source.Platform, from, to);

        var transactions = await source.GetTransactionsAsync(from, to, ct);

        var result = new MarketingImportResult();

        foreach (var transaction in transactions)
        {
            try
            {
                var exists = await _repository.ExistsAsync(source.Platform, transaction.TransactionId, ct);
                if (exists)
                {
                    _logger.LogDebug(
                        "Transaction {TransactionId} for {Platform} already imported — skipping",
                        transaction.TransactionId, source.Platform);
                    result.Skipped++;
                    continue;
                }

                var entity = new ImportedMarketingTransaction
                {
                    TransactionId = transaction.TransactionId,
                    Platform = source.Platform,
                    Amount = transaction.Amount,
                    TransactionDate = transaction.TransactionDate,
                    ImportedAt = DateTime.UtcNow,
                    IsSynced = false,
                };

                await _repository.AddAsync(entity, ct);
                await _repository.SaveChangesAsync(ct);

                result.Imported++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to import transaction {TransactionId} for {Platform}",
                    transaction.TransactionId, source.Platform);
                result.Failed++;
            }
        }

        _logger.LogInformation(
            "Marketing invoice import complete for {Platform}: Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
            source.Platform, result.Imported, result.Skipped, result.Failed);

        return result;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"`
Expected: PASS — all 3 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
git commit -m "refactor: pass transaction source to ImportAsync instead of constructor"
```

> Note: the build of `MetaAdsInvoiceImportJob` / `GoogleAdsInvoiceImportJob` is now broken (they still `new` the service with the old signature). They are fixed in Tasks 7 and 8. If your runner builds the whole solution between tasks, expect those two files to fail until then — that is intentional sequencing.

---

## Task 3: Register `MarketingInvoiceImportService` with DI

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs`

- [ ] **Step 1: Add the scoped registration**

Replace the full contents of `MarketingInvoicesModule.cs` with:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Features.MarketingInvoices;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.MarketingInvoices;

public static class MarketingInvoicesModule
{
    public static IServiceCollection AddMarketingInvoicesModule(this IServiceCollection services)
    {
        services.AddScoped<IImportedMarketingTransactionRepository, ImportedMarketingTransactionRepository>();
        services.AddScoped<MarketingInvoiceImportService>();

        return services;
    }
}
```

- [ ] **Step 2: Build the Application project**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs
git commit -m "refactor: register MarketingInvoiceImportService as scoped"
```

---

## Task 4: Create the `ImportMarketingInvoices` request and response DTOs

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesRequest.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesResponse.cs`

- [ ] **Step 1: Create the request DTO**

Create `ImportMarketingInvoicesRequest.cs`:

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;

public class ImportMarketingInvoicesRequest : IRequest<ImportMarketingInvoicesResponse>
{
    public string Platform { get; set; } = string.Empty;
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
```

- [ ] **Step 2: Create the response DTO**

Create `ImportMarketingInvoicesResponse.cs`:

```csharp
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;

public class ImportMarketingInvoicesResponse : BaseResponse
{
    public string Platform { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

- [ ] **Step 3: Build the Application project**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices
git commit -m "feat: add ImportMarketingInvoices request/response DTOs"
```

---

## Task 5: Create the `ImportMarketingInvoicesHandler`

The handler selects the source by platform, runs the import, and maps the result. It deliberately does NOT catch import exceptions (Design Notes).

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs`

- [ ] **Step 1: Write the failing handler tests (RED)**

Create `ImportMarketingInvoicesHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class ImportMarketingInvoicesHandlerTests
{
    private readonly Mock<IImportedMarketingTransactionRepository> _mockRepository = new();

    private MarketingInvoiceImportService CreateService() =>
        new(_mockRepository.Object, NullLogger<MarketingInvoiceImportService>.Instance);

    private ImportMarketingInvoicesHandler CreateHandler(IEnumerable<IMarketingTransactionSource> sources) =>
        new(sources, CreateService(), NullLogger<ImportMarketingInvoicesHandler>.Instance);

    private static Mock<IMarketingTransactionSource> SourceFor(string platform)
    {
        var mock = new Mock<IMarketingTransactionSource>();
        mock.Setup(s => s.Platform).Returns(platform);
        return mock;
    }

    [Fact]
    public async Task Handle_SelectsSourceMatchingPlatform_AndMapsResult()
    {
        // Arrange
        var from = new DateTime(2026, 5, 1);
        var to = new DateTime(2026, 5, 8);

        var meta = SourceFor("MetaAds");
        meta.Setup(s => s.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingTransaction>
            {
                new() { TransactionId = "TX-1", Platform = "MetaAds", Amount = 10m, TransactionDate = from },
            });

        var google = SourceFor("GoogleAds");

        _mockRepository.Setup(r => r.ExistsAsync("MetaAds", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _mockRepository.Setup(r => r.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var handler = CreateHandler(new[] { meta.Object, google.Object });

        // Act
        var response = await handler.Handle(
            new ImportMarketingInvoicesRequest { Platform = "MetaAds", From = from, To = to },
            CancellationToken.None);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("MetaAds", response.Platform);
        Assert.Equal(1, response.Imported);
        Assert.Equal(0, response.Skipped);
        Assert.Equal(0, response.Failed);
        google.Verify(
            s => s.GetTransactionsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_UnknownPlatform_ThrowsArgumentException()
    {
        var handler = CreateHandler(new[] { SourceFor("MetaAds").Object });

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(
            new ImportMarketingInvoicesRequest
            {
                Platform = "TikTokAds",
                From = DateTime.UtcNow.AddDays(-7),
                To = DateTime.UtcNow,
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_DuplicatePlatform_ThrowsInvalidOperationException()
    {
        var handler = CreateHandler(new[] { SourceFor("MetaAds").Object, SourceFor("MetaAds").Object });

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new ImportMarketingInvoicesRequest
            {
                Platform = "MetaAds",
                From = DateTime.UtcNow.AddDays(-7),
                To = DateTime.UtcNow,
            },
            CancellationToken.None));
    }

    [Fact]
    public async Task Handle_SourceThrows_ExceptionPropagates()
    {
        var meta = SourceFor("MetaAds");
        meta.Setup(s => s.GetTransactionsAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Meta API down"));

        var handler = CreateHandler(new[] { meta.Object });

        await Assert.ThrowsAsync<HttpRequestException>(() => handler.Handle(
            new ImportMarketingInvoicesRequest
            {
                Platform = "MetaAds",
                From = DateTime.UtcNow.AddDays(-7),
                To = DateTime.UtcNow,
            },
            CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ImportMarketingInvoicesHandlerTests"`
Expected: BUILD FAILURE — `ImportMarketingInvoicesHandler` does not exist yet.

- [ ] **Step 3: Create the handler**

Create `ImportMarketingInvoicesHandler.cs`:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Services;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;

public class ImportMarketingInvoicesHandler
    : IRequestHandler<ImportMarketingInvoicesRequest, ImportMarketingInvoicesResponse>
{
    private readonly IEnumerable<IMarketingTransactionSource> _sources;
    private readonly MarketingInvoiceImportService _importService;
    private readonly ILogger<ImportMarketingInvoicesHandler> _logger;

    public ImportMarketingInvoicesHandler(
        IEnumerable<IMarketingTransactionSource> sources,
        MarketingInvoiceImportService importService,
        ILogger<ImportMarketingInvoicesHandler> logger)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ImportMarketingInvoicesResponse> Handle(
        ImportMarketingInvoicesRequest request,
        CancellationToken cancellationToken)
    {
        var matches = _sources.Where(s => s.Platform == request.Platform).ToList();

        if (matches.Count == 0)
        {
            throw new ArgumentException(
                $"No marketing transaction source is registered for platform '{request.Platform}'.",
                nameof(request));
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple marketing transaction sources are registered for platform '{request.Platform}'.");
        }

        var source = matches[0];

        _logger.LogInformation(
            "Importing marketing invoices for {Platform} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            request.Platform, request.From, request.To);

        // Import-time exceptions are intentionally NOT caught here — they must
        // propagate to the job's catch-log-rethrow so Hangfire can retry.
        var result = await _importService.ImportAsync(source, request.From, request.To, cancellationToken);

        return new ImportMarketingInvoicesResponse
        {
            Platform = request.Platform,
            Imported = result.Imported,
            Skipped = result.Skipped,
            Failed = result.Failed,
        };
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ImportMarketingInvoicesHandlerTests"`
Expected: PASS — all 4 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/UseCases/ImportMarketingInvoices/ImportMarketingInvoicesHandler.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/ImportMarketingInvoicesHandlerTests.cs
git commit -m "feat: add ImportMarketingInvoicesHandler with source selection"
```

---

## Task 6: Forward each transaction source to the `IMarketingTransactionSource` interface

Without this, `IEnumerable<IMarketingTransactionSource>` injected into the handler resolves empty. Each adapter keeps its existing concrete registration and adds a forwarding line. A factory delegate is mandatory — `MetaAdsTransactionSource` is a transient typed `HttpClient` and `GoogleAdsTransactionSource` has an `internal` constructor, so reflection-based `AddScoped<TInterface, TConcrete>()` cannot be used for either.

**Files:**
- Modify: `backend/src/.../Anela.Heblo.Adapters.MetaAds/MetaAdsAdapterServiceCollectionExtensions.cs`
- Modify: `backend/src/.../Anela.Heblo.Adapters.GoogleAds/GoogleAdsAdapterServiceCollectionExtensions.cs`

- [ ] **Step 1: Add the forwarding registration in the MetaAds extension**

Replace the full contents of `MetaAdsAdapterServiceCollectionExtensions.cs` with:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.MetaAds;

public static class MetaAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddMetaAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MetaAdsSettings>(configuration.GetSection(MetaAdsSettings.ConfigurationKey));
        services.AddHttpClient<MetaAdsTransactionSource>();
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<MetaAdsTransactionSource>());
        services.AddScoped<IRecurringJob, MetaAdsInvoiceImportJob>();
        return services;
    }
}
```

- [ ] **Step 2: Add the forwarding registration in the GoogleAds extension**

Replace the full contents of `GoogleAdsAdapterServiceCollectionExtensions.cs` with:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.GoogleAds;

public static class GoogleAdsAdapterServiceCollectionExtensions
{
    public static IServiceCollection AddGoogleAdsAdapter(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<GoogleAdsSettings>(configuration.GetSection(GoogleAdsSettings.ConfigurationKey));
        services.AddSingleton<IAccountBudgetFetcher, SdkAccountBudgetFetcher>();
        services.AddScoped<GoogleAdsTransactionSource>(sp =>
            new GoogleAdsTransactionSource(
                sp.GetRequiredService<IAccountBudgetFetcher>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GoogleAdsTransactionSource>>()));
        services.AddScoped<IMarketingTransactionSource>(sp =>
            sp.GetRequiredService<GoogleAdsTransactionSource>());
        services.AddScoped<IRecurringJob, GoogleAdsInvoiceImportJob>();
        return services;
    }
}
```

- [ ] **Step 3: Build both adapter projects**

Run: `dotnet build backend/src/Adapters/Anela.Heblo.Adapters.MetaAds && dotnet build backend/src/Adapters/Anela.Heblo.Adapters.GoogleAds`
Expected: Build succeeded (substitute the correct project paths if the `Adapters/` subfolder differs).

- [ ] **Step 4: Commit**

```bash
git add backend/src
git commit -m "refactor: forward marketing transaction sources to IMarketingTransactionSource"
```

---

## Task 7: Reduce `MetaAdsInvoiceImportJob` to a MediatR dispatcher

**Files:**
- Modify: `backend/src/.../Anela.Heblo.Adapters.MetaAds/MetaAdsInvoiceImportJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsInvoiceImportJobTests.cs`

- [ ] **Step 1: Write the failing job tests (RED)**

Create `MetaAdsInvoiceImportJobTests.cs`:

```csharp
using Anela.Heblo.Adapters.MetaAds;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.MetaAds;

public class MetaAdsInvoiceImportJobTests
{
    private const string JobName = "meta-ads-invoice-import";

    private readonly Mock<IMediator> _mockMediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker = new();

    private MetaAdsInvoiceImportJob CreateJob() =>
        new(_mockMediator.Object, _mockStatusChecker.Object, NullLogger<MetaAdsInvoiceImportJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_JobDisabled_DoesNotDispatch()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(JobName)).ReturnsAsync(false);

        await CreateJob().ExecuteAsync();

        _mockMediator.Verify(
            m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_DispatchesMetaAdsRequestWithSevenDayWindow()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(JobName)).ReturnsAsync(true);
        _mockMediator.Setup(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportMarketingInvoicesResponse { Platform = "MetaAds", Imported = 3 });

        await CreateJob().ExecuteAsync();

        _mockMediator.Verify(
            m => m.Send(
                It.Is<ImportMarketingInvoicesRequest>(r =>
                    r.Platform == MetaAdsTransactionSource.PlatformName &&
                    (r.To - r.From) == TimeSpan.FromDays(7)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DispatchThrows_ExceptionIsRethrown()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(JobName)).ReturnsAsync(true);
        _mockMediator.Setup(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Meta API down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateJob().ExecuteAsync());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MetaAdsInvoiceImportJobTests"`
Expected: BUILD FAILURE — `MetaAdsInvoiceImportJob` still has the old 5-arg constructor.

- [ ] **Step 3: Rewrite `MetaAdsInvoiceImportJob` as a dispatcher**

Replace the full contents of `MetaAdsInvoiceImportJob.cs` with:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.MetaAds;

public class MetaAdsInvoiceImportJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<MetaAdsInvoiceImportJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "meta-ads-invoice-import",
        DisplayName = "Meta Ads Invoice Import",
        Description = "Fetches billing transactions from Meta Ads Graph API (7-day lookback)",
        CronExpression = "0 6,18 * * *", // 6 AM and 6 PM Prague time
        DefaultIsEnabled = true,
    };

    public MetaAdsInvoiceImportJob(
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        ILogger<MetaAdsInvoiceImportJob> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var to = DateTime.UtcNow;
            var from = to.AddDays(-7);

            var response = await _mediator.Send(
                new ImportMarketingInvoicesRequest
                {
                    Platform = MetaAdsTransactionSource.PlatformName,
                    From = from,
                    To = to,
                },
                cancellationToken);

            _logger.LogInformation(
                "{JobName} completed. Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
                Metadata.JobName, response.Imported, response.Skipped, response.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MetaAdsInvoiceImportJobTests"`
Expected: PASS — all 3 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src backend/test/Anela.Heblo.Tests/Adapters/MetaAds/MetaAdsInvoiceImportJobTests.cs
git commit -m "refactor: reduce MetaAdsInvoiceImportJob to a MediatR dispatcher"
```

---

## Task 8: Reduce `GoogleAdsInvoiceImportJob` to a MediatR dispatcher

**Files:**
- Modify: `backend/src/.../Anela.Heblo.Adapters.GoogleAds/GoogleAdsInvoiceImportJob.cs`
- Test: `backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsInvoiceImportJobTests.cs`

- [ ] **Step 1: Write the failing job tests (RED)**

Create `GoogleAdsInvoiceImportJobTests.cs`:

```csharp
using Anela.Heblo.Adapters.GoogleAds;
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Adapters.GoogleAds;

public class GoogleAdsInvoiceImportJobTests
{
    private const string JobName = "google-ads-invoice-import";

    private readonly Mock<IMediator> _mockMediator = new();
    private readonly Mock<IRecurringJobStatusChecker> _mockStatusChecker = new();

    private GoogleAdsInvoiceImportJob CreateJob() =>
        new(_mockMediator.Object, _mockStatusChecker.Object, NullLogger<GoogleAdsInvoiceImportJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_JobDisabled_DoesNotDispatch()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(JobName)).ReturnsAsync(false);

        await CreateJob().ExecuteAsync();

        _mockMediator.Verify(
            m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_JobEnabled_DispatchesGoogleAdsRequestWithSevenDayWindow()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(JobName)).ReturnsAsync(true);
        _mockMediator.Setup(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportMarketingInvoicesResponse { Platform = "GoogleAds", Imported = 5 });

        await CreateJob().ExecuteAsync();

        _mockMediator.Verify(
            m => m.Send(
                It.Is<ImportMarketingInvoicesRequest>(r =>
                    r.Platform == GoogleAdsTransactionSource.PlatformName &&
                    (r.To - r.From) == TimeSpan.FromDays(7)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_DispatchThrows_ExceptionIsRethrown()
    {
        _mockStatusChecker.Setup(c => c.IsJobEnabledAsync(JobName)).ReturnsAsync(true);
        _mockMediator.Setup(m => m.Send(It.IsAny<ImportMarketingInvoicesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Google API down"));

        await Assert.ThrowsAsync<HttpRequestException>(() => CreateJob().ExecuteAsync());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GoogleAdsInvoiceImportJobTests"`
Expected: BUILD FAILURE — `GoogleAdsInvoiceImportJob` still has the old 5-arg constructor.

- [ ] **Step 3: Rewrite `GoogleAdsInvoiceImportJob` as a dispatcher**

Replace the full contents of `GoogleAdsInvoiceImportJob.cs` with:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.ImportMarketingInvoices;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Adapters.GoogleAds;

public class GoogleAdsInvoiceImportJob : IRecurringJob
{
    private readonly IMediator _mediator;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<GoogleAdsInvoiceImportJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "google-ads-invoice-import",
        DisplayName = "Google Ads Invoice Import",
        Description = "Fetches billing transactions from Google Ads API via account_budget GAQL queries (7-day lookback)",
        CronExpression = "15 6,18 * * *",
        DefaultIsEnabled = true,
    };

    public GoogleAdsInvoiceImportJob(
        IMediator mediator,
        IRecurringJobStatusChecker statusChecker,
        ILogger<GoogleAdsInvoiceImportJob> logger)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _statusChecker = statusChecker ?? throw new ArgumentNullException(nameof(statusChecker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping execution.", Metadata.JobName);
            return;
        }

        try
        {
            _logger.LogInformation("Starting {JobName}", Metadata.JobName);

            var to = DateTime.UtcNow;
            var from = to.AddDays(-7);

            var response = await _mediator.Send(
                new ImportMarketingInvoicesRequest
                {
                    Platform = GoogleAdsTransactionSource.PlatformName,
                    From = from,
                    To = to,
                },
                cancellationToken);

            _logger.LogInformation(
                "{JobName} completed. Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
                Metadata.JobName,
                response.Imported,
                response.Skipped,
                response.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{JobName} failed", Metadata.JobName);
            throw;
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~GoogleAdsInvoiceImportJobTests"`
Expected: PASS — all 3 tests green.

- [ ] **Step 5: Commit**

```bash
git add backend/src backend/test/Anela.Heblo.Tests/Adapters/GoogleAds/GoogleAdsInvoiceImportJobTests.cs
git commit -m "refactor: reduce GoogleAdsInvoiceImportJob to a MediatR dispatcher"
```

---

## Task 9: Full verification

Confirms the whole solution builds, no concrete-source injection remains, and all touched tests pass.

- [ ] **Step 1: Confirm no remaining concrete-source usage**

Run: `grep -rn "new MarketingInvoiceImportService" backend/src backend/test`
Expected: no matches (every manual construction is gone; test helpers use `new MarketingInvoiceImportService(repo, logger)` only inside `ImportMarketingInvoicesHandlerTests` and `MarketingInvoiceImportServiceTests`, which is intended — re-scan and confirm there is no `new MarketingInvoiceImportService(` with three arguments and no production-code usage).

Run: `grep -rn "MetaAdsTransactionSource\|GoogleAdsTransactionSource" backend/src --include=*.cs`
Expected: the only references are the source class definitions, the `AddHttpClient<MetaAdsTransactionSource>()` / `AddScoped<GoogleAdsTransactionSource>(...)` registrations, the `IMarketingTransactionSource` forwarding registrations, and the jobs' `*.PlatformName` const reference. No constructor injection of the concrete types remains.

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run code formatting**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: no formatting changes, or auto-applied formatting only.

- [ ] **Step 4: Run the full affected test set**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~MarketingInvoices|FullyQualifiedName~MetaAds|FullyQualifiedName~GoogleAds"`
Expected: PASS — `MarketingInvoiceImportServiceTests` (3), `ImportMarketingInvoicesHandlerTests` (4), `MetaAdsInvoiceImportJobTests` (3), `GoogleAdsInvoiceImportJobTests` (3), plus the pre-existing `MetaAdsTransactionSourceTests` and `GoogleAdsTransactionSourceTests` still green.

- [ ] **Step 5: Commit any formatting changes**

```bash
git add -A
git commit -m "chore: apply dotnet format after marketing-invoices refactor" || echo "nothing to commit"
```

---

## Self-Review

**Spec coverage:**

- **FR-1 (Introduce `ImportMarketingInvoicesCommand`)** — Tasks 4 (request/response) and 5 (handler with source selection, mapping, unknown/duplicate-platform throw). ✅
- **FR-2 (Register `MarketingInvoiceImportService`)** — Task 3 (`AddScoped<MarketingInvoiceImportService>()`); Task 2 removes the constructor-injected source so the scoped registration is valid; Tasks 7–8 remove every `new MarketingInvoiceImportService(...)`; Task 9 Step 1 verifies. ✅
- **FR-3 (Register sources against `IMarketingTransactionSource`)** — Task 6 forwards both. ✅
- **FR-4 / FR-5 (Jobs as MediatR dispatchers)** — Tasks 7 and 8: jobs inject only `IMediator`, `IRecurringJobStatusChecker`, logger; keep enabled-check, 7-day UTC window, catch-log-rethrow; metadata unchanged. ✅
- **FR-6 (Test coverage)** — Task 2 keeps `MarketingInvoiceImportServiceTests` green; Task 5 adds handler tests (selection, mapping, unknown, duplicate, propagation); Tasks 7–8 add job tests (disabled short-circuit, correct request dispatched, exception rethrown). ✅
- **Architecture amendments** — Service signature change (amendment 1) is Task 2; forwarding registration wording (amendment 2) is Task 6; handler non-swallowing exception behavior (amendment 3) is Task 5 Step 3 with an explicit comment and a propagation test; platform literal via `const PlatformName` (amendment 4) is Task 1. ✅

**Placeholder scan:** No TBD/TODO/"add appropriate…" entries. Every code step shows complete file content or an exact replace target. All test code is concrete.

**Type consistency:** `ImportMarketingInvoicesRequest` (`Platform`, `From`, `To`) and `ImportMarketingInvoicesResponse` (`Platform`, `Imported`, `Skipped`, `Failed`, inherited `Success`) are defined in Task 4 and used identically in Tasks 5, 7, 8. `MarketingInvoiceImportService` constructor `(IImportedMarketingTransactionRepository, ILogger<MarketingInvoiceImportService>)` and `ImportAsync(IMarketingTransactionSource, DateTime, DateTime, CancellationToken)` are defined in Task 2 and used consistently in Tasks 3, 5. `PlatformName` const is defined in Task 1 and referenced in Tasks 7–8. Handler ctor `(IEnumerable<IMarketingTransactionSource>, MarketingInvoiceImportService, ILogger<ImportMarketingInvoicesHandler>)` is consistent between Task 5's implementation and test.

No gaps found.
