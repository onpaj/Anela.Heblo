### task: add-daily-counts-repository-method

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`

This task adds `GetDailyCountsAsync` to the repository layer, with tests, without touching the adapter yet (the adapter still works exactly as before after this task — it is only wired up in the next task).

- [ ] **Step 1: Write the failing repository tests**

Open `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`. It currently starts like this:

```csharp
using Anela.Heblo.Application.Features.Invoices.Contracts;
using Anela.Heblo.Persistence.Invoices;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public class IssuedInvoiceRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly IssuedInvoiceRepository _repository;
    private readonly Mock<ILogger<IssuedInvoiceRepository>> _mockLogger;

    public IssuedInvoiceRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"IssuedInvoiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _mockLogger = new Mock<ILogger<IssuedInvoiceRepository>>();
        _repository = new IssuedInvoiceRepository(_context, _mockLogger.Object);
    }
    // ... existing tests below ...
```

Add `using Anela.Heblo.Domain.Features.Analytics;` and `using FluentAssertions;` to the top of the file (both are needed by the new tests — `Analytics` for `ImportDateType`/`DailyInvoiceCount`, `FluentAssertions` for `.Should()`), then add this private helper method and these five `[Fact]` test methods anywhere inside the `IssuedInvoiceRepositoryTests` class body (e.g. right after the constructor):

```csharp
    private static IssuedInvoice MakeInvoiceForDailyCounts(string id, DateTime invoiceDate, DateTime? lastSyncTime = null)
    {
        var invoice = new IssuedInvoice
        {
            Id = id,
            InvoiceDate = invoiceDate,
            DueDate = invoiceDate.AddDays(14),
            TaxDate = invoiceDate,
        };

        if (lastSyncTime is not null)
        {
            typeof(IssuedInvoice)
                .GetProperty(nameof(IssuedInvoice.LastSyncTime))!
                .SetValue(invoice, lastSyncTime);
        }

        return invoice;
    }

    [Fact]
    public async Task GetDailyCountsAsync_InvoiceDateBranch_ReturnsCountsGroupedByDay()
    {
        // Arrange
        var day1 = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);

        _context.IssuedInvoices.AddRange(
            MakeInvoiceForDailyCounts("INV-1", day1),
            MakeInvoiceForDailyCounts("INV-2", day1.AddHours(3)),
            MakeInvoiceForDailyCounts("INV-3", day2));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _repository.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Date.Should().Be(DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Utc));
        result[0].Date.Kind.Should().Be(DateTimeKind.Utc);
        result[0].Count.Should().Be(2);
        result[1].Date.Should().Be(DateTime.SpecifyKind(new DateTime(2026, 6, 2), DateTimeKind.Utc));
        result[1].Count.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyCountsAsync_SyncTimeBranch_IgnoresInvoicesWithNullSyncTime()
    {
        // Arrange
        var syncedDay = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);
        _context.IssuedInvoices.AddRange(
            MakeInvoiceForDailyCounts("INV-A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: syncedDay),
            MakeInvoiceForDailyCounts("INV-B", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: syncedDay.AddHours(2)),
            MakeInvoiceForDailyCounts("INV-NULL", new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: null));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 5, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _repository.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.LastSyncTime, CancellationToken.None);

        // Assert
        result.Should().HaveCount(1);
        result[0].Count.Should().Be(2);
    }

    [Fact]
    public async Task GetDailyCountsAsync_EmptyRange_ReturnsZeroCountsForEveryDay()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _repository.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().Equal(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 2),
            new DateTime(2026, 6, 3));
        result.Should().OnlyContain(r => r.Count == 0);
        result.Should().OnlyContain(r => r.Date.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public async Task GetDailyCountsAsync_InclusiveBoundaries_IncludesInvoicesOnStartAndEndDate()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 12, 23, 59, 59, DateTimeKind.Utc);

        _context.IssuedInvoices.AddRange(
            MakeInvoiceForDailyCounts("INV-START", startDate),
            MakeInvoiceForDailyCounts("INV-MID", new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc)),
            MakeInvoiceForDailyCounts("INV-END", endDate));
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 10)).Count.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 11)).Count.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 12)).Count.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyCountsAsync_GapFill_EmitsZeroRowsForMissingDays()
    {
        // Arrange
        _context.IssuedInvoices.Add(
            MakeInvoiceForDailyCounts("INV-1", new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc)));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _repository.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().BeInAscendingOrder();
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).Count.Should().Be(0);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).Count.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 3)).Count.Should().Be(0);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail to compile**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests" 2>&1 | tail -40
```
Expected: build error — `'IssuedInvoiceRepository' does not contain a definition for 'GetDailyCountsAsync'` (the method does not exist yet). This confirms the tests actually exercise new code, not something already passing.

- [ ] **Step 3: Add the method to the `IIssuedInvoiceRepository` interface**

In `backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`, the file currently reads:

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Invoices;

public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);

    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
}
```

Replace it with:

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Invoices;

public interface IIssuedInvoiceRepository : IRepository<IssuedInvoice, string>
{
    Task<IssuedInvoice?> GetByIdWithSyncHistoryAsync(string id, CancellationToken cancellationToken = default);

    Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);

    Task<PaginatedResult<IssuedInvoice>> GetPaginatedAsync(IssuedInvoiceFilters filters, CancellationToken cancellationToken = default);

    Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns daily invoice counts in the inclusive range [<paramref name="startDate"/>, <paramref name="endDate"/>].
    /// Missing dates are gap-filled with zero-count rows. <c>Date</c> values on the result are tagged
    /// <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default);

    Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement the method in `IssuedInvoiceRepository`**

In `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`, the file currently has this method (among others):

```csharp
    public async Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return await DbSet
            .Where(x => x.InvoiceDate >= start && x.InvoiceDate <= end)
            .ToListAsync(cancellationToken);
    }

    public Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
```

Insert a new method between `GetHeadersByDateAsync` and `RevertTrackedChangesAsync`:

```csharp
    public async Task<IEnumerable<IssuedInvoice>> GetHeadersByDateAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        return await DbSet
            .Where(x => x.InvoiceDate >= start && x.InvoiceDate <= end)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = startDate.ToUniversalTime();
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = endDate.ToUniversalTime();

        var startDateUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endDateUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        List<DailyInvoiceCount> results;

        if (dateType == ImportDateType.InvoiceDate)
        {
            var rawResults = await DbSet
                .Where(i => i.InvoiceDate >= startDateUnspecified && i.InvoiceDate <= endDateUnspecified)
                .GroupBy(i => new { Year = i.InvoiceDate.Year, Month = i.InvoiceDate.Month, Day = i.InvoiceDate.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Count = g.Count()
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyInvoiceCount
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                Count = r.Count
            }).ToList();
        }
        else
        {
            var rawResults = await DbSet
                .Where(i => i.LastSyncTime.HasValue &&
                            i.LastSyncTime.Value >= startDateUnspecified &&
                            i.LastSyncTime.Value <= endDateUnspecified)
                .GroupBy(i => new { Year = i.LastSyncTime!.Value.Year, Month = i.LastSyncTime!.Value.Month, Day = i.LastSyncTime!.Value.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    Count = g.Count()
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyInvoiceCount
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                Count = r.Count
            }).ToList();
        }

        var resultsByDate = results.ToDictionary(r => r.Date.Date);
        var filledResults = new List<DailyInvoiceCount>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            if (resultsByDate.TryGetValue(currentDate.Date, out var existingResult))
            {
                filledResults.Add(existingResult);
            }
            else
            {
                filledResults.Add(new DailyInvoiceCount
                {
                    Date = currentDate,
                    Count = 0
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return filledResults;
    }

    public Task RevertTrackedChangesAsync(IssuedInvoice entity, CancellationToken cancellationToken = default)
```

Add `using Anela.Heblo.Domain.Features.Analytics;` to the top of this file (needed for `DailyInvoiceCount` and `ImportDateType`) — the existing usings are:

```csharp
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
```

become:

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence.Repositories;
using Anela.Heblo.Xcc.Persistance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
```

- [ ] **Step 5: Run the tests again to verify they pass**

Run:
```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests" 2>&1 | tail -40
```
Expected: `Passed!` — all `IssuedInvoiceRepositoryTests` tests pass, including the 5 new `GetDailyCountsAsync_*` tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs \
        backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs
git commit -m "feat(invoices): add GetDailyCountsAsync to IIssuedInvoiceRepository"
```

---

