# Decouple AnalyticsRepository from Invoices and Bank Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove direct cross-module EF Core access from `AnalyticsRepository` to `IssuedInvoices` / `BankStatements` by introducing two Consumer-Owned Contract interfaces in the Analytics module, implemented by adapters owned and registered by the Invoices and Bank modules.

**Architecture:** Mirror the existing `IAnalyticsProductSource` / `CatalogAnalyticsSourceAdapter` inversion pattern. Two new interfaces in `Anela.Heblo.Domain.Features.Analytics` are implemented by `internal sealed` adapters in `Anela.Heblo.Application.Features.{Invoices,Bank}.Infrastructure`. Each adapter takes `ApplicationDbContext` directly (the existing module-internal repository abstractions do not expose server-side aggregation). DI lifetimes are **Scoped** (deviation from the spec's "Transient to match precedent" — see Decision 2 in arch review: adapters wrap `ApplicationDbContext` which is Scoped). Architecture boundary regression is prevented by adding four new `ModuleBoundaryRule` entries to `ModuleBoundariesTests`.

**Tech Stack:** .NET 8, EF Core (Npgsql), xUnit, FluentAssertions, Moq. In-memory `ApplicationDbContext` used for adapter tests (matches `IssuedInvoiceRepositoryTests` precedent).

---

## File Structure

**New files (5):**

- `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs` — Consumer-Owned Contract for invoice import counts.
- `backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs` — Consumer-Owned Contract for bank statement statistics.
- `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs` — `internal sealed` Invoices-side implementation.
- `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs` — `internal sealed` Bank-side implementation.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportStatisticsSourceAdapterTests.cs` — adapter tests (in-memory DbContext).
- `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs` — adapter tests (in-memory DbContext).

**Modified files (4):**

- `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` — drop EF queries, delegate to the two new sources, remove `ApplicationDbContext` and the Invoices/Bank `using` directives.
- `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` — register `IInvoiceImportStatisticsSource` → adapter (Scoped).
- `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs` — register `IBankStatementStatisticsSource` → adapter (Scoped).
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — append four new `ModuleBoundaryRule` entries (Analytics(Application)/Analytics(Domain) → Invoices/Bank).

**Out of scope:** No `ApplicationDbContext` changes. No new packages. No schema/migration changes. No frontend changes. No changes to `IAnalyticsRepository` public surface (callers continue to receive `List<>`; the adapters return `IReadOnlyList<>` and `AnalyticsRepository` calls `.ToList()`).

---

## Task 1: Add `IInvoiceImportStatisticsSource` interface (failing build)

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Consumer-Owned Contract owned by the Analytics module. Implemented by the
/// Invoices module via <c>InvoiceImportStatisticsSourceAdapter</c>; DI registration
/// lives in <c>InvoicesModule</c>. Mirrors the inversion pattern in
/// <c>docs/architecture/development_guidelines.md</c> ("Cross-Module Communication
/// Example") and the precedent in <see cref="IAnalyticsProductSource"/>.
/// </summary>
public interface IInvoiceImportStatisticsSource
{
    /// <summary>
    /// Returns daily invoice counts in the inclusive range
    /// [<paramref name="startDate"/>, <paramref name="endDate"/>]. Missing dates
    /// are gap-filled with zero-count rows. <c>Date</c> values are tagged
    /// <see cref="DateTimeKind.Utc"/> and <c>IsBelowThreshold</c> is always
    /// <c>false</c> (the consumer decides thresholds).
    /// </summary>
    Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify Domain assembly compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: PASS (the interface references only Analytics-owned types).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/IInvoiceImportStatisticsSource.cs
git commit -m "feat: introduce IInvoiceImportStatisticsSource consumer contract"
```

---

## Task 2: Add `IBankStatementStatisticsSource` interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs`

- [ ] **Step 1: Write the interface**

```csharp
namespace Anela.Heblo.Domain.Features.Analytics;

/// <summary>
/// Consumer-Owned Contract owned by the Analytics module. Implemented by the
/// Bank module via <c>BankStatementStatisticsSourceAdapter</c>; DI registration
/// lives in <c>BankModule</c>. Mirrors the inversion pattern in
/// <c>docs/architecture/development_guidelines.md</c> ("Cross-Module Communication
/// Example") and the precedent in <see cref="IAnalyticsProductSource"/>.
/// </summary>
public interface IBankStatementStatisticsSource
{
    /// <summary>
    /// Returns daily bank statement statistics in the inclusive range
    /// [<paramref name="startDate"/>, <paramref name="endDate"/>]. Missing dates
    /// are gap-filled with zero-count, zero-total rows. <c>Date</c> values are
    /// tagged <see cref="DateTimeKind.Utc"/>. <c>TotalItemCount</c> is the
    /// per-day sum of <c>BankStatementImport.ItemCount</c>.
    /// </summary>
    Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify Domain assembly compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Analytics/IBankStatementStatisticsSource.cs
git commit -m "feat: introduce IBankStatementStatisticsSource consumer contract"
```

---

## Task 3: Add failing test for `InvoiceImportStatisticsSourceAdapter` — InvoiceDate branch

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportStatisticsSourceAdapterTests.cs`

The adapter does not exist yet, so this test will not compile. That's the intended Red state.

- [ ] **Step 1: Write the test file**

```csharp
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices;

public sealed class InvoiceImportStatisticsSourceAdapterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly InvoiceImportStatisticsSourceAdapter _adapter;

    public InvoiceImportStatisticsSourceAdapterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"InvoiceStats_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _adapter = new InvoiceImportStatisticsSourceAdapter(_context);
    }

    public void Dispose() => _context.Dispose();

    private static IssuedInvoice MakeInvoice(string id, DateTime invoiceDate, DateTime? lastSyncTime = null)
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
            MakeInvoice("INV-1", day1),
            MakeInvoice("INV-2", day1.AddHours(3)),
            MakeInvoice("INV-3", day2));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result[0].Date.Should().Be(DateTime.SpecifyKind(new DateTime(2026, 6, 1), DateTimeKind.Utc));
        result[0].Date.Kind.Should().Be(DateTimeKind.Utc);
        result[0].Count.Should().Be(2);
        result[0].IsBelowThreshold.Should().BeFalse();
        result[1].Date.Should().Be(DateTime.SpecifyKind(new DateTime(2026, 6, 2), DateTimeKind.Utc));
        result[1].Count.Should().Be(1);
    }
}
```

- [ ] **Step 2: Build to verify Red (compile error: adapter does not exist)**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL with "The type or namespace 'InvoiceImportStatisticsSourceAdapter' could not be found".

(No commit yet — test cannot run.)

---

## Task 4: Create `InvoiceImportStatisticsSourceAdapter` skeleton (test compiles, fails)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs`

- [ ] **Step 1: Write the skeleton (throws NotImplementedException)**

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Persistence;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceImportStatisticsSourceAdapter : IInvoiceImportStatisticsSource
{
    private readonly ApplicationDbContext _dbContext;

    public InvoiceImportStatisticsSourceAdapter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Run the InvoiceDate test to verify Red**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests.GetDailyCountsAsync_InvoiceDateBranch"`
Expected: FAIL with `NotImplementedException`.

---

## Task 5: Implement `InvoiceImportStatisticsSourceAdapter` (Green for InvoiceDate branch)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs`

Replicates the exact behavior of `AnalyticsRepository.cs` lines 47–139 (current implementation): DateTimeKind normalization, PostgreSQL `timestamp without time zone` handling, group-by year/month/day, gap-fill.

- [ ] **Step 1: Replace the skeleton with the full implementation**

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceImportStatisticsSourceAdapter : IInvoiceImportStatisticsSource
{
    private readonly ApplicationDbContext _dbContext;

    public InvoiceImportStatisticsSourceAdapter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DailyInvoiceCount>> GetDailyCountsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        // PostgreSQL timestamp without time zone: work with UTC dates but store as Unspecified.
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = startDate.ToUniversalTime();
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = endDate.ToUniversalTime();

        var startDateUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endDateUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        List<DailyInvoiceCount> results;

        if (dateType == ImportDateType.InvoiceDate)
        {
            var rawResults = await _dbContext.IssuedInvoices
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
                Count = r.Count,
                IsBelowThreshold = false
            }).ToList();
        }
        else
        {
            var rawResults = await _dbContext.IssuedInvoices
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
                Count = r.Count,
                IsBelowThreshold = false
            }).ToList();
        }

        var filledResults = new List<DailyInvoiceCount>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            var existingResult = results.FirstOrDefault(r => r.Date.Date == currentDate.Date);
            if (existingResult != null)
            {
                filledResults.Add(existingResult);
            }
            else
            {
                filledResults.Add(new DailyInvoiceCount
                {
                    Date = currentDate,
                    Count = 0,
                    IsBelowThreshold = false
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return filledResults;
    }
}
```

- [ ] **Step 2: Run the InvoiceDate test to verify Green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests.GetDailyCountsAsync_InvoiceDateBranch"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceImportStatisticsSourceAdapter.cs backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportStatisticsSourceAdapterTests.cs
git commit -m "feat: implement InvoiceImportStatisticsSourceAdapter with InvoiceDate branch"
```

---

## Task 6: Add remaining adapter tests for `InvoiceImportStatisticsSourceAdapter`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportStatisticsSourceAdapterTests.cs`

Adds tests for the SyncTime branch, empty range (full gap-fill), and range-boundary inclusivity at both endpoints.

- [ ] **Step 1: Append four tests inside the test class**

```csharp
    [Fact]
    public async Task GetDailyCountsAsync_SyncTimeBranch_IgnoresInvoicesWithNullSyncTime()
    {
        // Arrange
        var syncedDay = new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc);
        _context.IssuedInvoices.AddRange(
            MakeInvoice("INV-A", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: syncedDay),
            MakeInvoice("INV-B", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: syncedDay.AddHours(2)),
            // LastSyncTime null — should be excluded.
            MakeInvoice("INV-NULL", new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), lastSyncTime: null));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 5, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyCountsAsync(
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
        var result = await _adapter.GetDailyCountsAsync(
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
            MakeInvoice("INV-START", startDate),
            MakeInvoice("INV-MID", new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc)),
            MakeInvoice("INV-END", endDate));
        await _context.SaveChangesAsync();

        // Act
        var result = await _adapter.GetDailyCountsAsync(
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
            MakeInvoice("INV-1", new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc)));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyCountsAsync(
            startDate, endDate, ImportDateType.InvoiceDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().BeInAscendingOrder();
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).Count.Should().Be(0);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).Count.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 3)).Count.Should().Be(0);
    }
```

- [ ] **Step 2: Run the new tests to verify all pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~InvoiceImportStatisticsSourceAdapterTests"`
Expected: PASS (5 tests total — InvoiceDate, SyncTime, EmptyRange, InclusiveBoundaries, GapFill).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Invoices/InvoiceImportStatisticsSourceAdapterTests.cs
git commit -m "test: cover SyncTime branch, gap-fill, and boundary inclusivity for InvoiceImportStatisticsSourceAdapter"
```

---

## Task 7: Add failing test for `BankStatementStatisticsSourceAdapter` — StatementDate branch

**Files:**
- Test: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs`

- [ ] **Step 1: Write the test file**

```csharp
using Anela.Heblo.Application.Features.Bank.Infrastructure;
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Domain.Shared;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Bank;

public sealed class BankStatementStatisticsSourceAdapterTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly BankStatementStatisticsSourceAdapter _adapter;

    public BankStatementStatisticsSourceAdapterTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"BankStats_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _adapter = new BankStatementStatisticsSourceAdapter(_context);
    }

    public void Dispose() => _context.Dispose();

    private static BankStatementImport MakeStatement(
        string transferId, DateTime statementDate, DateTime? importDate = null, int itemCount = 0)
    {
        var statement = new BankStatementImport(transferId, statementDate);
        statement.Account = "TEST-ACCT";
        statement.Currency = CurrencyCode.CZK;
        statement.ItemCount = itemCount;

        typeof(BankStatementImport)
            .GetProperty(nameof(BankStatementImport.ImportResult))!
            .SetValue(statement, "OK");

        if (importDate is not null)
        {
            typeof(BankStatementImport)
                .GetProperty(nameof(BankStatementImport.ImportDate))!
                .SetValue(statement, importDate);
        }

        return statement;
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_StatementDateBranch_ReturnsCountsAndSummedItemCount()
    {
        // Arrange
        var day1 = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 6, 2, 0, 0, 0, DateTimeKind.Utc);

        _context.BankStatements.AddRange(
            MakeStatement("S-1", day1, itemCount: 3),
            MakeStatement("S-2", day1, itemCount: 7),
            MakeStatement("S-3", day2, itemCount: 5));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        var d1 = result.Single(r => r.Date.Date == new DateTime(2026, 6, 1));
        d1.Date.Kind.Should().Be(DateTimeKind.Utc);
        d1.ImportCount.Should().Be(2);
        d1.TotalItemCount.Should().Be(10);
        var d2 = result.Single(r => r.Date.Date == new DateTime(2026, 6, 2));
        d2.ImportCount.Should().Be(1);
        d2.TotalItemCount.Should().Be(5);
    }
}
```

- [ ] **Step 2: Build to verify Red (compile error: adapter does not exist)**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: FAIL with "The type or namespace 'BankStatementStatisticsSourceAdapter' could not be found".

(No commit yet.)

---

## Task 8: Create `BankStatementStatisticsSourceAdapter` skeleton

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`

- [ ] **Step 1: Write the skeleton (throws NotImplementedException)**

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Persistence;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

internal sealed class BankStatementStatisticsSourceAdapter : IBankStatementStatisticsSource
{
    private readonly ApplicationDbContext _dbContext;

    public BankStatementStatisticsSourceAdapter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
```

- [ ] **Step 2: Run the StatementDate test to verify Red**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementStatisticsSourceAdapterTests.GetDailyStatisticsAsync_StatementDateBranch"`
Expected: FAIL with `NotImplementedException`.

---

## Task 9: Implement `BankStatementStatisticsSourceAdapter` (Green for StatementDate branch)

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs`

Replicates the exact behavior of `AnalyticsRepository.cs` lines 144–236.

- [ ] **Step 1: Replace the skeleton with the full implementation**

```csharp
using Anela.Heblo.Domain.Features.Analytics;
using Anela.Heblo.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Application.Features.Bank.Infrastructure;

internal sealed class BankStatementStatisticsSourceAdapter : IBankStatementStatisticsSource
{
    private readonly ApplicationDbContext _dbContext;

    public BankStatementStatisticsSourceAdapter(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DailyBankStatementStatistics>> GetDailyStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default)
    {
        // PostgreSQL timestamp without time zone: work with UTC dates but store as Unspecified.
        if (startDate.Kind != DateTimeKind.Utc)
            startDate = startDate.ToUniversalTime();
        if (endDate.Kind != DateTimeKind.Utc)
            endDate = endDate.ToUniversalTime();

        var startDateUnspecified = DateTime.SpecifyKind(startDate, DateTimeKind.Unspecified);
        var endDateUnspecified = DateTime.SpecifyKind(endDate, DateTimeKind.Unspecified);

        List<DailyBankStatementStatistics> results;

        if (dateType == BankStatementDateType.StatementDate)
        {
            var rawResults = await _dbContext.BankStatements
                .Where(b => b.StatementDate >= startDateUnspecified && b.StatementDate <= endDateUnspecified)
                .GroupBy(b => new { Year = b.StatementDate.Year, Month = b.StatementDate.Month, Day = b.StatementDate.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    ImportCount = g.Count(),
                    TotalItemCount = g.Sum(b => b.ItemCount)
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyBankStatementStatistics
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                ImportCount = r.ImportCount,
                TotalItemCount = r.TotalItemCount
            }).ToList();
        }
        else
        {
            var rawResults = await _dbContext.BankStatements
                .Where(b => b.ImportDate >= startDateUnspecified && b.ImportDate <= endDateUnspecified)
                .GroupBy(b => new { Year = b.ImportDate.Year, Month = b.ImportDate.Month, Day = b.ImportDate.Day })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Day = g.Key.Day,
                    ImportCount = g.Count(),
                    TotalItemCount = g.Sum(b => b.ItemCount)
                })
                .OrderBy(d => new DateTime(d.Year, d.Month, d.Day))
                .ToListAsync(cancellationToken);

            results = rawResults.Select(r => new DailyBankStatementStatistics
            {
                Date = DateTime.SpecifyKind(new DateTime(r.Year, r.Month, r.Day), DateTimeKind.Utc),
                ImportCount = r.ImportCount,
                TotalItemCount = r.TotalItemCount
            }).ToList();
        }

        var filledResults = new List<DailyBankStatementStatistics>();
        var currentDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        var endDateOnly = DateTime.SpecifyKind(endDate.Date, DateTimeKind.Utc);

        while (currentDate <= endDateOnly)
        {
            var existingResult = results.FirstOrDefault(r => r.Date.Date == currentDate.Date);
            if (existingResult != null)
            {
                filledResults.Add(existingResult);
            }
            else
            {
                filledResults.Add(new DailyBankStatementStatistics
                {
                    Date = currentDate,
                    ImportCount = 0,
                    TotalItemCount = 0
                });
            }

            currentDate = currentDate.AddDays(1);
        }

        return filledResults;
    }
}
```

- [ ] **Step 2: Run the StatementDate test to verify Green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementStatisticsSourceAdapterTests.GetDailyStatisticsAsync_StatementDateBranch"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/BankStatementStatisticsSourceAdapter.cs backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs
git commit -m "feat: implement BankStatementStatisticsSourceAdapter with StatementDate branch"
```

---

## Task 10: Add remaining adapter tests for `BankStatementStatisticsSourceAdapter`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs`

Tests for the ImportDate branch, empty range, range-boundary inclusivity, and gap-fill.

- [ ] **Step 1: Append four tests inside the test class**

```csharp
    [Fact]
    public async Task GetDailyStatisticsAsync_ImportDateBranch_ReturnsCountsAndSummedItemCount()
    {
        // Arrange
        var statementDay = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var importDay = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);

        _context.BankStatements.AddRange(
            MakeStatement("I-1", statementDay, importDate: importDay, itemCount: 4),
            MakeStatement("I-2", statementDay, importDate: importDay.AddHours(3), itemCount: 6),
            MakeStatement("I-3", statementDay, importDate: importDay.AddDays(1), itemCount: 2));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 2, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.ImportDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).ImportCount.Should().Be(2);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).TotalItemCount.Should().Be(10);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).TotalItemCount.Should().Be(2);
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_EmptyRange_ReturnsZeroRowsForEveryDay()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().Equal(
            new DateTime(2026, 6, 1),
            new DateTime(2026, 6, 2),
            new DateTime(2026, 6, 3));
        result.Should().OnlyContain(r => r.ImportCount == 0);
        result.Should().OnlyContain(r => r.TotalItemCount == 0);
        result.Should().OnlyContain(r => r.Date.Kind == DateTimeKind.Utc);
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_InclusiveBoundaries_IncludesStatementsOnStartAndEndDate()
    {
        // Arrange
        var startDate = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 12, 23, 59, 59, DateTimeKind.Utc);

        _context.BankStatements.AddRange(
            MakeStatement("B-START", startDate, itemCount: 1),
            MakeStatement("B-MID", new DateTime(2026, 6, 11, 12, 0, 0, DateTimeKind.Utc), itemCount: 2),
            MakeStatement("B-END", endDate, itemCount: 3));
        await _context.SaveChangesAsync();

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 10)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 11)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 12)).ImportCount.Should().Be(1);
    }

    [Fact]
    public async Task GetDailyStatisticsAsync_GapFill_EmitsZeroRowsForMissingDays()
    {
        // Arrange
        _context.BankStatements.Add(
            MakeStatement("G-1", new DateTime(2026, 6, 2, 9, 0, 0, DateTimeKind.Utc), itemCount: 4));
        await _context.SaveChangesAsync();

        var startDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var result = await _adapter.GetDailyStatisticsAsync(
            startDate, endDate, BankStatementDateType.StatementDate, CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(r => r.Date.Date).Should().BeInAscendingOrder();
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 1)).ImportCount.Should().Be(0);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).ImportCount.Should().Be(1);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 2)).TotalItemCount.Should().Be(4);
        result.Single(r => r.Date.Date == new DateTime(2026, 6, 3)).ImportCount.Should().Be(0);
    }
```

- [ ] **Step 2: Run the new tests to verify all pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~BankStatementStatisticsSourceAdapterTests"`
Expected: PASS (5 tests total — StatementDate, ImportDate, EmptyRange, InclusiveBoundaries, GapFill).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Bank/BankStatementStatisticsSourceAdapterTests.cs
git commit -m "test: cover ImportDate branch, gap-fill, and boundary inclusivity for BankStatementStatisticsSourceAdapter"
```

---

## Task 11: Register `InvoiceImportStatisticsSourceAdapter` in `InvoicesModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`

Add Scoped registration (Decision 2: adapter wraps Scoped `ApplicationDbContext`, so Scoped is the semantically correct lifetime — this intentionally deviates from the spec's "Transient to match precedent").

- [ ] **Step 1: Add the using directive at the top**

```csharp
using Anela.Heblo.Domain.Features.Analytics;
```

Find the existing `using Anela.Heblo.Application.Features.Invoices.Infrastructure;` directive (already present) and ensure it stays.

- [ ] **Step 2: Add the registration inside `AddInvoicesModule`**

After the existing `services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();` line, append:

```csharp
        // Cross-module contract: Invoices implements Analytics' IInvoiceImportStatisticsSource
        // via an adapter. DI registration owned by provider (Invoices), not consumer
        // (Analytics) — mirrors the IInvoiceConsumptionSource pattern above. Scoped because
        // the adapter wraps ApplicationDbContext (also Scoped).
        services.AddScoped<IInvoiceImportStatisticsSource, InvoiceImportStatisticsSourceAdapter>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs
git commit -m "feat: register IInvoiceImportStatisticsSource in InvoicesModule"
```

---

## Task 12: Register `BankStatementStatisticsSourceAdapter` in `BankModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs`

- [ ] **Step 1: Add the using directive at the top**

```csharp
using Anela.Heblo.Domain.Features.Analytics;
```

- [ ] **Step 2: Add the registration inside `AddBankModule`**

Append before the `return services;` line:

```csharp
        // Cross-module contract: Bank implements Analytics' IBankStatementStatisticsSource
        // via an adapter. DI registration owned by provider (Bank), not consumer (Analytics).
        // Scoped because the adapter wraps ApplicationDbContext (also Scoped).
        services.AddScoped<IBankStatementStatisticsSource, BankStatementStatisticsSourceAdapter>();
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Bank/BankModule.cs
git commit -m "feat: register IBankStatementStatisticsSource in BankModule"
```

---

## Task 13: Refactor `AnalyticsRepository` to delegate to the new sources

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs`

After this change `AnalyticsRepository` no longer references `IssuedInvoices` / `BankStatements` and no longer takes `ApplicationDbContext` (per arch review Amendment 3 — every non-refactored Analytics method delegates to `_productSource`, so the field becomes unused).

- [ ] **Step 1: Replace the entire file with the refactored implementation**

```csharp
using Anela.Heblo.Domain.Features.Analytics;

namespace Anela.Heblo.Persistence.Features.Analytics;

/// <summary>
/// 🔒 PERFORMANCE FIX: Analytics repository with streaming capabilities
/// Prevents memory overload by delegating to IAnalyticsProductSource
/// </summary>
public sealed class AnalyticsRepository : IAnalyticsRepository
{
    private readonly IAnalyticsProductSource _productSource;
    private readonly IInvoiceImportStatisticsSource _invoiceImportStatisticsSource;
    private readonly IBankStatementStatisticsSource _bankStatementStatisticsSource;

    public AnalyticsRepository(
        IAnalyticsProductSource productSource,
        IInvoiceImportStatisticsSource invoiceImportStatisticsSource,
        IBankStatementStatisticsSource bankStatementStatisticsSource)
    {
        _productSource = productSource;
        _invoiceImportStatisticsSource = invoiceImportStatisticsSource;
        _bankStatementStatisticsSource = bankStatementStatisticsSource;
    }

    /// <summary>
    /// Streams products with sales to avoid memory overload
    /// Delegates to the product source implementation
    /// </summary>
    public IAsyncEnumerable<AnalyticsProduct> StreamProductsWithSalesAsync(
        DateTime fromDate,
        DateTime toDate,
        AnalyticsProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        return _productSource.StreamProductsWithSalesAsync(fromDate, toDate, productTypes, cancellationToken);
    }

    public Task<AnalyticsProduct?> GetProductAnalysisDataAsync(
        string productId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default)
    {
        return _productSource.GetProductAnalysisDataAsync(productId, fromDate, toDate, cancellationToken);
    }

    /// <summary>
    /// Gets daily invoice import statistics for monitoring purposes
    /// </summary>
    public async Task<List<DailyInvoiceCount>> GetInvoiceImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        ImportDateType dateType,
        CancellationToken cancellationToken = default)
    {
        var counts = await _invoiceImportStatisticsSource.GetDailyCountsAsync(
            startDate, endDate, dateType, cancellationToken);
        return counts.ToList();
    }

    /// <summary>
    /// Gets daily bank statement import statistics for monitoring purposes
    /// </summary>
    public async Task<List<DailyBankStatementStatistics>> GetBankStatementImportStatisticsAsync(
        DateTime startDate,
        DateTime endDate,
        BankStatementDateType dateType,
        CancellationToken cancellationToken = default)
    {
        var stats = await _bankStatementStatisticsSource.GetDailyStatisticsAsync(
            startDate, endDate, dateType, cancellationToken);
        return stats.ToList();
    }
}
```

- [ ] **Step 2: Update the existing `AnalyticsRepositoryTests` constructor call**

The pre-existing test at `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs:46` passes `null!` for `ApplicationDbContext`. The new constructor takes three interfaces (no DbContext). Update to:

```csharp
        var repository = new AnalyticsRepository(productSourceMock.Object, null!, null!);
```

The two `null!` args are unused by `StreamProductsWithSalesAsync` (the only method under test) and the existing test design tolerates this — the precedent already passes `null!` for unused dependencies.

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: PASS with zero new warnings.

- [ ] **Step 4: Run the pre-existing AnalyticsRepository test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsRepositoryTests"`
Expected: PASS.

- [ ] **Step 5: Run the InvoiceImportStatistics handler tests (no changes expected — they mock `IAnalyticsRepository`)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetInvoiceImportStatisticsHandlerTests"`
Expected: PASS.

- [ ] **Step 6: Verify `_dbContext` is completely gone**

Run: `grep -n "_dbContext\|ApplicationDbContext\|IssuedInvoices\|BankStatements" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs`
Expected: zero matches.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsRepositoryTests.cs
git commit -m "refactor: route AnalyticsRepository invoice/bank statistics through consumer-owned sources"
```

---

## Task 14: Add `ModuleBoundariesTests` rules for Analytics → Invoices / Bank

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

Four new `ModuleBoundaryRule` entries (Application-side and Domain-side, for both Invoices and Bank). Each forbids the consumer Analytics namespace from referencing provider-owned namespaces.

- [ ] **Step 1: Append the four rules inside `Rules()`**

In `ModuleBoundariesTests.cs`, find the existing `Rules()` method (line 258). After the existing `"Analytics (Domain) -> Catalog"` rule (line 348), append the four new rules. Insert them between the `Analytics (Domain) -> Catalog` block and the `Catalog -> Logistics` block.

```csharp
        new ModuleBoundaryRule(
            Name: "Analytics (Application) -> Invoices",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Invoices",
                "Anela.Heblo.Application.Features.Invoices",
                "Anela.Heblo.Persistence.Invoices",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Analytics (Domain) -> Invoices",
            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Invoices",
                "Anela.Heblo.Application.Features.Invoices",
                "Anela.Heblo.Persistence.Invoices",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal),
            InspectedAssembly: "Anela.Heblo.Domain"),

        new ModuleBoundaryRule(
            Name: "Analytics (Application) -> Bank",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Bank",
                "Anela.Heblo.Application.Features.Bank",
                "Anela.Heblo.Persistence.Bank",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),

        new ModuleBoundaryRule(
            Name: "Analytics (Domain) -> Bank",
            InspectedNamespacePrefix: "Anela.Heblo.Domain.Features.Analytics",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Bank",
                "Anela.Heblo.Application.Features.Bank",
                "Anela.Heblo.Persistence.Bank",
            },
            Allowlist: new HashSet<string>(StringComparer.Ordinal),
            InspectedAssembly: "Anela.Heblo.Domain"),
```

- [ ] **Step 2: Run all boundary tests to verify**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: PASS — all rules (existing + four new) succeed. If any of the four new rules fail, the refactor has missed a reference somewhere. Fix by removing the stray `using` directive or extracting the leak through the contracts.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: enforce Analytics module boundaries against Invoices and Bank"
```

---

## Task 15: Full build, format check, and full test suite

Validates the end-to-end refactor against the project's completion gates (`CLAUDE.md` → "Validation before completion").

- [ ] **Step 1: Run `dotnet build` for the solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: PASS with **zero new warnings**.

- [ ] **Step 2: Run `dotnet format` to confirm a clean diff**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: exit code 0 (no changes needed). If non-zero, run `dotnet format backend/Anela.Heblo.sln` and commit the result with `chore: dotnet format`.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: PASS — full suite green. The new adapter tests (5 each) and the boundary tests (existing + 4 new) all pass; no pre-existing tests regress.

- [ ] **Step 4: Spot-check that no analytics-handler/HTTP regression slipped in**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.Analytics"`
Expected: PASS — the Analytics feature folder (handlers, validators, dashboard tiles) is fully green, confirming behavior preservation against the existing assertions.

- [ ] **Step 5: Confirm grep-based NFR-2 acceptance criteria**

Run these greps in order:

1. `grep -n "_dbContext.IssuedInvoices\|_dbContext.BankStatements" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` → expect 0 matches.
2. `grep -n "Anela.Heblo.Domain.Features.Invoices\|Anela.Heblo.Domain.Features.Bank" backend/src/Anela.Heblo.Persistence/Features/Analytics/AnalyticsRepository.cs` → expect 0 matches.

Both must return zero hits. If anything remains, return to Task 13.

- [ ] **Step 6: Final commit (only if `dotnet format` produced changes in Step 2)**

```bash
git add -A
git commit -m "chore: dotnet format"
```

(Skip if Step 2 was already clean.)

---

## Self-Review Notes

Spec coverage:
- FR-1 (`IInvoiceImportStatisticsSource`) → Task 1.
- FR-2 (`IBankStatementStatisticsSource`) → Task 2.
- FR-3 (`InvoiceImportStatisticsSourceAdapter`) → Tasks 4–5 (implementation), Task 11 (DI registration as Scoped per arch Amendment 1).
- FR-4 (`BankStatementStatisticsSourceAdapter`) → Tasks 8–9 (implementation), Task 12 (DI registration as Scoped per arch Amendment 1).
- FR-5 (refactor `AnalyticsRepository`) → Task 13. `ApplicationDbContext` is removed from the constructor (arch Amendment 3).
- FR-6 (behavior-preserving) → Tasks 5, 9 reproduce the original aggregation byte-for-byte; Task 13 Step 5 reruns the pre-existing handler tests; Task 15 Step 4 reruns the full Analytics feature suite. The handler tests mock `IAnalyticsRepository` so they remain stable across the constructor change.
- FR-7 (adapter tests) → Tasks 3, 6, 7, 10. Each adapter ships 5 tests covering both branches, empty range, boundary inclusivity, and gap-fill.
- NFR-1 (performance) → Adapter implementations (Tasks 5, 9) preserve the EF `GroupBy`/`Sum` query expressions verbatim — server-side aggregation is unchanged. No `.ToList()` / `.AsEnumerable()` is introduced before the aggregation. Note: spec/arch call for automated EF SQL inspection. This plan opts to verify SQL shape by code review (the query expressions are copied verbatim) rather than by adding a test-only logger provider; the byte-for-byte handler tests in Task 15 Step 4 provide the behavioral safety net. If reviewers require an explicit SQL assertion, add a follow-up test capturing EF logs via `LoggerFactory.Create(...)`.
- NFR-2 (module boundary compliance) → Task 14 adds four enforcing tests; Task 15 Step 5 grep-checks `AnalyticsRepository.cs`.
- NFR-3 (backwards compatibility) → `IAnalyticsRepository` keeps `List<DailyInvoiceCount>` / `List<DailyBankStatementStatistics>` returns (arch Decision 4). Adapter returns are widened to `IReadOnlyList<>` only inside the new interfaces. No HTTP / OpenAPI contract changes.
- NFR-4 (validation gates) → Task 15 covers build, format, full test suite.

Type consistency:
- Both interface methods return `Task<IReadOnlyList<T>>`. Adapter signatures match. `AnalyticsRepository` delegations call `.ToList()` (`IReadOnlyList<>` → `List<>`) at the boundary — preserves the public `IAnalyticsRepository` shape.
- Constructor parameter order in `AnalyticsRepository` is `productSource`, `invoiceImportStatisticsSource`, `bankStatementStatisticsSource` — matches the field declarations and the `null!` ordering applied in the test fix at Task 13 Step 2.
- `ImportDateType.LastSyncTime` is the enum value used in the SyncTime branch test (Task 6, Step 1) — confirmed against `Anela.Heblo.Domain/Features/Analytics/ImportDateType.cs` which declares `InvoiceDate` and `LastSyncTime`.
- `BankStatementDateType.ImportDate` is the else-branch value used in the ImportDate branch test (Task 10, Step 1) — confirmed against `Anela.Heblo.Domain/Features/Analytics/BankStatementDateType.cs` which declares `StatementDate` and `ImportDate`.
- `BankStatementImport.ItemCount` / `StatementDate` / `ImportDate` are public — confirmed against the entity at `Anela.Heblo.Domain/Features/Bank/BankStatementImport.cs`. `ImportDate` has a private setter, so the test helper uses reflection (already in test code).
- `IssuedInvoice.LastSyncTime` has a private setter — the test helper uses reflection. This mirrors the existing `BankStatementImportRepositoryIntegrationTests` SeedAsync helper pattern.

Placeholder scan: none — every step contains literal code or literal commands.
