# Decouple PackingMaterials.ConsumptionCalculationService from Invoices — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ConsumptionCalculationService`'s direct dependency on `IIssuedInvoiceRepository` / `IssuedInvoice` with a PackingMaterials-owned contract (`IInvoiceConsumptionSource` + `InvoiceConsumptionHeader`) implemented by an `internal sealed` adapter in the Invoices module, then enforce the new boundary in `ModuleBoundariesTests`.

**Architecture:** Apply the established consumer-owns-contract / provider-owns-adapter pattern (`docs/architecture/development_guidelines.md` §"Cross-Module Communication Example") used by Leaflet→KnowledgeBase (2026-05-15) and Logistics→Manufacture (2026-05-16). PackingMaterials declares an interface + a sealed-record DTO in its `Contracts/` folder; Invoices provides an `internal sealed` adapter in `Application/Features/Invoices/Infrastructure/`; `InvoicesModule` registers the binding. The architecture test gets a third theory row with an empty inline allowlist (clean cutover, no residual violations).

**Tech Stack:** .NET 8, C# (nullable enabled), xUnit, FluentAssertions, Moq, MediatR, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`. No new NuGet packages, no schema changes, no MediatR/HTTP contract changes.

---

## File Structure

**Files to create:**

| Path | Responsibility |
|------|----------------|
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/IInvoiceConsumptionSource.cs` | Consumer-owned interface — single `GetHeadersByDateAsync` method returning `IReadOnlyList<InvoiceConsumptionHeader>`. |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/InvoiceConsumptionHeader.cs` | Consumer-owned `sealed record InvoiceConsumptionHeader(string Id, int ItemsCount)`. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs` | `internal sealed` adapter — projects `IssuedInvoice` to `InvoiceConsumptionHeader`. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockInvoiceConsumptionSource.cs` | In-memory test double of `IInvoiceConsumptionSource` (replaces the old `MockIssuedInvoiceRepository`). |
| `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs` | xUnit + Moq tests for the adapter (projection + token + date passthrough). |

**Files to modify:**

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs` | Drop `using Anela.Heblo.Domain.Features.Invoices;`; rename field/ctor param from `_invoiceRepository : IIssuedInvoiceRepository` to `_invoiceSource : IInvoiceConsumptionSource`; drop the redundant `.ToList()` on line 37; change `BuildFactRows` parameter from `List<IssuedInvoice>` to `IReadOnlyList<InvoiceConsumptionHeader>`. |
| `backend/src/Anela.Heblo.Application/Features/PackingMaterials/PackingMaterialsModule.cs` | Delete the `// Note: ConsumptionCalculationService depends on IIssuedInvoiceRepository…` comment at line 18. |
| `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs` | Add `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;`; register `services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();` after the repository registration, with the cross-module comment. |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Append a third theory row `PackingMaterials -> Invoices` with an inline empty `HashSet<string>(StringComparer.Ordinal)` allowlist. |
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs` | Drop `using Anela.Heblo.Domain.Features.Invoices;`; replace `MakeInvoice` helper with `MakeHeader`; replace `MockIssuedInvoiceRepository` with `MockInvoiceConsumptionSource`; update `BuildService` signature. |

**Files to delete:**

| Path | Reason |
|------|--------|
| `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockIssuedInvoiceRepository.cs` | Superseded by `MockInvoiceConsumptionSource`. Was only ever used by `ConsumptionCalculationServiceTests` (verified — no other test references it). |

---

## Task 1: Add the failing architecture-test rule for PackingMaterials → Invoices

This locks the boundary in place before any source changes. Run it once to **observe the violation**, then proceed — subsequent tasks will turn it green.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs:66-89`

- [ ] **Step 1: Append the third theory row**

Locate the `Rules()` method at line 66. Append a new `ModuleBoundaryRule` to the `TheoryData` after the Logistics row. The complete `Rules()` body should read:

```csharp
public static TheoryData<ModuleBoundaryRule> Rules() => new()
{
    new ModuleBoundaryRule(
        Name: "Leaflet -> KnowledgeBase",
        InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Leaflet",
        ForbiddenNamespacePrefixes: new[]
        {
            "Anela.Heblo.Domain.Features.KnowledgeBase",
            "Anela.Heblo.Application.Features.KnowledgeBase",
            "Anela.Heblo.Persistence.KnowledgeBase",
        },
        Allowlist: LeafletAllowlist),

    new ModuleBoundaryRule(
        Name: "Logistics -> Manufacture",
        InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Logistics",
        ForbiddenNamespacePrefixes: new[]
        {
            "Anela.Heblo.Domain.Features.Manufacture",
            "Anela.Heblo.Application.Features.Manufacture",
            "Anela.Heblo.Persistence.Manufacture",
        },
        Allowlist: LogisticsAllowlist),

    new ModuleBoundaryRule(
        Name: "PackingMaterials -> Invoices",
        InspectedNamespacePrefix: "Anela.Heblo.Application.Features.PackingMaterials",
        ForbiddenNamespacePrefixes: new[]
        {
            "Anela.Heblo.Domain.Features.Invoices",
            "Anela.Heblo.Application.Features.Invoices",
            "Anela.Heblo.Persistence.Invoices",
        },
        Allowlist: new HashSet<string>(StringComparer.Ordinal)),
};
```

The empty allowlist is **inlined deliberately** — no named constant. A named empty constant invites future drift ("just add one entry here, it's already a list"). The clean cutover is the goal.

- [ ] **Step 2: Verify the new theory case currently fails**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests.Consumer_types_should_not_reference_provider_owned_namespaces" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: `PackingMaterials -> Invoices` theory case FAILS with violations listing references such as:
- `…ConsumptionCalculationService -> Anela.Heblo.Domain.Features.Invoices.IIssuedInvoiceRepository`
- `…ConsumptionCalculationService -> Anela.Heblo.Domain.Features.Invoices.IssuedInvoice`

The two pre-existing theory cases (Leaflet, Logistics) must still PASS.

- [ ] **Step 3: Commit the failing test**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(arch): add failing PackingMaterials -> Invoices boundary rule"
```

---

## Task 2: Create the PackingMaterials-owned contract types

Pure type declarations. No tests — these are inert data definitions; their correctness is exercised by every subsequent task.

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/InvoiceConsumptionHeader.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/IInvoiceConsumptionSource.cs`

- [ ] **Step 1: Create `InvoiceConsumptionHeader.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

/// <summary>
/// Immutable projection of an invoice header containing only the fields the
/// daily consumption calculation needs. Owned by PackingMaterials; populated
/// by the Invoices module via <see cref="IInvoiceConsumptionSource"/> adapter.
/// </summary>
public sealed record InvoiceConsumptionHeader(string Id, int ItemsCount);
```

- [ ] **Step 2: Create `IInvoiceConsumptionSource.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.PackingMaterials.Contracts;

/// <summary>
/// PackingMaterials-owned read-only abstraction over invoice headers for a
/// single processing date. Implemented by the Invoices module via an adapter
/// (see <c>InvoiceConsumptionSourceAdapter</c>) per the cross-module
/// communication pattern in <c>docs/architecture/development_guidelines.md</c>.
/// </summary>
public interface IInvoiceConsumptionSource
{
    /// <summary>
    /// Returns the materialized list of invoice headers whose invoice date
    /// falls on <paramref name="date"/>. Each header carries only the fields
    /// required by <c>ConsumptionCalculationService.BuildFactRows</c>.
    /// </summary>
    Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default);
}
```

**Verify:** neither file declares `using Anela.Heblo.Domain.Features.Invoices;`. If it does, delete it — the contract must not leak provider types.

- [ ] **Step 3: Build the application project to confirm both files compile**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: build succeeds. No warnings about unused usings.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/
git commit -m "feat(packing-materials): add IInvoiceConsumptionSource contract and InvoiceConsumptionHeader DTO"
```

---

## Task 3: Add the new in-memory test double

Replaces the soon-to-be-deleted `MockIssuedInvoiceRepository`. Keep the same `SetInvoices`/date-filtering ergonomics so the test rewrite stays small.

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockInvoiceConsumptionSource.cs`

- [ ] **Step 1: Create `MockInvoiceConsumptionSource.cs`**

Write the file exactly:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class MockInvoiceConsumptionSource : IInvoiceConsumptionSource
{
    private readonly Dictionary<DateOnly, List<InvoiceConsumptionHeader>> _byDate = new();

    public void SetHeaders(DateOnly date, IEnumerable<InvoiceConsumptionHeader> headers)
    {
        _byDate[date] = headers.ToList();
    }

    public Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<InvoiceConsumptionHeader> result =
            _byDate.TryGetValue(date, out var headers) ? headers : new List<InvoiceConsumptionHeader>();
        return Task.FromResult(result);
    }
}
```

**Verify:** the file has no `using Anela.Heblo.Domain.Features.Invoices;` directive. The test assembly must stop referencing the Invoices domain namespace through this code path.

- [ ] **Step 2: Build the test project**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: build succeeds. (The existing `ConsumptionCalculationServiceTests.cs` still compiles because the old `MockIssuedInvoiceRepository.cs` is still present.)

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockInvoiceConsumptionSource.cs
git commit -m "test(packing-materials): add MockInvoiceConsumptionSource"
```

---

## Task 4: Refactor `ConsumptionCalculationService` to depend on the contract

This is the single largest behavioral edit. Apply all changes in one shot — the file is small (117 lines) and partial edits leave it uncompilable.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs`

- [ ] **Step 1: Replace the file contents**

The complete new file body is:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.Services;

public class ConsumptionCalculationService : IConsumptionCalculationService
{
    private readonly IPackingMaterialRepository _repository;
    private readonly IInvoiceConsumptionSource _invoiceSource;
    private readonly ILogger<ConsumptionCalculationService> _logger;

    public ConsumptionCalculationService(
        IPackingMaterialRepository repository,
        IInvoiceConsumptionSource invoiceSource,
        ILogger<ConsumptionCalculationService> logger)
    {
        _repository = repository;
        _invoiceSource = invoiceSource;
        _logger = logger;
    }

    public async Task<ProcessDailyConsumptionResult> ProcessDailyConsumptionAsync(
        DateOnly processingDate,
        CancellationToken cancellationToken = default)
    {
        if (await HasDayAlreadyBeenProcessedAsync(processingDate, cancellationToken))
        {
            _logger.LogInformation("Daily consumption processing for {Date} already completed, skipping", processingDate);
            return new ProcessDailyConsumptionResult(false, 0);
        }

        _logger.LogInformation("Starting daily consumption processing for {Date}", processingDate);

        var materials = (await _repository.GetAllWithAllocationsAsync(cancellationToken)).ToList();
        var invoices = await _invoiceSource.GetHeadersByDateAsync(processingDate, cancellationToken);

        var allFactRows = new List<PackingMaterialConsumption>();
        var decrementByMaterial = new Dictionary<PackingMaterial, decimal>();

        var processedCount = 0;

        foreach (var material in materials)
        {
            var rows = BuildFactRows(material, invoices, processingDate);
            var total = rows.Sum(r => r.Amount);

            if (total > 0)
            {
                allFactRows.AddRange(rows);
                decrementByMaterial[material] = total;
                processedCount++;
            }
        }

        foreach (var material in materials)
        {
            if (decrementByMaterial.TryGetValue(material, out var decrement))
            {
                var newQuantity = Math.Max(0, material.CurrentQuantity - decrement);
                material.UpdateQuantity(newQuantity, processingDate, LogEntryType.AutomaticConsumption);

                _logger.LogInformation("Processed material {MaterialName}: consumed {Consumption}, new quantity: {NewQuantity}",
                    material.Name, decrement, newQuantity);
            }
        }

        // Relies on EF change tracking — GetAllWithAllocationsAsync must NOT use AsNoTracking
        if (processedCount == 0 && materials.Count > 0)
        {
            var marker = materials[0];
            marker.UpdateQuantity(marker.CurrentQuantity, processingDate, LogEntryType.AutomaticConsumption);
        }

        if (allFactRows.Count > 0)
            await _repository.AddConsumptionRowsAsync(allFactRows, cancellationToken);

        await _repository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Completed daily consumption processing for {Date}. Processed {ProcessedCount} materials",
            processingDate, processedCount);

        return new ProcessDailyConsumptionResult(true, processedCount);
    }

    public async Task<bool> HasDayAlreadyBeenProcessedAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        return await _repository.HasDailyProcessingBeenRunAsync(date, cancellationToken);
    }

    private static List<PackingMaterialConsumption> BuildFactRows(
        PackingMaterial material,
        IReadOnlyList<InvoiceConsumptionHeader> invoices,
        DateOnly date)
    {
        return material.ConsumptionType switch
        {
            ConsumptionType.PerDay => new List<PackingMaterialConsumption>
            {
                new PackingMaterialConsumption(material.Id, date, ConsumptionType.PerDay, material.ConsumptionRate)
            },
            ConsumptionType.PerOrder => invoices
                .Select(inv => new PackingMaterialConsumption(
                    material.Id, date, ConsumptionType.PerOrder, material.ConsumptionRate, inv.Id))
                .ToList(),
            ConsumptionType.PerProduct => invoices
                .Select(inv => new PackingMaterialConsumption(
                    material.Id, date, ConsumptionType.PerProduct, material.ConsumptionRate * inv.ItemsCount, inv.Id))
                .Where(r => r.Amount > 0)
                .ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(material.ConsumptionType))
        };
    }
}
```

**Diff highlights vs. the original:**
- Removed `using Anela.Heblo.Domain.Features.Invoices;`.
- Added `using Anela.Heblo.Application.Features.PackingMaterials.Contracts;`.
- `_invoiceRepository : IIssuedInvoiceRepository` → `_invoiceSource : IInvoiceConsumptionSource`.
- Constructor parameter renamed accordingly.
- Line 37 changed from `(await _invoiceRepository.GetHeadersByDateAsync(...)).ToList()` to `await _invoiceSource.GetHeadersByDateAsync(...)` — the contract returns `IReadOnlyList<…>` already; the `.ToList()` becomes dead and is removed.
- `BuildFactRows` signature: `List<IssuedInvoice>` → `IReadOnlyList<InvoiceConsumptionHeader>`. `inv.Id` and `inv.ItemsCount` access patterns are identical.
- Everything else (EF change-tracking marker write, save semantics, three switch arms) is byte-identical.

- [ ] **Step 2: Confirm the application project still builds**

Run:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: succeeds. (The DI registration of `IInvoiceConsumptionSource` happens in Task 7 — the build doesn't require it.)

- [ ] **Step 3: Confirm the test project does NOT build yet**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: FAILS. `ConsumptionCalculationServiceTests.cs` still references `MockIssuedInvoiceRepository` / `IssuedInvoice` and constructs the service with the old constructor signature. Errors like `CS1503: cannot convert from 'MockIssuedInvoiceRepository' to 'IInvoiceConsumptionSource'` are expected — Task 5 fixes them.

- [ ] **Step 4: Do NOT commit yet**

Tasks 4 and 5 land together — the test project would be red between them. Move on.

---

## Task 5: Update `ConsumptionCalculationServiceTests` and remove the old mock

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs`
- Delete: `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockIssuedInvoiceRepository.cs`

- [ ] **Step 1: Replace the test file contents**

The complete new test file body is:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Application.Features.PackingMaterials.Services;
using Anela.Heblo.Domain.Features.PackingMaterials;
using Anela.Heblo.Domain.Features.PackingMaterials.Enums;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Anela.Heblo.Tests.Features.PackingMaterials;

public class ConsumptionCalculationServiceTests
{
    private readonly ILogger<ConsumptionCalculationService> _mockLogger;

    public ConsumptionCalculationServiceTests()
    {
        _mockLogger = new MockLogger<ConsumptionCalculationService>();
    }

    private static InvoiceConsumptionHeader MakeHeader(string id, int itemsCount)
    {
        return new InvoiceConsumptionHeader(id, itemsCount);
    }

    private static ConsumptionCalculationService BuildService(
        MockPackingMaterialRepository materialRepo,
        MockInvoiceConsumptionSource invoiceSource,
        ILogger<ConsumptionCalculationService> logger)
    {
        return new ConsumptionCalculationService(materialRepo, invoiceSource, logger);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_PerDay_EmitsOneFactRowPerMaterial()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Tape", 3m, ConsumptionType.PerDay, 100m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        var invoiceSource = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed);
        Assert.Single(materialRepo.AddedConsumptionRows);

        var row = materialRepo.AddedConsumptionRows[0];
        Assert.Equal(3m, row.Amount);
        Assert.Null(row.InvoiceId);
        Assert.Equal(ConsumptionType.PerDay, row.ConsumptionType);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_PerOrder_EmitsOneFactRowPerInvoicePerMaterial()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Box", 2m, ConsumptionType.PerOrder, 50m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });

        var invoiceSource = new MockInvoiceConsumptionSource();
        invoiceSource.SetHeaders(date, new[]
        {
            MakeHeader("INV-1", itemsCount: 3),
            MakeHeader("INV-2", itemsCount: 5)
        });

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed);
        Assert.Equal(2, materialRepo.AddedConsumptionRows.Count);

        Assert.All(materialRepo.AddedConsumptionRows, row => Assert.Equal(2m, row.Amount));
        Assert.All(materialRepo.AddedConsumptionRows, row => Assert.Equal(ConsumptionType.PerOrder, row.ConsumptionType));
        Assert.NotNull(materialRepo.AddedConsumptionRows[0].InvoiceId);
        Assert.NotNull(materialRepo.AddedConsumptionRows[1].InvoiceId);

        var totalDecrement = materialRepo.AddedConsumptionRows.Sum(r => r.Amount);
        Assert.Equal(4m, totalDecrement);

        // Quantity should be decremented by 4 — material mutated in-place
        var updated = materialRepo.Materials[0];
        Assert.Equal(46m, updated.CurrentQuantity);
        var log = updated.Logs.Single();
        Assert.Equal(50m, log.OldQuantity);
        Assert.Equal(46m, log.NewQuantity);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_PerProduct_ScalesByItemsCount()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Sticker", 1m, ConsumptionType.PerProduct, 100m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });

        var invoiceSource = new MockInvoiceConsumptionSource();
        invoiceSource.SetHeaders(date, new[]
        {
            MakeHeader("INV-A", itemsCount: 3),
            MakeHeader("INV-B", itemsCount: 5)
        });

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed);
        Assert.Equal(2, materialRepo.AddedConsumptionRows.Count);

        var amounts = materialRepo.AddedConsumptionRows.Select(r => r.Amount).OrderBy(a => a).ToList();
        Assert.Equal(new[] { 3m, 5m }, amounts);

        var totalDecrement = materialRepo.AddedConsumptionRows.Sum(r => r.Amount);
        Assert.Equal(8m, totalDecrement);

        var updated = materialRepo.Materials[0];
        Assert.Equal(92m, updated.CurrentQuantity);
        var log = updated.Logs.Single();
        Assert.Equal(100m, log.OldQuantity);
        Assert.Equal(92m, log.NewQuantity);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_ReturnsWasRunFalse_WhenAlreadyProcessed()
    {
        // Arrange
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        materialRepo.SetHasDailyProcessingBeenRun(date, true);
        var invoiceSource = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.False(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);
        Assert.Empty(materialRepo.AddedConsumptionRows);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_WritesMarkerLog_WhenZeroConsumption()
    {
        // Arrange — PerOrder material but zero invoices means zero consumption
        var date = new DateOnly(2025, 6, 15);
        var material = new PackingMaterial("Cards", 1m, ConsumptionType.PerOrder, 8000m);
        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { material });
        var invoiceSource = new MockInvoiceConsumptionSource();

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(0, result.MaterialsProcessed);

        // Quantity must NOT have changed
        var updated = materialRepo.Materials[0];
        Assert.Equal(8000m, updated.CurrentQuantity);

        var markerLog = updated.Logs.Single();
        Assert.Equal(LogEntryType.AutomaticConsumption, markerLog.LogType);
        Assert.Equal(date, markerLog.Date);
        Assert.Equal(8000m, markerLog.OldQuantity);
        Assert.Equal(8000m, markerLog.NewQuantity);

        // No fact rows for zero consumption (no invoices means no PerOrder rows)
        Assert.Empty(materialRepo.AddedConsumptionRows);
    }

    [Fact]
    public async Task ProcessDailyConsumptionAsync_MixedTypes_ZeroInvoices_PerDayDecrementsPerOrderGetsMarkerLog()
    {
        // Arrange — PerDay material always decrements; PerOrder material gets marker log when zero invoices
        var date = new DateOnly(2025, 6, 15);
        var perDayMaterial = new PackingMaterial("Tape", 5m, ConsumptionType.PerDay, 200m);
        var perOrderMaterial = new PackingMaterial("Box", 2m, ConsumptionType.PerOrder, 100m);

        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { perDayMaterial, perOrderMaterial });
        var invoiceSource = new MockInvoiceConsumptionSource(); // no invoices

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert
        Assert.True(result.WasRun);
        Assert.Equal(1, result.MaterialsProcessed); // only PerDay counted

        // PerDay material should be decremented
        var tape = materialRepo.Materials.First(m => m.Name == "Tape");
        Assert.Equal(195m, tape.CurrentQuantity);
        Assert.Single(tape.Logs);

        // PerOrder material should be untouched — but idempotency marker written on first material (Tape)
        var box = materialRepo.Materials.First(m => m.Name == "Box");
        Assert.Equal(100m, box.CurrentQuantity);
        Assert.Empty(box.Logs);

        // One PerDay fact row only
        Assert.Single(materialRepo.AddedConsumptionRows);
        Assert.Equal(ConsumptionType.PerDay, materialRepo.AddedConsumptionRows[0].ConsumptionType);

        // Subsequent re-run should be blocked (Tape's log counts as the marker)
        materialRepo.SetHasDailyProcessingBeenRun(date, true);
        var rerun = await service.ProcessDailyConsumptionAsync(date);
        Assert.False(rerun.WasRun);
    }

    [Fact]
    public async Task HasDayAlreadyBeenProcessedAsync_ShouldReturnCorrectValue()
    {
        // Arrange
        var materialRepo = new MockPackingMaterialRepository();
        var invoiceSource = new MockInvoiceConsumptionSource();
        var service = BuildService(materialRepo, invoiceSource, _mockLogger);
        var date = DateOnly.FromDateTime(DateTime.Today);
        materialRepo.SetHasDailyProcessingBeenRun(date, true);

        // Act
        var result = await service.HasDayAlreadyBeenProcessedAsync(date);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task LogDecrement_Invariant()
    {
        // Arrange: 1 PerDay rate=5, 1 PerOrder rate=2 with 3 invoices, 1 PerProduct rate=1 with invoices ItemsCount=[4,6,0]
        var date = new DateOnly(2025, 6, 15);

        var perDayMaterial = new PackingMaterial("Tape", 5m, ConsumptionType.PerDay, 200m);
        var perOrderMaterial = new PackingMaterial("Box", 2m, ConsumptionType.PerOrder, 100m);
        var perProductMaterial = new PackingMaterial("Sticker", 1m, ConsumptionType.PerProduct, 150m);

        var materialRepo = new MockPackingMaterialRepository();
        materialRepo.SetMaterials(new[] { perDayMaterial, perOrderMaterial, perProductMaterial });

        var invoiceSource = new MockInvoiceConsumptionSource();
        invoiceSource.SetHeaders(date, new[]
        {
            MakeHeader("INV-1", itemsCount: 4),
            MakeHeader("INV-2", itemsCount: 6),
            MakeHeader("INV-3", itemsCount: 0)
        });

        var service = BuildService(materialRepo, invoiceSource, _mockLogger);

        // Act
        var result = await service.ProcessDailyConsumptionAsync(date);

        // Assert: verify SUM(fact rows per material) == |ChangeAmount in log| for each material
        Assert.True(result.WasRun);

        var allRows = materialRepo.AddedConsumptionRows;

        // PerDay: 1 row, amount = 5
        var perDayRows = allRows.Where(r => r.ConsumptionType == ConsumptionType.PerDay).ToList();
        Assert.Single(perDayRows);
        Assert.Equal(5m, perDayRows.Sum(r => r.Amount));

        var tapeLog = materialRepo.Materials.First(m => m.Name == "Tape").Logs.Single();
        Assert.Equal(5m, Math.Abs(tapeLog.ChangeAmount));
        Assert.Equal(perDayRows.Sum(r => r.Amount), Math.Abs(tapeLog.ChangeAmount));

        // PerOrder: 3 rows (one per invoice), each amount = 2, total = 6
        var perOrderRows = allRows.Where(r => r.ConsumptionType == ConsumptionType.PerOrder).ToList();
        Assert.Equal(3, perOrderRows.Count);
        Assert.Equal(6m, perOrderRows.Sum(r => r.Amount));

        var boxLog = materialRepo.Materials.First(m => m.Name == "Box").Logs.Single();
        Assert.Equal(6m, Math.Abs(boxLog.ChangeAmount));
        Assert.Equal(perOrderRows.Sum(r => r.Amount), Math.Abs(boxLog.ChangeAmount));

        // PerProduct: 2 rows (zero-amount row for INV-3 is filtered), amounts = 4, 6. Total = 10.
        var perProductRows = allRows.Where(r => r.ConsumptionType == ConsumptionType.PerProduct).ToList();
        Assert.Equal(2, perProductRows.Count);
        var perProductTotal = perProductRows.Sum(r => r.Amount);
        Assert.Equal(10m, perProductTotal);

        var stickerLog = materialRepo.Materials.First(m => m.Name == "Sticker").Logs.Single();
        Assert.Equal(10m, Math.Abs(stickerLog.ChangeAmount));
        Assert.Equal(perProductRows.Sum(r => r.Amount), Math.Abs(stickerLog.ChangeAmount));
    }
}
```

**Note on the date-binding helper change:** the old `MakeInvoice` took an `InvoiceDate`; the new `MakeHeader` doesn't — the mock now maps headers by date via `SetHeaders(date, …)`. This is intentional: the contract returns only the projection PackingMaterials needs, and the test mock keeps its routing externalized rather than embedded in the value.

- [ ] **Step 2: Delete the obsolete mock**

```bash
git rm backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockIssuedInvoiceRepository.cs
```

- [ ] **Step 3: Build the test project to verify the rewrite compiles**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: succeeds with zero errors. (Other test files that may still use `MockIssuedInvoiceRepository` would fail here — none are expected since the spec lists it as used only by `ConsumptionCalculationServiceTests`. If a build error surfaces a different consumer, stop and report.)

- [ ] **Step 4: Run the PackingMaterials tests — all eight should pass**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ConsumptionCalculationServiceTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: `Passed: 8, Failed: 0, Skipped: 0`. (Eight `[Fact]` cases in the file.)

- [ ] **Step 5: Re-run the architecture theory — `PackingMaterials -> Invoices` now passes**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: all three theory cases (Leaflet, Logistics, PackingMaterials) PASS, plus the `Logistics_types_should_not_reference_Purchase_owned_namespaces` fact PASSES.

- [ ] **Step 6: Commit Tasks 4 and 5 together**

```bash
git add backend/src/Anela.Heblo.Application/Features/PackingMaterials/Services/ConsumptionCalculationService.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/ConsumptionCalculationServiceTests.cs \
        backend/test/Anela.Heblo.Tests/Features/PackingMaterials/MockIssuedInvoiceRepository.cs
git commit -m "refactor(packing-materials): depend on IInvoiceConsumptionSource contract, not IIssuedInvoiceRepository"
```

---

## Task 6: Add the Invoices-side adapter and its tests (TDD)

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs`

The `Anela.Heblo.Application` project already declares `[assembly: InternalsVisibleTo("Anela.Heblo.Tests")]` in `AssemblyInfo.cs:3`, so the `internal sealed` adapter is directly testable.

- [ ] **Step 1: Write the failing adapter tests**

Create the directory if needed (`backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/`) and write the file exactly:

```csharp
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Invoices.Infrastructure;

public class InvoiceConsumptionSourceAdapterTests
{
    private readonly Mock<IIssuedInvoiceRepository> _repository = new();

    private InvoiceConsumptionSourceAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task GetHeadersByDateAsync_forwards_date_and_token_to_repository()
    {
        var date = new DateOnly(2025, 7, 4);
        using var cts = new CancellationTokenSource();
        var ct = cts.Token;

        _repository
            .Setup(r => r.GetHeadersByDateAsync(date, ct))
            .ReturnsAsync(new List<IssuedInvoice>());

        var adapter = CreateAdapter();

        await adapter.GetHeadersByDateAsync(date, ct);

        _repository.Verify(r => r.GetHeadersByDateAsync(date, ct), Times.Once);
    }

    [Fact]
    public async Task GetHeadersByDateAsync_projects_each_invoice_to_header_with_id_and_items_count()
    {
        var date = new DateOnly(2025, 7, 4);
        var invoices = new List<IssuedInvoice>
        {
            new IssuedInvoice { Id = "INV-1", ItemsCount = 3 },
            new IssuedInvoice { Id = "INV-2", ItemsCount = 7 },
        };

        _repository
            .Setup(r => r.GetHeadersByDateAsync(date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(invoices);

        var adapter = CreateAdapter();

        var result = await adapter.GetHeadersByDateAsync(date, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Should().Be(new InvoiceConsumptionHeader("INV-1", 3));
        result[1].Should().Be(new InvoiceConsumptionHeader("INV-2", 7));
    }

    [Fact]
    public async Task GetHeadersByDateAsync_returns_empty_list_when_repository_returns_empty()
    {
        _repository
            .Setup(r => r.GetHeadersByDateAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IssuedInvoice>());

        var adapter = CreateAdapter();

        var result = await adapter.GetHeadersByDateAsync(new DateOnly(2025, 7, 4), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Verify the adapter tests don't compile yet**

Run:

```bash
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: FAILS with `CS0246: The type or namespace name 'InvoiceConsumptionSourceAdapter' could not be found …`. This is the "red" of TDD.

- [ ] **Step 3: Create the adapter to make the build green**

Write `backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs` exactly:

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
using Anela.Heblo.Domain.Features.Invoices;

namespace Anela.Heblo.Application.Features.Invoices.Infrastructure;

internal sealed class InvoiceConsumptionSourceAdapter : IInvoiceConsumptionSource
{
    private readonly IIssuedInvoiceRepository _repository;

    public InvoiceConsumptionSourceAdapter(IIssuedInvoiceRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<InvoiceConsumptionHeader>> GetHeadersByDateAsync(
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var invoices = await _repository.GetHeadersByDateAsync(date, cancellationToken);
        return invoices
            .Select(invoice => new InvoiceConsumptionHeader(invoice.Id, invoice.ItemsCount))
            .ToList();
    }
}
```

**Pure pass-through.** No filtering, no enrichment, no retry, no caching. The `.ToList()` is required: the underlying repository returns `IEnumerable<IssuedInvoice>` (potentially deferred), and the adapter materializes once at the boundary so PackingMaterials can enumerate the snapshot multiple times safely.

- [ ] **Step 4: Run the adapter tests**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~InvoiceConsumptionSourceAdapterTests" \
  --no-restore --logger "console;verbosity=normal"
```

Expected: `Passed: 3, Failed: 0`.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Invoices/Infrastructure/InvoiceConsumptionSourceAdapterTests.cs
git commit -m "feat(invoices): add InvoiceConsumptionSourceAdapter implementing IInvoiceConsumptionSource"
```

---

## Task 7: Wire up DI and remove the PackingMaterials cross-module note

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/PackingMaterials/PackingMaterialsModule.cs`

- [ ] **Step 1: Register the adapter in `InvoicesModule`**

Open `backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs`. The file currently starts:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Anela.Heblo.Domain.Features.Invoices;
using Anela.Heblo.Domain.Features.Bank;
using Anela.Heblo.Persistence.Features.Invoices;
using Anela.Heblo.Application.Features.Invoices.Infrastructure;
using Anela.Heblo.Application.Features.Invoices.Infrastructure.Transformations;
using Anela.Heblo.Application.Features.Invoices.Services;
```

Add a `using` for the consumer contract namespace immediately after the existing `Microsoft.Extensions.DependencyInjection` line (or wherever it sorts):

```csharp
using Anela.Heblo.Application.Features.PackingMaterials.Contracts;
```

Then, in `AddInvoicesModule`, after line `services.AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>();` (currently line 19), insert:

```csharp
// Cross-module contract: Invoices implements PackingMaterials' IInvoiceConsumptionSource
// via an adapter. DI registration owned by provider (Invoices), not consumer
// (PackingMaterials) — keeps the dependency direction inverted properly.
services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();
```

The final `AddInvoicesModule` body should read:

```csharp
public static IServiceCollection AddInvoicesModule(this IServiceCollection services)
{
    // Register repositories
    services.AddScoped<IIssuedInvoiceRepository, IssuedInvoiceRepository>();

    // Cross-module contract: Invoices implements PackingMaterials' IInvoiceConsumptionSource
    // via an adapter. DI registration owned by provider (Invoices), not consumer
    // (PackingMaterials) — keeps the dependency direction inverted properly.
    services.AddScoped<IInvoiceConsumptionSource, InvoiceConsumptionSourceAdapter>();

    // Register services
    services.AddScoped<IInvoiceImportService, InvoiceImportService>();

    // Hangfire jobs are now automatically discovered via IRecurringJob interface

    // Register FlexiBee client (from SDK)
    // Note: IIssuedInvoiceClient registration should be done in Flexi adapter module

    // Register transformations
    services.AddTransient<IIssuedInvoiceImportTransformation, GiftWithoutVATIssuedInvoiceImportTransformation>();
    services.AddTransient<IIssuedInvoiceImportTransformation, RemoveDAtTheEndOfProductCodeIssuedInvoiceImportTransformation>();

    // Product mapping transformations can be registered based on configuration
    services.AddTransient<IIssuedInvoiceImportTransformation>(provider =>
        new ProductMappingIssuedInvoiceImportTransformation("1287", "SLU000001"));

    // MediatR handlers are automatically registered by MediatR scan

    return services;
}
```

- [ ] **Step 2: Remove the cross-module note from `PackingMaterialsModule`**

Open `backend/src/Anela.Heblo.Application/Features/PackingMaterials/PackingMaterialsModule.cs`. Delete line 18:

```csharp
        // Note: ConsumptionCalculationService depends on IIssuedInvoiceRepository, which is registered by InvoicesModule
```

The remaining `// Register services` comment on the line above stays. The final method body should read:

```csharp
public static IServiceCollection AddPackingMaterialsModule(this IServiceCollection services)
{
    // Register repositories
    services.AddScoped<IPackingMaterialRepository, PackingMaterialRepository>();
    services.AddScoped<IPackingMaterialAllocationRepository, PackingMaterialAllocationRepository>();

    // Register services
    services.AddScoped<IConsumptionCalculationService, ConsumptionCalculationService>();

    // Register Hangfire jobs
    services.AddScoped<DailyConsumptionJob>();

    // MediatR handlers are automatically registered by MediatR scan

    return services;
}
```

- [ ] **Step 3: Build the entire solution**

Run:

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: succeeds with zero errors.

- [ ] **Step 4: Run the full backend test suite**

Run:

```bash
dotnet test backend/Anela.Heblo.sln --no-build --logger "console;verbosity=normal"
```

Expected: all tests pass, including:
- The three `ModuleBoundariesTests` theory cases.
- All eight `ConsumptionCalculationServiceTests` cases.
- All three `InvoiceConsumptionSourceAdapterTests` cases.

If any unrelated test fails, investigate before proceeding — it likely reveals an unexpected `IIssuedInvoiceRepository` consumer in PackingMaterials missed in earlier steps.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Invoices/InvoicesModule.cs \
        backend/src/Anela.Heblo.Application/Features/PackingMaterials/PackingMaterialsModule.cs
git commit -m "feat(invoices): register InvoiceConsumptionSourceAdapter; drop PackingMaterials cross-module note"
```

---

## Task 8: Final FR-8 audit, format, and full validation

Triple-check the boundary is clean before declaring done. This task discovers any reference the architecture test might have missed (e.g. inside method bodies, which the reflection helper acknowledges as a known limitation at `ModuleBoundariesTests.cs:230`).

**Files:** none (audit + commands only).

- [ ] **Step 1: Grep for any residual `Anela.Heblo.*Features.Invoices` references inside PackingMaterials source and persistence**

Run:

```bash
grep -rn "Anela.Heblo.*Features.Invoices" \
  backend/src/Anela.Heblo.Application/Features/PackingMaterials \
  backend/src/Anela.Heblo.Persistence/PackingMaterials \
  || echo "CLEAN"
```

Expected output: exactly `CLEAN`. If any line is returned, inspect it:
- If it's a legitimate cross-module reference, it must be lifted behind the same contract pattern or added to the architecture-test allowlist with a justification comment (matching the precedent in `ModuleBoundariesTests.cs:29-45`).
- If it's a stale `using` directive, delete it.

- [ ] **Step 2: Grep the test side too**

Run:

```bash
grep -rn "Anela.Heblo.*Features.Invoices" \
  backend/test/Anela.Heblo.Tests/Features/PackingMaterials \
  || echo "CLEAN"
```

Expected output: exactly `CLEAN`. (FR-7 acceptance.)

- [ ] **Step 3: Run `dotnet format` over the touched projects**

Run:

```bash
dotnet format backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet format backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: no formatting drift (or any drift is auto-corrected with no failures).

- [ ] **Step 4: Full build + full test run as the final gate**

Run:

```bash
dotnet build backend/Anela.Heblo.sln && \
dotnet test backend/Anela.Heblo.sln --no-build --logger "console;verbosity=normal"
```

Expected: build succeeds; every test passes (no regressions anywhere in the solution).

- [ ] **Step 5: If `dotnet format` or any auto-cleanup made changes, commit them**

```bash
git status
# If files are modified:
git add -A
git commit -m "chore: apply dotnet format after PackingMaterials -> Invoices decoupling"
```

If `git status` shows nothing, skip the commit.

---

## Self-Review (already performed)

**Spec coverage:**

| Spec requirement | Task(s) |
|------------------|---------|
| FR-1: Define `IInvoiceConsumptionSource` in PackingMaterials Contracts | Task 2, Step 2 |
| FR-2: Define `InvoiceConsumptionHeader` sealed record | Task 2, Step 1 |
| FR-3: Refactor `ConsumptionCalculationService` to depend on the contract | Task 4 |
| FR-4: Provide `InvoiceConsumptionSourceAdapter` in Invoices module | Task 6, Step 3 |
| FR-5: Register adapter in `InvoicesModule`; remove `PackingMaterialsModule` note | Task 7 |
| FR-6: Extend `ModuleBoundariesTests` with `PackingMaterials -> Invoices` rule | Task 1 |
| FR-7: Update unit tests to use the contract; new `MockInvoiceConsumptionSource` | Task 3 + Task 5 |
| FR-8: Verify no other PackingMaterials code references Invoices | Task 8, Steps 1-2 |
| NFR-1: Performance — adapter is in-memory projection only | Task 6, Step 3 (`.Select(...).ToList()` pass-through) |
| NFR-2: Security — exposes strictly less data than before | Inherent to FR-2 (two-field record) |
| NFR-3: Maintainability — zero PackingMaterials → Invoices compile-time refs | Enforced by FR-6 test |
| NFR-4: Test coverage preserved + adapter covered | Task 5, Step 4 + Task 6, Step 4 |
| Spec "Out of Scope" items (rename existing `IIssuedInvoiceSource`, EF marker subtlety, etc.) | Not modified — Task 4, Step 1 preserves the marker block verbatim |

**Placeholder scan:** all "implementation later," "appropriate error handling," "similar to Task N" patterns checked — none present. Every code step contains the complete code to write.

**Type consistency:**
- `IInvoiceConsumptionSource.GetHeadersByDateAsync` returns `Task<IReadOnlyList<InvoiceConsumptionHeader>>` in Task 2, Task 5, Task 6, Task 7 — consistent everywhere.
- `InvoiceConsumptionHeader(string Id, int ItemsCount)` signature is consistent across all references.
- `_invoiceSource : IInvoiceConsumptionSource` field name is consistent in Task 4 and Task 5.
- `MockInvoiceConsumptionSource.SetHeaders(DateOnly, IEnumerable<InvoiceConsumptionHeader>)` declared in Task 3, used in Task 5.
- `MakeHeader(string id, int itemsCount)` helper signature defined in Task 5 and used five times within the same file.

No issues found.
