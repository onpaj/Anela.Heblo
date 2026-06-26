# Decouple `CatalogRepository` from Logistics, Purchase, and Manufacture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sever `CatalogRepository`'s direct dependencies on six provider-owned interfaces (Logistics / Purchase / Manufacture / Manufacture.Inventory) by introducing three Catalog-owned source contracts implemented by provider-side adapters; also remove the dead `IManufactureClient` field.

**Architecture:** Apply the established consumer-owned contract + provider-owned adapter pattern already used for `ILeafletKnowledgeSource` and `IInventoryReservationService`. Three new `internal sealed` adapters live under each provider's `Features/<Module>/Infrastructure/` folder and are registered for DI in their respective provider `Module.cs`. `CatalogRepository` consumes only the three new contracts. Behavior-preserving: no API, persistence, or cache-logic changes. A new trio of rules in `ModuleBoundariesTests` (Catalog → Logistics, Catalog → Purchase, Catalog → Manufacture) guards against regression — the Manufacture rule allowlists `ManufactureHistoryRecord` (a deliberate pragmatic leak retained because it threads through `CachedManufactureHistoryData` and `CatalogAggregate.ManufactureHistory`) and the three pre-existing `IManufactureClient` handler injections that are out of scope.

**Tech Stack:** .NET 8, C# 12, xUnit, FluentAssertions, Moq, Microsoft.Extensions.DependencyInjection.

---

## File Structure

**New files:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogTransportSource.cs` — Catalog-owned read contract over transport-box aggregates.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogPurchaseSource.cs` — Catalog-owned read contract for purchase ordered quantities.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs` — Catalog-owned read contract for planned quantities, history, and manufactured inventory.
- `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs` — Adapter implementing `ICatalogTransportSource` over `ITransportBoxRepository`; performs the SelectMany/GroupBy/Sum aggregation that lives in `CatalogRepository` today.
- `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs` — Adapter implementing `ICatalogPurchaseSource` over `IPurchaseOrderRepository`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs` — Adapter implementing `ICatalogManufactureSource` over `IManufactureOrderRepository`, `IManufactureHistoryClient`, and `IManufacturedProductInventoryRepository`.
- `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapterTests.cs` — Adapter unit tests with golden-test fixtures for the aggregation logic.
- `backend/test/Anela.Heblo.Tests/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapterTests.cs` — Adapter delegation test.
- `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapterTests.cs` — Adapter delegation tests for the three methods.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleContractResolutionTests.cs` — DI smoke test resolving the three new contracts from the application's `IServiceProvider`.

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — Constructor delta −3 (drop six provider interfaces + add three contracts), call sites at lines 129 / 250 / 892–924 rewired, four cross-module `using` directives removed (one `Domain.Features.Manufacture` retained for `ManufactureHistoryRecord`), three private helpers (`GetProductsInTransport`, `GetProductsInReserve`, `GetProductsInQuarantine`, plus `GetProductsOrdered`, `GetProductsPlanned`) deleted.
- `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs` — Add one `AddScoped` line for `ICatalogTransportSource → LogisticsCatalogTransportSourceAdapter`.
- `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs` — Add one `AddScoped` line for `ICatalogPurchaseSource → PurchaseCatalogSourceAdapter`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` — Add one `AddScoped` line for `ICatalogManufactureSource → ManufactureCatalogSourceAdapter`.
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` — Add three new `ModuleBoundaryRule` entries (Catalog → Logistics / Purchase / Manufacture) with allowlists.
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — Replace six provider-interface mocks with three contract mocks in constructor invocation; drop `_manufactureClientMock`.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — Same substitution as above.

**Not modified (verified out of scope):**
- `backend/test/Anela.Heblo.Tests/Controllers/CatalogRepositoryDebugTest.cs` — Resolves `ICatalogRepository` from DI; no constructor reference.
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/{GetProductComposition,GetProductUsage,UpdateProductCompositionOrder}/*Handler.cs` — Still inject `IManufactureClient`; tracked as follow-up via allowlist entries (see Task 11).

---

## Task 1: Add the three Catalog-owned source contracts

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogTransportSource.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogPurchaseSource.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs`

Each contract is **public** (consumed by provider-side adapters that live in the same assembly but a different namespace — they need to see the type). No tests required for plain interface definitions; the DI smoke test in Task 10 exercises them.

- [ ] **Step 1: Create `ICatalogTransportSource`**

Write `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogTransportSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Logistics transport-box state.
/// Implemented by the Logistics module via an adapter.
/// Returns productCode → summed item amount dictionaries.
/// </summary>
public interface ICatalogTransportSource
{
    Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken);

    Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken);

    Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Create `ICatalogPurchaseSource`**

Write `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogPurchaseSource.cs`:

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Purchase ordered-quantity totals.
/// Implemented by the Purchase module via an adapter.
/// </summary>
public interface ICatalogPurchaseSource
{
    Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Create `ICatalogManufactureSource`**

Write `backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

/// <summary>
/// Catalog-owned read abstraction over Manufacture planned-quantities, production history,
/// and manufactured-inventory totals. Implemented by the Manufacture module via an adapter.
///
/// NOTE: Returns Domain.Features.Manufacture.ManufactureHistoryRecord — a deliberate
/// pragmatic leak because this type is already woven through Catalog's CachedManufactureHistoryData
/// and CatalogAggregate.ManufactureHistory. The leak is allowlisted in ModuleBoundariesTests.
/// Tracked follow-up: introduce a Catalog-owned CatalogManufactureHistoryRecord DTO.
/// </summary>
public interface ICatalogManufactureSource
{
    Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken);

    Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Build to verify the new files compile**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS with no errors. Existing warnings unchanged.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogTransportSource.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogPurchaseSource.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Contracts/ICatalogManufactureSource.cs
git commit -m "feat(catalog): add Catalog-owned source contracts for transport / purchase / manufacture"
```

---

## Task 2: Write failing tests for the Logistics adapter

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapterTests.cs`

The Logistics adapter is the only one with non-trivial behavior (the SelectMany/GroupBy/Sum aggregation). Tests exercise each method against a synthetic `TransportBox` fixture and assert byte-identical output to what `CatalogRepository` produces today. Use Moq for `ITransportBoxRepository`. Predicates are `Expression<Func<TransportBox, bool>>` — match via `It.IsAny<Expression<...>>()` or `It.Is<...>(...)` for predicate identity.

- [ ] **Step 1: Write the failing tests**

Write `backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapterTests.cs`:

```csharp
using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Logistics.Infrastructure;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Infrastructure;

public class LogisticsCatalogTransportSourceAdapterTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock = new();
    private readonly LogisticsCatalogTransportSourceAdapter _adapter;

    public LogisticsCatalogTransportSourceAdapterTests()
    {
        _adapter = new LogisticsCatalogTransportSourceAdapter(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_AggregatesItemAmountsByProductCode()
    {
        // Arrange
        var boxes = new[]
        {
            CreateBoxWithItems(("PROD-A", 3.0), ("PROD-A", 2.0), ("PROD-B", 7.0)),
            CreateBoxWithItems(("PROD-A", 5.0), ("PROD-C", 1.0)),
        };
        _repositoryMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(boxes);

        // Act
        var result = await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["PROD-A"] = 10, // 3 + 2 + 5 (cast to int)
            ["PROD-B"] = 7,
            ["PROD-C"] = 1,
        });
    }

    [Fact]
    public async Task GetProductsInTransportAsync_UsesInTransportPredicate()
    {
        // Arrange
        Expression<Func<TransportBox, bool>>? capturedPredicate = null;
        _repositoryMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                true,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<TransportBox, bool>>, bool, CancellationToken>(
                (predicate, _, _) => capturedPredicate = predicate)
            .ReturnsAsync(Array.Empty<TransportBox>());

        // Act
        await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        // Assert
        capturedPredicate.Should().BeSameAs(TransportBox.IsInTransportPredicate);
    }

    [Fact]
    public async Task GetProductsInReserveAsync_UsesInReservePredicate()
    {
        Expression<Func<TransportBox, bool>>? capturedPredicate = null;
        _repositoryMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                true,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<TransportBox, bool>>, bool, CancellationToken>(
                (predicate, _, _) => capturedPredicate = predicate)
            .ReturnsAsync(Array.Empty<TransportBox>());

        await _adapter.GetProductsInReserveAsync(CancellationToken.None);

        capturedPredicate.Should().BeSameAs(TransportBox.IsInReservePredicate);
    }

    [Fact]
    public async Task GetProductsInQuarantineAsync_UsesInQuarantinePredicate()
    {
        Expression<Func<TransportBox, bool>>? capturedPredicate = null;
        _repositoryMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                true,
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<TransportBox, bool>>, bool, CancellationToken>(
                (predicate, _, _) => capturedPredicate = predicate)
            .ReturnsAsync(Array.Empty<TransportBox>());

        await _adapter.GetProductsInQuarantineAsync(CancellationToken.None);

        capturedPredicate.Should().BeSameAs(TransportBox.IsInQuarantinePredicate);
    }

    [Fact]
    public async Task GetProductsInTransportAsync_AlwaysPassesIncludeDetailsTrue()
    {
        // Arrange
        bool capturedIncludeDetails = false;
        _repositoryMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<Expression<Func<TransportBox, bool>>, bool, CancellationToken>(
                (_, includeDetails, _) => capturedIncludeDetails = includeDetails)
            .ReturnsAsync(Array.Empty<TransportBox>());

        // Act
        await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        // Assert
        capturedIncludeDetails.Should().BeTrue("the existing CatalogRepository helper passes includeDetails: true");
    }

    [Fact]
    public async Task GetProductsInTransportAsync_NoBoxes_ReturnsEmptyDictionary()
    {
        _repositoryMock
            .Setup(r => r.FindAsync(
                It.IsAny<Expression<Func<TransportBox, bool>>>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<TransportBox>());

        var result = await _adapter.GetProductsInTransportAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    private static TransportBox CreateBoxWithItems(params (string ProductCode, double Amount)[] items)
    {
        var box = new TransportBox();
        foreach (var (productCode, amount) in items)
        {
            box.Items.Add(new TransportBoxItem
            {
                ProductCode = productCode,
                Amount = amount,
            });
        }
        return box;
    }
}
```

> **Note on `TransportBox` / `TransportBoxItem` construction.** If the parameterless `new TransportBox()` is not viable (private setters / required ctor args), inspect `backend/src/Anela.Heblo.Domain/Features/Logistics/Transport/TransportBox.cs` and adapt the factory to match. The aggregation logic only requires `.Items` to be iterable and each item to expose `.ProductCode` and `.Amount`.

- [ ] **Step 2: Run tests to verify they fail (red)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsCatalogTransportSourceAdapterTests"`
Expected: FAIL — `LogisticsCatalogTransportSourceAdapter` not defined.

- [ ] **Step 3: Commit (failing tests first)**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapterTests.cs
git commit -m "test(catalog): failing tests for LogisticsCatalogTransportSourceAdapter"
```

---

## Task 3: Implement the Logistics adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs`

The aggregation logic moves verbatim from `CatalogRepository` lines 892–914. Keep `includeDetails: true`, the `(int)s.Amount` cast, and the same three static predicates.

- [ ] **Step 1: Create the Infrastructure folder placeholder (if missing)**

The folder `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/` does not exist today. It will be created automatically when the file below is written. No `.gitkeep` needed.

- [ ] **Step 2: Write the adapter**

Write `backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Logistics.Transport;

namespace Anela.Heblo.Application.Features.Logistics.Infrastructure;

internal sealed class LogisticsCatalogTransportSourceAdapter : ICatalogTransportSource
{
    private readonly ITransportBoxRepository _transportBoxRepository;

    public LogisticsCatalogTransportSourceAdapter(ITransportBoxRepository transportBoxRepository)
    {
        _transportBoxRepository = transportBoxRepository;
    }

    public Task<Dictionary<string, int>> GetProductsInTransportAsync(CancellationToken cancellationToken) =>
        AggregateByProductCodeAsync(TransportBox.IsInTransportPredicate, cancellationToken);

    public Task<Dictionary<string, int>> GetProductsInReserveAsync(CancellationToken cancellationToken) =>
        AggregateByProductCodeAsync(TransportBox.IsInReservePredicate, cancellationToken);

    public Task<Dictionary<string, int>> GetProductsInQuarantineAsync(CancellationToken cancellationToken) =>
        AggregateByProductCodeAsync(TransportBox.IsInQuarantinePredicate, cancellationToken);

    private async Task<Dictionary<string, int>> AggregateByProductCodeAsync(
        System.Linq.Expressions.Expression<Func<TransportBox, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var boxes = await _transportBoxRepository.FindAsync(predicate, includeDetails: true, cancellationToken: cancellationToken);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }
}
```

- [ ] **Step 3: Run tests to verify they pass (green)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~LogisticsCatalogTransportSourceAdapterTests"`
Expected: PASS — six tests green.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Infrastructure/LogisticsCatalogTransportSourceAdapter.cs
git commit -m "feat(catalog): implement LogisticsCatalogTransportSourceAdapter"
```

---

## Task 4: Write failing tests for the Purchase adapter

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapterTests.cs`

- [ ] **Step 1: Write the failing test**

Write `backend/test/Anela.Heblo.Tests/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase.Infrastructure;

public class PurchaseCatalogSourceAdapterTests
{
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock = new();
    private readonly PurchaseCatalogSourceAdapter _adapter;

    public PurchaseCatalogSourceAdapterTests()
    {
        _adapter = new PurchaseCatalogSourceAdapter(_repositoryMock.Object);
    }

    [Fact]
    public async Task GetOrderedQuantitiesAsync_DelegatesToRepositoryAndReturnsResult()
    {
        // Arrange
        var expected = new Dictionary<string, decimal>
        {
            ["PROD-A"] = 12.5m,
            ["PROD-B"] = 0m,
        };
        _repositoryMock
            .Setup(r => r.GetOrderedQuantitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _adapter.GetOrderedQuantitiesAsync(CancellationToken.None);

        // Assert
        result.Should().BeSameAs(expected);
        _repositoryMock.Verify(r => r.GetOrderedQuantitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseCatalogSourceAdapterTests"`
Expected: FAIL — `PurchaseCatalogSourceAdapter` not defined.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapterTests.cs
git commit -m "test(catalog): failing test for PurchaseCatalogSourceAdapter"
```

---

## Task 5: Implement the Purchase adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs`

- [ ] **Step 1: Write the adapter**

Write `backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Purchase;

namespace Anela.Heblo.Application.Features.Purchase.Infrastructure;

internal sealed class PurchaseCatalogSourceAdapter : ICatalogPurchaseSource
{
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;

    public PurchaseCatalogSourceAdapter(IPurchaseOrderRepository purchaseOrderRepository)
    {
        _purchaseOrderRepository = purchaseOrderRepository;
    }

    public Task<Dictionary<string, decimal>> GetOrderedQuantitiesAsync(CancellationToken cancellationToken) =>
        _purchaseOrderRepository.GetOrderedQuantitiesAsync(cancellationToken);
}
```

- [ ] **Step 2: Run to confirm green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PurchaseCatalogSourceAdapterTests"`
Expected: PASS — one test green.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Purchase/Infrastructure/PurchaseCatalogSourceAdapter.cs
git commit -m "feat(catalog): implement PurchaseCatalogSourceAdapter"
```

---

## Task 6: Write failing tests for the Manufacture adapter

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapterTests.cs`

- [ ] **Step 1: Write the failing tests**

Write `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapterTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Infrastructure;

public class ManufactureCatalogSourceAdapterTests
{
    private readonly Mock<IManufactureOrderRepository> _orderRepositoryMock = new();
    private readonly Mock<IManufactureHistoryClient> _historyClientMock = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock = new();
    private readonly ManufactureCatalogSourceAdapter _adapter;

    public ManufactureCatalogSourceAdapterTests()
    {
        _adapter = new ManufactureCatalogSourceAdapter(
            _orderRepositoryMock.Object,
            _historyClientMock.Object,
            _inventoryRepositoryMock.Object);
    }

    [Fact]
    public async Task GetPlannedQuantitiesAsync_DelegatesToOrderRepository()
    {
        var expected = new Dictionary<string, decimal> { ["PROD-A"] = 3m };
        _orderRepositoryMock
            .Setup(r => r.GetPlannedQuantitiesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _adapter.GetPlannedQuantitiesAsync(CancellationToken.None);

        result.Should().BeSameAs(expected);
        _orderRepositoryMock.Verify(r => r.GetPlannedQuantitiesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetManufactureHistoryAsync_DelegatesAndPassesNullProductCode()
    {
        var dateFrom = new DateTime(2026, 1, 1);
        var dateTo = new DateTime(2026, 2, 1);
        var records = new List<ManufactureHistoryRecord>
        {
            new() { ProductCode = "PROD-A", Date = dateFrom, Amount = 5 },
        };
        _historyClientMock
            .Setup(c => c.GetHistoryAsync(dateFrom, dateTo, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(records);

        var result = await _adapter.GetManufactureHistoryAsync(dateFrom, dateTo, CancellationToken.None);

        result.Should().BeEquivalentTo(records);
        _historyClientMock.Verify(
            c => c.GetHistoryAsync(dateFrom, dateTo, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetManufacturedInventoryAsync_DelegatesToInventoryRepository()
    {
        var expected = new Dictionary<string, decimal> { ["PROD-X"] = 99m };
        _inventoryRepositoryMock
            .Setup(r => r.GetTotalAmountByProductCodeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await _adapter.GetManufacturedInventoryAsync(CancellationToken.None);

        result.Should().BeSameAs(expected);
        _inventoryRepositoryMock.Verify(r => r.GetTotalAmountByProductCodeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 2: Run to confirm failure**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureCatalogSourceAdapterTests"`
Expected: FAIL — `ManufactureCatalogSourceAdapter` not defined.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapterTests.cs
git commit -m "test(catalog): failing tests for ManufactureCatalogSourceAdapter"
```

---

## Task 7: Implement the Manufacture adapter

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`

`IManufactureHistoryClient.GetHistoryAsync` returns `Task<List<ManufactureHistoryRecord>>`; `List<T>` already implements `IReadOnlyList<T>`, so no copy is needed — return the result directly.

- [ ] **Step 1: Write the adapter**

Write `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

internal sealed class ManufactureCatalogSourceAdapter : ICatalogManufactureSource
{
    private readonly IManufactureOrderRepository _orderRepository;
    private readonly IManufactureHistoryClient _historyClient;
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;

    public ManufactureCatalogSourceAdapter(
        IManufactureOrderRepository orderRepository,
        IManufactureHistoryClient historyClient,
        IManufacturedProductInventoryRepository inventoryRepository)
    {
        _orderRepository = orderRepository;
        _historyClient = historyClient;
        _inventoryRepository = inventoryRepository;
    }

    public Task<Dictionary<string, decimal>> GetPlannedQuantitiesAsync(CancellationToken cancellationToken) =>
        _orderRepository.GetPlannedQuantitiesAsync(cancellationToken);

    public async Task<IReadOnlyList<ManufactureHistoryRecord>> GetManufactureHistoryAsync(
        DateTime dateFrom,
        DateTime dateTo,
        CancellationToken cancellationToken) =>
        await _historyClient.GetHistoryAsync(dateFrom, dateTo, productCode: null, cancellationToken);

    public Task<Dictionary<string, decimal>> GetManufacturedInventoryAsync(CancellationToken cancellationToken) =>
        _inventoryRepository.GetTotalAmountByProductCodeAsync(cancellationToken);
}
```

- [ ] **Step 2: Run to confirm green**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureCatalogSourceAdapterTests"`
Expected: PASS — three tests green.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureCatalogSourceAdapter.cs
git commit -m "feat(catalog): implement ManufactureCatalogSourceAdapter"
```

---

## Task 8: Register the three adapters in their provider modules

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`

Each provider gets exactly one `AddScoped` line with the cross-module-contract comment matching the precedent in `KnowledgeBaseModule.cs:36–47`. No DI registration is added to `CatalogModule.cs` (provider-owned DI is the whole point).

- [ ] **Step 1: Register the Logistics adapter**

Edit `backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs`. After the existing `services.AddTransient<ITransportBoxCompletionService, …>` registration and before the dashboard-tile block, add:

```csharp
        // Cross-module contract: Logistics implements Catalog's ICatalogTransportSource via adapter.
        // DI registration is owned by the provider (Logistics), not the consumer (Catalog).
        services.AddScoped<ICatalogTransportSource, LogisticsCatalogTransportSourceAdapter>();
```

Also add the two new `using` directives at the top:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Logistics.Infrastructure;
```

- [ ] **Step 2: Register the Purchase adapter**

Edit `backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs`. After `services.AddScoped<IStockSeverityCalculator, …>` and before the validator block, add:

```csharp
        // Cross-module contract: Purchase implements Catalog's ICatalogPurchaseSource via adapter.
        // DI registration is owned by the provider (Purchase), not the consumer (Catalog).
        services.AddScoped<ICatalogPurchaseSource, PurchaseCatalogSourceAdapter>();
```

Also add the two new `using` directives at the top:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Purchase.Infrastructure;
```

- [ ] **Step 3: Register the Manufacture adapter**

Edit `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`. Right after the existing `services.AddScoped<IInventoryReservationService, ManufactureInventoryReservationAdapter>();` line (already in the cross-module block), append:

```csharp
        // Cross-module contract: Manufacture implements Catalog's ICatalogManufactureSource via adapter.
        // DI registration is owned by the provider (Manufacture), not the consumer (Catalog).
        services.AddScoped<ICatalogManufactureSource, ManufactureCatalogSourceAdapter>();
```

The existing `using Anela.Heblo.Application.Features.Manufacture.Infrastructure;` already covers the adapter type. Add one new `using` for the contract:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
```

- [ ] **Step 4: Build to verify modules compile**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS with no new warnings.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/LogisticsModule.cs \
        backend/src/Anela.Heblo.Application/Features/Purchase/PurchaseModule.cs \
        backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs
git commit -m "feat(catalog): register Catalog source adapters in provider modules"
```

---

## Task 9: Rewire `CatalogRepository` to consume the new contracts

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`

This is the largest single edit. Before starting, re-read lines 1–119, 129, 250, 892–924 of the current file. After the edit:

- Constructor parameter count drops by 3 net (−6 provider interfaces + 3 source contracts).
- Six field declarations replaced by three (`_transportSource`, `_purchaseSource`, `_manufactureSource`).
- The dead `_manufactureClient` field, parameter, and `?? throw …` assignment are deleted.
- Four cross-module `using` directives removed; `using Anela.Heblo.Domain.Features.Manufacture;` is retained (needed for `ManufactureHistoryRecord` return type at line 250's `CachedManufactureHistoryData` assignment) with a justification comment.
- Three private helpers (`GetProductsInTransport`, `GetProductsInReserve`, `GetProductsInQuarantine`) deleted. Two more (`GetProductsOrdered`, `GetProductsPlanned`) deleted — callers invoke the contract directly.
- Call sites at lines 129, 250, and the three `Refresh…` paths that called the deleted helpers all rewired to the contracts.
- `ICatalogRepository` public surface unchanged.

> **Approach for the agent:** make this a single coherent edit (multiple `Edit` calls, but in one logical pass). Then build immediately — the test files in Task 10 expect the new constructor shape.

- [ ] **Step 1: Replace the `using` directives**

In `CatalogRepository.cs`, change lines 16–19 from:

```csharp
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
```

to:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
// Retained for ManufactureHistoryRecord return type on CachedManufactureHistoryData / CatalogAggregate.ManufactureHistory.
// Track follow-up: introduce Catalog-owned CatalogManufactureHistoryRecord DTO to drop this last cross-module type.
using Anela.Heblo.Domain.Features.Manufacture;
```

- [ ] **Step 2: Replace the six provider fields with three contract fields**

In `CatalogRepository.cs`, replace the six field declarations on lines 38, 40–43, 45 with three new ones. Concretely:

- Delete lines 38, 40, 41, 42, 43, 45 (transport repo, manufactureClient, purchaseOrderRepository, manufactureOrderRepository, manufactureHistoryClient, manufacturedInventoryRepository).
- Keep `_stockTakingRepository` (39) and `_manufactureDifficultyRepository` (44) unchanged — both are Catalog-owned and stay.
- Add three new fields in the same region (between `_productEshopUrlClient` on line 37 and `_stockTakingRepository` on line 39):

```csharp
    private readonly ICatalogTransportSource _transportSource;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICatalogPurchaseSource _purchaseSource;
    private readonly ICatalogManufactureSource _manufactureSource;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
```

Final field-order block (replace the existing transport-through-inventory section):

```csharp
    private readonly ICatalogTransportSource _transportSource;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly ICatalogPurchaseSource _purchaseSource;
    private readonly ICatalogManufactureSource _manufactureSource;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
```

- [ ] **Step 3: Rewrite the constructor signature and body**

Replace the constructor (lines 64–119) so that:

- `ITransportBoxRepository transportBoxRepository` → `ICatalogTransportSource transportSource`
- `IManufactureClient manufactureClient` parameter and its `_manufactureClient = manufactureClient ?? throw …` assignment line — **deleted entirely**
- `IPurchaseOrderRepository purchaseOrderRepository` → `ICatalogPurchaseSource purchaseSource`
- `IManufactureOrderRepository manufactureOrderRepository`, `IManufactureHistoryClient manufactureHistoryClient`, `IManufacturedProductInventoryRepository manufacturedInventoryRepository` → all three collapsed into a single `ICatalogManufactureSource manufactureSource`

The other 14 parameters and their assignments are unchanged. The `_mergeScheduler.SetMergeCallback(ExecuteBackgroundMergeAsync);` final line stays.

Final constructor:

```csharp
    public CatalogRepository(
        ICatalogSalesClient salesClient,
        ICatalogAttributesClient attributesClient,
        IEshopStockClient eshopStockClient,
        IConsumedMaterialsClient consumedMaterialClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IErpStockClient erpStockClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ICatalogTransportSource transportSource,
        IStockTakingRepository stockTakingRepository,
        ICatalogPurchaseSource purchaseSource,
        ICatalogManufactureSource manufactureSource,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        ICatalogResilienceService resilienceService,
        ICatalogMergeScheduler mergeScheduler,
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> _options,
        IOptions<CatalogCacheOptions> cacheOptions,
        ILogger<CatalogRepository> logger)
    {
        _salesClient = salesClient;
        _attributesClient = attributesClient;
        _eshopStockClient = eshopStockClient;
        _consumedMaterialClient = consumedMaterialClient;
        _purchaseHistoryClient = purchaseHistoryClient;
        _erpStockClient = erpStockClient;
        _lotsClient = lotsClient;
        _productPriceEshopClient = productPriceEshopClient;
        _productPriceErpClient = productPriceErpClient;
        _productEshopUrlClient = productEshopUrlClient;
        _transportSource = transportSource;
        _stockTakingRepository = stockTakingRepository;
        _purchaseSource = purchaseSource;
        _manufactureSource = manufactureSource;
        _manufactureDifficultyRepository = manufactureDifficultyRepository;
        _resilienceService = resilienceService;
        _mergeScheduler = mergeScheduler;
        _cache = cache;
        _timeProvider = timeProvider;
        this._options = _options;
        _cacheOptions = cacheOptions;
        _logger = logger;

        // Initialize merge callback to avoid circular dependency
        _mergeScheduler.SetMergeCallback(ExecuteBackgroundMergeAsync);
    }
```

- [ ] **Step 4: Rewrite the `RefreshManufacturedData` call site (was line 129)**

Replace:

```csharp
        var manufacturedData = await _manufacturedInventoryRepository.GetTotalAmountByProductCodeAsync(ct);
```

with:

```csharp
        var manufacturedData = await _manufactureSource.GetManufacturedInventoryAsync(ct);
```

- [ ] **Step 5: Rewrite the `RefreshManufactureHistoryData` call site (was line 250)**

Replace:

```csharp
        CachedManufactureHistoryData = (await _manufactureHistoryClient.GetHistoryAsync(_timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays), _timeProvider.GetUtcNow().Date, cancellationToken: ct))
            .ToList();
```

with:

```csharp
        CachedManufactureHistoryData = (await _manufactureSource.GetManufactureHistoryAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays),
            _timeProvider.GetUtcNow().Date,
            ct)).ToList();
```

- [ ] **Step 6: Delete the five private helpers and inline their replacements**

Delete the entire helper block (was lines 892–924). Then update the two call sites that previously invoked the now-deleted helpers:

1. `RefreshTransportData` — change `var transportData = await GetProductsInTransport(ct);` to `var transportData = await _transportSource.GetProductsInTransportAsync(ct);`.
2. Locate the other two callers of `GetProductsInReserve` and `GetProductsInQuarantine` in the file (`RefreshReserveData` / `RefreshQuarantineData` or analogous) and replace identically:
   - `GetProductsInReserve(ct)` → `_transportSource.GetProductsInReserveAsync(ct)`
   - `GetProductsInQuarantine(ct)` → `_transportSource.GetProductsInQuarantineAsync(ct)`
3. Locate the caller of `GetProductsOrdered(ct)` (likely a `RefreshOrderedData` method) and replace with `_purchaseSource.GetOrderedQuantitiesAsync(ct)`.
4. Locate the caller of `GetProductsPlanned(ct)` (likely a `RefreshPlannedData` method) and replace with `_manufactureSource.GetPlannedQuantitiesAsync(ct)`.

> **Verification tip:** after editing, search the file for `_transportBoxRepository`, `_purchaseOrderRepository`, `_manufactureOrderRepository`, `_manufactureHistoryClient`, `_manufacturedInventoryRepository`, `_manufactureClient`, `GetProductsInTransport(`, `GetProductsInReserve(`, `GetProductsInQuarantine(`, `GetProductsOrdered(`, `GetProductsPlanned(`. All must return zero matches.

- [ ] **Step 7: Build to verify the rewrite compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: PASS for the Application project. Test projects will fail to build until Task 10 updates them — that's fine.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
git commit -m "refactor(catalog): rewire CatalogRepository through Catalog-owned source contracts"
```

---

## Task 10: Update existing `CatalogRepository` tests and add the DI smoke test

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleContractResolutionTests.cs`

`CatalogRepositoryDebugTest.cs` (the third file the spec mentions) resolves `ICatalogRepository` from DI and never names the constructor — no edit needed once DI is wired correctly. Confirm by reading the file before skipping.

### Sub-task 10a: Update `CatalogRepositoryTests.cs`

- [ ] **Step 1: Swap mocks**

In `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`:

1. Delete the cross-module `using` directives (lines 14, 15, 16, 17 currently): `Logistics.Transport`, `Manufacture`, `Manufacture.Inventory`, `Purchase`.
2. Add `using Anela.Heblo.Application.Features.Catalog.Contracts;`.
3. Replace the six provider mocks (`_transportBoxRepositoryMock`, `_manufactureClientMock`, `_purchaseOrderRepositoryMock`, `_manufactureOrderRepositoryMock`, `_manufactureHistoryClientMock`, `_manufacturedInventoryRepositoryMock`) with three contract mocks:

```csharp
    private readonly Mock<ICatalogTransportSource> _transportSourceMock;
    private readonly Mock<ICatalogPurchaseSource> _purchaseSourceMock;
    private readonly Mock<ICatalogManufactureSource> _manufactureSourceMock;
```

(Keep `_stockTakingRepositoryMock` and `_manufactureDifficultyRepositoryMock` — they're Catalog-owned.)

4. In the constructor body, replace mock instantiations correspondingly. The `_manufacturedInventoryRepositoryMock.Setup(x => x.GetTotalAmountByProductCodeAsync …)` and `_manufactureHistoryClientMock.Setup(x => x.GetHistoryAsync …)` defaults must be rewritten against the new contract surface:

```csharp
        _transportSourceMock = new Mock<ICatalogTransportSource>();
        _purchaseSourceMock = new Mock<ICatalogPurchaseSource>();
        _manufactureSourceMock = new Mock<ICatalogManufactureSource>();
        _manufactureSourceMock
            .Setup(x => x.GetManufacturedInventoryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());
        _manufactureSourceMock
            .Setup(x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ManufactureHistoryRecord>());
```

5. Rewrite the `new CatalogRepository(...)` constructor invocation to match the new parameter order (Task 9 Step 3):

```csharp
        _repository = new CatalogRepository(
            _salesClientMock.Object,
            _attributesClientMock.Object,
            _eshopStockClientMock.Object,
            _consumedMaterialClientMock.Object,
            _purchaseHistoryClientMock.Object,
            _erpStockClientMock.Object,
            _lotsClientMock.Object,
            _productPriceEshopClientMock.Object,
            _productPriceErpClientMock.Object,
            _productEshopUrlClientMock.Object,
            _transportSourceMock.Object,
            _stockTakingRepositoryMock.Object,
            _purchaseSourceMock.Object,
            _manufactureSourceMock.Object,
            _manufactureDifficultyRepositoryMock.Object,
            _resilienceServiceMock.Object,
            _mergeSchedulerMock.Object,
            _cache,
            _timeProviderMock.Object,
            _optionsMock.Object,
            _cacheOptionsMock.Object,
            _loggerMock.Object);
```

6. If any test body inside this file directly setups `_manufactureClientMock`, `_purchaseOrderRepositoryMock`, etc., update those setups to the corresponding `_transportSourceMock` / `_purchaseSourceMock` / `_manufactureSourceMock` method.

> **For the agent:** search the file for `_transportBoxRepositoryMock`, `_manufactureClientMock`, `_purchaseOrderRepositoryMock`, `_manufactureOrderRepositoryMock`, `_manufactureHistoryClientMock`, `_manufacturedInventoryRepositoryMock` — every match must be either deleted or rewritten against the new contract.

### Sub-task 10b: Add three new behavior-preservation tests to `CatalogRepositoryTests.cs`

Append three focused regression tests verifying each `Refresh…` method calls exactly the corresponding source method once. These are the per-spec FR-9 "focused tests asserting each Refresh* method calls the correct source method once."

- [ ] **Step 1: Add the three tests**

Append to `CatalogRepositoryTests`:

```csharp
    [Fact]
    public async Task RefreshTransportData_InvokesTransportSourceOnce()
    {
        _transportSourceMock
            .Setup(x => x.GetProductsInTransportAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, int>());

        await _repository.RefreshTransportData(CancellationToken.None);

        _transportSourceMock.Verify(
            x => x.GetProductsInTransportAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshManufacturedData_InvokesManufactureSourceOnce()
    {
        await _repository.RefreshManufacturedData(CancellationToken.None);

        _manufactureSourceMock.Verify(
            x => x.GetManufacturedInventoryAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshManufactureHistoryData_InvokesManufactureSourceOnce()
    {
        await _repository.RefreshManufactureHistoryData(CancellationToken.None);

        _manufactureSourceMock.Verify(
            x => x.GetManufactureHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

### Sub-task 10c: Update `CatalogRepositoryCacheOptimizationTests.cs`

- [ ] **Step 1: Apply the same substitutions**

Apply the exact same six-mock-to-three-mock substitution in `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs`:

1. Drop the four cross-module `using` directives (`Logistics.Transport`, `Manufacture`, `Manufacture.Inventory`, `Purchase`).
2. Add `using Anela.Heblo.Application.Features.Catalog.Contracts;`.
3. Replace the six mocks and their default setups (lines 39–46, 71–82 today) with the three contract mocks (same code as Sub-task 10a Step 1).
4. Update the `SetupBasicMockData()` body to set up `_manufactureSourceMock` instead of `_manufactureClientMock` / `_manufactureHistoryClientMock`. Specifically, replace the two lines that today set up `_manufactureClientMock.Setup(x => x.FindByIngredientAsync …)` (delete it — `IManufactureClient` is gone) and `_manufactureHistoryClientMock.Setup(x => x.GetHistoryAsync …)` (rewrite as the source-contract setup shown above). The `_transportBoxRepositoryMock.Setup(x => x.FindAsync …)` block on line 200 of the file is now redundant and must be deleted.
5. Rewrite the `new CatalogRepository(...)` invocation (lines 128–153) to match the new constructor parameter list.

### Sub-task 10d: Add the DI smoke test

- [ ] **Step 1: Write the smoke test**

Write `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleContractResolutionTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// Asserts that the three Catalog-owned source contracts are resolvable from the
/// application's IServiceProvider — covers FR-9 DI smoke test. Each adapter is registered
/// by its provider module (LogisticsModule / PurchaseModule / ManufactureModule); this
/// test fails if any of those bindings get removed.
/// </summary>
public class CatalogModuleContractResolutionTests : IClassFixture<ManufactureOrderTestFactory>
{
    private readonly ManufactureOrderTestFactory _factory;

    public CatalogModuleContractResolutionTests(ManufactureOrderTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void TransportSource_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();
        var resolved = scope.ServiceProvider.GetService<ICatalogTransportSource>();
        resolved.Should().NotBeNull();
    }

    [Fact]
    public void PurchaseSource_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();
        var resolved = scope.ServiceProvider.GetService<ICatalogPurchaseSource>();
        resolved.Should().NotBeNull();
    }

    [Fact]
    public void ManufactureSource_IsResolvableFromDi()
    {
        using var scope = _factory.Services.CreateScope();
        var resolved = scope.ServiceProvider.GetService<ICatalogManufactureSource>();
        resolved.Should().NotBeNull();
    }
}
```

> **If `ManufactureOrderTestFactory` does not register all three provider modules**, find the test fixture that does (`CatalogRepositoryDebugTest.cs` uses `ManufactureOrderTestFactory` and `ICatalogRepository` resolves correctly there — that means `LogisticsModule.AddTransportModule()`, `ManufactureModule.AddManufactureModule(...)`, and `PurchaseModule.AddPurchaseModule()` are all called by that factory's host setup). Confirm before relying on it; otherwise switch to a `WebApplicationFactory<Program>` fixture that loads the full module chain.

- [ ] **Step 2: Run all touched tests**

Run:

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
    --filter "FullyQualifiedName~CatalogRepositoryTests|FullyQualifiedName~CatalogRepositoryCacheOptimizationTests|FullyQualifiedName~CatalogRepositoryDebugTest|FullyQualifiedName~CatalogModuleContractResolutionTests|FullyQualifiedName~LogisticsCatalogTransportSourceAdapterTests|FullyQualifiedName~PurchaseCatalogSourceAdapterTests|FullyQualifiedName~ManufactureCatalogSourceAdapterTests"
```

Expected: all pass.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogModuleContractResolutionTests.cs
git commit -m "test(catalog): rewire CatalogRepository tests against source contracts + add DI smoke test"
```

---

## Task 11: Add the three module-boundary rules

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

The Catalog → Logistics and Catalog → Purchase rules use empty allowlists. The Catalog → Manufacture rule must allowlist:

1. The three pre-existing `IManufactureClient` handler injections (out of scope per arch-review § Specification Amendments §1).
2. The deliberate `ManufactureHistoryRecord` leak inside `CatalogRepository`.

Per the architecture review, run the test once after writing the rules to discover any additional residual references (e.g. via `CachedManufactureHistoryData` typing) and add allowlist entries for those.

- [ ] **Step 1: Add the allowlists**

In `ModuleBoundariesTests.cs`, after the existing `LogisticsCatalogAllowlist` block (around line 90), add three new allowlists:

```csharp
    // Allowlist for Catalog -> Logistics. Empty — all violations cleared by the Catalog source-contract refactor.
    private static readonly HashSet<string> CatalogLogisticsAllowlist = new(StringComparer.Ordinal);

    // Allowlist for Catalog -> Purchase. Empty — all violations cleared by the Catalog source-contract refactor.
    private static readonly HashSet<string> CatalogPurchaseAllowlist = new(StringComparer.Ordinal);

    // Allowlist for Catalog -> Manufacture. Pre-existing handler-level IManufactureClient injections
    // and ManufactureHistoryRecord return-type leak from CatalogRepository are out of scope for the
    // 2026-06-01 CatalogRepository decoupling. Track as follow-ups:
    //   - Migrate the three handlers off IManufactureClient onto a Catalog-owned contract.
    //   - Introduce a Catalog-owned CatalogManufactureHistoryRecord DTO and map in the adapter.
    private static readonly HashSet<string> CatalogManufactureAllowlist = new(StringComparer.Ordinal)
    {
        // Follow-up: migrate UpdateProductCompositionOrderHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.UpdateProductCompositionOrder.UpdateProductCompositionOrderHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductCompositionHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductComposition.GetProductCompositionHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Follow-up: migrate GetProductUsageHandler off IManufactureClient.
        "Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage.GetProductUsageHandler -> Anela.Heblo.Domain.Features.Manufacture.IManufactureClient",

        // Deliberate pragmatic leak: ManufactureHistoryRecord is the return-element type of
        // ICatalogManufactureSource.GetManufactureHistoryAsync and is woven through Catalog's
        // CachedManufactureHistoryData and CatalogAggregate.ManufactureHistory. Tracked
        // follow-up: introduce Catalog-owned CatalogManufactureHistoryRecord DTO.
        "Anela.Heblo.Application.Features.Catalog.CatalogRepository -> Anela.Heblo.Domain.Features.Manufacture.ManufactureHistoryRecord",
    };
```

- [ ] **Step 2: Add three rules to `Rules()`**

In the `Rules()` method body (around line 92), append the three new `ModuleBoundaryRule` entries before the closing `}`:

```csharp
        new ModuleBoundaryRule(
            Name: "Catalog -> Logistics",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Catalog",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Logistics",
                "Anela.Heblo.Application.Features.Logistics",
                "Anela.Heblo.Persistence.Logistics",
            },
            Allowlist: CatalogLogisticsAllowlist),

        new ModuleBoundaryRule(
            Name: "Catalog -> Purchase",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Catalog",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Purchase",
                "Anela.Heblo.Application.Features.Purchase",
                "Anela.Heblo.Persistence.Purchase",
            },
            Allowlist: CatalogPurchaseAllowlist),

        new ModuleBoundaryRule(
            Name: "Catalog -> Manufacture",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.Catalog",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Manufacture",
                "Anela.Heblo.Application.Features.Manufacture",
                "Anela.Heblo.Persistence.Manufacture",
            },
            Allowlist: CatalogManufactureAllowlist),
```

- [ ] **Step 3: Run the boundary tests to detect any residual violations**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: All three new rules pass. If any one fails with a violation **not** in the allowlist:

1. Inspect the violation message — it names the type and the referenced provider type.
2. Decide: is the new reference a genuine code defect (rewire it through the contract) or a pre-existing carry-over that should be allowlisted with a follow-up note?
3. Fix or allowlist with comment, re-run.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test(architecture): add Catalog -> {Logistics,Purchase,Manufacture} boundary rules"
```

---

## Task 12: Full validation pass

- [ ] **Step 1: Solution build clean**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: PASS, no new warnings.

- [ ] **Step 2: Format clean**

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: PASS (no formatting drift). If it fails, run `dotnet format backend/Anela.Heblo.sln` and commit:

```bash
git add -u
git commit -m "style: dotnet format"
```

- [ ] **Step 3: Full backend test pass**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests pass. If a test in a feature unrelated to this refactor fails, check whether it depended on the deleted constructor surface (e.g. shared test helpers); update it.

- [ ] **Step 4: Diff sanity check**

Run: `git diff --stat origin/main`
Expected: changes confined to the file set in the File Structure section. Specifically: no `Domain.Features.*` changes, no API project changes, no migrations.

If extra files appear, justify each one or revert it.

- [ ] **Step 5: Verify `IManufactureClient` interface and its non-Catalog consumers untouched**

Run (via Grep tool):
- Pattern: `IManufactureClient`
- Inspect: `backend/src` and `backend/test`

Expected: the only deletions are inside `CatalogRepository.cs`. `IManufactureClient` itself, `FlexiManufactureClient`, and the three Catalog handlers (`UpdateProductCompositionOrderHandler`, `GetProductCompositionHandler`, `GetProductUsageHandler`) still reference it.

- [ ] **Step 6: Final commit (if any format-only changes)**

If Step 2 produced changes, the commit was made in Step 2. Otherwise nothing to commit here.

---

## Self-Review Checklist (run mentally before declaring complete)

**Spec coverage:**
- FR-1 (three new contracts) → Task 1.
- FR-2 (Logistics adapter) → Tasks 2, 3.
- FR-3 (Purchase adapter) → Tasks 4, 5.
- FR-4 (Manufacture adapter) → Tasks 6, 7.
- FR-5 (remove dead `IManufactureClient`) → Task 9 Step 3 (per arch-review amendment, scoped to `CatalogRepository.cs` only — the three handlers remain).
- FR-6 (provider-owned DI registration) → Task 8.
- FR-7 (rewire `CatalogRepository`) → Task 9.
- FR-8 (no out-of-scope changes) → Task 12 Step 4.
- FR-9 (behavior-preservation tests + DI smoke) → Task 10.
- FR-10 (boundary tests, added by arch-review) → Task 11.
- NFR-1/2 (behavior preservation) → Tasks 9 + 10b regression tests.
- NFR-3 (security) — adapters are `internal sealed`.
- NFR-4 (each adapter ≤ 50 lines) — verify in Tasks 3, 5, 7.
- NFR-5 (`dotnet build` / `dotnet format` / `dotnet test` green) → Task 12.

**Risk mitigations from arch-review:**
- Aggregation behavior preserved verbatim (same predicates, `includeDetails: true`, `(int)` cast) → Task 3.
- FR-5 scope corrected → Task 9 Step 3 wording.
- Manufacture allowlist seeded with handler references + `ManufactureHistoryRecord` → Task 11.
- DI lifetime: `AddScoped` matches underlying repositories → Task 8.
- Test files updated atomically → Task 10.
