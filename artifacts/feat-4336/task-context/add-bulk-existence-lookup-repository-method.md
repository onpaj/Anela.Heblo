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
