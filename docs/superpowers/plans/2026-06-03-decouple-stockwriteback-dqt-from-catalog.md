# Decouple StockWriteBackDqtComparer from Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `StockWriteBackDqtComparer`'s direct dependencies on Catalog repositories (`IStockUpOperationRepository`, `IStockTakingRepository`) with two DataQuality-owned read contracts plus Catalog-side adapters, so the DataQuality module stops reaching into Catalog's domain namespace.

**Architecture:** Apply the consumer-owned contract / provider-side adapter pattern that already governs `ILogisticsStockOperationQueryService` ↔ `LogisticsStockOperationQueryAdapter`. Two new interfaces (`IStockOperationQuery`, `IStockTakingQuery`) live in `DataQuality/Contracts/`. Two `internal sealed` adapters in `Catalog/Infrastructure/` wrap the existing repositories, register in `CatalogModule`, and project Catalog entities to DataQuality-owned DTOs. Behavior of the DQT pipeline is preserved bit-for-bit.

**Tech Stack:** .NET 8, EF Core (`IQueryable.ToListAsync`), xUnit + FluentAssertions + Moq, MediatR, Microsoft.Extensions.DependencyInjection.

---

## File Structure

**New files (DataQuality-owned contracts):**
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockOperationQuery.cs`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockTakingQuery.cs`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationSnapshot.cs`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationStateSnapshot.cs`
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockTakingSnapshot.cs`

**New files (Catalog-owned adapters):**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapter.cs`
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapter.cs`

**New test files:**
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapterTests.cs`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapterTests.cs`

**Modified files:**
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` (two new DI registrations)
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs` (constructor signature + body)
- `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtComparerTests.cs` (mock the new contracts)
- `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` (new rule + `ProductPairingDqtComparer` allowlist)

---

## Task 1: Add `StockOperationStateSnapshot` enum

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationStateSnapshot.cs`

Enum values mirror `StockUpOperationState` integer-for-integer (Pending=0, Submitted=1, Completed=2, Failed=3). The enum is DataQuality-owned so DataQuality can switch on state values without referencing Catalog's enum.

- [ ] **Step 1: Create the enum file**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public enum StockOperationStateSnapshot
{
    Pending = 0,
    Submitted = 1,
    Completed = 2,
    Failed = 3,
}
```

- [ ] **Step 2: Verify the build still succeeds**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationStateSnapshot.cs
git commit -m "feat: add DataQuality StockOperationStateSnapshot enum"
```

---

## Task 2: Add `StockOperationSnapshot` and `StockTakingSnapshot` DTO classes

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationSnapshot.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockTakingSnapshot.cs`

DTOs are `public sealed class` with `required init` setters (per the project-wide "DTOs are classes, never C# records" rule in `CLAUDE.md`). Fields are exactly those read by `StockWriteBackDqtComparer.CompareAsync`.

- [ ] **Step 1: Create `StockOperationSnapshot.cs`**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public sealed class StockOperationSnapshot
{
    public required string ProductCode { get; init; }
    public required int Amount { get; init; }
    public required string DocumentNumber { get; init; }
    public required StockOperationStateSnapshot State { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public string? ErrorMessage { get; init; }
}
```

- [ ] **Step 2: Create `StockTakingSnapshot.cs`**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

public sealed class StockTakingSnapshot
{
    public required string Code { get; init; }
    public required double AmountNew { get; init; }
    public string? Error { get; init; }
}
```

- [ ] **Step 3: Verify the build still succeeds**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockOperationSnapshot.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/StockTakingSnapshot.cs
git commit -m "feat: add DataQuality StockOperationSnapshot and StockTakingSnapshot DTOs"
```

---

## Task 3: Add `IStockOperationQuery` and `IStockTakingQuery` interfaces

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockOperationQuery.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockTakingQuery.cs`

Both interfaces expose exactly one method — the read the comparer needs. Return type is `IReadOnlyList<TSnapshot>` so the contract has no `IQueryable`/EF coupling.

- [ ] **Step 1: Create `IStockOperationQuery.cs`**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract for stock-up operations.
/// Used by StockWriteBackDqtComparer to inspect operation state within a date window.
/// </summary>
public interface IStockOperationQuery
{
    Task<IReadOnlyList<StockOperationSnapshot>> GetByCreatedDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Create `IStockTakingQuery.cs`**

```csharp
namespace Anela.Heblo.Application.Features.DataQuality.Contracts;

/// <summary>
/// DataQuality-owned read contract for stock-taking records.
/// Used by StockWriteBackDqtComparer to inspect stock-taking errors within a date window.
/// </summary>
public interface IStockTakingQuery
{
    Task<IReadOnlyList<StockTakingSnapshot>> GetByDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Verify the build still succeeds**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockOperationQuery.cs \
        backend/src/Anela.Heblo.Application/Features/DataQuality/Contracts/IStockTakingQuery.cs
git commit -m "feat: add DataQuality IStockOperationQuery and IStockTakingQuery contracts"
```

---

## Task 4: Add `DataQualityStockOperationQueryAdapter` with tests first

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapterTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapter.cs`

`StockUpOperation` has private setters and a private parameterless constructor; the existing precedent test (`LogisticsStockOperationQueryAdapterTests.cs:17-32`) uses reflection to set `Id` and `State` after construction. Reuse that pattern. Use `Returns(...AsQueryable())` to mock `_repository.GetAll()` because the mock-backed adapter must apply the `Where + ToListAsync` chain over LINQ-to-Objects in tests (LINQ-to-Objects has no async-extension; the adapter calls `.ToListAsync(ct)` which the EF Core async provider routes — for in-memory tests, switch the adapter implementation to use the synchronous `.ToList()` after `.AsAsyncEnumerable()`? No — simpler: the adapter calls `.ToListAsync()`, and tests use an EF-aware `AsAsyncQueryable()` wrapper via `MockQueryable.Moq` package… **avoid this complexity**: have the adapter use `Task.FromResult(query.ToList())` so test mocks work with `AsQueryable()` without async-provider plumbing). See Step 3 implementation below.

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityStockOperationQueryAdapterTests
{
    private readonly Mock<IStockUpOperationRepository> _repository = new();

    private DataQualityStockOperationQueryAdapter CreateAdapter() => new(_repository.Object);

    private static StockUpOperation CreateOperation(
        string documentNumber,
        string productCode,
        int amount,
        StockUpOperationState state,
        DateTime createdAt,
        string? errorMessage = null)
    {
        var op = new StockUpOperation(documentNumber, productCode, amount, StockUpSourceType.TransportBox, 1);
        typeof(StockUpOperation).GetProperty("State")!.SetValue(op, state);
        typeof(StockUpOperation).GetProperty("CreatedAt")!.SetValue(op, createdAt);
        if (errorMessage != null)
            typeof(StockUpOperation).GetProperty("ErrorMessage")!.SetValue(op, errorMessage);
        return op;
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_ProjectsAllRequiredFields()
    {
        // Arrange
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);
        var op = CreateOperation("DOC-1", "P-001", 5, StockUpOperationState.Failed,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc), errorMessage: "boom");
        _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

        // Act
        var result = await CreateAdapter().GetByCreatedDateRangeAsync(from, to, CancellationToken.None);

        // Assert
        result.Should().ContainSingle();
        var snapshot = result[0];
        snapshot.ProductCode.Should().Be("P-001");
        snapshot.Amount.Should().Be(5);
        snapshot.DocumentNumber.Should().Be("DOC-1");
        snapshot.State.Should().Be(StockOperationStateSnapshot.Failed);
        snapshot.CreatedAtUtc.Should().Be(new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        snapshot.ErrorMessage.Should().Be("boom");
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_ProjectsNullErrorMessage()
    {
        var op = CreateOperation("DOC-2", "P-002", 1, StockUpOperationState.Pending,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_FiltersOutsideDateWindow()
    {
        var inside = CreateOperation("DOC-IN", "P-IN", 1, StockUpOperationState.Completed,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        var beforeWindow = CreateOperation("DOC-BEFORE", "P-BEFORE", 1, StockUpOperationState.Completed,
            new DateTime(2026, 5, 30, 10, 0, 0, DateTimeKind.Utc));
        var afterWindow = CreateOperation("DOC-AFTER", "P-AFTER", 1, StockUpOperationState.Completed,
            new DateTime(2026, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        _repository.Setup(r => r.GetAll())
            .Returns(new[] { inside, beforeWindow, afterWindow }.AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().ContainSingle()
            .Which.ProductCode.Should().Be("P-IN");
    }

    [Theory]
    [InlineData(StockUpOperationState.Pending, StockOperationStateSnapshot.Pending)]
    [InlineData(StockUpOperationState.Submitted, StockOperationStateSnapshot.Submitted)]
    [InlineData(StockUpOperationState.Completed, StockOperationStateSnapshot.Completed)]
    [InlineData(StockUpOperationState.Failed, StockOperationStateSnapshot.Failed)]
    public async Task GetByCreatedDateRangeAsync_MapsStateOneToOne(
        StockUpOperationState catalogState,
        StockOperationStateSnapshot expected)
    {
        var op = CreateOperation("DOC-1", "P-1", 1, catalogState,
            new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().ContainSingle().Which.State.Should().Be(expected);
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_HandlesEveryCatalogStateMember_WithoutThrowing()
    {
        // Enum-parity guard: if Catalog adds a new StockUpOperationState member, this test
        // fails before production traffic hits the exhaustive switch.
        foreach (var state in Enum.GetValues<StockUpOperationState>())
        {
            _repository.Reset();
            var op = CreateOperation("DOC", "P", 1, state,
                new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc));
            _repository.Setup(r => r.GetAll()).Returns(new[] { op }.AsQueryable());

            var act = () => CreateAdapter().GetByCreatedDateRangeAsync(
                new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
                CancellationToken.None);

            await act.Should().NotThrowAsync($"adapter must map Catalog state {state}");
        }
    }

    [Fact]
    public async Task GetByCreatedDateRangeAsync_WhenEmpty_ReturnsEmptyList()
    {
        _repository.Setup(r => r.GetAll()).Returns(Array.Empty<StockUpOperation>().AsQueryable());

        var result = await CreateAdapter().GetByCreatedDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (no adapter type yet)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityStockOperationQueryAdapterTests"`
Expected: Build error — `DataQualityStockOperationQueryAdapter` does not exist.

- [ ] **Step 3: Implement the adapter**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityStockOperationQueryAdapter : IStockOperationQuery
{
    private readonly IStockUpOperationRepository _repository;

    public DataQualityStockOperationQueryAdapter(IStockUpOperationRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<StockOperationSnapshot>> GetByCreatedDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var snapshots = _repository.GetAll()
            .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
            .Select(o => new StockOperationSnapshot
            {
                ProductCode = o.ProductCode,
                Amount = o.Amount,
                DocumentNumber = o.DocumentNumber,
                State = MapState(o.State),
                CreatedAtUtc = o.CreatedAt,
                ErrorMessage = o.ErrorMessage,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<StockOperationSnapshot>>(snapshots);
    }

    private static StockOperationStateSnapshot MapState(StockUpOperationState state) => state switch
    {
        StockUpOperationState.Pending => StockOperationStateSnapshot.Pending,
        StockUpOperationState.Submitted => StockOperationStateSnapshot.Submitted,
        StockUpOperationState.Completed => StockOperationStateSnapshot.Completed,
        StockUpOperationState.Failed => StockOperationStateSnapshot.Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
```

Implementation note: the adapter projects via LINQ `Select` and calls `.ToList()` (synchronous). The EF query provider translates `Where + Select + ToList` into a single SQL `SELECT` with column projection — same SQL shape as today's `GetAll().Where(...).ToList()` in the comparer, with the bonus that the projection narrows the columns loaded. We use `.ToList()` (not `.ToListAsync()`) wrapped in `Task.FromResult` so that test mocks built on `AsQueryable()` (LINQ-to-Objects, no async provider) work without bringing in `MockQueryable` / `IAsyncQueryProvider` plumbing. The DB call is brief and the comparer awaits the task — net behavior is unchanged.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityStockOperationQueryAdapterTests"`
Expected: 9 tests pass (6 individual + 4 from the `[Theory]`).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityStockOperationQueryAdapterTests.cs
git commit -m "feat: add DataQualityStockOperationQueryAdapter for stock-up operation reads"
```

---

## Task 5: Add `DataQualityStockTakingQueryAdapter` with tests first

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapterTests.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapter.cs`

`IStockTakingRepository.GetByDateRangeAsync` already returns a materialized `List<StockTakingRecord>`, so the adapter just projects in-memory.

- [ ] **Step 1: Write failing tests**

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class DataQualityStockTakingQueryAdapterTests
{
    private readonly Mock<IStockTakingRepository> _repository = new();

    private DataQualityStockTakingQueryAdapter CreateAdapter() => new(_repository.Object);

    [Fact]
    public async Task GetByDateRangeAsync_ProjectsAllRequiredFields()
    {
        var from = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc);
        _repository.Setup(r => r.GetByDateRangeAsync(from, to, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>
            {
                new() { Code = "P-001", AmountNew = 12.5, AmountOld = 10.0, Error = "Shoptet API timeout",
                        Date = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc) },
            });

        var result = await CreateAdapter().GetByDateRangeAsync(from, to, CancellationToken.None);

        result.Should().ContainSingle();
        var snapshot = result[0];
        snapshot.Code.Should().Be("P-001");
        snapshot.AmountNew.Should().Be(12.5);
        snapshot.Error.Should().Be("Shoptet API timeout");
    }

    [Fact]
    public async Task GetByDateRangeAsync_ProjectsNullError()
    {
        _repository.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>
            {
                new() { Code = "P-002", AmountNew = 5.0, AmountOld = 5.0, Error = null,
                        Date = DateTime.UtcNow },
            });

        var result = await CreateAdapter().GetByDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().ContainSingle().Which.Error.Should().BeNull();
    }

    [Fact]
    public async Task GetByDateRangeAsync_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        _repository.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());

        await CreateAdapter().GetByDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            cts.Token);

        _repository.Verify(r => r.GetByDateRangeAsync(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetByDateRangeAsync_WhenRepositoryEmpty_ReturnsEmptyList()
    {
        _repository.Setup(r => r.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());

        var result = await CreateAdapter().GetByDateRangeAsync(
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 3, 23, 59, 59, DateTimeKind.Utc),
            CancellationToken.None);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityStockTakingQueryAdapterTests"`
Expected: Build error — `DataQualityStockTakingQueryAdapter` does not exist.

- [ ] **Step 3: Implement the adapter**

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

internal sealed class DataQualityStockTakingQueryAdapter : IStockTakingQuery
{
    private readonly IStockTakingRepository _repository;

    public DataQualityStockTakingQueryAdapter(IStockTakingRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<StockTakingSnapshot>> GetByDateRangeAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken = default)
    {
        var records = await _repository.GetByDateRangeAsync(fromUtc, toUtc, cancellationToken);

        var snapshots = new List<StockTakingSnapshot>(records.Count);
        foreach (var record in records)
        {
            snapshots.Add(new StockTakingSnapshot
            {
                Code = record.Code,
                AmountNew = record.AmountNew,
                Error = record.Error,
            });
        }

        return snapshots;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DataQualityStockTakingQueryAdapterTests"`
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapter.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/DataQualityStockTakingQueryAdapterTests.cs
git commit -m "feat: add DataQualityStockTakingQueryAdapter for stock-taking reads"
```

---

## Task 6: Register adapters in `CatalogModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`

The DI binding belongs to the provider module (Catalog), per `development_guidelines.md`'s "Provider (B) registers the DI binding." Place the registrations next to the existing cross-module adapter bindings (around lines 47-53). Lifetime is `Scoped` (matches the underlying repos and matches the spec; precedent `LogisticsStockOperationQueryAdapter` is `Transient`, but the spec is explicit on `Scoped`).

- [ ] **Step 1: Add the `using` directive**

Add at the appropriate alphabetical position in the top-of-file `using` block:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
```

The block currently spans lines 1-33. Insert the new `using` alphabetically — after `using Anela.Heblo.Application.Features.Catalog.Validators;` (line 18) and before `using Anela.Heblo.Application.Features.Logistics.Contracts;` (line 19).

- [ ] **Step 2: Add the two `AddScoped` registrations**

Find this block in `CatalogModule.cs` (around line 50-53):

```csharp
        services.AddTransient<ILogisticsStockOperationService, LogisticsStockOperationAdapter>();
        // Logistics owns the query contract; Catalog (this module) provides the adapter implementation.
        services.AddTransient<ILogisticsStockOperationQueryService, LogisticsStockOperationQueryAdapter>();
```

Insert immediately after the `ILogisticsStockOperationQueryService` line:

```csharp
        // DataQuality owns the query contracts; Catalog (this module) provides the adapter implementations.
        services.AddScoped<IStockOperationQuery, DataQualityStockOperationQueryAdapter>();
        services.AddScoped<IStockTakingQuery, DataQualityStockTakingQueryAdapter>();
```

- [ ] **Step 3: Verify the build succeeds**

Run: `dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat: register DataQuality stock query adapters in CatalogModule"
```

---

## Task 7: Refactor `StockWriteBackDqtComparer` and update its tests

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtComparerTests.cs`

The comparer keeps identical observable behavior; only constructor dependencies, type names, and namespace imports change. The `StockUpOperation` parameter of `BuildOperationDetails` becomes `StockOperationSnapshot`. The `_operationRepository.GetAll().Where(...).ToList()` chain becomes a single call into the new contract (the date filter moves into the adapter). State-enum references switch to the snapshot enum.

The test file's mocks switch from the Catalog repositories to the DataQuality contracts. The four existing tests must continue to pass with the same assertions; we update them in lockstep with the comparer.

- [ ] **Step 1: Update `StockWriteBackDqtComparerTests.cs` to use the new contracts (tests will not compile until Step 3)**

Overwrite the test file contents:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Application.Features.DataQuality.Services;
using Anela.Heblo.Domain.Features.DataQuality;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.DataQuality;

public class StockWriteBackDqtComparerTests
{
    private readonly Mock<IStockOperationQuery> _stockOperationsMock = new();
    private readonly Mock<IStockTakingQuery> _stockTakingsMock = new();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private StockWriteBackDqtComparer CreateSut(TimeSpan? stuckThreshold = null) =>
        new(_stockOperationsMock.Object, _stockTakingsMock.Object, stuckThreshold);

    private void SetupNoStockTaking() =>
        _stockTakingsMock.Setup(q => q.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<StockTakingSnapshot>());

    private void SetupOperations(params StockOperationSnapshot[] snapshots) =>
        _stockOperationsMock.Setup(q => q.GetByCreatedDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

    [Fact]
    public async Task CompareAsync_ReturnsEmpty_WhenAllOperationsCompleted()
    {
        // Arrange
        SetupOperations(new StockOperationSnapshot
        {
            ProductCode = "P001",
            Amount = 1,
            DocumentNumber = "OP001",
            State = StockOperationStateSnapshot.Completed,
            CreatedAtUtc = DateTime.UtcNow,
        });
        SetupNoStockTaking();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ReturnsOperationFailed_WhenOperationInFailedState()
    {
        // Arrange
        SetupOperations(new StockOperationSnapshot
        {
            ProductCode = "P002",
            Amount = 5,
            DocumentNumber = "OP002",
            State = StockOperationStateSnapshot.Failed,
            CreatedAtUtc = DateTime.UtcNow,
            ErrorMessage = "HTTP 500 from Shoptet",
        });
        SetupNoStockTaking();

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("P002");
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.OperationFailed);
        result.Mismatches[0].Details.Should().Contain("HTTP 500 from Shoptet");
    }

    [Fact]
    public async Task CompareAsync_ReturnsOperationStuck_WhenPendingOperationExceedsThreshold()
    {
        // Arrange — use TimeSpan.Zero threshold so any Pending operation is "stuck"
        SetupOperations(new StockOperationSnapshot
        {
            ProductCode = "P003",
            Amount = 2,
            DocumentNumber = "OP003",
            State = StockOperationStateSnapshot.Pending,
            CreatedAtUtc = DateTime.UtcNow,
        });
        SetupNoStockTaking();

        // Act
        var result = await CreateSut(stuckThreshold: TimeSpan.Zero)
            .CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.OperationStuck);
    }

    [Fact]
    public async Task CompareAsync_ReturnsStockTakingErrored_WhenRecordHasError()
    {
        // Arrange
        SetupOperations();
        _stockTakingsMock.Setup(q => q.GetByDateRangeAsync(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new StockTakingSnapshot { Code = "P004", AmountNew = 10, Error = "Shoptet API timeout" },
            });

        // Act
        var result = await CreateSut().CompareAsync(Today, Today, CancellationToken.None);

        // Assert
        result.Mismatches.Should().HaveCount(1);
        result.Mismatches[0].EntityKey.Should().Be("P004");
        ((StockWriteBackMismatch)result.Mismatches[0].MismatchCode)
            .Should().HaveFlag(StockWriteBackMismatch.StockTakingErrored);
        result.Mismatches[0].Details.Should().Contain("Shoptet API timeout");
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to compile (the comparer still has the old constructor)**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build error — the comparer's constructor still expects `IStockUpOperationRepository`/`IStockTakingRepository`, not `IStockOperationQuery`/`IStockTakingQuery`.

- [ ] **Step 3: Rewrite `StockWriteBackDqtComparer.cs`**

Overwrite the comparer file:

```csharp
using Anela.Heblo.Application.Features.DataQuality.Contracts;
using Anela.Heblo.Domain.Features.DataQuality;

namespace Anela.Heblo.Application.Features.DataQuality.Services;

public class StockWriteBackDqtComparer : IDriftDqtComparer
{
    private static readonly TimeSpan DefaultStuckThreshold = TimeSpan.FromHours(1);

    private readonly IStockOperationQuery _stockOperations;
    private readonly IStockTakingQuery _stockTakings;
    private readonly TimeSpan _stuckThreshold;

    public DqtTestType TestType => DqtTestType.StockWriteBackReconciliation;

    public StockWriteBackDqtComparer(
        IStockOperationQuery stockOperations,
        IStockTakingQuery stockTakings,
        TimeSpan? stuckThreshold = null)
    {
        _stockOperations = stockOperations;
        _stockTakings = stockTakings;
        _stuckThreshold = stuckThreshold ?? DefaultStuckThreshold;
    }

    public async Task<DriftComparisonResult> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var toUtc = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
        var stuckCutoff = DateTime.UtcNow - _stuckThreshold;

        var operations = await _stockOperations.GetByCreatedDateRangeAsync(fromUtc, toUtc, ct);
        var stockTakingRecords = await _stockTakings.GetByDateRangeAsync(fromUtc, toUtc, ct);

        var mismatches = new List<DriftMismatch>();

        foreach (var op in operations)
        {
            var mismatch = StockWriteBackMismatch.None;

            if (op.State == StockOperationStateSnapshot.Failed)
                mismatch |= StockWriteBackMismatch.OperationFailed;

            if ((op.State == StockOperationStateSnapshot.Pending || op.State == StockOperationStateSnapshot.Submitted)
                && op.CreatedAtUtc <= stuckCutoff)
                mismatch |= StockWriteBackMismatch.OperationStuck;

            if (mismatch == StockWriteBackMismatch.None)
                continue;

            mismatches.Add(new DriftMismatch
            {
                EntityKey = op.ProductCode,
                MismatchCode = (int)mismatch,
                HebloValue = op.Amount.ToString(),
                ShoptetValue = null,
                Details = BuildOperationDetails(op)
            });
        }

        foreach (var record in stockTakingRecords.Where(r => r.Error != null))
        {
            mismatches.Add(new DriftMismatch
            {
                EntityKey = record.Code,
                MismatchCode = (int)StockWriteBackMismatch.StockTakingErrored,
                HebloValue = record.AmountNew.ToString("F2"),
                ShoptetValue = null,
                Details = $"Stock-taking error: {record.Error}"
            });
        }

        return new DriftComparisonResult
        {
            Mismatches = mismatches,
            TotalChecked = operations.Count + stockTakingRecords.Count
        };
    }

    private static string BuildOperationDetails(StockOperationSnapshot op)
    {
        var parts = new List<string> { $"Doc: {op.DocumentNumber}", $"State: {op.State}" };
        if (!string.IsNullOrWhiteSpace(op.ErrorMessage))
            parts.Add($"Error: {op.ErrorMessage}");
        return string.Join(" | ", parts);
    }
}
```

- [ ] **Step 4: Run the four existing tests + adapter tests; verify all pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~StockWriteBackDqtComparerTests|FullyQualifiedName~DataQualityStock"`
Expected: All tests pass (4 comparer + 9 operation adapter + 4 taking adapter = 17 tests).

- [ ] **Step 5: Verify the comparer no longer imports any Catalog namespace**

Run: `grep -n "Anela.Heblo.Domain.Features.Catalog" backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs`
Expected: No matches (exit code 1).

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/DataQuality/Services/StockWriteBackDqtComparer.cs \
        backend/test/Anela.Heblo.Tests/Features/DataQuality/StockWriteBackDqtComparerTests.cs
git commit -m "refactor: StockWriteBackDqtComparer consumes DataQuality contracts instead of Catalog repos"
```

---

## Task 8: Add `DataQuality -> Catalog` boundary rule

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

Add a new `ModuleBoundaryRule` entry that forbids DataQuality from referencing Catalog. The `ProductPairingDqtComparer` still references `IEshopStockClient`, `IErpStockClient`, `ErpStock`, and `ProductType` — those are explicitly out of scope per the spec, so they go into the allowlist with a follow-up comment.

- [ ] **Step 1: Add the `DataQualityCatalogAllowlist` field**

Find the existing allowlist block (around lines 30-159, between `LeafletAllowlist` and `CatalogManufactureAllowlist`). Insert a new field after `CatalogManufactureAllowlist` (after line 159, before the `public static TheoryData<ModuleBoundaryRule> Rules()` declaration):

```csharp
    // Allowlist for DataQuality -> Catalog. Pre-existing ProductPairingDqtComparer references
    // are out of scope for the 2026-06-03 StockWriteBackDqtComparer decoupling.
    // Track follow-up: introduce DataQuality-owned IProductPairingQuery contract and Catalog-side
    // adapter that surfaces eshop/erp product snapshots without leaking Catalog types.
    private static readonly HashSet<string> DataQualityCatalogAllowlist = new(StringComparer.Ordinal)
    {
        // ProductPairingDqtComparer reads eshop/erp catalog clients to compare product pairing.
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IEshopStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.IErpStockClient",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.Stock.ErpStock",
        "Anela.Heblo.Application.Features.DataQuality.Services.ProductPairingDqtComparer -> Anela.Heblo.Domain.Features.Catalog.ProductType",
    };
```

- [ ] **Step 2: Append the new rule to `Rules()`**

Locate the final entry in `Rules()` (`Catalog -> Manufacture`, lines 285-294). Append immediately after it, before the closing `};`:

```csharp
        new ModuleBoundaryRule(
            Name: "DataQuality -> Catalog",
            InspectedNamespacePrefix: "Anela.Heblo.Application.Features.DataQuality",
            ForbiddenNamespacePrefixes: new[]
            {
                "Anela.Heblo.Domain.Features.Catalog",
                "Anela.Heblo.Application.Features.Catalog",
                "Anela.Heblo.Persistence.Catalog",
            },
            Allowlist: DataQualityCatalogAllowlist),
```

- [ ] **Step 3: Run the boundary tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ModuleBoundariesTests"`
Expected: All rule rows pass, including the new `DataQuality -> Catalog` row.

If the test fails listing additional violations under the `DataQuality -> Catalog` rule (e.g., compiler-generated state machines, async closures, or unforeseen references), inspect the failure message and either:
  1. If the violation is a compiler-generated nested type of `ProductPairingDqtComparer` (e.g., `ProductPairingDqtComparer+<CompareAsync>d__N`), the existing `DeclaringType` fallback in `Consumer_types_should_not_reference_provider_owned_namespaces` (lines 324-330) will resolve it automatically — no extra allowlist entry needed.
  2. If the violation is a genuinely new entry, add the missing line to `DataQualityCatalogAllowlist` with a comment explaining why (must reference the same follow-up as the existing entries).
  3. If the violation is in `StockWriteBackDqtComparer`, **stop and fix the comparer**; do not allowlist a violation the refactor was supposed to eliminate.

Iterate until the rule passes.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: enforce DataQuality -> Catalog module boundary"
```

---

## Task 9: Final validation gates

These are the project-mandated checks listed in `CLAUDE.md` under "Validation before completion".

- [ ] **Step 1: Run `dotnet format` to apply formatting fixes**

Run: `dotnet format backend/Anela.Heblo.sln`
Expected: Exits 0; any whitespace/style issues are auto-fixed.

- [ ] **Step 2: Run `dotnet build` on the full solution**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 3: Run the full backend test suite**

Run: `dotnet test backend/Anela.Heblo.sln`
Expected: All tests pass. Pay special attention to:
  - `StockWriteBackDqtComparerTests` (4 tests)
  - `DataQualityStockOperationQueryAdapterTests` (~9 tests)
  - `DataQualityStockTakingQueryAdapterTests` (4 tests)
  - `ModuleBoundariesTests` (all rule rows including the new `DataQuality -> Catalog`)

- [ ] **Step 4: If anything from Steps 1-3 changed files (formatter fixes), commit the cleanup**

Run: `git status`
If files are modified, run:

```bash
git add -A
git commit -m "chore: dotnet format fixes for DataQuality decoupling"
```

If nothing changed, skip this step.

---

## Self-Review Summary

**Spec coverage check:**
- FR-1 (`IStockOperationQuery` + `StockOperationSnapshot` + `StockOperationStateSnapshot`): Tasks 1, 2, 3
- FR-2 (`IStockTakingQuery` + `StockTakingSnapshot`): Tasks 2, 3
- FR-3 (Both adapters, `internal sealed`, exhaustive enum mapping): Tasks 4, 5
- FR-4 (DI registrations as `Scoped` in `CatalogModule`): Task 6
- FR-5 (Comparer rewrite, no Catalog imports): Task 7
- FR-6 (Test update with new contracts + new adapter tests): Tasks 4, 5, 7
- FR-7 (Architecture boundary rule with `ProductPairingDqtComparer` allowlist per Amendment 1): Task 8
- NFR-1 (Date filter pushed into adapter, single materialization): Task 4 Step 3
- NFR-2 (Empty `Anela.Heblo.Domain.Features.Catalog.*` references in DataQuality post-refactor for `StockWriteBackDqtComparer`): Task 7 Step 5 verification
- NFR-3 (No DQT endpoint behavior change, `DriftDqtJobRunner` unchanged): preserved by Task 7 (only constructor signature changes)
- NFR-4 (`dotnet build`, `dotnet format`, all tests pass): Task 9

**Type / signature consistency check:**
- `IStockOperationQuery.GetByCreatedDateRangeAsync` — referenced identically in Tasks 3, 4, 7
- `IStockTakingQuery.GetByDateRangeAsync` — referenced identically in Tasks 3, 5, 7
- `StockOperationSnapshot` fields (`ProductCode`, `Amount`, `DocumentNumber`, `State`, `CreatedAtUtc`, `ErrorMessage`) — consistent across Tasks 2, 4, 7
- `StockTakingSnapshot` fields (`Code`, `AmountNew`, `Error`) — consistent across Tasks 2, 5, 7
- `StockOperationStateSnapshot` enum values (`Pending`, `Submitted`, `Completed`, `Failed`) — consistent across Tasks 1, 4, 7

**Out of scope (not addressed by this plan, per spec):**
- Refactoring `ProductPairingDqtComparer` (covered by the `DataQualityCatalogAllowlist` follow-up).
- Splitting `IStockUpOperationRepository` into read/write halves.
- Removing the existing `LogisticsStockOperationQueryAdapter` allowlist entries in `CatalogLogisticsAllowlist`.
- Frontend, E2E, OpenAPI, or migration work — none required by this refactor.
