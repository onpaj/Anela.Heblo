### task: rewire-adapter-to-repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs`

**Depends on:** `add-daily-counts-repository-method` (this task requires `IIssuedInvoiceRepository.GetDailyCountsAsync` to already exist).

This task removes the `ApplicationDbContext` dependency from the adapter, making it a thin pass-through to `IIssuedInvoiceRepository`, and rewrites its unit tests to mock the repository instead of standing up an in-memory `DbContext` — matching the existing `InvoiceConsumptionSourceAdapterTests.cs` pattern.

- [ ] **Step 1: Rewrite the adapter test file to mock `IIssuedInvoiceRepository`**

Replace the entire contents of `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs` with:

```csharp
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public sealed class InvoiceImportStatisticsSourceAdapterTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repository = new();

    private InvoiceImportStatisticsSourceAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task GetDailyCountsAsync_ForwardsArgumentsToRepository()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        _repository
            .Setup(r => r.GetDailyCountsAsync(startDate, endDate, ImportDateType.InvoiceDate, ct))
            .ReturnsAsync(new List<DailyInvoiceCount>());

        var adapter = CreateAdapter();

        // Act
        await adapter.GetDailyCountsAsync(startDate, endDate, ImportDateType.InvoiceDate, ct);

        // Assert
        _repository.Verify(
            r => r.GetDailyCountsAsync(startDate, endDate, ImportDateType.InvoiceDate, ct),
            Times.Once);
    }

    [Fact]
    public async Task GetDailyCountsAsync_ReturnsRepositoryResultUnchanged()
    {
        // Arrange
        var expected = new List<DailyInvoiceCount>
        {
            new() { Date = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), Count = 2 },
            new() { Date = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc), Count = 0 },
        };

        _repository
            .Setup(r => r.GetDailyCountsAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<ImportDateType>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var adapter = CreateAdapter();

        // Act
        var result = await adapter.GetDailyCountsAsync(
            DateTime.UtcNow, DateTime.UtcNow, ImportDateType.LastSyncTime, CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task GetDailyCountsAsync_PassesLastSyncTimeDateType_WhenRequested()
    {
        // Arrange
        var startDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc);

        _repository
            .Setup(r => r.GetDailyCountsAsync(startDate, endDate, ImportDateType.LastSyncTime, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<DailyInvoiceCount>())
            .Verifiable();

        var adapter = CreateAdapter();

        // Act
        await adapter.GetDailyCountsAsync(startDate, endDate, ImportDateType.LastSyncTime, CancellationToken.None);

        // Assert
        _repository.Verify();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests" 2>&1 | tail -40
```
Expected: build error — `InvoiceImportStatisticsSourceAdapter` has no constructor accepting `IIssuedInvoiceRepository` (it still takes `ApplicationDbContext`). This confirms the test now exercises the target constructor shape, which doesn't exist yet.

- [ ] **Step 3: Rewrite the adapter**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` (currently 102 lines, injecting `ApplicationDbContext` and running the EF Core query directly) with:

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceImportStatisticsSourceAdapter : IInvoiceImportStatisticsSource
{
    private readonly IIssuedInvoiceRepository _repository;

    public InvoiceImportStatisticsSourceAdapter(IIssuedInvoiceRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        return _repository.GetDailyCountsAsync(startDate, endDate, dateType, cancellationToken);
    }
}
```

- [ ] **Step 4: Run the tests again to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests" 2>&1 | tail -40
```
Expected: `Passed!` — all 3 rewritten `InvoiceImportStatisticsSourceAdapterTests` tests pass.

- [ ] **Step 5: Confirm no remaining reference to `ApplicationDbContext` in the adapter**

Run:
```bash
grep -n "ApplicationDbContext\|EntityFrameworkCore\|Anela.Heblo.Persistence" \
  backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs
```
Expected: no output (no matches) — the Application-layer file no longer references `Anela.Heblo.Persistence`, `ApplicationDbContext`, or EF Core in any way.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapterTests.cs
git commit -m "fix(invoices): remove ApplicationDbContext dependency from InvoiceImportStatisticsSourceAdapter"
```

---

