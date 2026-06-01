# Marketing Invoice Import — Shared Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the shared domain, application, and persistence foundation for importing billing transactions from ad platforms (Meta Ads, Google Ads) into Anela Heblo.

**Architecture:** Domain interfaces + value objects live in `Domain/Features/MarketingInvoices/`. A service in `Application/Features/MarketingInvoices/Services/` orchestrates fetch → dedup → persist using an EF-backed repository in `Persistence/Features/MarketingInvoices/`. Adapter issues (#607, #608) will wire concrete `IMarketingTransactionSource` implementations against this foundation.

**Tech Stack:** .NET 8, Entity Framework Core (PostgreSQL via Npgsql), xUnit, Moq

---

## File Map

**Create:**
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IMarketingTransactionSource.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`
- `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`
- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs`
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`
- `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`

**Modify:**
- `backend/src/Anela.Heblo.Application/ApplicationModule.cs` — add `AddMarketingInvoicesModule()`
- `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs` — add `DbSet<ImportedMarketingTransaction>`

**Generated (EF migration):**
- `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddImportedMarketingTransactions.cs`

---

## Task 1: Domain Layer

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IMarketingTransactionSource.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/MarketingTransaction.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs`

- [ ] **Step 1: Create `IMarketingTransactionSource.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public interface IMarketingTransactionSource
{
    string Platform { get; }

    Task<List<MarketingTransaction>> GetTransactionsAsync(DateTime from, DateTime to, CancellationToken ct);
}
```

- [ ] **Step 2: Create `MarketingTransaction.cs`**

Pure value object — carries data from adapter to service. Not an EF entity.

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class MarketingTransaction
{
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? RawData { get; set; }
}
```

- [ ] **Step 3: Create `ImportedMarketingTransaction.cs`**

Persistence entity — implements `IEntity<int>` (from `Anela.Heblo.Xcc.Domain`). `Id` is auto-incremented by the database.

```csharp
using Anela.Heblo.Xcc.Domain;

namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public class ImportedMarketingTransaction : IEntity<int>
{
    public int Id { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public DateTime ImportedAt { get; set; }
    public bool IsSynced { get; set; } = false;
    public string? ErrorMessage { get; set; }
}
```

- [ ] **Step 4: Create `IImportedMarketingTransactionRepository.cs`**

```csharp
namespace Anela.Heblo.Domain.Features.MarketingInvoices;

public interface IImportedMarketingTransactionRepository
{
    Task<bool> ExistsAsync(string platform, string transactionId, CancellationToken ct);
    Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct);
    Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Build to verify no compile errors**

```bash
cd /path/to/repo && dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/MarketingInvoices/
git commit -m "feat(marketing-invoices): add domain interfaces and models"
```

---

## Task 2: Application Layer — Result DTO + Service

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingImportResult.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs`

- [ ] **Step 1: Create `MarketingImportResult.cs`**

```csharp
namespace Anela.Heblo.Application.Features.MarketingInvoices;

public class MarketingImportResult
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
```

- [ ] **Step 2: Write the failing tests first**

Create `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs`:

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
            _mockSource.Object,
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
        var result = await _service.ImportAsync(from, to);

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

        // Already exists in DB
        _mockRepository.Setup(x => x.ExistsAsync("TestPlatform", "TX-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _service.ImportAsync(from, to);

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

        // First transaction succeeds
        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-001"), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockRepository.Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Second transaction throws on AddAsync
        _mockRepository.Setup(x => x.AddAsync(It.Is<ImportedMarketingTransaction>(t => t.TransactionId == "TX-002"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB write failed"));

        // Act
        var result = await _service.ImportAsync(from, to);

        // Assert
        Assert.Equal(1, result.Imported);
        Assert.Equal(0, result.Skipped);
        Assert.Equal(1, result.Failed);
    }
}
```

- [ ] **Step 3: Run tests — verify they fail (class not found)**

```bash
cd /path/to/repo && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests" --no-build 2>&1 | tail -20
```

Expected: build error about `MarketingInvoiceImportService` not existing.

- [ ] **Step 4: Create `MarketingInvoiceImportService.cs`**

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public class MarketingInvoiceImportService
{
    private readonly IMarketingTransactionSource _source;
    private readonly IImportedMarketingTransactionRepository _repository;
    private readonly ILogger<MarketingInvoiceImportService> _logger;

    public MarketingInvoiceImportService(
        IMarketingTransactionSource source,
        IImportedMarketingTransactionRepository repository,
        ILogger<MarketingInvoiceImportService> logger)
    {
        _source = source;
        _repository = repository;
        _logger = logger;
    }

    public async Task<MarketingImportResult> ImportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        _logger.LogInformation("Starting marketing invoice import for platform {Platform} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            _source.Platform, from, to);

        var transactions = await _source.GetTransactionsAsync(from, to, ct);

        var result = new MarketingImportResult();

        foreach (var transaction in transactions)
        {
            try
            {
                var exists = await _repository.ExistsAsync(_source.Platform, transaction.TransactionId, ct);
                if (exists)
                {
                    _logger.LogDebug("Transaction {TransactionId} for {Platform} already imported — skipping",
                        transaction.TransactionId, _source.Platform);
                    result.Skipped++;
                    continue;
                }

                var entity = new ImportedMarketingTransaction
                {
                    TransactionId = transaction.TransactionId,
                    Platform = _source.Platform,
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
                _logger.LogError(ex, "Failed to import transaction {TransactionId} for {Platform}",
                    transaction.TransactionId, _source.Platform);
                result.Failed++;
            }
        }

        _logger.LogInformation("Marketing invoice import complete for {Platform}: Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
            _source.Platform, result.Imported, result.Skipped, result.Failed);

        return result;
    }
}
```

- [ ] **Step 5: Run tests — verify all 3 pass**

```bash
cd /path/to/repo && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~MarketingInvoiceImportServiceTests" 2>&1 | tail -20
```

Expected:
```
Passed!  - Failed: 0, Passed: 3, Skipped: 0
```

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/
git add backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/
git commit -m "feat(marketing-invoices): add import service with deduplication and unit tests"
```

---

## Task 3: Persistence Layer

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionConfiguration.cs`
- Create: `backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/ImportedMarketingTransactionRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`

- [ ] **Step 1: Create `ImportedMarketingTransactionConfiguration.cs`**

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Anela.Heblo.Persistence.Features.MarketingInvoices;

public class ImportedMarketingTransactionConfiguration : IEntityTypeConfiguration<ImportedMarketingTransaction>
{
    public void Configure(EntityTypeBuilder<ImportedMarketingTransaction> builder)
    {
        builder.ToTable("imported_marketing_transactions", "dbo");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("Id")
            .HasColumnType("integer")
            .ValueGeneratedOnAdd();

        builder.Property(e => e.TransactionId)
            .IsRequired()
            .HasMaxLength(255)
            .HasColumnName("TransactionId")
            .HasColumnType("character varying(255)");

        builder.Property(e => e.Platform)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("Platform")
            .HasColumnType("character varying(50)");

        builder.Property(e => e.Amount)
            .IsRequired()
            .HasColumnName("Amount")
            .HasColumnType("numeric(18,2)");

        builder.Property(e => e.TransactionDate)
            .IsRequired()
            .HasColumnName("TransactionDate")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.ImportedAt)
            .IsRequired()
            .HasColumnName("ImportedAt")
            .HasColumnType("timestamp without time zone");

        builder.Property(e => e.IsSynced)
            .IsRequired()
            .HasDefaultValue(false)
            .HasColumnName("IsSynced")
            .HasColumnType("boolean");

        builder.Property(e => e.ErrorMessage)
            .HasColumnName("ErrorMessage")
            .HasColumnType("text");

        builder.HasIndex(e => new { e.Platform, e.TransactionId })
            .IsUnique()
            .HasDatabaseName("IX_imported_marketing_transactions_Platform_TransactionId");
    }
}
```

- [ ] **Step 2: Create `ImportedMarketingTransactionRepository.cs`**

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

    public async Task AddAsync(ImportedMarketingTransaction entity, CancellationToken ct)
    {
        await base.AddAsync(entity, ct);
    }

    public async Task<List<ImportedMarketingTransaction>> GetUnsyncedAsync(CancellationToken ct)
    {
        return (await FindAsync(x => !x.IsSynced, ct)).ToList();
    }
}
```

- [ ] **Step 3: Add `DbSet` to `ApplicationDbContext.cs`**

Open `backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs`. Add the using at the top:

```csharp
using Anela.Heblo.Domain.Features.MarketingInvoices;
```

Add the `DbSet` property after the existing `// Grid Layouts module` section (before `protected override void OnModelCreating`):

```csharp
// Marketing Invoices module
public DbSet<ImportedMarketingTransaction> ImportedMarketingTransactions { get; set; } = null!;
```

- [ ] **Step 4: Build full solution to verify**

```bash
cd /path/to/repo && dotnet build backend/
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/MarketingInvoices/
git add backend/src/Anela.Heblo.Persistence/ApplicationDbContext.cs
git commit -m "feat(marketing-invoices): add EF Core configuration and repository"
```

---

## Task 4: Application Module Wiring

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs`

- [ ] **Step 1: Create `MarketingInvoicesModule.cs`**

Note: `MarketingInvoiceImportService` is registered as `Scoped` but NOT bound to an interface — adapters (#607, #608) will inject it directly as a concrete type.

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

- [ ] **Step 2: Register module in `ApplicationModule.cs`**

Open `backend/src/Anela.Heblo.Application/ApplicationModule.cs`. Add after `services.AddGridLayoutsModule();`:

```csharp
services.AddMarketingInvoicesModule();
```

Also add the using at the top of the file:

```csharp
using Anela.Heblo.Application.Features.MarketingInvoices;
```

- [ ] **Step 3: Build full solution to verify**

```bash
cd /path/to/repo && dotnet build backend/
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/MarketingInvoices/MarketingInvoicesModule.cs
git add backend/src/Anela.Heblo.Application/ApplicationModule.cs
git commit -m "feat(marketing-invoices): register module in ApplicationModule"
```

---

## Task 5: EF Core Migration

**Files:**
- Generated: `backend/src/Anela.Heblo.Persistence/Migrations/<timestamp>_AddImportedMarketingTransactions.cs`

- [ ] **Step 1: Generate the migration**

Run from the repo root:

```bash
dotnet ef migrations add AddImportedMarketingTransactions \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected: output ending with `Done.` and two new files in `backend/src/Anela.Heblo.Persistence/Migrations/`.

- [ ] **Step 2: Verify the migration content**

Open the generated `<timestamp>_AddImportedMarketingTransactions.cs` and confirm it contains:
- `migrationBuilder.CreateTable(name: "imported_marketing_transactions", schema: "dbo", ...)`
- Columns: `Id`, `TransactionId`, `Platform`, `Amount`, `TransactionDate`, `ImportedAt`, `IsSynced`, `ErrorMessage`
- `migrationBuilder.CreateIndex(name: "IX_imported_marketing_transactions_Platform_TransactionId", unique: true)`

If the migration looks wrong (missing columns, wrong types), delete it, fix the configuration, and regenerate.

- [ ] **Step 3: Commit the migration**

```bash
git add backend/src/Anela.Heblo.Persistence/Migrations/
git commit -m "feat(marketing-invoices): add EF Core migration for imported_marketing_transactions table"
```

---

## Task 6: Final Verification

- [ ] **Step 1: Run all tests**

```bash
cd /path/to/repo && dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: All tests pass, 0 failures.

- [ ] **Step 2: Run dotnet format**

```bash
cd /path/to/repo && dotnet format backend/
```

Expected: No changes needed (or apply any auto-fixes and verify build still passes).

- [ ] **Step 3: Full build**

```bash
cd /path/to/repo && dotnet build backend/
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit format fixes if any**

```bash
git add -A
git status  # verify only formatting changes
git commit -m "style: apply dotnet format"
```

(Skip this step if `dotnet format` made no changes.)
