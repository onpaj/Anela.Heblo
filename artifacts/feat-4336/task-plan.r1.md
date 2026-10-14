# Bulk Existence Lookup For Marketing Invoice Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `MarketingInvoiceImportService.ImportAsync`'s per-transaction `ExistsAsync` database round-trip with a single bulk lookup executed once per import run, eliminating the N+1 query pattern while keeping `stagedIds` as the mandatory in-memory guard against in-batch duplicates.

**Architecture:** Add `GetExistingTransactionIdsAsync(platform, transactionIds, ct)` to `IImportedMarketingTransactionRepository` (Domain) and implement it in `ImportedMarketingTransactionRepository` (Persistence) with a single `WHERE Platform == platform && ids.Contains(TransactionId)` query, mirroring the early-return-on-empty-input, plain-`Contains` pattern already used by `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync`. `MarketingInvoiceImportService.ImportAsync` calls this once before its `foreach` loop and replaces the per-row `await _repository.ExistsAsync(...)` branch with a synchronous `alreadyImported.Contains(...)` check, while `stagedIds` stays exactly as it is today (unconditional guard, per the unique `(Platform, TransactionId)` index and the save-time rethrow).

**Tech Stack:** .NET 8, EF Core 8 (Npgsql/PostgreSQL in production, EF Core InMemory provider in repository tests), xUnit, Moq, FluentAssertions.

---

### task: add-bulk-existence-lookup-repository-method

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepositoryTests.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs`

- [ ] **Step 1: Write the failing repository test**

Create `backend/test/Anela.Heblo.Tests/Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepositoryTests.cs` with this exact content:

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.MarketingInvoices;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ImportedMarketingTransactionTests_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ImportedMarketingTransaction CreateTransaction(string platform, string transactionId)
        => new()
        {
            Platform = platform,
            TransactionId = transactionId,
            Amount = 100m,
            Currency = "CZK",
            TransactionDate = DateTime.UtcNow,
            ImportedAt = DateTime.UtcNow,
        };

    [Fact]
    public async Task GetExistingTransactionIdsAsync_ReturnsOnlyIdsPresentForGivenPlatform()
    {
        // Arrange
        await using var context = CreateContext();
        context.ImportedMarketingTransactions.AddRange(
            CreateTransaction("MetaAds", "TX-001"),
            CreateTransaction("MetaAds", "TX-002"),
            CreateTransaction("GoogleAds", "TX-001")); // same TransactionId, different platform
        await context.SaveChangesAsync();
        var repository = new ImportedMarketingTransactionRepository(context);

        // Act
        var result = await repository.GetExistingTransactionIdsAsync(
            "MetaAds", new[] { "TX-001", "TX-002", "TX-999" }, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new HashSet<string> { "TX-001", "TX-002" });
    }

    [Fact]
    public async Task GetExistingTransactionIdsAsync_EmptyIdList_ReturnsEmptySet()
    {
        // Arrange
        await using var context = CreateContext();
        var repository = new ImportedMarketingTransactionRepository(context);

        // Act
        var result = await repository.GetExistingTransactionIdsAsync(
            "MetaAds", Array.Empty<string>(), CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportedMarketingTransactionRepositoryTests"
```

Expected output: a build error, not a test failure —

```
error CS1061: 'ImportedMarketingTransactionRepository' does not contain a definition for 'GetExistingTransactionIdsAsync' and no accessible extension method 'GetExistingTransactionIdsAsync' accepting a first argument of type 'ImportedMarketingTransactionRepository' could be found
```

- [ ] **Step 3: Add the method signature to the interface**

In `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`, replace the whole file with:

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task<HashSet<string>> GetExistingTransactionIdsAsync(string platform, IEnumerable<string> transactionIds, CancellationToken ct);
    Task<ImportedMarketingTransaction> AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

- [ ] **Step 4: Implement the method in the repository**

In `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs`, replace the whole file with:

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Anela.Heblo.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionRepository
    : BaseRepository<ImportedMarketingTransaction, int>, IImportedMarketingTransactionRepository
{
    public ImportedMarketingTransactionRepository(ApplicationDbContext context)
        : base(context)
    {
    }

    public async Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct)
    {
        return await AnyAsync(
            x => x.Platform == platform && x.TransactionId == transactionId,
            ct);
    }

    public async Task<HashSet<string>> GetExistingTransactionIdsAsync(
        string platform, IEnumerable<string> transactionIds, CancellationToken ct)
    {
        var ids = transactionIds is ICollection<string> c ? c : transactionIds.ToList();
        if (ids.Count == 0)
            return new HashSet<string>();

        var existing = await DbSet
            .Where(x => x.Platform == platform && ids.Contains(x.TransactionId))
            .Select(x => x.TransactionId)
            .ToListAsync(ct);

        return existing.ToHashSet();
    }

}
```

(This mirrors `ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync` at `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs:20-30`: early-return before touching `DbSet` on an empty input, plain `Contains`, no case normalization — `Platform`/`TransactionId` are plain `character varying` columns with no `citext` type or collation override, so Postgres `=`/`IN` is already byte-exact, matching `ExistsAsync`'s existing `==` semantics.)

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ImportedMarketingTransactionRepositoryTests"
```

Expected output:

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs backend/test/Anela.Heblo.Tests/Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepositoryTests.cs
git commit -m "$(cat <<'EOF'
feat(marketing-invoices): add bulk existence-lookup repository method

Add GetExistingTransactionIdsAsync to IImportedMarketingTransactionRepository
and implement it with a single WHERE Platform == p && ids.Contains(TransactionId)
query, mirroring ManufacturedProductInventoryRepository.GetByProductCodesWithLogsAsync.
Prepares the N+1 fix in MarketingInvoiceImportService.ImportAsync.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DV8unGeuHcoYkS1r325gkh
EOF
)"
```

---

### task: use-bulk-lookup-in-marketing-invoice-import-service

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs:20-116`

- [ ] **Step 1: Update the test file to use the bulk lookup, add new coverage, and expect it to fail (RED)**

Replace the whole file `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
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
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_DuplicateTransaction_IsSkipped()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "TX-001" });

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_PerTransactionError_CountsAsFailed_DoesNotAbortRun()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-001"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);
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

    [Fact]
    public async Task ImportAsync_FinalSaveChangesThrows_Rethrows()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

        // The single post-loop flush fails — none of the staged records are persisted
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("flush failed"));

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ImportAsync(_mockSource.Object, from, to));

        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ImportAsync_EmptyInput_DoesNotCallSaveChanges()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MarketingTransaction>());

        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        // Same TransactionId returned twice by the source in one run
        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-DUP", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-DUP", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // Not present in the DB yet — the bulk lookup is a pre-loop snapshot that cannot see
        // un-flushed staged entities from earlier in this same batch, so both copies of
        // TX-DUP pass it; stagedIds is what catches the second one.
        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(
            x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ImportAsync_NewTransaction_PersistsCurrencyDescriptionAndRawData()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new()
            {
                TransactionId = "TX-EUR-001",
                Amount = 123.45m,
                TransactionDate = from,
                Description = "campaign X",
                Currency = "EUR",
                RawData = "{\"foo\":1}",
            },
        };

        _mockSource
            .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository
            .Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        ImportedMarketingTransaction? captured = null;
        _mockRepository
            .Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .Callback<ImportedMarketingTransaction, CancellationToken>((entity, _) => captured = entity)
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

        _mockRepository
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(0, result.Failed);
        Assert.NotNull(captured);
        Assert.Equal("EUR", captured!.Currency);
        Assert.Equal("campaign X", captured.Description);
        Assert.Equal("{\"foo\":1}", captured.RawData);
    }

    [Fact]
    public async Task ImportAsync_EmptyCurrency_Skips_CountsFailed_DoesNotCallExistsOrAdd()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new()
            {
                TransactionId = "TX-BAD-001",
                Amount = 100m,
                TransactionDate = from,
                Description = "missing currency",
                Currency = "",
            },
        };

        _mockSource
            .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository
            .Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
        _mockRepository.Verify(
            x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);

        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("TX-BAD-001") &&
                    v.ToString()!.Contains("TestPlatform")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ImportAsync_WhitespaceCurrency_TreatedAsEmpty_CountsFailed()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new()
            {
                TransactionId = "TX-WS-001",
                Amount = 50m,
                TransactionDate = from,
                Description = "whitespace currency",
                Currency = "   ",
            },
        };

        _mockSource
            .Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository
            .Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(0, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
        _mockRepository.Verify(
            x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _mockRepository.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        _mockLogger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("TX-WS-001") &&
                    v.ToString()!.Contains("TestPlatform")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ImportAsync_FinalSaveChangesThrows_ExceptionTypeIsPreserved()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string>());

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("flush failed"));

        // Act
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.ImportAsync(_mockSource.Object, from, to));

        // Assert — exception type propagates unchanged (proves `throw;` not `throw ex;`)
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal("flush failed", ex.Message);
    }

    [Fact]
    public async Task ImportAsync_BatchOfTransactions_CallsBulkLookupExactlyOnce_NeverCallsPerRowExistsAsync()
    {
        // Arrange
        var from = new DateTime(2026, 4, 1);
        var to = new DateTime(2026, 4, 2);

        var transactions = new List<MarketingTransaction>
        {
            new() { TransactionId = "TX-001", Amount = 100m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-002", Amount = 200m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
            new() { TransactionId = "TX-003", Amount = 300m, TransactionDate = from, Description = "Ad charge", Currency = "CZK" },
        };

        _mockSource.Setup(x => x.GetTransactionsAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        // TX-002 was already imported in a prior run; TX-001 and TX-003 are new
        _mockRepository.Setup(x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HashSet<string> { "TX-002" });

        _mockRepository.Setup(x => x.AddAsync(It.IsAny<ImportedMarketingTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction e, CancellationToken _) => e);

        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _service.ImportAsync(_mockSource.Object, from, to);

        // Assert
        Assert.Equal(2, result.Imported);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Failed);
        _mockRepository.Verify(
            x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockRepository.Verify(
            x => x.ExistsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (RED)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"
```

Expected output: several failures, because production code still calls the now-unmocked `ExistsAsync` (which a loose Moq mock resolves to `false` by default) instead of the newly-mocked `GetExistingTransactionIdsAsync` — e.g.:

```
Failed  Anela.Heblo.Tests.Features.MarketingInvoices.MarketingInvoiceImportServiceTests.ImportAsync_DuplicateTransaction_IsSkipped
  Assert.Equal() Failure: Values differ
  Expected: 0
  Actual:   1
Failed  Anela.Heblo.Tests.Features.MarketingInvoices.MarketingInvoiceImportServiceTests.ImportAsync_DuplicateTransactionIdWithinSameRun_StagesOnlyOnce
  Moq.MockException: 
  x => x.GetExistingTransactionIdsAsync("TestPlatform", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())
  Expected invocation on the mock once, but was 0 times
Failed  Anela.Heblo.Tests.Features.MarketingInvoices.MarketingInvoiceImportServiceTests.ImportAsync_BatchOfTransactions_CallsBulkLookupExactlyOnce_NeverCallsPerRowExistsAsync
  Assert.Equal() Failure: Values differ
  Expected: 2
  Actual:   3
```

- [ ] **Step 3: Update `ImportAsync` to call the bulk lookup once and drop the per-row `ExistsAsync` call**

Replace the whole file `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` with:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public class MarketingInvoiceImportService : IMarketingInvoiceImportService
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

        var allIds = transactions.Select(t => t.TransactionId).ToList();
        var alreadyImported = await _repository.GetExistingTransactionIdsAsync(source.Platform, allIds, ct);

        var result = new MarketingImportResult();
        var stagedCount = 0;

        // Within-run duplicate guard — kept unconditionally, do not remove. `alreadyImported`
        // is a snapshot fetched once, before this loop starts, so it cannot see a TransactionId
        // that appears twice within this same `transactions` batch (neither copy is in the
        // database yet). ImportedMarketingTransactionConfiguration declares a unique index on
        // (Platform, TransactionId), and the SaveChangesAsync call below rethrows on failure —
        // so without this guard, two in-batch duplicates would both pass the alreadyImported
        // check, both get AddAsync'd, and fail the entire run's save instead of cleanly
        // skipping one row.
        var stagedIds = new HashSet<string>();

        foreach (var transaction in transactions)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(transaction.Currency))
                {
                    _logger.LogWarning(
                        "Marketing transaction {TransactionId} for {Platform} has empty Currency — skipping",
                        transaction.TransactionId, source.Platform);
                    result.Failed++;
                    continue;
                }

                if (stagedIds.Contains(transaction.TransactionId))
                {
                    _logger.LogDebug(
                        "Transaction {TransactionId} for {Platform} already staged in this run — skipping",
                        transaction.TransactionId, source.Platform);
                    result.Skipped++;
                    continue;
                }

                if (alreadyImported.Contains(transaction.TransactionId))
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
                    Currency = transaction.Currency,
                    TransactionDate = transaction.TransactionDate,
                    ImportedAt = DateTime.UtcNow,
                    Description = transaction.Description,
                    RawData = transaction.RawData,
                };

                await _repository.AddAsync(entity, ct);
                stagedIds.Add(transaction.TransactionId);
                stagedCount++;
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

        if (stagedCount > 0)
        {
            try
            {
                await _repository.SaveChangesAsync(ct);
                result.Imported = stagedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist {Count} marketing transactions for {Platform}",
                    stagedCount, source.Platform);
                throw;
            }
        }

        _logger.LogInformation(
            "Marketing invoice import complete for {Platform}: Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
            source.Platform, result.Imported, result.Skipped, result.Failed);

        return result;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass (GREEN)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests"
```

Expected output:

```
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11
```

- [ ] **Step 5: Run the full backend test suite and build/format checks**

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Expected output: all three commands succeed with zero errors/failures (the `dotnet format` command reports no files needing changes).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs
git commit -m "$(cat <<'EOF'
fix(marketing-invoices): replace per-row ExistsAsync with bulk lookup in ImportAsync

Call GetExistingTransactionIdsAsync once before the import loop instead of
awaiting ExistsAsync per transaction, cutting the per-run DB round-trip count
for existence checking from O(N) to O(1). stagedIds is kept unconditionally as
the in-memory guard against in-batch duplicates, since the bulk lookup is a
pre-loop snapshot that cannot see duplicates within the same fetched batch and
the unique (Platform, TransactionId) index would otherwise fail the whole
run's SaveChangesAsync on a same-batch duplicate.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01DV8unGeuHcoYkS1r325gkh
EOF
)"
```
