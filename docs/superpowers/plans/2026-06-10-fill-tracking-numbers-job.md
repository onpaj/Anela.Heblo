# Fill Tracking Numbers Background Job Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Hangfire recurring job that finds Package rows with a null `TrackingNumber` (created within the last 3 days), fetches current label data from Shoptet, and fills in any tracking numbers that are now available.

**Architecture:** The job queries the package repository for recently-created rows with null `TrackingNumber`, groups them by `OrderCode` to make one Shoptet API call per order, matches labels by `PackageName == PackageNumber`, and updates only rows where a tracking number is now available. Rows where Shoptet still returns null are left alone and retried on the next run (every 10 minutes).

**Tech Stack:** .NET 8, EF Core 8, Hangfire, xUnit, Moq, FluentAssertions

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs` | Add 2 new methods |
| Modify | `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs` | Implement 2 new methods |
| Create | `backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs` | The recurring job |
| Create | `backend/test/Anela.Heblo.Tests/Features/Packaging/FillTrackingNumbersJobTests.cs` | Unit tests for the job |

---

## Task 1: Extend IPackageRepository with two new methods

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs`

- [ ] **Step 1: Add the two method signatures**

Replace the file content with:

```csharp
namespace Anela.Heblo.Domain.Features.Packaging;

public interface IPackageRepository
{
    Task<(List<Package> Items, int TotalCount)> GetPaginatedAsync(
        string? orderCode,
        string? customerName,
        string? packageNumber,
        IReadOnlyList<string>? shippingProviderCodes,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken = default);

    Task<Package?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task AddAsync(Package package, CancellationToken cancellationToken = default);
    Task DeleteAsync(Package package, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns packages created within the last <paramref name="daysBack"/> days
    /// whose <see cref="Package.TrackingNumber"/> is null.
    /// </summary>
    Task<List<Package>> GetWithNullTrackingNumberAsync(int daysBack, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a tracking number onto an existing package row.
    /// No-ops silently if the row no longer exists.
    /// </summary>
    Task SetTrackingNumberAsync(int id, string trackingNumber, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Verify the project still builds**

```bash
cd backend && dotnet build
```

Expected: Build succeeded, 0 errors.

---

## Task 2: Implement the two new methods in PackageRepository

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs`

- [ ] **Step 1: Add GetWithNullTrackingNumberAsync and SetTrackingNumberAsync**

Append these two methods before the closing `}` of the class (after `EscapeLike`):

```csharp
    public async Task<List<Package>> GetWithNullTrackingNumberAsync(
        int daysBack,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-daysBack);
        return await _db.Packages
            .AsNoTracking()
            .Where(p => p.TrackingNumber == null && p.CreatedAt >= cutoff)
            .ToListAsync(cancellationToken);
    }

    public async Task SetTrackingNumberAsync(
        int id,
        string trackingNumber,
        CancellationToken cancellationToken = default)
    {
        var package = await _db.Packages.FindAsync([id], cancellationToken);
        if (package is null)
            return;
        package.TrackingNumber = trackingNumber;
        await _db.SaveChangesAsync(cancellationToken);
    }
```

- [ ] **Step 2: Verify the project still builds**

```bash
cd backend && dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Packaging/IPackageRepository.cs \
        backend/src/Anela.Heblo.Persistence/Repositories/Packaging/PackageRepository.cs
git commit -m "feat(packaging): add GetWithNullTrackingNumber and SetTrackingNumber to IPackageRepository"
```

---

## Task 3: Write unit tests for FillTrackingNumbersJob (RED phase)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Packaging/FillTrackingNumbersJobTests.cs`

The job class does not exist yet — the tests will fail to compile until Task 4. Write them first.

- [ ] **Step 1: Create the test file**

```csharp
using Anela.Heblo.Application.Features.Packaging.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Packaging;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Packaging;

public class FillTrackingNumbersJobTests
{
    private static (
        FillTrackingNumbersJob Sut,
        Mock<IPackageRepository> Repo,
        Mock<IShipmentClient> Client,
        Mock<IRecurringJobStatusChecker> StatusChecker)
        MakeSut(bool jobEnabled = true)
    {
        var repo = new Mock<IPackageRepository>();
        var client = new Mock<IShipmentClient>();
        var statusChecker = new Mock<IRecurringJobStatusChecker>();
        statusChecker
            .Setup(s => s.IsJobEnabledAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(jobEnabled);
        var logger = NullLogger<FillTrackingNumbersJob>.Instance;
        var sut = new FillTrackingNumbersJob(repo.Object, client.Object, statusChecker.Object, logger);
        return (sut, repo, client, statusChecker);
    }

    private static Package SamplePackage(int id = 1, string orderCode = "ORD-1", string packageNumber = "PKG-1") =>
        new()
        {
            Id = id,
            OrderCode = orderCode,
            CustomerName = "Alice",
            PackageNumber = packageNumber,
            TrackingNumber = null,
            ShippingProviderCode = "PPL",
            ShipmentGuid = Guid.NewGuid(),
            PackedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    [Fact]
    public async Task ExecuteAsync_SkipsWork_WhenJobDisabled()
    {
        var (sut, repo, client, _) = MakeSut(jobEnabled: false);

        await sut.ExecuteAsync();

        repo.Verify(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        client.Verify(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNothing_WhenNoPackagesWithNullTracking()
    {
        var (sut, repo, client, _) = MakeSut();
        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await sut.ExecuteAsync();

        client.Verify(c => c.GetLabelsByOrderCodeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.SetTrackingNumberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UpdatesTrackingNumber_WhenShoptetReturnsIt()
    {
        var (sut, repo, client, _) = MakeSut();
        var package = SamplePackage(id: 5, orderCode: "ORD-42", packageNumber: "PKG-1");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([package]);

        client.Setup(c => c.GetLabelsByOrderCodeAsync("ORD-42", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel
                {
                    OrderCode = "ORD-42",
                    PackageName = "PKG-1",
                    ShipmentGuid = package.ShipmentGuid,
                    TrackingNumber = "70603624124",
                }
            ]);

        await sut.ExecuteAsync();

        repo.Verify(r => r.SetTrackingNumberAsync(5, "70603624124", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsPackage_WhenShoptetStillReturnsNullTracking()
    {
        var (sut, repo, client, _) = MakeSut();
        var package = SamplePackage(id: 3, orderCode: "ORD-5", packageNumber: "PKG-A");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([package]);

        client.Setup(c => c.GetLabelsByOrderCodeAsync("ORD-5", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { OrderCode = "ORD-5", PackageName = "PKG-A", TrackingNumber = null }
            ]);

        await sut.ExecuteAsync();

        repo.Verify(r => r.SetTrackingNumberAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MakesOneShoptetCallPerOrder_WhenOrderHasMultipleNullPackages()
    {
        var (sut, repo, client, _) = MakeSut();
        var pkg1 = SamplePackage(id: 1, orderCode: "ORD-10", packageNumber: "PKG-A");
        var pkg2 = SamplePackage(id: 2, orderCode: "ORD-10", packageNumber: "PKG-B");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([pkg1, pkg2]);

        client.Setup(c => c.GetLabelsByOrderCodeAsync("ORD-10", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { OrderCode = "ORD-10", PackageName = "PKG-A", TrackingNumber = "TRK-A" },
                new ShipmentLabel { OrderCode = "ORD-10", PackageName = "PKG-B", TrackingNumber = "TRK-B" },
            ]);

        await sut.ExecuteAsync();

        client.Verify(c => c.GetLabelsByOrderCodeAsync("ORD-10", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SetTrackingNumberAsync(1, "TRK-A", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.SetTrackingNumberAsync(2, "TRK-B", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ContinuesProcessing_WhenShoptetThrowsForOneOrder()
    {
        var (sut, repo, client, _) = MakeSut();
        var pkg1 = SamplePackage(id: 1, orderCode: "ORD-FAIL", packageNumber: "PKG-1");
        var pkg2 = SamplePackage(id: 2, orderCode: "ORD-OK", packageNumber: "PKG-2");

        repo.Setup(r => r.GetWithNullTrackingNumberAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([pkg1, pkg2]);

        client.Setup(c => c.GetLabelsByOrderCodeAsync("ORD-FAIL", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Shoptet 500"));

        client.Setup(c => c.GetLabelsByOrderCodeAsync("ORD-OK", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ShipmentLabel { OrderCode = "ORD-OK", PackageName = "PKG-2", TrackingNumber = "TRK-OK" }
            ]);

        await sut.ExecuteAsync();

        // ORD-OK still processed despite ORD-FAIL throwing
        repo.Verify(r => r.SetTrackingNumberAsync(2, "TRK-OK", It.IsAny<CancellationToken>()), Times.Once);
        // ORD-FAIL package not updated
        repo.Verify(r => r.SetTrackingNumberAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
```

- [ ] **Step 2: Confirm the tests don't compile yet (RED)**

```bash
cd backend && dotnet build
```

Expected: Build **fails** with errors like `The type or namespace name 'FillTrackingNumbersJob' could not be found`.

---

## Task 4: Implement FillTrackingNumbersJob (GREEN phase)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs`

- [ ] **Step 1: Create the job class**

```csharp
using Anela.Heblo.Application.Features.ShipmentLabels;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Domain.Features.Packaging;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Packaging.Infrastructure.Jobs;

public sealed class FillTrackingNumbersJob : IRecurringJob
{
    private const int DaysBack = 3;

    private readonly IPackageRepository _repo;
    private readonly IShipmentClient _client;
    private readonly IRecurringJobStatusChecker _statusChecker;
    private readonly ILogger<FillTrackingNumbersJob> _logger;

    public RecurringJobMetadata Metadata { get; } = new()
    {
        JobName = "fill-tracking-numbers",
        DisplayName = "Fill Tracking Numbers",
        Description = "Backfills TrackingNumber for recently-packed shipments where Shoptet had not yet generated the carrier label at scan time.",
        CronExpression = "*/10 * * * *",
        DefaultIsEnabled = true,
    };

    public FillTrackingNumbersJob(
        IPackageRepository repo,
        IShipmentClient client,
        IRecurringJobStatusChecker statusChecker,
        ILogger<FillTrackingNumbersJob> logger)
    {
        _repo = repo;
        _client = client;
        _statusChecker = statusChecker;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!await _statusChecker.IsJobEnabledAsync(Metadata.JobName, cancellationToken))
        {
            _logger.LogInformation("Job {JobName} is disabled. Skipping.", Metadata.JobName);
            return;
        }

        var packages = await _repo.GetWithNullTrackingNumberAsync(DaysBack, cancellationToken);
        if (packages.Count == 0)
            return;

        _logger.LogInformation(
            "FillTrackingNumbers: found {Count} package(s) with null TrackingNumber in the last {Days} days.",
            packages.Count, DaysBack);

        var byOrder = packages.GroupBy(p => p.OrderCode);
        var updated = 0;

        foreach (var group in byOrder)
        {
            var orderCode = group.Key;
            IReadOnlyList<ShipmentLabel> labels;

            try
            {
                labels = await _client.GetLabelsByOrderCodeAsync(orderCode, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "FillTrackingNumbers: failed to fetch labels for order {OrderCode}. Will retry next run.",
                    orderCode);
                continue;
            }

            var labelByPackageName = labels
                .Where(l => l.TrackingNumber is not null)
                .ToDictionary(l => l.PackageName, l => l.TrackingNumber!);

            foreach (var package in group)
            {
                if (!labelByPackageName.TryGetValue(package.PackageNumber, out var trackingNumber))
                    continue;

                await _repo.SetTrackingNumberAsync(package.Id, trackingNumber, cancellationToken);
                updated++;

                _logger.LogInformation(
                    "FillTrackingNumbers: set TrackingNumber={TrackingNumber} on Package {Id} (order {OrderCode}).",
                    trackingNumber, package.Id, orderCode);
            }
        }

        _logger.LogInformation("FillTrackingNumbers: updated {Updated}/{Total} package(s).", updated, packages.Count);
    }
}
```

- [ ] **Step 2: Run the tests (GREEN)**

```bash
cd backend && dotnet test --filter "FillTrackingNumbersJobTests" --no-build
```

Wait — build first:

```bash
cd backend && dotnet build && dotnet test --filter "FillTrackingNumbersJobTests"
```

Expected: 6 tests pass.

- [ ] **Step 3: Run the full test suite**

```bash
cd backend && dotnet test
```

Expected: all existing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Packaging/Infrastructure/Jobs/FillTrackingNumbersJob.cs \
        backend/test/Anela.Heblo.Tests/Features/Packaging/FillTrackingNumbersJobTests.cs
git commit -m "feat(packaging): FillTrackingNumbers recurring job backfills null TrackingNumbers from Shoptet"
```

---

## Task 5: Seed the job configuration (database)

The `RecurringJobDiscoveryService` auto-discovers and registers the job with Hangfire via reflection — no code registration needed. However, the job schedule is stored in the `RecurringJobConfigurations` table. This table is seeded by the `RecurringJobDiscoveryService` on startup for any jobs not yet present.

**No code changes are needed** for registration. The job will be active the next time the application starts.

- [ ] **Step 1: Verify auto-discovery picks up the new job**

Check `RecurringJobDiscoveryService` to confirm it scans the Application assembly. If so, no action needed.

```bash
grep -r "AddRecurringJobs\|RecurringJobDiscovery" backend/src --include="*.cs" -l
```

Expected: at least one file found.

- [ ] **Step 2: Run dotnet build and dotnet test one final time**

```bash
cd backend && dotnet build && dotnet test
```

Expected: Build succeeded, all tests pass.

- [ ] **Step 3: Final commit (if any files changed)**

```bash
git add -p
git commit -m "chore(packaging): verify FillTrackingNumbers job auto-discovery"
```

---

## Verification

After deploying to staging:

1. Check the Background Jobs UI (Hangfire dashboard) — the `fill-tracking-numbers` job should appear under recurring jobs.
2. Trigger it manually from the UI.
3. Check that order `126000035` now shows `70603624124` in the Sledovací č. column on the Zásilky page.
4. Monitor logs: look for `FillTrackingNumbers: updated` entries.
