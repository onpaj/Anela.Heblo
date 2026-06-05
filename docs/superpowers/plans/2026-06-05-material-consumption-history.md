# Material Consumption History Tab Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the packing-materials screen into two tabs — Tab 1 keeps today's materials/settings table; Tab 2 is a new, date-ordered grid that unions consumption-fact records and quantity-change logs, with filtering, paging, and sorting.

**Architecture:** New `GetConsumptionHistory` MediatR vertical slice. The two underlying tables (`PackingMaterialConsumption`, `PackingMaterialLog`) are projected into one read model and combined with EF Core `Concat` (→ SQL `UNION ALL`) so ordering + paging happen in the database. Material names are resolved in the handler (the existing breakdown handler does the same). The frontend page becomes a thin tab shell; the existing content moves into a settings-tab component; a new history-tab component renders the grid using the existing shared `Pagination` component and the `StockOperationsPage` filter pattern.

**Tech Stack:** .NET 8, MediatR, EF Core (Npgsql + InMemory for tests), FluentValidation, xUnit + FluentAssertions; React 18, TanStack Query, generated NSwag TypeScript client, Tailwind, Jest + React Testing Library.

---

## Context

`/logistics/packing-materials` (`frontend/src/pages/PackingMaterialsPage.tsx`) today shows only a single table of materials and their consumption *settings* (rate, type, current quantity, forecast) plus CRUD modals. There is no global, browsable history of what was consumed or how stock quantities changed over time — only a per-material last-60-days view inside a detail modal and a single-day grouped breakdown endpoint.

The user wants that existing screen to become the first tab ("Nastavení" / settings) and a second tab ("Historie spotřeby" / consumption history) showing **a single grid, ordered by date, with a union of columns** drawn from **both** record types, with filters (date range, material, consumption type, product code, invoice ID), paging, and sorting.

**Behavior note (intended):** Quantity-change logs have no consumption-type / product-code / invoice-ID fields. Therefore, setting any consumption-only filter narrows the grid to consumption-fact rows (logs are excluded). This is the natural union semantics and is implemented deliberately in the repository.

**Decisions locked in:**
- Single unified grid, union of columns, date-ordered. A "Typ záznamu" column distinguishes fact rows from quantity-change rows; non-applicable cells render `—`.
- Filters: date range + material + consumption type + product code + invoice ID.
- Sorting: the **Date** column is sortable (toggle asc/desc), default descending. (Material/Amount sorting is intentionally out of scope for v1 because "amount" is ambiguous across the two sources.)
- In-page state tabs (like `ArticlesPage`), same `/logistics/packing-materials` route — no routing change.

## File Structure

**Backend (create unless noted):**
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/Enums/HistoryRecordType.cs`
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/MaterialConsumptionHistoryRecord.cs` — query read model
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/MaterialConsumptionHistoryFilter.cs` — filter record
- `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs` — **modify** (add method)
- `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs` — **modify** (implement)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/MaterialConsumptionHistoryItemDto.cs`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryRequest.cs`
- `.../GetConsumptionHistory/GetConsumptionHistoryResponse.cs`
- `.../GetConsumptionHistory/GetConsumptionHistoryHandler.cs`
- `.../GetConsumptionHistory/GetConsumptionHistoryRequestValidator.cs`
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — **modify** (add endpoint)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs` — **modify** (implement new interface method)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryHandlerTests.cs` — create
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryRequestValidatorTests.cs` — create
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryConsumptionHistoryTests.cs` — create

**Frontend (create unless noted):**
- `frontend/src/api/hooks/usePackingMaterials.ts` — **modify** (add types + `useConsumptionHistory`)
- `frontend/src/components/packing-materials/PackingMaterialsSettingsTab.tsx` — existing page body, moved
- `frontend/src/components/packing-materials/ConsumptionHistoryTab.tsx` — new grid
- `frontend/src/pages/PackingMaterialsPage.tsx` — **modify** (becomes tab shell)
- `frontend/src/components/packing-materials/__tests__/ConsumptionHistoryTab.test.tsx` — create
- Reuse existing `frontend/src/components/common/Pagination.tsx` (no change).

---

## Task 1: Backend read model, enum, and filter

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/Enums/HistoryRecordType.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/MaterialConsumptionHistoryRecord.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/MaterialConsumptionHistoryFilter.cs`

- [ ] **Step 1: Create the record-type enum**

```csharp
namespace Anela.Heblo.Domain.Features.PackingMaterials.Enums;

public enum HistoryRecordType
{
    Consumption = 1,
    QuantityChange = 2,
}
```

- [ ] **Step 2: Create the query read model**

The properties are mutable because this type exists only as an EF `Select` projection target (object-initializer projection). It is not a domain entity with invariants.

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public class MaterialConsumptionHistoryRecord
{
    public HistoryRecordType RecordType { get; set; }
    public int PackingMaterialId { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }

    // Consumption-fact fields (null on quantity-change rows)
    public ConsumptionType? ConsumptionType { get; set; }
    public string? InvoiceId { get; set; }
    public string? ProductCode { get; set; }
    public decimal? ProductQuantity { get; set; }
    public decimal? Amount { get; set; }

    // Quantity-change fields (null on consumption-fact rows)
    public decimal? OldQuantity { get; set; }
    public decimal? NewQuantity { get; set; }
    public decimal? ChangeAmount { get; set; }
    public LogEntryType? LogType { get; set; }
    public string? UserId { get; set; }
}
```

- [ ] **Step 3: Create the filter record**

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Domain.Features.PackingMaterials;

public record MaterialConsumptionHistoryFilter(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    int? PackingMaterialId,
    ConsumptionType? ConsumptionType,
    string? ProductCode,
    string? InvoiceId);
```

- [ ] **Step 4: Build the Domain project to confirm it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/PackingMaterials/
git commit -m "feat: add material consumption history read model and filter"
```

---

## Task 2: Repository method (interface + real impl + mock)

Adding a method to `IPackingMaterialRepository` breaks compilation of `MockPackingMaterialRepository` until it is implemented — so the interface, the EF implementation, and the mock implementation are all part of this task. The failing test is a repository integration test (EF InMemory) following `PackingMaterialRepositoryRecentLogsTests`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/PackingMaterials/IPackingMaterialRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PackingMaterials/PackingMaterialRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryConsumptionHistoryTests.cs`

- [ ] **Step 1: Write the failing repository integration test**

Create `PackingMaterialRepositoryConsumptionHistoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.PackingMaterials;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class PackingMaterialRepositoryConsumptionHistoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly PackingMaterialRepository _repository;

    public PackingMaterialRepositoryConsumptionHistoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"PackingMaterialConsumptionHistory_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new PackingMaterialRepository(_context);
    }

    [Fact]
    public async Task GetConsumptionHistoryAsync_UnionsBothSources_OrderedByDateDescending()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerOrder, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        var consumption = new PackingMaterialConsumption(
            material.Id, new DateOnly(2026, 1, 10), ConsumptionType.PerOrder, 5m, invoiceId: "INV-1", productCode: "P1");
        var log = new PackingMaterialLog(
            material.Id, new DateOnly(2026, 1, 12), 100m, 90m, LogEntryType.Manual);
        await _context.Set<PackingMaterialConsumption>().AddAsync(consumption);
        await _context.Set<PackingMaterialLog>().AddAsync(log);
        await _context.SaveChangesAsync();

        var filter = new MaterialConsumptionHistoryFilter(null, null, null, null, null, null);

        // Act
        var (items, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip: 0, take: 20, ascending: false, CancellationToken.None);

        // Assert
        totalCount.Should().Be(2);
        items.Should().HaveCount(2);
        items[0].RecordType.Should().Be(HistoryRecordType.QuantityChange); // 2026-01-12 newest first
        items[0].ChangeAmount.Should().Be(-10m);
        items[1].RecordType.Should().Be(HistoryRecordType.Consumption);    // 2026-01-10
        items[1].Amount.Should().Be(5m);
        items[1].InvoiceId.Should().Be("INV-1");
    }

    [Fact]
    public async Task GetConsumptionHistoryAsync_ConsumptionOnlyFilter_ExcludesQuantityLogs()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerOrder, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        await _context.Set<PackingMaterialConsumption>().AddAsync(
            new PackingMaterialConsumption(material.Id, new DateOnly(2026, 1, 10), ConsumptionType.PerOrder, 5m, invoiceId: "INV-1"));
        await _context.Set<PackingMaterialLog>().AddAsync(
            new PackingMaterialLog(material.Id, new DateOnly(2026, 1, 12), 100m, 90m, LogEntryType.Manual));
        await _context.SaveChangesAsync();

        var filter = new MaterialConsumptionHistoryFilter(null, null, null, null, null, InvoiceId: "INV-1");

        // Act
        var (items, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip: 0, take: 20, ascending: false, CancellationToken.None);

        // Assert
        totalCount.Should().Be(1);
        items.Should().ContainSingle()
            .Which.RecordType.Should().Be(HistoryRecordType.Consumption);
    }

    [Fact]
    public async Task GetConsumptionHistoryAsync_PagesResults()
    {
        // Arrange
        var material = new PackingMaterial("Tape", 1m, ConsumptionType.PerOrder, 100m);
        await _context.PackingMaterials.AddAsync(material);
        await _context.SaveChangesAsync();

        for (var day = 1; day <= 5; day++)
        {
            await _context.Set<PackingMaterialConsumption>().AddAsync(
                new PackingMaterialConsumption(material.Id, new DateOnly(2026, 1, day), ConsumptionType.PerDay, day));
        }
        await _context.SaveChangesAsync();

        var filter = new MaterialConsumptionHistoryFilter(null, null, null, null, null, null);

        // Act
        var (page1, total) = await _repository.GetConsumptionHistoryAsync(filter, skip: 0, take: 2, ascending: false, CancellationToken.None);
        var (page2, _) = await _repository.GetConsumptionHistoryAsync(filter, skip: 2, take: 2, ascending: false, CancellationToken.None);

        // Assert
        total.Should().Be(5);
        page1.Should().HaveCount(2);
        page1[0].Date.Should().Be(new DateOnly(2026, 1, 5));
        page2.Should().HaveCount(2);
        page2[0].Date.Should().Be(new DateOnly(2026, 1, 3));
    }

    public void Dispose() => _context.Dispose();
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConsumptionHistory"`
Expected: Build error — `IPackingMaterialRepository` / `PackingMaterialRepository` has no `GetConsumptionHistoryAsync`.

- [ ] **Step 3: Add the method to the repository interface**

In `IPackingMaterialRepository.cs`, add inside the interface (the file already references the `PackingMaterials` namespace types):

```csharp
    Task<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)> GetConsumptionHistoryAsync(
        MaterialConsumptionHistoryFilter filter,
        int skip,
        int take,
        bool ascending,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement the method in `PackingMaterialRepository`**

Add `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` to the top of `PackingMaterialRepository.cs`, then add:

```csharp
    public async Task<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)> GetConsumptionHistoryAsync(
        MaterialConsumptionHistoryFilter filter,
        int skip,
        int take,
        bool ascending,
        CancellationToken cancellationToken = default)
    {
        var consumptions = Context.Set<PackingMaterialConsumption>().AsQueryable();
        if (filter.DateFrom.HasValue) consumptions = consumptions.Where(c => c.Date >= filter.DateFrom.Value);
        if (filter.DateTo.HasValue) consumptions = consumptions.Where(c => c.Date <= filter.DateTo.Value);
        if (filter.PackingMaterialId.HasValue) consumptions = consumptions.Where(c => c.PackingMaterialId == filter.PackingMaterialId.Value);
        if (filter.ConsumptionType.HasValue) consumptions = consumptions.Where(c => c.ConsumptionType == filter.ConsumptionType.Value);
        if (!string.IsNullOrWhiteSpace(filter.ProductCode)) consumptions = consumptions.Where(c => c.ProductCode == filter.ProductCode);
        if (!string.IsNullOrWhiteSpace(filter.InvoiceId)) consumptions = consumptions.Where(c => c.InvoiceId == filter.InvoiceId);

        IQueryable<MaterialConsumptionHistoryRecord> combined = consumptions.Select(c => new MaterialConsumptionHistoryRecord
        {
            RecordType = HistoryRecordType.Consumption,
            PackingMaterialId = c.PackingMaterialId,
            Date = c.Date,
            CreatedAt = c.CreatedAt,
            ConsumptionType = c.ConsumptionType,
            InvoiceId = c.InvoiceId,
            ProductCode = c.ProductCode,
            ProductQuantity = c.ProductQuantity,
            Amount = c.Amount,
            OldQuantity = null,
            NewQuantity = null,
            ChangeAmount = null,
            LogType = null,
            UserId = null,
        });

        // Consumption-only filters cannot match quantity-change logs (logs lack those fields),
        // so when any is set, exclude the logs source entirely.
        var consumptionOnlyFilter = filter.ConsumptionType.HasValue
            || !string.IsNullOrWhiteSpace(filter.ProductCode)
            || !string.IsNullOrWhiteSpace(filter.InvoiceId);

        if (!consumptionOnlyFilter)
        {
            var logs = Context.Set<PackingMaterialLog>().AsQueryable();
            if (filter.DateFrom.HasValue) logs = logs.Where(l => l.Date >= filter.DateFrom.Value);
            if (filter.DateTo.HasValue) logs = logs.Where(l => l.Date <= filter.DateTo.Value);
            if (filter.PackingMaterialId.HasValue) logs = logs.Where(l => l.PackingMaterialId == filter.PackingMaterialId.Value);

            var logRecords = logs.Select(l => new MaterialConsumptionHistoryRecord
            {
                RecordType = HistoryRecordType.QuantityChange,
                PackingMaterialId = l.PackingMaterialId,
                Date = l.Date,
                CreatedAt = l.CreatedAt,
                ConsumptionType = null,
                InvoiceId = null,
                ProductCode = null,
                ProductQuantity = null,
                Amount = null,
                OldQuantity = l.OldQuantity,
                NewQuantity = l.NewQuantity,
                ChangeAmount = l.NewQuantity - l.OldQuantity,
                LogType = l.LogType,
                UserId = l.UserId,
            });

            combined = combined.Concat(logRecords);
        }

        var totalCount = await combined.CountAsync(cancellationToken);

        var ordered = ascending
            ? combined.OrderBy(r => r.Date).ThenBy(r => r.CreatedAt)
            : combined.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt);

        var items = await ordered.Skip(skip).Take(take).ToListAsync(cancellationToken);

        return (items, totalCount);
    }
```

Note: project `l.NewQuantity - l.OldQuantity` directly — `PackingMaterialLog.ChangeAmount` is an unmapped computed property and cannot be translated to SQL.

- [ ] **Step 5: Implement the method in `MockPackingMaterialRepository`**

Add `using Anela.Heblo.Domain.Features.PackingMaterials.Enums;` to the top of the mock, then add (the mock already exposes `ConsumptionRowsByDate` and `RecentLogsByMaterial`):

```csharp
    public Task<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)> GetConsumptionHistoryAsync(
        MaterialConsumptionHistoryFilter filter,
        int skip,
        int take,
        bool ascending,
        CancellationToken cancellationToken = default)
    {
        var records = ConsumptionRowsByDate.Values.SelectMany(v => v).Select(c => new MaterialConsumptionHistoryRecord
        {
            RecordType = HistoryRecordType.Consumption,
            PackingMaterialId = c.PackingMaterialId,
            Date = c.Date,
            CreatedAt = c.CreatedAt,
            ConsumptionType = c.ConsumptionType,
            InvoiceId = c.InvoiceId,
            ProductCode = c.ProductCode,
            ProductQuantity = c.ProductQuantity,
            Amount = c.Amount,
        }).ToList();

        var consumptionOnlyFilter = filter.ConsumptionType.HasValue
            || !string.IsNullOrWhiteSpace(filter.ProductCode)
            || !string.IsNullOrWhiteSpace(filter.InvoiceId);

        if (!consumptionOnlyFilter)
        {
            records.AddRange(RecentLogsByMaterial.Values.SelectMany(v => v).Select(l => new MaterialConsumptionHistoryRecord
            {
                RecordType = HistoryRecordType.QuantityChange,
                PackingMaterialId = l.PackingMaterialId,
                Date = l.Date,
                CreatedAt = l.CreatedAt,
                OldQuantity = l.OldQuantity,
                NewQuantity = l.NewQuantity,
                ChangeAmount = l.NewQuantity - l.OldQuantity,
                LogType = l.LogType,
                UserId = l.UserId,
            }));
        }

        IEnumerable<MaterialConsumptionHistoryRecord> query = records;
        if (filter.DateFrom.HasValue) query = query.Where(r => r.Date >= filter.DateFrom.Value);
        if (filter.DateTo.HasValue) query = query.Where(r => r.Date <= filter.DateTo.Value);
        if (filter.PackingMaterialId.HasValue) query = query.Where(r => r.PackingMaterialId == filter.PackingMaterialId.Value);
        if (filter.ConsumptionType.HasValue) query = query.Where(r => r.ConsumptionType == filter.ConsumptionType.Value);
        if (!string.IsNullOrWhiteSpace(filter.ProductCode)) query = query.Where(r => r.ProductCode == filter.ProductCode);
        if (!string.IsNullOrWhiteSpace(filter.InvoiceId)) query = query.Where(r => r.InvoiceId == filter.InvoiceId);

        var filtered = query.ToList();
        var ordered = ascending
            ? filtered.OrderBy(r => r.Date).ThenBy(r => r.CreatedAt)
            : filtered.OrderByDescending(r => r.Date).ThenByDescending(r => r.CreatedAt);

        var paged = ordered.Skip(skip).Take(take).ToList();
        return Task.FromResult<(IReadOnlyList<MaterialConsumptionHistoryRecord> Items, int TotalCount)>((paged, filtered.Count));
    }
```

- [ ] **Step 6: Run the repository test to verify it passes**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConsumptionHistory"`
Expected: 3 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain backend/src/Anela.Heblo.Persistence backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockPackingMaterialRepository.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialRepositoryConsumptionHistoryTests.cs
git commit -m "feat: add GetConsumptionHistoryAsync union query to packing material repository"
```

---

## Task 3: Contracts — DTO, request, response

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/MaterialConsumptionHistoryItemDto.cs`
- Create: `.../UseCases/GetConsumptionHistory/GetConsumptionHistoryRequest.cs`
- Create: `.../UseCases/GetConsumptionHistory/GetConsumptionHistoryResponse.cs`

- [ ] **Step 1: Create the DTO (class, not record — OpenAPI generator requirement)**

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;

namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

public class MaterialConsumptionHistoryItemDto
{
    public HistoryRecordType RecordType { get; set; }
    public string RecordTypeText { get; set; } = string.Empty;
    public int PackingMaterialId { get; set; }
    public string MaterialName { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; }

    public ConsumptionType? ConsumptionType { get; set; }
    public string? ConsumptionTypeText { get; set; }
    public string? InvoiceId { get; set; }
    public string? ProductCode { get; set; }
    public decimal? ProductQuantity { get; set; }
    public decimal? Amount { get; set; }

    public decimal? OldQuantity { get; set; }
    public decimal? NewQuantity { get; set; }
    public decimal? ChangeAmount { get; set; }
    public LogEntryType? LogType { get; set; }
    public string? LogTypeText { get; set; }
    public string? UserId { get; set; }
}
```

- [ ] **Step 2: Create the request**

```csharp
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryRequest : IRequest<GetConsumptionHistoryResponse>
{
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public int? PackingMaterialId { get; set; }
    public ConsumptionType? ConsumptionType { get; set; }
    public string? ProductCode { get; set; }
    public string? InvoiceId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public bool SortDescending { get; set; } = true;
}
```

- [ ] **Step 3: Create the response**

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryResponse
{
    public List<MaterialConsumptionHistoryItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}
```

- [ ] **Step 4: Build the Application project**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/MaterialConsumptionHistoryItemDto.cs backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/
git commit -m "feat: add GetConsumptionHistory contracts (dto, request, response)"
```

---

## Task 4: Handler

**Files:**
- Create: `.../UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryHandlerTests.cs`

- [ ] **Step 1: Write the failing handler tests**

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetConsumptionHistoryHandlerTests
{
    private static PackingMaterial MakeMaterial(int id, string name)
    {
        var material = new PackingMaterial(name, 1m, ConsumptionType.PerOrder, 100m);
        typeof(PackingMaterial).GetProperty("Id")!.SetValue(material, id);
        return material;
    }

    private static PackingMaterialConsumption MakeConsumption(int materialId, DateOnly date, decimal amount, string? invoiceId = null, string? productCode = null)
        => new(materialId, date, ConsumptionType.PerOrder, amount, invoiceId, productCode);

    private static PackingMaterialLog MakeLog(int materialId, DateOnly date, decimal oldQty, decimal newQty)
        => new(materialId, date, oldQty, newQty, LogEntryType.Manual);

    private static (GetConsumptionHistoryHandler handler, MockPackingMaterialRepository repo) Build()
    {
        var repo = new MockPackingMaterialRepository();
        repo.SetMaterials(new[] { MakeMaterial(1, "Tape") });
        var handler = new GetConsumptionHistoryHandler(repo, new MockLogger<GetConsumptionHistoryHandler>());
        return (handler, repo);
    }

    [Fact]
    public async Task Handle_ResolvesMaterialName_AndUnionsSources()
    {
        // Arrange
        var (handler, repo) = Build();
        repo.ConsumptionRowsByDate[new DateOnly(2026, 1, 10)] = new() { MakeConsumption(1, new DateOnly(2026, 1, 10), 5m, "INV-1") };
        repo.RecentLogsByMaterial[1] = new() { MakeLog(1, new DateOnly(2026, 1, 12), 100m, 90m) };

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest(), CancellationToken.None);

        // Assert
        response.TotalCount.Should().Be(2);
        response.PageNumber.Should().Be(1);
        response.PageSize.Should().Be(20);
        response.TotalPages.Should().Be(1);
        response.Items.Should().HaveCount(2);
        response.Items[0].RecordType.Should().Be(HistoryRecordType.QuantityChange);
        response.Items[0].RecordTypeText.Should().Be("Změna množství");
        response.Items[0].ChangeAmount.Should().Be(-10m);
        response.Items[1].RecordType.Should().Be(HistoryRecordType.Consumption);
        response.Items[1].MaterialName.Should().Be("Tape");
        response.Items[1].Amount.Should().Be(5m);
    }

    [Fact]
    public async Task Handle_ClampsPageSizeToMaximum()
    {
        // Arrange
        var (handler, _) = Build();

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest { PageSize = 5000 }, CancellationToken.None);

        // Assert
        response.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task Handle_ConsumptionOnlyFilter_ExcludesLogs()
    {
        // Arrange
        var (handler, repo) = Build();
        repo.ConsumptionRowsByDate[new DateOnly(2026, 1, 10)] = new() { MakeConsumption(1, new DateOnly(2026, 1, 10), 5m, "INV-1") };
        repo.RecentLogsByMaterial[1] = new() { MakeLog(1, new DateOnly(2026, 1, 12), 100m, 90m) };

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest { InvoiceId = "INV-1" }, CancellationToken.None);

        // Assert
        response.TotalCount.Should().Be(1);
        response.Items.Should().ContainSingle().Which.RecordType.Should().Be(HistoryRecordType.Consumption);
    }

    [Fact]
    public async Task Handle_UnknownMaterial_FallsBackToPlaceholderName()
    {
        // Arrange
        var repo = new MockPackingMaterialRepository(); // no materials registered
        repo.ConsumptionRowsByDate[new DateOnly(2026, 1, 10)] = new() { MakeConsumption(99, new DateOnly(2026, 1, 10), 5m) };
        var handler = new GetConsumptionHistoryHandler(repo, new MockLogger<GetConsumptionHistoryHandler>());

        // Act
        var response = await handler.Handle(new GetConsumptionHistoryRequest(), CancellationToken.None);

        // Assert
        response.Items.Should().ContainSingle().Which.MaterialName.Should().Be("Neznámý");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryHandlerTests"`
Expected: Build error — `GetConsumptionHistoryHandler` does not exist.

- [ ] **Step 3: Implement the handler**

```csharp
using System.Globalization;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryHandler
    : IRequestHandler<GetConsumptionHistoryRequest, GetConsumptionHistoryResponse>
{
    private const int MaxPageSize = 100;

    private readonly IPackingMaterialRepository _repository;
    private readonly ILogger<GetConsumptionHistoryHandler> _logger;

    public GetConsumptionHistoryHandler(
        IPackingMaterialRepository repository,
        ILogger<GetConsumptionHistoryHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<GetConsumptionHistoryResponse> Handle(
        GetConsumptionHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber < 1 ? 1 : request.PageNumber;
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var skip = (pageNumber - 1) * pageSize;

        var filter = new MaterialConsumptionHistoryFilter(
            DateFrom: ParseDateOrNull(request.DateFrom),
            DateTo: ParseDateOrNull(request.DateTo),
            PackingMaterialId: request.PackingMaterialId,
            ConsumptionType: request.ConsumptionType,
            ProductCode: NormalizeNullableString(request.ProductCode),
            InvoiceId: NormalizeNullableString(request.InvoiceId));

        _logger.LogInformation(
            "Loading consumption history page {PageNumber} (size {PageSize})", pageNumber, pageSize);

        var (records, totalCount) = await _repository.GetConsumptionHistoryAsync(
            filter, skip, pageSize, ascending: !request.SortDescending, cancellationToken);

        var materialNames = (await _repository.GetAllWithAllocationsAsync(cancellationToken))
            .ToDictionary(m => m.Id, m => m.Name);

        var items = records.Select(r => MapToDto(r, materialNames)).ToList();

        return new GetConsumptionHistoryResponse
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
        };
    }

    private static MaterialConsumptionHistoryItemDto MapToDto(
        MaterialConsumptionHistoryRecord record,
        IReadOnlyDictionary<int, string> materialNames)
        => new()
        {
            RecordType = record.RecordType,
            RecordTypeText = record.RecordType == HistoryRecordType.Consumption ? "Spotřeba" : "Změna množství",
            PackingMaterialId = record.PackingMaterialId,
            MaterialName = materialNames.TryGetValue(record.PackingMaterialId, out var name) ? name : "Neznámý",
            Date = record.Date,
            CreatedAt = record.CreatedAt,
            ConsumptionType = record.ConsumptionType,
            ConsumptionTypeText = record.ConsumptionType?.ToString(),
            InvoiceId = record.InvoiceId,
            ProductCode = record.ProductCode,
            ProductQuantity = record.ProductQuantity,
            Amount = record.Amount,
            OldQuantity = record.OldQuantity,
            NewQuantity = record.NewQuantity,
            ChangeAmount = record.ChangeAmount,
            LogType = record.LogType,
            LogTypeText = record.LogType?.ToString(),
            UserId = record.UserId,
        };

    private static DateOnly? ParseDateOrNull(string? value)
        => !string.IsNullOrWhiteSpace(value)
           && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    private static string? NormalizeNullableString(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryHandlerTests"`
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryHandler.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryHandlerTests.cs
git commit -m "feat: add GetConsumptionHistory handler"
```

---

## Task 5: Validator

**Files:**
- Create: `.../UseCases/GetConsumptionHistory/GetConsumptionHistoryRequestValidator.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryRequestValidatorTests.cs`

- [ ] **Step 1: Write the failing validator tests**

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class GetConsumptionHistoryRequestValidatorTests
{
    private readonly GetConsumptionHistoryRequestValidator _validator = new();

    [Fact]
    public void Validate_DefaultRequest_IsValid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest());
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Validate_PageSizeOutOfRange_IsInvalid(int pageSize)
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { PageSize = pageSize });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(GetConsumptionHistoryRequest.PageSize));
    }

    [Fact]
    public void Validate_PageNumberBelowOne_IsInvalid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { PageNumber = 0 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_MalformedDateFrom_IsInvalid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { DateFrom = "10-01-2026" });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_WellFormedDates_IsValid()
    {
        var result = _validator.Validate(new GetConsumptionHistoryRequest { DateFrom = "2026-01-01", DateTo = "2026-01-31" });
        result.IsValid.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryRequestValidatorTests"`
Expected: Build error — validator does not exist.

- [ ] **Step 3: Implement the validator**

```csharp
using System.Globalization;
using FluentValidation;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;

public class GetConsumptionHistoryRequestValidator : AbstractValidator<GetConsumptionHistoryRequest>
{
    public GetConsumptionHistoryRequestValidator()
    {
        RuleFor(x => x.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("PageNumber must be greater than or equal to 1.");

        RuleFor(x => x.PageSize)
            .GreaterThan(0).WithMessage("PageSize must be greater than 0.")
            .LessThanOrEqualTo(100).WithMessage("PageSize must not exceed 100.");

        RuleFor(x => x.DateFrom)
            .Must(BeValidDateOrEmpty).WithMessage("DateFrom must be in yyyy-MM-dd format.");

        RuleFor(x => x.DateTo)
            .Must(BeValidDateOrEmpty).WithMessage("DateTo must be in yyyy-MM-dd format.");
    }

    private static bool BeValidDateOrEmpty(string? value)
        => string.IsNullOrWhiteSpace(value)
           || DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetConsumptionHistoryRequestValidatorTests"`
Expected: 6 tests PASS.

- [ ] **Step 5: Confirm the validator is auto-registered**

The Bank feature registers validators by assembly scan (`AddValidatorsFromAssembly`). Verify the same applies here:

Run: `grep -rn "AddValidatorsFromAssembly" backend/src`
Expected: at least one match in application/program startup. If found, no registration code is needed. If NOT found, register explicitly where MediatR validators are wired and note it in the commit.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetConsumptionHistory/GetConsumptionHistoryRequestValidator.cs backend/test/Anela.Heblo.Tests/Features/PackingMaterials/GetConsumptionHistoryRequestValidatorTests.cs
git commit -m "feat: add GetConsumptionHistory request validator"
```

---

## Task 6: Controller endpoint

**Files:**
- Modify: `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs`

- [ ] **Step 1: Add the using directive**

At the top of the controller, alongside the other `UseCases` usings, add:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.UseCases.GetConsumptionHistory;
```

- [ ] **Step 2: Add the endpoint action**

Add this action to `PackingMaterialsController` (e.g. just after `GetDailyConsumptionBreakdown`). `[FromQuery]` binds every filter/paging field from the query string:

```csharp
    [HttpGet("consumption-history")]
    [ProducesResponseType(typeof(GetConsumptionHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GetConsumptionHistoryResponse>> GetConsumptionHistory(
        [FromQuery] GetConsumptionHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(request, cancellationToken);
        return Ok(response);
    }
```

- [ ] **Step 3: Build the API project + format**

Run: `dotnet build backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: Build succeeded.

Run: `dotnet format backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj`
Expected: completes with no errors.

- [ ] **Step 4: Run the whole packing-materials backend test suite**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"`
Expected: all PASS (existing + new).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs
git commit -m "feat: expose GET /api/packing-materials/consumption-history endpoint"
```

---

## Task 7: Regenerate the TypeScript client

**Files:** none authored — this regenerates `frontend/src/api/generated/api-client.ts`.

- [ ] **Step 1: Build the frontend (triggers OpenAPI client generation)**

Run: `cd frontend && npm run build`
Expected: build succeeds and the generated client now contains a `packingMaterials_GetConsumptionHistory` method.

- [ ] **Step 2: Confirm the generated method and capture its exact signature**

Run: `grep -n "packingMaterials_GetConsumptionHistory" frontend/src/api/generated/api-client.ts`
Expected: a method declaration. **Record the exact parameter order** — NSwag generates one positional parameter per query field (declaration order: `dateFrom, dateTo, packingMaterialId, consumptionType, productCode, invoiceId, pageNumber, pageSize, sortDescending`). Task 8's hook must match this signature exactly.

- [ ] **Step 3: Commit the regenerated client**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate api client with consumption-history endpoint"
```

---

## Task 8: Frontend data hook

**Files:**
- Modify: `frontend/src/api/hooks/usePackingMaterials.ts`

- [ ] **Step 1: Add history types after the existing exported interfaces**

Add to `usePackingMaterials.ts` (reuse the existing `ConsumptionType` / `LogEntryType` enums already defined in this file):

```typescript
export interface ConsumptionHistoryItemDto {
  recordType: number;
  recordTypeText: string;
  packingMaterialId: number;
  materialName: string;
  date: string;
  createdAt: string;
  consumptionType?: ConsumptionType;
  consumptionTypeText?: string;
  invoiceId?: string;
  productCode?: string;
  productQuantity?: number;
  amount?: number;
  oldQuantity?: number;
  newQuantity?: number;
  changeAmount?: number;
  logType?: LogEntryType;
  logTypeText?: string;
  userId?: string;
}

export interface ConsumptionHistoryParams {
  dateFrom?: string;
  dateTo?: string;
  packingMaterialId?: number;
  consumptionType?: ConsumptionType;
  productCode?: string;
  invoiceId?: string;
  pageNumber?: number;
  pageSize?: number;
  sortDescending?: boolean;
}

export interface GetConsumptionHistoryResponse {
  items: ConsumptionHistoryItemDto[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
}
```

- [ ] **Step 2: Add the query-key entry**

Extend the existing `QUERY_KEYS` object in this file:

```typescript
const QUERY_KEYS = {
  packingMaterials: ['packingMaterials'] as const,
  packingMaterialLogs: (id: number, days: number) => ['packingMaterials', id, 'logs', days] as const,
  consumptionHistory: (params: ConsumptionHistoryParams) =>
    ['packingMaterials', 'consumptionHistory', params] as const,
};
```

- [ ] **Step 3: Add the hook (uses the generated typed client)**

Append:

```typescript
export const useConsumptionHistory = (params: ConsumptionHistoryParams) => {
  return useQuery({
    queryKey: QUERY_KEYS.consumptionHistory(params),
    queryFn: async (): Promise<GetConsumptionHistoryResponse> => {
      const apiClient = getAuthenticatedApiClient();
      const response = await apiClient.packingMaterials_GetConsumptionHistory(
        params.dateFrom ?? undefined,
        params.dateTo ?? undefined,
        params.packingMaterialId ?? undefined,
        params.consumptionType ?? undefined,
        params.productCode ?? undefined,
        params.invoiceId ?? undefined,
        params.pageNumber ?? undefined,
        params.pageSize ?? undefined,
        params.sortDescending ?? undefined,
      );
      return response as unknown as GetConsumptionHistoryResponse;
    },
  });
};
```

If the generated signature recorded in Task 7 differs (e.g. parameter order), reorder the arguments to match it exactly. `getAuthenticatedApiClient` is already imported at the top of the file.

- [ ] **Step 4: Type-check**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors in `usePackingMaterials.ts`.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/api/hooks/usePackingMaterials.ts
git commit -m "feat: add useConsumptionHistory query hook"
```

---

## Task 9: Extract settings tab and convert the page into a tab shell

This is a behavior-preserving move: the entire current page body (table + modals + handlers + `useScreenView`) moves into a new `PackingMaterialsSettingsTab` component, and `PackingMaterialsPage` becomes a tab shell that renders it.

**Files:**
- Create: `frontend/src/components/packing-materials/PackingMaterialsSettingsTab.tsx`
- Modify: `frontend/src/pages/PackingMaterialsPage.tsx`

- [ ] **Step 1: Create `PackingMaterialsSettingsTab.tsx` by moving the current page body**

Move the **entire current implementation** of `PackingMaterialsPage.tsx` into this new file with three changes: rename the component to `PackingMaterialsSettingsTab`, drop the page-level outer `p-6` wrapper's responsibility for tab layout (keep the existing markup as-is otherwise), and keep `useScreenView('Logistics', 'PackingMaterials')` here. The full content is the existing file body; the only edits are the component name and default export:

```typescript
// frontend/src/components/packing-materials/PackingMaterialsSettingsTab.tsx
// (Body is the existing PackingMaterialsPage implementation, verbatim, with the
//  component renamed. Keep all imports, state, handlers, the materials <table>,
//  and all five modals exactly as they are today.)
import React, { useState } from 'react';
import { Plus, Edit, Package, Trash2, Calculator, TrendingDown } from 'lucide-react';
import { usePackingMaterials, useDeletePackingMaterial, PackingMaterialDto, ConsumptionType } from '../../api/hooks/usePackingMaterials';
import { LoadingIndicator } from '../ui/LoadingIndicator';
import AddMaterialModal from './modals/AddMaterialModal';
import EditMaterialModal from './modals/EditMaterialModal';
import UpdateQuantityModal from './modals/UpdateQuantityModal';
import ProcessDailyConsumptionModal from './modals/ProcessDailyConsumptionModal';
import PackingMaterialDetailModal from './modals/PackingMaterialDetailModal';
import { useScreenView } from '../../telemetry/useScreenView';

const PackingMaterialsSettingsTab: React.FC = () => {
  // ...EXISTING BODY OF PackingMaterialsPage, UNCHANGED...
  // (keep every line from the current component: state, formatters,
  //  handleDeleteMaterial, handleRowClick, loading/error guards, the
  //  header with the two action buttons, the materials table, and all modals)
};

export default PackingMaterialsSettingsTab;
```

Important: update the relative import paths (now one directory deeper): `../api/...` → `../../api/...`, `../components/...` → `../...`, `../telemetry/...` → `../../telemetry/...`, and the modal imports become `./modals/...`.

- [ ] **Step 2: Replace `PackingMaterialsPage.tsx` with a tab shell**

```typescript
import React, { useState } from 'react';
import { Package } from 'lucide-react';
import PackingMaterialsSettingsTab from '../components/packing-materials/PackingMaterialsSettingsTab';
import ConsumptionHistoryTab from '../components/packing-materials/ConsumptionHistoryTab';

type Tab = 'settings' | 'history';

const PackingMaterialsPage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('settings');

  const tabs: { id: Tab; label: string }[] = [
    { id: 'settings', label: 'Nastavení' },
    { id: 'history', label: 'Historie spotřeby' },
  ];

  return (
    <div className="p-6">
      <div className="flex items-center space-x-3 mb-4">
        <Package className="h-8 w-8 text-gray-700" />
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Sledování materiálů</h1>
          <p className="text-sm text-gray-500">Správa spotřebních materiálů a historie spotřeby</p>
        </div>
      </div>

      <div className="border-b border-gray-200 mb-6">
        <nav className="flex gap-6" aria-label="Tabs">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`py-2 text-sm font-medium border-b-2 transition-colors ${
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600'
                  : 'border-transparent text-gray-500 hover:text-gray-700'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === 'settings' && <PackingMaterialsSettingsTab />}
      {activeTab === 'history' && <ConsumptionHistoryTab />}
    </div>
  );
};

export default PackingMaterialsPage;
```

Note: the settings tab currently renders its own `Package` header inside its body. Keep that as-is for now (it duplicates the icon harmlessly under the page header); removing the inner header is an optional cleanup left to the implementer's judgment if it looks visually redundant. Do not change its behavior.

- [ ] **Step 3: Add a temporary stub so the page compiles before Task 10**

Create a minimal placeholder `ConsumptionHistoryTab.tsx` so the import resolves; Task 10 replaces it:

```typescript
import React from 'react';
const ConsumptionHistoryTab: React.FC = () => <div />;
export default ConsumptionHistoryTab;
```

- [ ] **Step 4: Type-check + build**

Run: `cd frontend && npx tsc --noEmit`
Expected: no errors.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/pages/PackingMaterialsPage.tsx frontend/src/components/packing-materials/PackingMaterialsSettingsTab.tsx frontend/src/components/packing-materials/ConsumptionHistoryTab.tsx
git commit -m "refactor: split packing materials page into settings tab + tab shell"
```

---

## Task 10: Consumption history grid component

Replaces the Task 9 stub. Mirrors the `StockOperationsPage` filter pattern (separate input vs applied state with Apply/Clear), reuses the shared `Pagination` component, and shows the union of columns with `—` for non-applicable cells. Sortable Date column.

**Files:**
- Modify (replace stub): `frontend/src/components/packing-materials/ConsumptionHistoryTab.tsx`

- [ ] **Step 1: Implement the grid**

```typescript
import React, { useState } from 'react';
import { Search, X, ChevronDown, ChevronUp, RefreshCw, AlertCircle } from 'lucide-react';
import {
  useConsumptionHistory,
  usePackingMaterials,
  ConsumptionType,
  ConsumptionHistoryParams,
  ConsumptionHistoryItemDto,
} from '../../api/hooks/usePackingMaterials';
import Pagination from '../common/Pagination';

const CONSUMPTION_TYPE_OPTIONS: { value: ConsumptionType; label: string }[] = [
  { value: ConsumptionType.PerOrder, label: 'Na objednávku' },
  { value: ConsumptionType.PerProduct, label: 'Na produkt' },
  { value: ConsumptionType.PerDay, label: 'Na den' },
];

const DASH = '—';

const formatNumber = (value?: number): string =>
  value === undefined || value === null
    ? DASH
    : value.toLocaleString('cs-CZ', { minimumFractionDigits: 0, maximumFractionDigits: 2 });

const formatDate = (value?: string): string => (value ? new Date(value).toLocaleDateString('cs-CZ') : DASH);

const ConsumptionHistoryTab: React.FC = () => {
  const { data: materialsData } = usePackingMaterials();
  const materials = materialsData?.materials ?? [];

  // Filter input state (what the user is editing)
  const [dateFromInput, setDateFromInput] = useState('');
  const [dateToInput, setDateToInput] = useState('');
  const [materialInput, setMaterialInput] = useState<string>('');
  const [typeInput, setTypeInput] = useState<string>('');
  const [productCodeInput, setProductCodeInput] = useState('');
  const [invoiceIdInput, setInvoiceIdInput] = useState('');

  // Applied filter state (sent to the API)
  const [appliedFilters, setAppliedFilters] = useState<ConsumptionHistoryParams>({});

  // Paging + sorting
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [sortDescending, setSortDescending] = useState(true);

  const { data, isLoading, error, refetch } = useConsumptionHistory({
    ...appliedFilters,
    pageNumber,
    pageSize,
    sortDescending,
  });

  const handleApplyFilters = () => {
    setAppliedFilters({
      dateFrom: dateFromInput || undefined,
      dateTo: dateToInput || undefined,
      packingMaterialId: materialInput ? Number(materialInput) : undefined,
      consumptionType: typeInput ? (Number(typeInput) as ConsumptionType) : undefined,
      productCode: productCodeInput || undefined,
      invoiceId: invoiceIdInput || undefined,
    });
    setPageNumber(1);
  };

  const handleClearFilters = () => {
    setDateFromInput('');
    setDateToInput('');
    setMaterialInput('');
    setTypeInput('');
    setProductCodeInput('');
    setInvoiceIdInput('');
    setAppliedFilters({});
    setPageNumber(1);
  };

  const handleSortByDate = () => {
    setSortDescending((prev) => !prev);
    setPageNumber(1);
  };

  const items: ConsumptionHistoryItemDto[] = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = data?.totalPages ?? 0;

  if (error) {
    return (
      <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
        <div className="flex items-center gap-2">
          <AlertCircle className="h-5 w-5 text-red-600" />
          <div>
            <h3 className="text-red-800 font-semibold">Chyba při načítání historie spotřeby</h3>
            <p className="text-red-600 text-sm mt-1">
              {error instanceof Error ? error.message : 'Neznámá chyba'}
            </p>
            <button
              onClick={() => refetch()}
              className="mt-2 px-3 py-1 bg-red-600 text-white rounded hover:bg-red-700 text-sm"
            >
              Zkusit znovu
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col">
      {/* Filters */}
      <div className="bg-white rounded-lg shadow mb-4 p-3 space-y-3">
        <div className="flex flex-wrap gap-3 items-end">
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Datum od</label>
            <input
              type="date"
              value={dateFromInput}
              onChange={(e) => setDateFromInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Datum do</label>
            <input
              type="date"
              value={dateToInput}
              onChange={(e) => setDateToInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="w-56">
            <label className="block text-xs font-medium text-gray-700 mb-1">Materiál</label>
            <select
              value={materialInput}
              onChange={(e) => setMaterialInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="">Všechny</option>
              {materials.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.name}
                </option>
              ))}
            </select>
          </div>
          <div className="w-48">
            <label className="block text-xs font-medium text-gray-700 mb-1">Typ spotřeby</label>
            <select
              value={typeInput}
              onChange={(e) => setTypeInput(e.target.value)}
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            >
              <option value="">Všechny</option>
              {CONSUMPTION_TYPE_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Kód produktu</label>
            <input
              type="text"
              value={productCodeInput}
              onChange={(e) => setProductCodeInput(e.target.value)}
              placeholder="Kód produktu"
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
          <div className="w-40">
            <label className="block text-xs font-medium text-gray-700 mb-1">Faktura</label>
            <input
              type="text"
              value={invoiceIdInput}
              onChange={(e) => setInvoiceIdInput(e.target.value)}
              placeholder="ID faktury"
              className="w-full border border-gray-300 rounded-md px-2 py-1.5 text-xs focus:outline-none focus:ring-1 focus:ring-indigo-500"
            />
          </div>
        </div>
        <div className="flex justify-end gap-2">
          <button
            onClick={handleClearFilters}
            className="flex items-center gap-1 px-3 py-1.5 text-xs text-gray-600 hover:text-gray-800 hover:bg-gray-100 rounded-md border border-gray-300"
          >
            <X className="h-3 w-3" />
            Vymazat filtry
          </button>
          <button
            onClick={handleApplyFilters}
            className="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-md"
          >
            <Search className="h-3 w-3" />
            Použít filtry
          </button>
        </div>
      </div>

      {/* Table */}
      <div className="bg-white rounded-lg shadow overflow-hidden flex flex-col">
        {isLoading ? (
          <div className="flex items-center justify-center py-16">
            <RefreshCw className="h-6 w-6 animate-spin text-gray-400" />
            <span className="ml-2 text-gray-600 text-sm">Načítání dat...</span>
          </div>
        ) : items.length === 0 ? (
          <div className="text-center py-16 text-gray-500">
            <AlertCircle className="h-10 w-10 mx-auto text-gray-300 mb-3" />
            <p className="text-sm">Žádné záznamy historie spotřeby.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-gray-200">
              <thead className="bg-gray-50">
                <tr>
                  <th
                    onClick={handleSortByDate}
                    className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider cursor-pointer hover:bg-gray-100"
                  >
                    <div className="flex items-center">
                      Datum
                      {sortDescending ? <ChevronDown className="ml-1 h-4 w-4" /> : <ChevronUp className="ml-1 h-4 w-4" />}
                    </div>
                  </th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Typ záznamu</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Materiál</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Typ spotřeby</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Faktura</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Produkt</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Spotřeba</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Původní mn.</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Nové mn.</th>
                  <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider">Změna</th>
                  <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider">Typ změny</th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-gray-200">
                {items.map((item, index) => (
                  <tr key={`${item.recordType}-${item.packingMaterialId}-${item.createdAt}-${index}`} className="hover:bg-gray-50">
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-900">{formatDate(item.date)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.recordTypeText}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm font-medium text-gray-900">{item.materialName}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.consumptionTypeText ?? DASH}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.invoiceId ?? DASH}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.productCode ?? DASH}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-900">{formatNumber(item.amount)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-700">{formatNumber(item.oldQuantity)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-700">{formatNumber(item.newQuantity)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-right text-gray-900">{formatNumber(item.changeAmount)}</td>
                    <td className="px-4 py-3 whitespace-nowrap text-sm text-gray-700">{item.logTypeText ?? DASH}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        <Pagination
          totalCount={totalCount}
          pageNumber={pageNumber}
          pageSize={pageSize}
          totalPages={totalPages}
          onPageChange={setPageNumber}
          onPageSizeChange={(size) => {
            setPageSize(size);
            setPageNumber(1);
          }}
        />
      </div>
    </div>
  );
};

export default ConsumptionHistoryTab;
```

- [ ] **Step 2: Type-check + build + lint**

Run: `cd frontend && npx tsc --noEmit && npm run lint`
Expected: no errors. (No `console.log`; no `any`.)

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/packing-materials/ConsumptionHistoryTab.tsx
git commit -m "feat: add consumption history grid with filters, paging, and date sorting"
```

---

## Task 11: Frontend component test

**Files:**
- Test: `frontend/src/components/packing-materials/__tests__/ConsumptionHistoryTab.test.tsx`

- [ ] **Step 1: Write the component test (mock both hooks)**

```typescript
import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import ConsumptionHistoryTab from '../ConsumptionHistoryTab';
import * as hooks from '../../../api/hooks/usePackingMaterials';

jest.mock('../../../api/hooks/usePackingMaterials', () => {
  const actual = jest.requireActual('../../../api/hooks/usePackingMaterials');
  return {
    ...actual,
    usePackingMaterials: jest.fn(),
    useConsumptionHistory: jest.fn(),
  };
});

const mockUsePackingMaterials = hooks.usePackingMaterials as jest.Mock;
const mockUseConsumptionHistory = hooks.useConsumptionHistory as jest.Mock;

const sampleResponse = {
  items: [
    {
      recordType: 1,
      recordTypeText: 'Spotřeba',
      packingMaterialId: 1,
      materialName: 'Tape',
      date: '2026-01-10',
      createdAt: '2026-01-10T08:00:00Z',
      consumptionType: 1,
      consumptionTypeText: 'PerOrder',
      invoiceId: 'INV-1',
      productCode: 'P1',
      amount: 5,
    },
    {
      recordType: 2,
      recordTypeText: 'Změna množství',
      packingMaterialId: 1,
      materialName: 'Tape',
      date: '2026-01-12',
      createdAt: '2026-01-12T08:00:00Z',
      oldQuantity: 100,
      newQuantity: 90,
      changeAmount: -10,
      logTypeText: 'Manual',
    },
  ],
  totalCount: 2,
  pageNumber: 1,
  pageSize: 20,
  totalPages: 1,
};

describe('ConsumptionHistoryTab', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockUsePackingMaterials.mockReturnValue({ data: { materials: [{ id: 1, name: 'Tape' }] } });
    mockUseConsumptionHistory.mockReturnValue({ data: sampleResponse, isLoading: false, error: null, refetch: jest.fn() });
  });

  it('renders union rows for both record types', () => {
    render(<ConsumptionHistoryTab />);
    expect(screen.getByText('Spotřeba')).toBeInTheDocument();
    expect(screen.getByText('Změna množství')).toBeInTheDocument();
    expect(screen.getByText('INV-1')).toBeInTheDocument();
    expect(screen.getAllByText('Tape').length).toBeGreaterThan(0);
  });

  it('shows an empty state when there are no records', () => {
    mockUseConsumptionHistory.mockReturnValue({
      data: { items: [], totalCount: 0, pageNumber: 1, pageSize: 20, totalPages: 0 },
      isLoading: false,
      error: null,
      refetch: jest.fn(),
    });
    render(<ConsumptionHistoryTab />);
    expect(screen.getByText('Žádné záznamy historie spotřeby.')).toBeInTheDocument();
  });

  it('applies filters and requests page 1', () => {
    render(<ConsumptionHistoryTab />);
    fireEvent.change(screen.getByPlaceholderText('ID faktury'), { target: { value: 'INV-1' } });
    fireEvent.click(screen.getByText('Použít filtry'));
    expect(mockUseConsumptionHistory).toHaveBeenLastCalledWith(
      expect.objectContaining({ invoiceId: 'INV-1', pageNumber: 1 }),
    );
  });

  it('toggles date sort direction', () => {
    render(<ConsumptionHistoryTab />);
    fireEvent.click(screen.getByText('Datum'));
    expect(mockUseConsumptionHistory).toHaveBeenLastCalledWith(
      expect.objectContaining({ sortDescending: false }),
    );
  });
});
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `cd frontend && npm test -- --watchAll=false ConsumptionHistoryTab`
Expected: 4 tests PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/packing-materials/__tests__/ConsumptionHistoryTab.test.tsx
git commit -m "test: add consumption history grid component tests"
```

---

## Task 12: Full verification

- [ ] **Step 1: Backend build + format + tests**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded.

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting changes required (run `dotnet format backend/Anela.Heblo.sln` first if it reports changes, then re-run verify).

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterials"`
Expected: all PASS.

- [ ] **Step 2: Frontend build + lint + tests**

Run: `cd frontend && npm run build && npm run lint`
Expected: both succeed.

Run: `cd frontend && npm test -- --watchAll=false packing-materials`
Expected: all PASS.

- [ ] **Step 3: Manual smoke test (run the app, exercise both tabs)**

- Navigate to `/logistics/packing-materials`.
- **Tab "Nastavení":** confirm the materials table, the "Odečíst spotřebu" / "Přidat materiál" actions, and all modals behave exactly as before.
- **Tab "Historie spotřeby":** confirm the grid lists both consumption-fact rows and quantity-change rows in one table, newest date first. Verify:
  - Clicking the **Datum** header flips the order.
  - **Material** and **date-range** filters narrow both record types.
  - Setting **Faktura** or **Kód produktu** or **Typ spotřeby** hides quantity-change rows (expected union semantics).
  - **Pagination** controls and the page-size selector page through results.

- [ ] **Step 4 (optional): E2E spec**

If adding E2E coverage, create a Playwright spec under `frontend/test/e2e/<packing-materials module folder>/` that authenticates via `navigateToApp()`, opens `/logistics/packing-materials`, switches to the History tab, and asserts the grid renders. The E2E suite runs nightly, not in PR CI. Run locally with `./scripts/run-playwright-tests.sh` against staging.

- [ ] **Step 5: Final commit (if any formatting/lint fixes were applied)**

```bash
git add -A
git commit -m "chore: formatting and lint fixes for consumption history feature"
```

---

## Self-Review Notes

- **Spec coverage:** Settings tab (Task 9) ✓; History tab single grid (Task 10) ✓; union of both sources (Tasks 2/4/10) ✓; union of columns with `—` (Task 10) ✓; date ordering + sort toggle (Tasks 2/10) ✓; filters date/material/type/product/invoice (Tasks 2–4, 10) ✓; paging (Tasks 2–4, 10, shared `Pagination`) ✓.
- **Type consistency:** repository returns `MaterialConsumptionHistoryRecord`; handler maps to `MaterialConsumptionHistoryItemDto`; FE consumes `ConsumptionHistoryItemDto` (structurally matching the generated response). Method name `GetConsumptionHistoryAsync` and hook `useConsumptionHistory` used consistently across tasks.
- **Known post-generation step:** the FE hook (Task 8) depends on the NSwag-generated `packingMaterials_GetConsumptionHistory` signature produced in Task 7 — Task 7 Step 2 records the exact parameter order to align against. This is the one unavoidable generate-then-wire dependency.
- **Intentional scope limit:** sorting is Date-only (asc/desc); material/amount sorting is excluded because "amount" is ambiguous across the two unioned sources. Stated in Context.
