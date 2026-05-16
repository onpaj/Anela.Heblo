# Decouple Logistics handlers from Manufacture inventory — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the direct `IManufacturedProductInventoryRepository` injection in `AddItemToBoxHandler` and `ChangeTransportBoxStateHandler` with a Logistics-owned `IInventoryReservationService` contract, implemented by an internal adapter in the Manufacture module. Extend the module-boundary CI test to cover Logistics → Manufacture, with no behavioral change.

**Architecture:** Apply the Leaflet/KnowledgeBase consumer-owns-contract / provider-owns-adapter precedent (`docs/architecture/development_guidelines.md` §"Cross-Module Communication Example"). Logistics declares the interface and a sealed-record result type in its `Contracts/` folder; Manufacture provides an `internal sealed` adapter in a new `Infrastructure/` folder; `ManufactureModule` registers the binding. Persistence remains in the shared `ApplicationDbContext` (ADR-001 Phase 1) — the adapter must not call `SaveChangesAsync`; the caller's existing `_repository.SaveChangesAsync(cancellationToken)` commits both the box mutation and the inventory mutation atomically. The reflection-based `ModuleBoundariesTests` is refactored to a `[Theory]` so a Logistics row is one `MemberData` entry, and the existing Leaflet row keeps its allowlist.

**Tech Stack:** .NET 8, C#, xUnit, FluentAssertions, Moq, MediatR, EF Core (`ApplicationDbContext`), `Microsoft.Extensions.Logging`, `Microsoft.Extensions.DependencyInjection`.

---

## File Structure

**Files to create:**

| Path | Responsibility |
|------|----------------|
| `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs` | Logistics-owned interface (`TryConsumeAsync`, `RestoreAsync`). |
| `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs` | Logistics-owned sealed record + `ConsumeInventoryOutcome` enum (Success/InventoryNotFound/InsufficientStock). |
| `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs` | `internal sealed` adapter implementing the contract by delegating to `IManufacturedProductInventoryRepository` and `ManufacturedProductInventoryItem.Consume/Restore`. |
| `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapterTests.cs` | Unit tests for the adapter (6 cases). |

**Files to modify:**

| Path | Change |
|------|--------|
| `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs` | Register `IInventoryReservationService` → `ManufactureInventoryReservationAdapter`. |
| `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs` | Refactor existing `[Fact]` into a `[Theory]` with `MemberData`; add Logistics → Manufacture row with empty allowlist. |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs` | Replace `IManufacturedProductInventoryRepository` field/ctor param with `IInventoryReservationService`; replace `GetByIdAsync`/`Consume`/`UpdateAsync` block + `try/catch (InvalidOperationException)` with a single `TryConsumeAsync` call mapped on its outcome. Drop `using Anela.Heblo.Domain.Features.Manufacture.Inventory;`. |
| `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs` | Replace `IManufacturedProductInventoryRepository` field/ctor param with `IInventoryReservationService`; reimplement `RestoreInventoryForItemsAsync` to delegate per item. Drop `using Anela.Heblo.Domain.Features.Manufacture.Inventory;`. |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs` | Swap the mock target from `IManufacturedProductInventoryRepository` to `IInventoryReservationService`; rewrite verifications against the new contract. Drop `using Anela.Heblo.Domain.Features.Manufacture.Inventory;`. |
| `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs` | Swap the mock target; add two Opened→New restore tests (happy-path delegates per item; missing-inventory is logged-and-skipped by the adapter — handler test asserts it still calls `RestoreAsync`). Drop `using Anela.Heblo.Domain.Features.Manufacture.Inventory;`. |

**No other files change.** No NuGet packages added (NFR-6). No schema, no migration, no OpenAPI regeneration.

---

## Task 1: Add the Logistics-owned contract types

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs`

- [ ] **Step 1: Create `ConsumeInventoryResult.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

/// <summary>
/// Outcome of an <see cref="IInventoryReservationService.TryConsumeAsync"/> call.
/// </summary>
public enum ConsumeInventoryOutcome
{
    Success,
    InventoryNotFound,
    InsufficientStock,
}

/// <summary>
/// Logistics-owned result of attempting to consume inventory.
/// Sealed record with an outcome discriminator — extensible to carry an optional
/// available-amount field without breaking the contract.
/// </summary>
public sealed record ConsumeInventoryResult(ConsumeInventoryOutcome Outcome);
```

- [ ] **Step 2: Create `IInventoryReservationService.cs`**

Write the file exactly:

```csharp
namespace Anela.Heblo.Application.Features.Logistics.Contracts;

/// <summary>
/// Logistics-owned abstraction over inventory reservation for transport-box operations.
/// Implemented by the Manufacture module via an adapter
/// (see <c>ManufactureInventoryReservationAdapter</c>) per the cross-module communication
/// pattern in <c>docs/architecture/development_guidelines.md</c>.
///
/// Implementations MUST NOT call SaveChangesAsync on any repository. The caller owns the
/// unit of work and commits the inventory mutation together with the transport-box mutation
/// against the shared ApplicationDbContext (ADR-001 Phase 1).
/// </summary>
public interface IInventoryReservationService
{
    /// <summary>
    /// Attempts to decrement inventory for a transport-box item. Returns a structured
    /// result distinguishing success, missing inventory record, and insufficient stock.
    /// Implementations must not throw Manufacture-owned exceptions across this boundary.
    /// </summary>
    Task<ConsumeInventoryResult> TryConsumeAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        bool allowNegativeStock,
        CancellationToken cancellationToken);

    /// <summary>
    /// Restores inventory amount after a transport box transitions Opened → New.
    /// If the inventory id does not exist, the call is a no-op (implementations log a
    /// warning and return) — matching the original "log and skip" recovery semantics
    /// in <c>ChangeTransportBoxStateHandler</c>.
    /// </summary>
    Task RestoreAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Verify build**

Run from repo root:

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded, 0 errors. The interface compiles standalone; nothing references it yet.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/IInventoryReservationService.cs \
        backend/src/Anela.Heblo.Application/Features/Logistics/Contracts/ConsumeInventoryResult.cs
git commit -m "feat: add Logistics-owned IInventoryReservationService contract"
```

---

## Task 2: Implement `ManufactureInventoryReservationAdapter`

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs`

- [ ] **Step 1: Create the adapter file**

The folder `backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/` does not yet exist — create it as part of writing the file.

Write the file exactly:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Manufacture.Infrastructure;

/// <summary>
/// Manufacture-owned adapter that implements the Logistics-owned
/// <see cref="IInventoryReservationService"/> contract by delegating to the Manufacture
/// inventory repository and domain methods.
///
/// Does not commit — the caller's unit of work (the transport-box repository's
/// SaveChangesAsync) commits both mutations atomically against the shared
/// ApplicationDbContext.
/// </summary>
internal sealed class ManufactureInventoryReservationAdapter : IInventoryReservationService
{
    private readonly IManufacturedProductInventoryRepository _inventoryRepository;
    private readonly ILogger<ManufactureInventoryReservationAdapter> _logger;

    public ManufactureInventoryReservationAdapter(
        IManufacturedProductInventoryRepository inventoryRepository,
        ILogger<ManufactureInventoryReservationAdapter> logger)
    {
        _inventoryRepository = inventoryRepository;
        _logger = logger;
    }

    public async Task<ConsumeInventoryResult> TryConsumeAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        bool allowNegativeStock,
        CancellationToken cancellationToken)
    {
        var item = await _inventoryRepository.GetByIdAsync(inventoryId, cancellationToken);
        if (item is null)
        {
            return new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound);
        }

        try
        {
            // ManufacturedProductInventoryItem.Consume is the sole producer of
            // InvalidOperationException on this call path (insufficient stock guard at
            // Domain/Features/Manufacture/Inventory/ManufacturedProductInventoryItem.cs:55-57).
            // If a future invariant adds another InvalidOperationException here, this catch
            // would miscategorize it — track upgrade to a typed InsufficientInventoryException
            // as a follow-up.
            item.Consume(amount, userName, timestamp, boxId, boxCode, allowNegativeStock);
        }
        catch (InvalidOperationException)
        {
            return new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock);
        }

        await _inventoryRepository.UpdateAsync(item, cancellationToken);
        return new ConsumeInventoryResult(ConsumeInventoryOutcome.Success);
    }

    public async Task RestoreAsync(
        int inventoryId,
        decimal amount,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken)
    {
        var item = await _inventoryRepository.GetByIdAsync(inventoryId, cancellationToken);
        if (item is null)
        {
            _logger.LogWarning(
                "InventoryItem {InventoryId} not found during restore for transport box {BoxId} — skipping restore",
                inventoryId, boxId);
            return;
        }

        item.Restore(amount, userName, timestamp, boxId, boxCode);
        await _inventoryRepository.UpdateAsync(item, cancellationToken);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapter.cs
git commit -m "feat: add ManufactureInventoryReservationAdapter implementing IInventoryReservationService"
```

---

## Task 3: Register DI binding in `ManufactureModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs`

- [ ] **Step 1: Add the using directives**

Open the file and locate the `using` block at the top (lines 1-13). Add these two `using` statements alphabetically among the existing ones:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
```

The using block already imports `Anela.Heblo.Domain.Features.Manufacture.Inventory` and `Anela.Heblo.Application.Features.Manufacture.Configuration`, so insert the two new lines preserving alphabetical order.

- [ ] **Step 2: Register the binding**

Locate the comment `// Register repositories` (around line 46 / line 47). Immediately after the existing two `services.AddScoped<IManufactureOrderRepository, ManufactureOrderRepository>();` and `services.AddScoped<IManufacturedProductInventoryRepository, ManufacturedProductInventoryRepository>();` lines, insert:

```csharp

        // Cross-module contract: Manufacture implements Logistics' IInventoryReservationService.
        // DI registration is owned by the provider (Manufacture), not the consumer (Logistics).
        services.AddScoped<IInventoryReservationService, ManufactureInventoryReservationAdapter>();
```

(One blank line above the comment to separate it from the repository-registration block.)

- [ ] **Step 3: Verify build and app composition**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors, 0 warnings introduced by the change.

- [ ] **Step 4: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/ManufactureModule.cs
git commit -m "feat: register IInventoryReservationService -> ManufactureInventoryReservationAdapter in ManufactureModule"
```

---

## Task 4: Add adapter unit tests

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapterTests.cs`

The test project (`Anela.Heblo.Tests`) already has `InternalsVisibleTo` granted by `Anela.Heblo.Application` (verified: `backend/src/Anela.Heblo.Application/AssemblyInfo.cs:3`), so the test can instantiate the `internal` adapter directly.

- [ ] **Step 1: Write the failing test file**

The folder `backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/` does not yet exist — create it as part of writing the file.

Write the file exactly:

```csharp
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Infrastructure;

public class ManufactureInventoryReservationAdapterTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ILogger<ManufactureInventoryReservationAdapter>> _loggerMock;
    private readonly ManufactureInventoryReservationAdapter _adapter;

    private static readonly DateTime FixedTime = new(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public ManufactureInventoryReservationAdapterTests()
    {
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _loggerMock = new Mock<ILogger<ManufactureInventoryReservationAdapter>>();
        _adapter = new ManufactureInventoryReservationAdapter(
            _inventoryRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task TryConsumeAsync_ItemNotFound_ReturnsInventoryNotFound()
    {
        // Arrange
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 5m, userName: "u", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.InventoryNotFound);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryConsumeAsync_HappyPath_DecrementsAndReturnsSuccess()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 100m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 10m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.Success);
        item.Amount.Should().Be(90m);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(item, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryConsumeAsync_AmountExceedsAvailable_ReturnsInsufficientStock()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 5m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 100m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.InsufficientStock);
        item.Amount.Should().Be(5m, "domain mutation must not be persisted when consume fails");
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryConsumeAsync_AllowNegativeStock_SucceedsWhenAmountExceedsAvailable()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 5m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 100m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: true,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.Success);
        item.Amount.Should().Be(-95m);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(item, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryConsumeAsync_DoesNotCallSaveChanges()
    {
        // Arrange — guard NFR-3: adapter must never commit
        var item = CreateInventoryItem("PROD-001", 100m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 10m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_HappyPath_IncrementsAndUpdates()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 10m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        await _adapter.RestoreAsync(
            inventoryId: 42, amount: 3m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001",
            cancellationToken: CancellationToken.None);

        // Assert
        item.Amount.Should().Be(13m);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(item, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_ItemNotFound_IsNoOpAndLogsWarning()
    {
        // Arrange
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        // Act
        await _adapter.RestoreAsync(
            inventoryId: 999, amount: 3m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001",
            cancellationToken: CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("InventoryItem") && v.ToString()!.Contains("999")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static ManufacturedProductInventoryItem CreateInventoryItem(string productCode, decimal amount)
    {
        return new ManufacturedProductInventoryItem(
            productCode: productCode,
            productName: "Test Product",
            amount: amount,
            createdBy: "system",
            createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
```

- [ ] **Step 2: Run the new tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureInventoryReservationAdapterTests"
```

Expected: 7 tests passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Manufacture/Infrastructure/ManufactureInventoryReservationAdapterTests.cs
git commit -m "test: cover ManufactureInventoryReservationAdapter consume and restore paths"
```

---

## Task 5: Refactor `ModuleBoundariesTests` to `[Theory]` (no behavior change yet)

Goal of this task: convert the existing single `[Fact]` to a data-driven `[Theory]` that still has exactly one row (Leaflet → KnowledgeBase). The Logistics row is added in Task 6 to keep the "red on this branch" step distinct.

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Replace the class body with the parameterized version**

Replace the entire contents of the file with:

```csharp
using System.Reflection;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Architecture;

/// <summary>
/// Enforces module boundary rules from docs/architecture/development_guidelines.md:
/// Consumer modules must not reference provider-owned types directly. All cross-module
/// communication goes through consumer-owned contracts (e.g. ILeafletKnowledgeSource,
/// IInventoryReservationService) implemented by the provider via an adapter.
/// </summary>
public class ModuleBoundariesTests
{
    public sealed record ModuleBoundaryRule(
        string Name,
        string InspectedNamespacePrefix,
        IReadOnlyList<string> ForbiddenNamespacePrefixes,
        IReadOnlySet<string> Allowlist);

    // Pre-existing allowlist for Leaflet → KnowledgeBase. Each entry needs a comment with the
    // justification. Entries should be removed as the underlying violations are fixed.
    //
    // Entry format: "{ConsumerFullyQualifiedTypeName} -> {ProviderTypeFullName}"
    //
    // Compiler-generated types (e.g. DisplayClasses for closures, state machines for async
    // methods) are automatically handled by matching against the declaring type's namespace
    // prefix below.
    private static readonly HashSet<string> LeafletAllowlist = new(StringComparer.Ordinal)
    {
        // Pre-existing dependency: UploadLeafletHandler and IndexLeafletHandler consume
        // IDocumentTextExtractor, which currently lives in
        // Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting this is out of
        // scope for the 2026-05-15 Leaflet decoupling. Track separately and remove these
        // entries when IDocumentTextExtractor is relocated to a shared namespace.
        "Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet.UploadLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",
        "Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet.IndexLeafletHandler -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IDocumentTextExtractor",

        // Pre-existing dependency: LeafletIngestionJob consumes IOneDriveService, which
        // currently lives in Anela.Heblo.Application.Features.KnowledgeBase.Services. Lifting
        // this is out of scope for the 2026-05-15 Leaflet decoupling. Track separately and
        // remove these entries when IOneDriveService is relocated to a shared namespace.
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.IOneDriveService",
        "Anela.Heblo.Application.Features.Leaflet.Infrastructure.Jobs.LeafletIngestionJob -> Anela.Heblo.Application.Features.KnowledgeBase.Services.OneDriveFile",
    };

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
    };

    [Theory]
    [MemberData(nameof(Rules))]
    public void Consumer_types_should_not_reference_provider_owned_namespaces(ModuleBoundaryRule rule)
    {
        var assembly = Assembly.Load("Anela.Heblo.Application");
        var consumerTypes = assembly.GetTypes()
            .Where(t => t.Namespace is not null
                && t.Namespace.StartsWith(rule.InspectedNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        var violations = new List<string>();

        foreach (var consumerType in consumerTypes)
        {
            foreach (var (referencedType, memberDescription) in EnumerateReferencedTypes(consumerType))
            {
                if (!IsForbidden(referencedType, rule.ForbiddenNamespacePrefixes))
                    continue;

                var entry = $"{consumerType.FullName} -> {referencedType.FullName}";
                if (rule.Allowlist.Contains(entry))
                    continue;

                // Also check if the declaring type of a compiler-generated nested type is in
                // the allowlist. For example, if "UploadLeafletHandler+<>c__DisplayClass3_0"
                // references a forbidden type, check if "UploadLeafletHandler" references that
                // same forbidden type.
                var baseType = consumerType.DeclaringType;
                if (baseType is not null)
                {
                    var baseEntry = $"{baseType.FullName} -> {referencedType.FullName}";
                    if (rule.Allowlist.Contains(baseEntry))
                        continue;
                }

                violations.Add($"{entry} (via {memberDescription})");
            }
        }

        violations.Should().BeEmpty(
            $"{rule.Name}: consumer types must not reference provider-owned namespaces. " +
            "Define a consumer-owned contract in the consumer module's Contracts/ folder " +
            "and have the provider module implement it via an adapter. " +
            "Found:\n  " + string.Join("\n  ", violations));
    }

    private static bool IsForbidden(Type type, IReadOnlyList<string> forbiddenPrefixes)
    {
        if (type.Namespace is null)
            return false;

        foreach (var prefix in forbiddenPrefixes)
        {
            if (type.Namespace.Equals(prefix, StringComparison.Ordinal) ||
                type.Namespace.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Enumerates every type referenced by a given type: constructor parameters, fields,
    /// properties, method parameters, method return types, generic type arguments,
    /// and attribute types. Returns (referencedType, "where it appeared") tuples.
    ///
    /// Known limitation: does not inspect method bodies (local variable types,
    /// inlined call targets). Generic constraints and attribute constructor args
    /// are covered partially via Type/CustomAttribute traversal.
    /// </summary>
    private static IEnumerable<(Type Type, string Where)> EnumerateReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                    BindingFlags.Instance | BindingFlags.Static |
                                    BindingFlags.DeclaredOnly;

        foreach (var attr in type.GetCustomAttributesData())
            foreach (var t in ExpandGenerics(attr.AttributeType))
                yield return (t, $"attribute [{attr.AttributeType.Name}]");

        foreach (var field in type.GetFields(flags))
            foreach (var t in ExpandGenerics(field.FieldType))
                yield return (t, $"field {field.Name}");

        foreach (var prop in type.GetProperties(flags))
            foreach (var t in ExpandGenerics(prop.PropertyType))
                yield return (t, $"property {prop.Name}");

        foreach (var ctor in type.GetConstructors(flags))
            foreach (var param in ctor.GetParameters())
                foreach (var t in ExpandGenerics(param.ParameterType))
                    yield return (t, $"ctor parameter {param.Name}");

        foreach (var method in type.GetMethods(flags))
        {
            foreach (var t in ExpandGenerics(method.ReturnType))
                yield return (t, $"method {method.Name} return");

            foreach (var param in method.GetParameters())
                foreach (var t in ExpandGenerics(param.ParameterType))
                    yield return (t, $"method {method.Name} parameter {param.Name}");
        }
    }

    private static IEnumerable<Type> ExpandGenerics(Type type)
    {
        if (type.IsByRef || type.IsPointer)
            type = type.GetElementType() ?? type;

        if (type.IsArray)
        {
            var elem = type.GetElementType();
            if (elem is not null)
                foreach (var t in ExpandGenerics(elem))
                    yield return t;
            yield break;
        }

        yield return type;

        if (type.IsGenericType)
        {
            foreach (var arg in type.GetGenericArguments())
                foreach (var t in ExpandGenerics(arg))
                    yield return t;
        }
    }
}
```

- [ ] **Step 2: Run the test — confirm Leaflet row still passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: 1 test passed (the parameterized fact runs once for the Leaflet row), 0 failed.

If it fails, the refactor introduced a regression — fix before proceeding. Do not move on.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "refactor: parameterize ModuleBoundariesTests for multiple module-pair rules"
```

---

## Task 6: Add the Logistics → Manufacture rule row (expected to go red)

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

- [ ] **Step 1: Add a second row to `Rules()`**

In `ModuleBoundariesTests.cs`, locate the `Rules()` method (added in Task 5). Append a second `TheoryData` entry so the method body becomes:

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
            Allowlist: new HashSet<string>(StringComparer.Ordinal)),
    };
```

- [ ] **Step 2: Run the test — confirm Logistics row goes RED**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: 1 passed (Leaflet row), 1 failed (Logistics row). The failure message must list at least:

- `Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox.AddItemToBoxHandler -> Anela.Heblo.Domain.Features.Manufacture.Inventory.IManufacturedProductInventoryRepository`
- `Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox.AddItemToBoxHandler -> Anela.Heblo.Domain.Features.Manufacture.Inventory.ManufacturedProductInventoryItem` (via method return / parameter via `GetByIdAsync`)
- `Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState.ChangeTransportBoxStateHandler -> Anela.Heblo.Domain.Features.Manufacture.Inventory.IManufacturedProductInventoryRepository`

If the test passes, the migration was already done somewhere else and the plan is out of order — stop and investigate. If the test fails with violations not listed above (e.g. some other Logistics → Manufacture leak we did not anticipate), stop and add the leaks to the spec's Out-of-Scope or extend the migration in Task 7/Task 8.

- [ ] **Step 3: Commit the red test**

```bash
git add backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs
git commit -m "test: enforce Logistics -> Manufacture module boundary in ModuleBoundariesTests"
```

The test is committed in failing state. Tasks 7 and 8 fix the production code; the test goes green at the end of Task 8.

---

## Task 7: Migrate `AddItemToBoxHandler` to the new contract

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs`

- [ ] **Step 1: Update the handler imports**

In `AddItemToBoxHandler.cs`, replace the `using` block (lines 1-9) with:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using MediatR;
using Microsoft.Extensions.Logging;
```

(Net effect: drop `using Anela.Heblo.Domain.Features.Manufacture.Inventory;`. `Anela.Heblo.Application.Features.Logistics.Contracts` is already present from the previous version, so re-confirm it's still there.)

- [ ] **Step 2: Replace the inventory field and constructor parameter**

Replace lines 15-36 (field declarations + constructor) with:

```csharp
    private readonly ITransportBoxRepository _repository;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<AddItemToBoxHandler> _logger;
    private readonly IMapper _mapper;
    private readonly TimeProvider _timeProvider;

    public AddItemToBoxHandler(
        ITransportBoxRepository repository,
        IInventoryReservationService inventoryReservationService,
        ICurrentUserService currentUserService,
        ILogger<AddItemToBoxHandler> logger,
        IMapper mapper,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _inventoryReservationService = inventoryReservationService;
        _currentUserService = currentUserService;
        _logger = logger;
        _mapper = mapper;
        _timeProvider = timeProvider;
    }
```

- [ ] **Step 3: Replace the inventory-consumption block in `Handle`**

In the same file, replace the entire `if (request.SourceInventoryId != null) { ... }` block (originally lines 57-85, including its inner `try/catch (InvalidOperationException)`) with:

```csharp
            if (request.SourceInventoryId != null)
            {
                var consumeResult = await _inventoryReservationService.TryConsumeAsync(
                    inventoryId: request.SourceInventoryId.Value,
                    amount: (decimal)request.Amount,
                    userName: userName,
                    timestamp: timestamp,
                    boxId: transportBox.Id,
                    boxCode: transportBox.Code,
                    allowNegativeStock: request.AllowNegativeStock,
                    cancellationToken: cancellationToken);

                switch (consumeResult.Outcome)
                {
                    case ConsumeInventoryOutcome.InventoryNotFound:
                        return new AddItemToBoxResponse
                        {
                            Success = false,
                            ErrorCode = ErrorCodes.ManufacturedInventoryItemNotFound,
                            Params = new Dictionary<string, string> { { "sourceInventoryId", request.SourceInventoryId.Value.ToString() } }
                        };
                    case ConsumeInventoryOutcome.InsufficientStock:
                        return new AddItemToBoxResponse
                        {
                            Success = false,
                            ErrorCode = ErrorCodes.ManufacturedInventoryInsufficientStock,
                            Params = new Dictionary<string, string> { { "sourceInventoryId", request.SourceInventoryId.Value.ToString() } }
                        };
                    case ConsumeInventoryOutcome.Success:
                        break;
                }
            }
```

Leave everything from `var addedItem = transportBox.AddItem(...)` onwards untouched. Order is preserved: reserve inventory → add box item → SaveChangesAsync.

- [ ] **Step 4: Migrate the handler tests**

In `AddItemToBoxHandlerTests.cs`, replace lines 1-12 (using block) with:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using AutoMapper;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

Replace the `_inventoryRepositoryMock` field (line 19) and its usage in the constructor (lines 31, 55) with `_inventoryReservationServiceMock`. The full top of the class (lines 17-60) becomes:

```csharp
public class AddItemToBoxHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<AddItemToBoxHandler>> _loggerMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly AddItemToBoxHandler _handler;

    private static readonly DateTime FixedTime = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public AddItemToBoxHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _inventoryReservationServiceMock = new Mock<IInventoryReservationService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<AddItemToBoxHandler>>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();

        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-id", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(FixedTime));

        _mapperMock
            .Setup(x => x.Map<TransportBoxItemDto>(It.IsAny<TransportBoxItem>()))
            .Returns(new TransportBoxItemDto());

        _mapperMock
            .Setup(x => x.Map<TransportBoxDto>(It.IsAny<TransportBox>()))
            .Returns(new TransportBoxDto());

        _handler = new AddItemToBoxHandler(
            _repositoryMock.Object,
            _inventoryReservationServiceMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object,
            _mapperMock.Object,
            _timeProviderMock.Object);
    }
```

Replace the body of `Handle_WithoutSourceInventoryId_AddsItemWithoutInventoryInteraction` (lines 87-118) with:

```csharp
    [Fact]
    public async Task Handle_WithoutSourceInventoryId_AddsItemWithoutInventoryInteraction()
    {
        // Arrange
        var box = CreateOpenBox();
        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 5.0
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        _inventoryReservationServiceMock.Verify(
            x => x.TryConsumeAsync(
                It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

Replace the body of `Handle_WithSourceInventoryId_ConsumesInventoryAndSetsLotOnItem` (lines 120-168) with:

```csharp
    [Fact]
    public async Task Handle_WithSourceInventoryId_ConsumesInventoryAndSetsLotOnItem()
    {
        // Arrange
        var box = CreateOpenBox();

        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 10.0,
            SourceInventoryId = 42,
            LotNumber = "LOT-123",
            ExpirationDate = new DateOnly(2026, 12, 31)
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryReservationServiceMock
            .Setup(x => x.TryConsumeAsync(
                42, 10m, It.IsAny<string>(), It.IsAny<DateTime>(),
                1, "B001", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.Success));

        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        _inventoryReservationServiceMock.Verify(
            x => x.TryConsumeAsync(
                42, 10m, "Test User", FixedTime,
                1, "B001", false, It.IsAny<CancellationToken>()),
            Times.Once);

        // Box item has the lot and source inventory set
        var addedItem = box.Items.Single();
        addedItem.SourceInventoryId.Should().Be(42);
        addedItem.LotNumber.Should().Be("LOT-123");
        addedItem.ExpirationDate.Should().Be(new DateOnly(2026, 12, 31));
    }
```

Replace the body of `Handle_WithSourceInventoryId_InsufficientStock_ReturnsError` (lines 170-205) with:

```csharp
    [Fact]
    public async Task Handle_WithSourceInventoryId_InsufficientStock_ReturnsError()
    {
        // Arrange
        var box = CreateOpenBox();
        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 100.0,
            SourceInventoryId = 42
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryReservationServiceMock
            .Setup(x => x.TryConsumeAsync(
                42, 100m, It.IsAny<string>(), It.IsAny<DateTime>(),
                1, "B001", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.InsufficientStock));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryInsufficientStock);
        result.Params.Should().ContainKey("sourceInventoryId").WhoseValue.Should().Be("42");

        // Box save should not have been called
        _repositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

Replace the body of `Handle_WithSourceInventoryId_InventoryNotFound_ReturnsError` (lines 207-235) with:

```csharp
    [Fact]
    public async Task Handle_WithSourceInventoryId_InventoryNotFound_ReturnsError()
    {
        // Arrange
        var box = CreateOpenBox();
        var request = new AddItemToBoxRequest
        {
            BoxId = 1,
            ProductCode = "PROD-001",
            ProductName = "Test Product",
            Amount = 5.0,
            SourceInventoryId = 999
        };

        _repositoryMock
            .Setup(x => x.GetByIdWithDetailsAsync(1))
            .ReturnsAsync(box);

        _inventoryReservationServiceMock
            .Setup(x => x.TryConsumeAsync(
                999, 5m, It.IsAny<string>(), It.IsAny<DateTime>(),
                1, "B001", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConsumeInventoryResult(ConsumeInventoryOutcome.InventoryNotFound));

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.ManufacturedInventoryItemNotFound);
        result.Params.Should().ContainKey("sourceInventoryId").WhoseValue.Should().Be("999");
    }
```

Delete the `CreateInventoryItem` helper at the bottom of the file (lines 254-262) — it's no longer needed since the tests don't construct `ManufacturedProductInventoryItem` directly. Keep `CreateOpenBox`.

- [ ] **Step 5: Run the handler tests and confirm they pass**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~AddItemToBoxHandlerTests"
```

Expected: 5 tests passed (BoxNotFound, WithoutSourceInventoryId, ConsumesInventoryAndSetsLotOnItem, InsufficientStock, InventoryNotFound), 0 failed.

- [ ] **Step 6: Run the boundary test — Logistics row should still be RED (only one handler done)**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: Leaflet row passes, Logistics row still fails — but the violations list should be smaller now, listing only `ChangeTransportBoxStateHandler` references (not `AddItemToBoxHandler` anymore). If `AddItemToBoxHandler` still appears in the failure message, re-check Step 1 (the `using` directive `Anela.Heblo.Domain.Features.Manufacture.Inventory` must be gone) and Step 2 (the field type must be `IInventoryReservationService`, not `IManufacturedProductInventoryRepository`).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/AddItemToBox/AddItemToBoxHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/AddItemToBoxHandlerTests.cs
git commit -m "refactor(logistics): route AddItemToBox inventory consume through IInventoryReservationService"
```

---

## Task 8: Migrate `ChangeTransportBoxStateHandler` to the new contract

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs`

- [ ] **Step 1: Update handler imports**

In `ChangeTransportBoxStateHandler.cs`, replace the `using` block (lines 1-10) with:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using MediatR;
using Microsoft.Extensions.Logging;
```

(Net effect: drop `using Anela.Heblo.Domain.Features.Manufacture.Inventory;`. Add `Anela.Heblo.Application.Features.Logistics.Contracts`.)

- [ ] **Step 2: Replace the inventory field and constructor parameter**

In the same file, replace the field block (lines 16-22) with:

```csharp
    private readonly ITransportBoxRepository _repository;
    private readonly IInventoryReservationService _inventoryReservationService;
    private readonly IMediator _mediator;
    private readonly ILogger<ChangeTransportBoxStateHandler> _logger;
    private readonly ICurrentUserService _currentUserService;
    private readonly IStockUpProcessingService _stockUpProcessingService;
    private readonly TimeProvider _timeProvider;
```

Replace the constructor (lines 40-56) with:

```csharp
    public ChangeTransportBoxStateHandler(
        ITransportBoxRepository repository,
        IInventoryReservationService inventoryReservationService,
        IMediator mediator,
        ILogger<ChangeTransportBoxStateHandler> logger,
        ICurrentUserService currentUserService,
        IStockUpProcessingService stockUpProcessingService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _inventoryReservationService = inventoryReservationService;
        _mediator = mediator;
        _logger = logger;
        _currentUserService = currentUserService;
        _stockUpProcessingService = stockUpProcessingService;
        _timeProvider = timeProvider;
    }
```

- [ ] **Step 3: Replace `RestoreInventoryForItemsAsync`**

Replace the entire `RestoreInventoryForItemsAsync` method (lines 266-288) with:

```csharp
    private async Task RestoreInventoryForItemsAsync(
        IReadOnlyList<TransportBoxItem> items,
        string userName,
        DateTime timestamp,
        int boxId,
        string? boxCode,
        CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            if (item.SourceInventoryId == null) continue;

            await _inventoryReservationService.RestoreAsync(
                inventoryId: item.SourceInventoryId.Value,
                amount: (decimal)item.Amount,
                userName: userName,
                timestamp: timestamp,
                boxId: boxId,
                boxCode: boxCode,
                cancellationToken: cancellationToken);
        }
    }
```

The missing-id warning now lives in `ManufactureInventoryReservationAdapter.RestoreAsync` (Task 2). The handler no longer logs it directly.

Leave the call site at line 132 unchanged — it still reads `await RestoreInventoryForItemsAsync(itemsToRestore, userName, currentTime, box.Id, box.Code, cancellationToken);`. Leave the `itemsToRestore` capture at lines 124-126 unchanged.

- [ ] **Step 4: Migrate the handler tests — swap the mock target**

In `ChangeTransportBoxStateHandlerTests.cs`, replace lines 1-15 (using block) with:

```csharp
using System.ComponentModel.DataAnnotations;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
```

Replace the field and constructor body (lines 19-67) with:

```csharp
public class ChangeTransportBoxStateHandlerTests
{
    private readonly Mock<ITransportBoxRepository> _repositoryMock;
    private readonly Mock<IInventoryReservationService> _inventoryReservationServiceMock;
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<ILogger<ChangeTransportBoxStateHandler>> _loggerMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<IStockUpProcessingService> _stockUpProcessingServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly ChangeTransportBoxStateHandler _handler;

    public ChangeTransportBoxStateHandlerTests()
    {
        _repositoryMock = new Mock<ITransportBoxRepository>();
        _inventoryReservationServiceMock = new Mock<IInventoryReservationService>();
        _mediatorMock = new Mock<IMediator>();
        _loggerMock = new Mock<ILogger<ChangeTransportBoxStateHandler>>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _stockUpProcessingServiceMock = new Mock<IStockUpProcessingService>();
        _timeProviderMock = new Mock<TimeProvider>();

        // Setup default returns for the new dependencies
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUser())
            .Returns(new CurrentUser("test-user", "Test User", "test@example.com", true));

        _timeProviderMock
            .Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));

        _stockUpProcessingServiceMock
            .Setup(x => x.CreateOperationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<StockUpSourceType>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _handler = new ChangeTransportBoxStateHandler(
            _repositoryMock.Object,
            _inventoryReservationServiceMock.Object,
            _mediatorMock.Object,
            _loggerMock.Object,
            _currentUserServiceMock.Object,
            _stockUpProcessingServiceMock.Object,
            _timeProviderMock.Object);
    }
```

- [ ] **Step 5: Add Opened → New restore-path tests**

Append two new `[Fact]` methods to the test class, immediately before the `SetupReceivedTransitionMocks` private helper. The handler did not previously have tests covering the `Opened → New` restore path; FR-8 requires that case to be exercised — and this is the test that proves NFR-1 (the handler still delegates restore per item).

Add this method:

```csharp
    [Fact]
    public async Task Handle_OpenedToNew_WithSourceInventoryItems_DelegatesRestorePerItem()
    {
        // Arrange — box in Opened with two items: one with SourceInventoryId, one without
        var box = CreateTestBox(TransportBoxState.Opened);
        box.Id = 1;
        var itemsField = typeof(TransportBox).GetField("_items",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var items = (List<TransportBoxItem>)itemsField!.GetValue(box)!;

        items.Add(new TransportBoxItem(
            productCode: "PROD-001",
            productName: "Test Product",
            amount: 7.0,
            dateAdded: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            userAdded: "TestUser",
            lotNumber: null,
            expirationDate: null,
            sourceInventoryId: 42));
        items.Add(new TransportBoxItem(
            productCode: "PROD-002",
            productName: "Other Product",
            amount: 3.0,
            dateAdded: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            userAdded: "TestUser",
            lotNumber: null,
            expirationDate: null,
            sourceInventoryId: null));

        _repositoryMock.Setup(x => x.GetByIdWithDetailsAsync(1)).ReturnsAsync(box);
        _repositoryMock
            .Setup(x => x.UpdateAsync(It.IsAny<TransportBox>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repositoryMock
            .Setup(x => x.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _mediatorMock
            .Setup(x => x.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetTransportBoxByIdResponse
            {
                TransportBox = new Anela.Heblo.Application.Features.Logistics.Contracts.TransportBoxDto()
            });

        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.New
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        // Only the item with SourceInventoryId triggers a restore call
        _inventoryReservationServiceMock.Verify(
            x => x.RestoreAsync(
                42,
                7m,
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                1,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _inventoryReservationServiceMock.Verify(
            x => x.RestoreAsync(
                It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
```

Add this second method:

```csharp
    [Fact]
    public async Task Handle_NonOpenedToNewTransition_DoesNotCallRestore()
    {
        // Arrange — InTransit -> Received transition (no inventory restore)
        var box = CreateTestBoxWithItems(TransportBoxState.InTransit);
        SetupReceivedTransitionMocks(box);

        var request = new ChangeTransportBoxStateRequest
        {
            BoxId = 1,
            NewState = TransportBoxState.Received
        };

        // Act
        await _handler.Handle(request, CancellationToken.None);

        // Assert
        _inventoryReservationServiceMock.Verify(
            x => x.RestoreAsync(
                It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<DateTime>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
```

- [ ] **Step 6: Run the handler tests and confirm everything passes**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ChangeTransportBoxStateHandlerTests"
```

Expected: all existing tests plus the two new ones pass (count = previous count + 2), 0 failed.

- [ ] **Step 7: Run the boundary test — Logistics row should now go GREEN**

```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ModuleBoundariesTests"
```

Expected: 2 tests passed (Leaflet row + Logistics row), 0 failed.

If the Logistics row still fails, the failure message names the remaining leak — fix it before commit. The two most likely causes are: (a) the `using Anela.Heblo.Domain.Features.Manufacture.Inventory;` directive still present somewhere in the two modified handler files (search both files and remove it), or (b) a method signature still references `ManufacturedProductInventoryItem`.

- [ ] **Step 8: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/ChangeTransportBoxState/ChangeTransportBoxStateHandler.cs \
        backend/test/Anela.Heblo.Tests/Features/Logistics/Transport/ChangeTransportBoxStateHandlerTests.cs
git commit -m "refactor(logistics): route ChangeTransportBoxState inventory restore through IInventoryReservationService"
```

---

## Task 9: Final validation — full build, full test run, format check

- [ ] **Step 1: Full backend build**

```bash
dotnet build backend/Anela.Heblo.sln
```

Expected: Build succeeded, 0 errors, 0 new warnings.

- [ ] **Step 2: Full backend test run**

```bash
dotnet test backend/Anela.Heblo.sln
```

Expected: All tests pass. Pay attention to the totals — the count should equal the pre-change count plus 9 new tests (7 adapter tests + 2 ChangeTransportBoxStateHandler restore tests). No skipped tests in modified files.

- [ ] **Step 3: Format check**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```

Expected: no output (clean). If format changes are reported, run `dotnet format backend/Anela.Heblo.sln`, review the diff, and amend the most-recent commit if the diff is trivial — otherwise add a separate format commit.

- [ ] **Step 4: Confirm no lingering Manufacture-inventory references in Logistics**

```bash
grep -rn "Anela.Heblo.Domain.Features.Manufacture\|IManufacturedProductInventoryRepository\|ManufacturedProductInventoryItem" \
  backend/src/Anela.Heblo.Application/Features/Logistics/
```

Expected: zero matches. If any line is printed, find which file still has the leak and remove it (the boundary test must still be passing — if it's passing but grep shows a hit, the hit is in a non-type-referencing place like a comment, which is acceptable; otherwise re-investigate).

```bash
grep -rn "Anela.Heblo.Domain.Features.Manufacture\|IManufacturedProductInventoryRepository\|ManufacturedProductInventoryItem" \
  backend/test/Anela.Heblo.Tests/Features/Logistics/
```

Expected: zero matches in the migrated test files (`AddItemToBoxHandlerTests.cs`, `ChangeTransportBoxStateHandlerTests.cs`). Hits in unrelated Logistics test files (e.g. `GiftPackageManufactureServiceTests.cs`) are acceptable — they're out of scope.

- [ ] **Step 5: Final summary log (no commit if nothing to commit)**

If `dotnet format` produced changes:

```bash
git add -A
git commit -m "chore: dotnet format after Logistics-Manufacture decoupling"
```

If everything is already clean, this step is a no-op — skip.

---

## Spec coverage check

| Spec requirement | Implemented in |
|------------------|----------------|
| FR-1 (Contract in Logistics) | Task 1 |
| FR-2 (Operation signatures + sealed-record result type) | Task 1 (locked-down result type per arch-review amendment #1) |
| FR-3 (Adapter in Manufacture, internal sealed, no SaveChanges, catch InvalidOperationException) | Task 2 (with NFR-3 guarded by Task 4 Step 5 + adapter Step 1 comment per arch-review amendment #5) |
| FR-4 (DI binding in ManufactureModule) | Task 3 |
| FR-5 (Migrate AddItemToBoxHandler) | Task 7 |
| FR-6 (Migrate ChangeTransportBoxStateHandler) | Task 8 |
| FR-7 (Extend ModuleBoundariesTests with Theory + Logistics row, empty allowlist) | Tasks 5–6 (Theory refactor first, then row added separately so red-state is auditable per arch-review Prerequisite #5) |
| FR-8 (Migrated handler tests + adapter tests) | Task 4 (adapter), Task 7 (AddItemToBox), Task 8 (ChangeTransportBoxState — including newly added Opened→New restore tests) |
| NFR-1 (Behavioral parity) | Verified via Task 7 / Task 8 / Task 9 test runs |
| NFR-2 (Module boundaries enforced in CI) | Task 6 + Task 8 (boundary test green) |
| NFR-3 (Transaction semantics — adapter has no SaveChanges) | Adapter design (Task 2) + adapter test `TryConsumeAsync_DoesNotCallSaveChanges` (Task 4) |
| NFR-4 (Logging in adapter preserves IDs) | Adapter Step 1 (Task 2) + adapter test `RestoreAsync_ItemNotFound_IsNoOpAndLogsWarning` (Task 4) |
| NFR-5 (Code style — internal sealed, nullable, CancellationToken last) | Adapter is `internal sealed` (Task 2); all async methods accept `CancellationToken` as last param (Task 1, Task 2); files <800 lines |
| NFR-6 (No new NuGet packages) | No package references touched in any task |

**Prerequisites from arch-review:**
- Prereq #1: `BaseRepository.UpdateAsync` only calls `DbSet.Update(entity)` and returns `Task.CompletedTask` — verified at plan-writing time (`backend/src/Anela.Heblo.Persistence/Repositories/BaseRepository.cs:70-74`); adapter design is safe.
- Prereq #2: `Anela.Heblo.Application` already grants `InternalsVisibleTo("Anela.Heblo.Tests")` — verified at plan-writing time (`backend/src/Anela.Heblo.Application/AssemblyInfo.cs:3` and `Anela.Heblo.Application.csproj:45`); the adapter unit test in Task 4 can construct the `internal` adapter directly.
- Prereq #5 ordering (a → b → c with red-then-green) is encoded in Task 5 / Task 6 / Tasks 7–8.

## Out of scope (per spec)

- Splitting `ApplicationDbContext` per module (ADR-001 Phase 2).
- Promoting `InvalidOperationException` in `ManufacturedProductInventoryItem.Consume` to a typed `InsufficientInventoryException` — track as follow-up (arch-review Decision 2 + Spec amendment #6).
- Refactoring `HandleReceived` in `ChangeTransportBoxStateHandler` (Catalog dependency, separate concern).
- Removing existing Leaflet→KnowledgeBase allowlist entries.
- Czech-language doc updates.
