# Fix `ForecastedDays` Always Null in Packing Materials List — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore an accurate `forecastedDays` value on every row of `GET /api/packing-materials` by loading the last-30-days `PackingMaterialLog` rows through a new bulk repository method, configuring EF Core so the `PackingMaterial._logs` aggregate collection is actually persisted, and deleting the misleading `*WithLogsAsync` repository methods.

**Architecture:** Aggregate pattern with `HasMany` + backing-field access for `PackingMaterial._logs` (mirroring the existing `_allocations` configuration). The list handler issues exactly two queries: one for materials via `GetAllAsync`, one for filtered logs via `GetRecentLogsForMaterialsAsync` that returns a `IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>` grouped in memory. The misleading `GetAllWithLogsAsync` / `GetByIdWithLogsAsync` methods are deleted; the single non-handler caller (`GetPackingMaterialLogsHandler`) is migrated to `GetByIdAsync` + the existing `GetRecentLogsAsync`. No schema migration, no API contract change.

**Tech Stack:** .NET 8, EF Core 8 (`Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.InMemory`, `Microsoft.EntityFrameworkCore.Sqlite` for query-count tests), MediatR, xUnit, FluentAssertions, Moq.

---

## File Structure

### Files to modify

| File | Responsibility | Change |
|---|---|---|
| `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` | Repository contract | Remove `GetAllWithLogsAsync`, `GetByIdWithLogsAsync`. Add `GetRecentLogsForMaterialsAsync`. Add XML docs to both `GetRecentLogs*` methods. |
| `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` | Repository implementation | Delete `GetAllWithLogsAsync`, `GetByIdWithLogsAsync`. Implement `GetRecentLogsForMaterialsAsync`. |
| `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs` | EF entity configuration | Add `HasMany`/`WithOne` for the `_logs` backing field with `PropertyAccessMode.Field`. |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` | List endpoint handler | Replace `GetAllWithLogsAsync` call with `GetAllAsync` + `GetRecentLogsForMaterialsAsync`. Inject `ILogger<>` and emit one Debug log line per request. |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs` | Logs endpoint handler | Replace `GetByIdWithLogsAsync` with `GetByIdAsync`. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` | Test double | Remove `GetAllWithLogsAsync` and `GetByIdWithLogsAsync`. Add `GetRecentLogsForMaterialsAsync` implementation using an in-memory log dictionary. |

### Files to create

| File | Responsibility |
|---|---|
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs` | FR-4 regression coverage. Asserts a `PackingMaterialLog` row is persisted after `UpdatePackingMaterialQuantityHandler.Handle(...)` and after `ConsumptionCalculationService.ProcessDailyConsumptionAsync(...)` run against a real `ApplicationDbContext`. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryRecentLogsTests.cs` | Integration coverage for `GetRecentLogsForMaterialsAsync`. Includes the empty-input branch. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs` | FR-1 branch coverage (numeric forecast, zero-quantity, no-logs-window) wired to a real `ApplicationDbContext` (InMemory) and the real `PackingMaterialRepository`. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs` | FR-2 acceptance via `Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor`. Uses SQLite in-memory so the interceptor reports realistic query counts. |

---

## Task 1: Configure EF for `PackingMaterial._logs` backing field (FR-4)

**Goal:** Teach EF Core that `PackingMaterial._logs` is a child collection of `PackingMaterialLog` so that `_logs.Add(log)` inside `PackingMaterial.UpdateQuantity` is observed by the change tracker and persisted on `SaveChangesAsync`. Failure mode without this change: the entity-side `_logs.Add(log)` is silent — no row is ever written, even though there are no exceptions.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs`

- [ ] **Step 1: Write the failing regression test for `UpdatePackingMaterialQuantityHandler`**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs` with the following content:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.UpdatePackingMaterialQuantity;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialLogPersistenceTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PackingMaterialRepository _repository;

    public PackingMaterialLogPersistenceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialLogPersistence_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PackingMaterialRepository(_context);
    }

    [Fact]
    public async Task UpdatePackingMaterialQuantityHandler_PersistsLogRow()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        var currentUser = new Mock<ICurrentUserService>();
        currentUser
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser { Id = "user-42" });

        var handler = new UpdatePackingMaterialQuantityHandler(_repository, currentUser.Object);

        // Act
        var response = await handler.Handle(
            new UpdatePackingMaterialQuantityRequest
            {
                Id = material.Id,
                NewQuantity = 80m,
                Date = new DateOnly(2026, 5, 21)
            },
            CancellationToken.None);

        // Assert
        var persistedLogs = await _context.Set<PackingMaterialLog>()
            .Where(l => l.PackingMaterialId == material.Id)
            .ToListAsync();

        persistedLogs.Should().HaveCount(1, "UpdateQuantity must persist exactly one log row");
        var log = persistedLogs.Single();
        log.OldQuantity.Should().Be(100m);
        log.NewQuantity.Should().Be(80m);
        log.LogType.Should().Be(LogEntryType.Manual);
        log.UserId.Should().Be("user-42");

        response.Material.CurrentQuantity.Should().Be(80m);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
```

Note: if `CurrentUser` lives in a different namespace, adjust the using line. To verify the type and namespace before writing the test:

```bash
grep -rn "class CurrentUser" backend/src --include="*.cs"
grep -rn "interface ICurrentUserService" backend/src --include="*.cs"
```

- [ ] **Step 2: Run the new test and confirm it fails**

Run from the repo root:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~PackingMaterialLogPersistenceTests.UpdatePackingMaterialQuantityHandler_PersistsLogRow"
```

Expected: **FAIL** with `persistedLogs.Should().HaveCount(1)` reporting `Expected count to be 1, but found 0`. This proves the bug — `_logs.Add(log)` is invisible to EF without the relationship configuration.

- [ ] **Step 3: Add the EF configuration for `_logs`**

Edit `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs`. At the end of `Configure(...)` — after the existing `HasIndex` line — append:

```csharp
        builder.HasMany(typeof(PackingMaterialLog), "_logs")
            .WithOne()
            .HasForeignKey(nameof(PackingMaterialLog.PackingMaterialId))
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation("_logs")!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
```

The `SetPropertyAccessMode(PropertyAccessMode.Field)` line is load-bearing — without it, EF tries to write through the read-only `Logs` property and fails silently.

- [ ] **Step 4: Run the test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~PackingMaterialLogPersistenceTests.UpdatePackingMaterialQuantityHandler_PersistsLogRow"
```

Expected: **PASS**.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialConfiguration.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs
git commit -m "fix(packing-materials): persist PackingMaterialLog rows via aggregate

Configure HasMany/WithOne for PackingMaterial._logs with backing-field
property access. Without this, _logs.Add(log) in UpdateQuantity is
invisible to EF and no log row is persisted."
```

---

## Task 2: Add regression test for `ConsumptionCalculationService.ProcessDailyConsumptionAsync`

**Goal:** Lock down that the daily consumption job also persists its `PackingMaterialLog` rows. Task 1's configuration fixes both this path and the update-quantity handler, but the arch review explicitly calls out that this second call site needs its own regression coverage to avoid future drift.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs`

- [ ] **Step 1: Add the test and run it**

Append the following `[Fact]` to `PackingMaterialLogPersistenceTests.cs` inside the existing class. Note that this test instantiates `ConsumptionCalculationService` with the **real** repository (which writes through `_context`) and a stub invoice repository.

```csharp
    [Fact]
    public async Task ProcessDailyConsumptionAsync_PersistsLogRow()
    {
        // Arrange
        var material = new PackingMaterial("Daily Box", 5m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        var invoiceRepo = new MockIssuedInvoiceRepository();
        var logger = new MockLogger<Anela.Heblo.Application.Features.PackingMaterials.Services.ConsumptionCalculationService>();
        var service = new Anela.Heblo.Application.Features.PackingMaterials.Services.ConsumptionCalculationService(
            _repository, invoiceRepo, logger);

        var date = new DateOnly(2026, 5, 21);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        result.WasRun.Should().BeTrue();

        var persistedLogs = await _context.Set<PackingMaterialLog>()
            .Where(l => l.PackingMaterialId == material.Id)
            .ToListAsync();

        persistedLogs.Should().HaveCount(1, "daily processing must persist one log row per processed material");
        persistedLogs.Single().LogType.Should().Be(LogEntryType.AutomaticConsumption);
    }
```

Note: `MockIssuedInvoiceRepository` and `MockLogger<T>` already exist in `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/`. The fully qualified type name for `ConsumptionCalculationService` keeps the file's `using` directives minimal.

`PackingMaterialRepository` needs to expose the `AddConsumptionRowsAsync` and `HasDailyProcessingBeenRunAsync` flow, which it already does — confirm by reading `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` lines 34-64.

- [ ] **Step 2: Run the test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~PackingMaterialLogPersistenceTests.ProcessDailyConsumptionAsync_PersistsLogRow"
```

Expected: **PASS** (Task 1's config already covers this code path; the test pins the behavior).

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs
git commit -m "test(packing-materials): pin log persistence for daily processing"
```

---

## Task 3: Add `GetRecentLogsForMaterialsAsync` bulk method (FR-2)

**Goal:** Provide a single repository method that returns the last-30-days logs for a set of material ids, keyed by material id, with no DB round-trip on empty input.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryRecentLogsTests.cs`
- Modify: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`

- [ ] **Step 1: Write the failing integration test**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryRecentLogsTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialRepositoryRecentLogsTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PackingMaterialRepository _repository;

    public PackingMaterialRepositoryRecentLogsTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialRecentLogs_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PackingMaterialRepository(_context);
    }

    [Fact]
    public async Task GetRecentLogsForMaterialsAsync_ReturnsLogsGroupedByMaterialId_WithinWindow()
    {
        // Arrange
        var m1 = new PackingMaterial("M1", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new PackingMaterial("M2", 1m, ConsumptionType.PerDay, 100m);
        var m3 = new PackingMaterial("M3", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddRangeAsync(m1, m2, m3);
        await _context.SaveChangesAsync();

        var inWindow = DateTime.UtcNow.AddDays(-5);
        var outOfWindow = DateTime.UtcNow.AddDays(-45);

        var logs = new[]
        {
            CreateLog(m1.Id, 100m, 90m, inWindow),
            CreateLog(m1.Id, 90m, 80m, inWindow.AddHours(1)),
            CreateLog(m2.Id, 100m, 70m, inWindow),
            CreateLog(m2.Id, 70m, 60m, outOfWindow), // outside window
        };
        await _context.Set<PackingMaterialLog>().AddRangeAsync(logs);
        await _context.SaveChangesAsync();

        var fromDate = DateTime.UtcNow.AddMonths(-1);

        // Act
        var result = await _repository.GetRecentLogsForMaterialsAsync(
            new[] { m1.Id, m2.Id, m3.Id },
            fromDate,
            CancellationToken.None);

        // Assert
        result.Should().ContainKey(m1.Id);
        result[m1.Id].Should().HaveCount(2);
        result.Should().ContainKey(m2.Id);
        result[m2.Id].Should().HaveCount(1, "the out-of-window log is excluded");
        result.Should().NotContainKey(m3.Id, "materials without qualifying logs are absent");
    }

    [Fact]
    public async Task GetRecentLogsForMaterialsAsync_ReturnsEmptyDictionary_WhenInputIsEmpty()
    {
        // Act
        var result = await _repository.GetRecentLogsForMaterialsAsync(
            Array.Empty<int>(),
            DateTime.UtcNow.AddMonths(-1),
            CancellationToken.None);

        // Assert
        result.Should().BeEmpty();
    }

    private static PackingMaterialLog CreateLog(int materialId, decimal oldQty, decimal newQty, DateTime createdAt)
    {
        var log = new PackingMaterialLog(
            materialId,
            DateOnly.FromDateTime(createdAt),
            oldQty,
            newQty,
            LogEntryType.Manual);

        typeof(PackingMaterialLog)
            .GetProperty(nameof(PackingMaterialLog.CreatedAt))!
            .SetValue(log, createdAt);

        return log;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
```

- [ ] **Step 2: Run the new tests and confirm they fail to compile**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~PackingMaterialRepositoryRecentLogsTests"
```

Expected: **BUILD FAILURE** — `'PackingMaterialRepository' does not contain a definition for 'GetRecentLogsForMaterialsAsync'`.

- [ ] **Step 3: Add the method to the repository interface**

Edit `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`. Insert the new method below `GetRecentLogsAsync` (line 9). Final file content:

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public interface IPackingMaterialRepository : IRepository<PackingMaterial, int>
{
    Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(int packingMaterialId, DateTime fromDate, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default);
    Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
```

The `GetAllWithLogsAsync` and `GetByIdWithLogsAsync` methods are left in place for now — they will be deleted in Task 7 after their last caller is migrated in Task 6. This keeps the build green between tasks.

- [ ] **Step 4: Add the implementation in `PackingMaterialRepository`**

Edit `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`. Add this method below the existing `GetRecentLogsAsync` (after line 32):

```csharp
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default)
    {
        var ids = packingMaterialIds as IReadOnlyCollection<int> ?? packingMaterialIds.ToArray();
        if (ids.Count == 0)
        {
            return new Dictionary<int, IReadOnlyList<PackingMaterialLog>>();
        }

        var logs = await Context.Set<PackingMaterialLog>()
            .Where(log => ids.Contains(log.PackingMaterialId) && log.CreatedAt >= fromDate)
            .OrderByDescending(log => log.CreatedAt)
            .ToListAsync(cancellationToken);

        return logs
            .GroupBy(log => log.PackingMaterialId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PackingMaterialLog>)g.ToList());
    }
```

- [ ] **Step 5: Add the stub to `MockPackingMaterialRepository`**

Edit `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`. Add a backing field for stored logs and the new method. After the existing `GetRecentLogsAsync` (line 48-51), insert:

```csharp
    public Dictionary<int, List<PackingMaterialLog>> RecentLogsByMaterial { get; } = new();

    public Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default)
    {
        var ids = packingMaterialIds.ToHashSet();
        var dict = new Dictionary<int, IReadOnlyList<PackingMaterialLog>>();
        foreach (var kvp in RecentLogsByMaterial)
        {
            if (!ids.Contains(kvp.Key)) continue;
            var filtered = kvp.Value
                .Where(l => l.CreatedAt >= fromDate)
                .OrderByDescending(l => l.CreatedAt)
                .ToList();
            if (filtered.Count > 0)
            {
                dict[kvp.Key] = filtered;
            }
        }
        return Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>>(dict);
    }
```

- [ ] **Step 6: Run the new tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~PackingMaterialRepositoryRecentLogsTests"
```

Expected: **PASS** for both `GetRecentLogsForMaterialsAsync_ReturnsLogsGroupedByMaterialId_WithinWindow` and `GetRecentLogsForMaterialsAsync_ReturnsEmptyDictionary_WhenInputIsEmpty`.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs \
        backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryRecentLogsTests.cs
git commit -m "feat(packing-materials): add bulk GetRecentLogsForMaterialsAsync repository method"
```

---

## Task 4: Wire `GetPackingMaterialsListHandler` to the bulk method (FR-1, NFR-4)

**Goal:** Stop calling the (broken) `GetAllWithLogsAsync` and instead use `GetAllAsync` + `GetRecentLogsForMaterialsAsync`. Emit one Debug log line per request summarizing forecast coverage (NFR-4).

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs`

- [ ] **Step 1: Write the three-branch failing test**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs`:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetPackingMaterialsListHandlerTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PackingMaterialRepository _repository;
    private readonly GetPackingMaterialsListHandler _handler;

    public GetPackingMaterialsListHandlerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialsList_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PackingMaterialRepository(_context);
        _handler = new GetPackingMaterialsListHandler(
            _repository,
            NullLogger<GetPackingMaterialsListHandler>.Instance);
    }

    [Fact]
    public async Task Handle_ReturnsNumericForecast_WhenMaterialHasNegativeChangeLogsInWindow()
    {
        // Arrange
        var material = new PackingMaterial("WithLogs", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        var inWindow = DateTime.UtcNow.AddDays(-3);
        var log1 = CreateLog(material.Id, oldQty: 100m, newQty: 90m, createdAt: inWindow);
        var log2 = CreateLog(material.Id, oldQty: 90m, newQty: 80m, createdAt: inWindow.AddHours(2));
        await _context.Set<PackingMaterialLog>().AddRangeAsync(log1, log2);
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        var dto = response.Materials.Single();
        dto.ForecastedDays.Should().NotBeNull();
        // CurrentQuantity = 100, avg daily consumption = 10 → 100 / 10 = 10
        dto.ForecastedDays.Should().Be(10m);
    }

    [Fact]
    public async Task Handle_ReturnsZeroForecast_WhenCurrentQuantityIsZero()
    {
        // Arrange
        var material = new PackingMaterial("ZeroQty", 1m, ConsumptionType.PerDay, 0m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        response.Materials.Single().ForecastedDays.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_ReturnsNullForecast_WhenNoQualifyingLogsInWindow()
    {
        // Arrange
        var material = new PackingMaterial("NoLogs", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        // Act
        var response = await _handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        response.Materials.Single().ForecastedDays.Should().BeNull();
    }

    private static PackingMaterialLog CreateLog(int materialId, decimal oldQty, decimal newQty, DateTime createdAt)
    {
        var log = new PackingMaterialLog(
            materialId,
            DateOnly.FromDateTime(createdAt),
            oldQty,
            newQty,
            LogEntryType.Manual);

        typeof(PackingMaterialLog)
            .GetProperty(nameof(PackingMaterialLog.CreatedAt))!
            .SetValue(log, createdAt);

        return log;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
```

- [ ] **Step 2: Run the new tests and confirm two of them fail**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetPackingMaterialsListHandlerTests"
```

Expected:
- `Handle_ReturnsNumericForecast_WhenMaterialHasNegativeChangeLogsInWindow` — **FAIL** (`ForecastedDays` is `null` because the handler still reads `material.Logs`, which is empty).
- `Handle_ReturnsZeroForecast_WhenCurrentQuantityIsZero` — **PASS** (zero-quantity short-circuit returns 0).
- `Handle_ReturnsNullForecast_WhenNoQualifyingLogsInWindow` — **PASS** (no logs → `decimal.MaxValue` → `null`).

Also: the test constructor passes `NullLogger` as a second argument, but the current handler only takes one. So the **build will fail first**. That is intentional — the next step adds the constructor argument.

- [ ] **Step 3: Refactor the handler**

Replace the entire contents of `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` with:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;

public class GetPackingMaterialsListHandler : IRequestHandler<GetPackingMaterialsListRequest, GetPackingMaterialsListResponse>
{
    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetPackingMaterialsListHandler> _logger;

    public GetPackingMaterialsListHandler(
        IPackingMaterialRepository repository,
        ILogger<GetPackingMaterialsListHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetPackingMaterialsListResponse> Handle(
        GetPackingMaterialsListRequest request,
        CancellationToken cancellationToken)
    {
        var materials = (await _repository.GetAllAsync(cancellationToken)).ToList();
        var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);

        var logsByMaterial = await _repository.GetRecentLogsForMaterialsAsync(
            materials.Select(m => m.Id),
            oneMonthAgo,
            cancellationToken);

        var withForecast = 0;
        var withoutForecast = 0;
        var totalLogs = 0;

        var materialDtos = materials.Select(material =>
        {
            var recentLogs = logsByMaterial.TryGetValue(material.Id, out var logs)
                ? logs.ToList()
                : new List<PackingMaterialLog>();
            totalLogs += recentLogs.Count;

            var forecastedDays = material.CalculateForecastedDays(recentLogs);
            var displayForecast = forecastedDays == decimal.MaxValue
                ? null
                : (decimal?)Math.Round(forecastedDays, 1);

            if (displayForecast.HasValue) withForecast++;
            else withoutForecast++;

            return new PackingMaterialDto
            {
                Id = material.Id,
                Name = material.Name,
                ConsumptionRate = material.ConsumptionRate,
                ConsumptionType = material.ConsumptionType,
                ConsumptionTypeText = GetConsumptionTypeText(material.ConsumptionType),
                CurrentQuantity = material.CurrentQuantity,
                ForecastedDays = displayForecast,
                CreatedAt = material.CreatedAt,
                UpdatedAt = material.UpdatedAt
            };
        }).ToList();

        _logger.LogDebug(
            "PackingMaterials list: materials={Count}, logsLoaded={LogCount}, withForecast={WithForecast}, withoutForecast={WithoutForecast}",
            materialDtos.Count, totalLogs, withForecast, withoutForecast);

        return new GetPackingMaterialsListResponse
        {
            Materials = materialDtos
        };
    }

    private static string GetConsumptionTypeText(ConsumptionType type) => type switch
    {
        ConsumptionType.PerOrder => "za zakázku",
        ConsumptionType.PerProduct => "za produkt",
        ConsumptionType.PerDay => "za den",
        _ => type.ToString()
    };
}
```

Note: this is the only consumer of `GetAllWithLogsAsync` in `backend/src/`. After this change the method has no production callers, but it still exists on the interface — Task 7 deletes it.

- [ ] **Step 4: Run the tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~GetPackingMaterialsListHandlerTests"
```

Expected: **PASS** for all three branch tests.

- [ ] **Step 5: Run the existing PackingMaterials test suite to confirm no regression**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.PackingMaterials"
```

Expected: **PASS** (all existing handler/service tests continue to pass).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetPackingMaterialsListHandlerTests.cs
git commit -m "fix(packing-materials): compute forecastedDays via bulk log query

Replace GetAllWithLogsAsync (which never loaded logs) with GetAllAsync +
GetRecentLogsForMaterialsAsync. Emit one debug log line per request
summarizing forecast coverage."
```

---

## Task 5: Add query-count test (FR-2 acceptance)

**Goal:** Prove the list endpoint issues exactly two database round-trips, regardless of material count.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`

EF InMemory does not flow queries through `IDbCommandInterceptor` (it isn't a relational provider). The test uses SQLite in-memory so the interceptor sees the real reader executions.

- [ ] **Step 1: Confirm `Microsoft.EntityFrameworkCore.Sqlite` is already referenced**

```bash
grep -l "Microsoft.EntityFrameworkCore.Sqlite" backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

If the package is not present, add it:

```bash
dotnet add backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj package Microsoft.EntityFrameworkCore.Sqlite
```

- [ ] **Step 2: Write the test**

Create `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs`:

```csharp
using System.Data.Common;
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetPackingMaterialsList;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialsListQueryCountTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CountingInterceptor _interceptor;
    private readonly ApplicationDbContext _context;

    public PackingMaterialsListQueryCountTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _interceptor = new CountingInterceptor();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(_interceptor)
            .Options;

        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
    }

    [Fact]
    public async Task Handle_IssuesExactlyTwoReaderExecutions()
    {
        // Arrange
        var m1 = new PackingMaterial("M1", 1m, ConsumptionType.PerDay, 100m);
        var m2 = new PackingMaterial("M2", 1m, ConsumptionType.PerDay, 100m);
        await _context.PackingMaterials.AddRangeAsync(m1, m2);
        await _context.SaveChangesAsync();
        _interceptor.Reset();

        var repository = new PackingMaterialRepository(_context);
        var handler = new GetPackingMaterialsListHandler(
            repository,
            NullLogger<GetPackingMaterialsListHandler>.Instance);

        // Act
        await handler.Handle(new GetPackingMaterialsListRequest(), CancellationToken.None);

        // Assert
        _interceptor.ReaderExecutions.Should().Be(2,
            "the list handler should issue one query for materials and one bulk query for logs");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private sealed class CountingInterceptor : DbCommandInterceptor
    {
        public int ReaderExecutions { get; private set; }

        public void Reset() => ReaderExecutions = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderExecutions++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
```

- [ ] **Step 3: Run the test and confirm it passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~PackingMaterialsListQueryCountTests"
```

Expected: **PASS**. If `EnsureCreated()` fails on SQLite because of `decimal(18,6)` types or other PostgreSQL-specific column types, the test will surface that. In that case, gate problematic entities with a no-op or use raw SQL `CREATE TABLE` for just the two tables needed; document the chosen approach in the test file header comment.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsListQueryCountTests.cs \
        backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
git commit -m "test(packing-materials): pin list endpoint to exactly two DB round-trips"
```

---

## Task 6: Migrate `GetPackingMaterialLogsHandler` off `GetByIdWithLogsAsync` (FR-3 prep)

**Goal:** Remove the only remaining caller of `GetByIdWithLogsAsync` so it can be deleted in Task 7. The handler already loads its own logs via `GetRecentLogsAsync`, so swapping the entity lookup for `GetByIdAsync` is sufficient.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs`

- [ ] **Step 1: Verify the current behavior is covered (read-only check)**

```bash
grep -rln "GetPackingMaterialLogsHandler" backend/test
```

If no test file exists, that's fine — Task 7's build/test gate will catch regressions. If a test exists, run it now and note the result so the next step has a baseline.

- [ ] **Step 2: Replace `GetByIdWithLogsAsync` with `GetByIdAsync`**

Edit `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs` line 21. Change:

```csharp
        var material = await _repository.GetByIdWithLogsAsync(request.PackingMaterialId, cancellationToken);
```

to:

```csharp
        var material = await _repository.GetByIdAsync(request.PackingMaterialId, cancellationToken);
```

No other lines change — `recentLogs` is already loaded separately via `GetRecentLogsAsync` (line 28).

- [ ] **Step 3: Build and run the full test suite**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.PackingMaterials"
```

Expected: **PASS**.

- [ ] **Step 4: Verify no remaining callers of `*WithLogsAsync`**

```bash
grep -rn "WithLogsAsync" backend/src
```

Expected output: only the definitions in `IPackingMaterialRepository.cs` and `PackingMaterialRepository.cs` — no callers.

```bash
grep -rn "WithLogsAsync" backend/test
```

Expected: only the implementations inside `MockPackingMaterialRepository.cs`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs
git commit -m "refactor(packing-materials): GetPackingMaterialLogsHandler uses GetByIdAsync"
```

---

## Task 7: Delete `GetAllWithLogsAsync` and `GetByIdWithLogsAsync` (FR-3)

**Goal:** Remove the misleading methods from the interface, implementation, and test mock now that no callers remain.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`

- [ ] **Step 1: Remove the two methods from the interface**

Edit `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`. Delete lines 7-8:

```csharp
    Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Remove the implementations from `PackingMaterialRepository`**

Edit `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`. Delete lines 14-24:

```csharp
    public async Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default)
    {
        // For now, return materials without logs since we removed the navigation property
        // We'll load logs separately when needed
        return await DbSet.ToListAsync(cancellationToken);
    }

    public async Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(pm => pm.Id == id, cancellationToken);
    }
```

- [ ] **Step 3: Remove the stubs from `MockPackingMaterialRepository`**

Edit `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`. Delete lines 37-46:

```csharp
    public Task<IEnumerable<PackingMaterial>> GetAllWithLogsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<PackingMaterial>>(_materials);
    }

    public Task<PackingMaterial?> GetByIdWithLogsAsync(int id, CancellationToken cancellationToken = default)
    {
        var material = _materials.FirstOrDefault(m => m.Id == id);
        return Task.FromResult(material);
    }
```

- [ ] **Step 4: Confirm zero remaining matches**

```bash
grep -rn "WithLogsAsync" backend
```

Expected: **no matches** anywhere in the repository.

- [ ] **Step 5: Build the solution and run all PackingMaterials tests**

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.PackingMaterials"
```

Expected: **PASS**.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs \
        backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs
git commit -m "refactor(packing-materials): remove misleading *WithLogsAsync repository methods

These methods never loaded logs. All callers have been migrated to
GetAllAsync / GetByIdAsync plus the new GetRecentLogsForMaterialsAsync."
```

---

## Task 8: Document the `GetRecentLogs*` contract (FR-5)

**Goal:** Add XML doc comments to both recent-log methods on the repository interface, stating the inclusive `fromDate` semantics and the descending-`CreatedAt` ordering.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`

- [ ] **Step 1: Add the XML docs**

Edit `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`. After the FR-3 deletions in Task 7, the file should now look like the version below — including the new doc comments:

```csharp
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public interface IPackingMaterialRepository : IRepository<PackingMaterial, int>
{
    /// <summary>
    /// Returns logs for a single packing material whose <see cref="PackingMaterialLog.CreatedAt"/>
    /// is greater than or equal to <paramref name="fromDate"/>, ordered by <c>CreatedAt</c> descending.
    /// </summary>
    /// <param name="packingMaterialId">The packing material identifier.</param>
    /// <param name="fromDate">Inclusive lower bound on <c>CreatedAt</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching logs, newest first.</returns>
    Task<IEnumerable<PackingMaterialLog>> GetRecentLogsAsync(
        int packingMaterialId,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bulk variant of <see cref="GetRecentLogsAsync"/>. Returns a dictionary keyed by
    /// <see cref="PackingMaterialLog.PackingMaterialId"/>. Materials with no qualifying logs in the window
    /// are absent from the result — callers must treat absence as an empty list. Each material's logs
    /// are ordered by <c>CreatedAt</c> descending.
    /// </summary>
    /// <param name="packingMaterialIds">
    /// The packing material identifiers to load logs for. When empty, the method returns an empty
    /// dictionary without executing a database query.
    /// </param>
    /// <param name="fromDate">Inclusive lower bound on <c>CreatedAt</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Dictionary of matching logs grouped by material id.</returns>
    Task<IReadOnlyDictionary<int, IReadOnlyList<PackingMaterialLog>>> GetRecentLogsForMaterialsAsync(
        IEnumerable<int> packingMaterialIds,
        DateTime fromDate,
        CancellationToken cancellationToken = default);

    Task<bool> HasDailyProcessingBeenRunAsync(DateOnly date, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterial>> GetAllWithAllocationsAsync(CancellationToken cancellationToken = default);
    Task<PackingMaterial?> GetByIdWithAllocationsAsync(int id, CancellationToken cancellationToken = default);
    Task AddConsumptionRowsAsync(IEnumerable<PackingMaterialConsumption> rows, CancellationToken cancellationToken = default);
    Task<IEnumerable<PackingMaterialConsumption>> GetConsumptionsByDateAsync(DateOnly date, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Build to confirm no syntax errors**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **PASS** (no analyzer warnings for missing XML on `internal` members; the file already had public methods without docs, so no new warnings appear).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs
git commit -m "docs(packing-materials): document GetRecentLogs* contract on repository interface"
```

---

## Task 9: Final validation

**Goal:** Confirm the full build, formatting, and test suite are clean before declaring the work done.

- [ ] **Step 1: Format C# files**

```bash
dotnet format backend/Anela.Heblo.sln
```

Expected: no diagnostic output (or only auto-applied formatting changes that you then stage).

- [ ] **Step 2: Run the full backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: **0 Errors, 0 Warnings** (or no new warnings beyond pre-existing ones).

- [ ] **Step 3: Run the entire test project**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: **PASS** for every test.

- [ ] **Step 4: Sanity-check for accidental schema-affecting changes**

```bash
git diff main -- backend/src/Anela.Heblo.Persistence/Migrations
```

Expected: **no output**. The EF configuration change uses the existing FK column; no migration was needed.

- [ ] **Step 5: Stage any formatting changes**

```bash
git status
```

If `dotnet format` produced changes, commit them:

```bash
git add -u
git commit -m "chore: apply dotnet format"
```

Otherwise this step is a no-op.

---

## Spec coverage map

| Requirement | Implementing task(s) |
|---|---|
| FR-1 (real forecast in list response, three branches) | Task 4 (handler change + 3 branch tests) |
| FR-2 (single bulk log query, exactly two queries total) | Task 3 (new bulk method + impl + tests), Task 4 (handler uses it), Task 5 (query-count test) |
| FR-3 (delete `*WithLogsAsync`) | Task 6 (migrate last caller), Task 7 (delete) |
| FR-4 (logs continue to persist) — Option 2 locked in by arch-review amendment | Task 1 (EF config + UpdateQuantity regression test), Task 2 (ProcessDailyConsumption regression test) |
| FR-5 (XML docs) | Task 8 |
| NFR-1 (performance: O(1) queries) | Task 4 + Task 5 (proven by query-count test) |
| NFR-2 (security: parameterized EF query) | Task 3 (uses LINQ-to-SQL composition; no raw SQL) |
| NFR-3 (compatibility: DTO unchanged) | Task 4 leaves `PackingMaterialDto` shape intact |
| NFR-4 (debug log per request) | Task 4 (`_logger.LogDebug(...)`) |
