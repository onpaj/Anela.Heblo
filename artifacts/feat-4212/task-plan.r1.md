# Decouple Manufacture Confirmation Workflows From UpdateManufactureOrderDto Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop `ConfirmProductCompletionWorkflow` and `ConfirmSemiProductManufactureWorkflow` (and the two helper interfaces they call, `IResidueDistributionCalculator` and `IManufactureNameBuilder`) from using `UpdateManufactureOrderDto` — the `UpdateManufactureOrder` use case's HTTP response DTO — as an internal business-logic data carrier. Replace it with the `ManufactureOrder` domain entity, fetched directly via `IManufactureOrderRepository.GetOrderByIdAsync` after the update use case persists its change.

**Architecture:** Both workflows keep calling `_mediator.Send(new UpdateManufactureOrderRequest {...})` to persist the change and check `.Success`/`.ErrorCode`, but no longer read `.Order` from the response. Immediately after, they call the already-DI-registered `IManufactureOrderRepository.GetOrderByIdAsync(orderId, ct)` and pass the resulting `ManufactureOrder` into every downstream business-logic step. `IResidueDistributionCalculator.CalculateAsync` and `IManufactureNameBuilder.Build` change their parameter type from `UpdateManufactureOrderDto` to `ManufactureOrder` (both have exactly one production caller each, confirmed by repo-wide search). `UpdateManufactureOrderDto`/`UpdateManufactureOrderResponse`/`UpdateManufactureOrderHandler` are not touched.

**Tech Stack:** .NET 8, MediatR, xUnit, Moq, FluentAssertions.

---

### task: residue-distribution-calculator-domain-entity

`IResidueDistributionCalculator` operates on `ManufactureOrder`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ResidueDistributionCalculatorTests.cs`

- [ ] **Step 1: Update the interface signature**

In `IResidueDistributionCalculator.cs`, change:
```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;

public interface IResidueDistributionCalculator
{
    Task<ResidueDistribution> CalculateAsync(UpdateManufactureOrderDto order, CancellationToken cancellationToken = default);
}
```
to:
```csharp
public interface IResidueDistributionCalculator
{
    Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default);
}
```
Remove the now-unused `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line (the file already has `using Anela.Heblo.Domain.Features.Manufacture;` for `ManufactureOrder`, or add it if missing).

- [ ] **Step 2: Update the implementation**

In `ResidueDistributionCalculator.cs`, change every occurrence of the parameter type from `UpdateManufactureOrderDto` to `ManufactureOrder` on both methods that take `order`:
```csharp
public async Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default)
```
and
```csharp
private async Task<List<ProductCalculationData>> BuildProductDataAsync(
    ManufactureOrder order,
    CancellationToken cancellationToken)
```
No other line in the method bodies changes — `order.ManufactureType`, `order.SemiProduct`, `order.SemiProduct.ProductCode`, `order.SemiProduct.ActualQuantity`, `order.SemiProduct.PlannedQuantity`, `order.Products`, `product.ProductCode`, `product.ActualQuantity`, `product.PlannedQuantity`, `product.ProductName` all exist with identical names/nullability on `ManufactureOrder`/`ManufactureOrderSemiProduct`/`ManufactureOrderProduct`. Remove the now-unused `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line from the top of the file.

- [ ] **Step 3: Build to confirm the compile error surface is limited to callers**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build fails only on the two workflow files (they still pass `UpdateManufactureOrderDto` at their `CalculateAsync` call sites) — no unexpected errors inside `ResidueDistributionCalculator.cs` itself. This is expected at this point in the plan; Task 3 fixes the workflow call site.

- [ ] **Step 4: Update the unit test file's order fixtures**

`ResidueDistributionCalculatorTests.cs` builds `UpdateManufactureOrderDto`/`UpdateManufactureOrderSemiProductDto`/`UpdateManufactureOrderProductDto` inline at every test's Arrange section (lines ~220, ~261, ~331, ~430) and inside the two private builder methods `BuildOrder` (~line 451) and `BuildOrderWithZeroProduct` (~line 479). Apply this exact rename throughout the file, in every one of those locations:
- `UpdateManufactureOrderDto` → `ManufactureOrder`
- `UpdateManufactureOrderSemiProductDto` → `ManufactureOrderSemiProduct`
- `UpdateManufactureOrderProductDto` → `ManufactureOrderProduct`

Concrete worked example — `BuildOrder` becomes:
```csharp
private ManufactureOrder BuildOrder(
    string semiCode, decimal semiActual, decimal semiPlanned,
    List<(string code, decimal? actual, decimal planned)> products,
    ManufactureType manufactureType = ManufactureType.MultiPhase)
{
    var productEntities = products.Select(p => new ManufactureOrderProduct
    {
        ProductCode = p.code,
        ProductName = p.code,
        ActualQuantity = p.actual,
        PlannedQuantity = p.planned,
    }).ToList();

    return new ManufactureOrder
    {
        ManufactureType = manufactureType,
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = semiCode,
            ProductName = semiCode,
            ActualQuantity = semiActual,
            PlannedQuantity = semiPlanned,
        },
        Products = productEntities,
    };
}
```
(Match `BuildOrder`'s actual current parameter list and body exactly as it exists in the file today — only the three type names change; do not alter any parameter names, default values, or logic.) Apply the identical three-way rename to `BuildOrderWithZeroProduct` and to every inline `new UpdateManufactureOrderDto { ... }` / `new UpdateManufactureOrderSemiProductDto { ... }` / `new UpdateManufactureOrderProductDto { ... }` block in the test methods at the four line ranges listed above. Remove the `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line from the top of the test file once no reference to the `UpdateManufactureOrder` namespace remains in it.

- [ ] **Step 5: Run the calculator's tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ResidueDistributionCalculatorTests"`
Expected: all tests PASS with identical assertions to before — this is a type substitution, not a logic change, so every existing expected value (`ActualSemiProductQuantity`, `Difference`, `DifferencePercentage`, `IsWithinAllowedThreshold`, per-product `AdjustedGramsPerUnit`, etc.) must still match.

- [ ] **Step 6: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs \
        src/Anela.Heblo.Application/Features/Manufacture/Services/ResidueDistributionCalculator.cs \
        test/Anela.Heblo.Tests/Features/Manufacture/Services/ResidueDistributionCalculatorTests.cs
git commit -m "refactor(manufacture): IResidueDistributionCalculator takes ManufactureOrder instead of UpdateManufactureOrderDto"
```

---

### task: manufacture-name-builder-domain-entity

`IManufactureNameBuilder` operates on `ManufactureOrder`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs`

- [ ] **Step 1: Update the interface and implementation**

In `ManufactureNameBuilder.cs`, replace:
```csharp
using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

public interface IManufactureNameBuilder
{
    string Build(UpdateManufactureOrderDto order, ErpManufactureType type);
}
```
with:
```csharp
using Anela.Heblo.Domain.Features.Manufacture;

namespace Anela.Heblo.Application.Features.Manufacture.Services.Workflows;

public interface IManufactureNameBuilder
{
    string Build(ManufactureOrder order, ErpManufactureType type);
}
```
and change the implementation's method signature the same way:
```csharp
public string Build(ManufactureOrder order, ErpManufactureType type)
```
No other line in the method body changes — `order.SemiProduct`, `order.SemiProduct.ProductCode`, `order.SemiProduct.ProductName`, `order.Products.All(p => p.ProductCode == semiCode)` all resolve identically on `ManufactureOrder`.

- [ ] **Step 2: Update the unit test file**

In `ManufactureNameBuilderTests.cs`, replace the `CreateOrder` helper (currently building `UpdateManufactureOrderDto`/`UpdateManufactureOrderSemiProductDto`/`UpdateManufactureOrderProductDto`) with:
```csharp
private static ManufactureOrder CreateOrder(
    string semiCode,
    string semiName,
    (string code, string name)[] products)
{
    return new ManufactureOrder
    {
        OrderNumber = "MO-TEST-001",
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = semiCode,
            ProductName = semiName,
            PlannedQuantity = 10m,
            ActualQuantity = 10m,
        },
        Products = products.Select(p => new ManufactureOrderProduct
        {
            ProductCode = p.code,
            ProductName = p.name,
            SemiProductCode = semiCode,
            PlannedQuantity = 10m,
            ActualQuantity = 10m,
        }).ToList(),
    };
}
```
Remove the `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line from the top of the file (no longer referenced). No other line in the file changes — all five `[Fact]` test bodies call `CreateOrder(...)` and `_builder.Build(order, ...)` exactly as before; only the helper's return/construction type changed.

- [ ] **Step 3: Run the name builder's tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureNameBuilderTests"`
Expected: all 5 tests PASS with identical results (`Build_WhenProductCodeShorterThan6Chars_DoesNotThrow`, `Build_WhenSemiProduct_PrependsMSuffix`, `Build_WhenSinglephaseProduct_ReturnsSemiCodeOnly`, `Build_WhenResultExceeds40Chars_TruncatesSafely`, `Build_WhenCodeShorterThanPrefix_UsesFullCode`).

- [ ] **Step 4: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs \
        test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ManufactureNameBuilderTests.cs
git commit -m "refactor(manufacture): IManufactureNameBuilder takes ManufactureOrder instead of UpdateManufactureOrderDto"
```

---

### task: confirm-product-completion-workflow-repository-read

`ConfirmProductCompletionWorkflow` reads the domain entity from the repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs`

- [ ] **Step 1: Add the repository dependency**

In `ConfirmProductCompletionWorkflow.cs`, add the using and constructor parameter:
```csharp
using Anela.Heblo.Domain.Features.Manufacture; // already present
```
Add a new field and constructor parameter:
```csharp
private readonly IMediator _mediator;
private readonly IManufactureOrderRepository _repository;
private readonly IResidueDistributionCalculator _residueCalculator;
private readonly IManufactureNameBuilder _nameBuilder;
private readonly TimeProvider _timeProvider;
private readonly ICurrentUserService _currentUserService;
private readonly ILogger<ConfirmProductCompletionWorkflow> _logger;

public ConfirmProductCompletionWorkflow(
    IMediator mediator,
    IManufactureOrderRepository repository,
    IResidueDistributionCalculator residueCalculator,
    IManufactureNameBuilder nameBuilder,
    TimeProvider timeProvider,
    ICurrentUserService currentUserService,
    ILogger<ConfirmProductCompletionWorkflow> logger)
{
    _mediator = mediator;
    _repository = repository;
    _residueCalculator = residueCalculator;
    _nameBuilder = nameBuilder;
    _timeProvider = timeProvider;
    _currentUserService = currentUserService;
    _logger = logger;
}
```

- [ ] **Step 2: Fetch the domain entity after the update, and use it downstream**

Replace the body of `ExecuteAsync` from Step 2 onward (`// Step 2: Calculate residue distribution` through the `SubmitToErpAsync`/`UpdateBoMIngredientsAsync` calls) with:
```csharp
// Step 2: Fetch the persisted domain entity directly — do not rely on the update
// handler's response DTO (UpdateManufactureOrderDto is an HTTP-response shape, not
// an internal business-logic data carrier; see issue #4212).
var order = await _repository.GetOrderByIdAsync(orderId, cancellationToken);
if (order == null)
{
    _logger.LogError("Order {OrderId} not found after successful update", orderId);
    return new ConfirmProductCompletionResult(
        string.Format(ManufactureMessages.ProductQuantityUpdateErrorFormat, ErrorCodes.ResourceNotFound));
}

// Step 3: Calculate residue distribution
var distribution = await _residueCalculator.CalculateAsync(order, cancellationToken);

// Step 4: If outside threshold and not yet confirmed by user, request confirmation
if (!distribution.IsWithinAllowedThreshold && !overrideConfirmed)
{
    _logger.LogInformation(
        "Order {OrderId} requires user confirmation: residue {DiffPct:F2}% exceeds allowed {AllowedPct:F2}%",
        orderId, distribution.DifferencePercentage, distribution.AllowedResiduePercentage);
    return ConfirmProductCompletionResult.NeedsConfirmation(distribution);
}

// Step 5: Submit to ERP with distribution data
var submitResult = await SubmitToErpAsync(orderId, order, distribution, cancellationToken);

// Step 6: Update BoM ingredient amounts per product if ERP submission succeeded
var bomFailures = new List<string>();
if (submitResult.Success)
{
    bomFailures = await UpdateBoMIngredientsAsync(submitResult, order, distribution, orderId, cancellationToken);
}
```
Renumber the trailing `// Step 6: Transition to Completed state` comment (now Step 7) — this is a comment-only renumbering, no functional change. Add `using Anela.Heblo.Application.Shared;` if `ErrorCodes` is not already resolvable in this file (check the existing `using` list first — `ManufactureMessages` is already referenced, confirm its namespace covers `ErrorCodes` too or add the explicit using).

Add `ErrorCodes` resolution check:
Run: `grep -n "^using" backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`
If `Anela.Heblo.Application.Shared` is not listed, add `using Anela.Heblo.Application.Shared;` to the top of the file (this is the namespace `ErrorCodes` lives in, per its use in `ConfirmSemiProductManufactureWorkflow.cs`).

- [ ] **Step 3: Update the two private helper signatures**

Change:
```csharp
private async Task<SubmitManufactureResponse> SubmitToErpAsync(
    int orderId,
    UpdateManufactureOrderDto order,
    ResidueDistribution distribution,
    CancellationToken cancellationToken)
```
to:
```csharp
private async Task<SubmitManufactureResponse> SubmitToErpAsync(
    int orderId,
    ManufactureOrder order,
    ResidueDistribution distribution,
    CancellationToken cancellationToken)
```
and:
```csharp
private async Task<List<string>> UpdateBoMIngredientsAsync(
    SubmitManufactureResponse submitResult,
    UpdateManufactureOrderDto order,
    ResidueDistribution distribution,
    int orderId,
    CancellationToken cancellationToken)
```
to:
```csharp
private async Task<List<string>> UpdateBoMIngredientsAsync(
    SubmitManufactureResponse submitResult,
    ManufactureOrder order,
    ResidueDistribution distribution,
    int orderId,
    CancellationToken cancellationToken)
```
No line inside either method body changes — `order.SemiProduct`, `order.ManufactureType`, `order.Products`, `order.OrderNumber`, `_nameBuilder.Build(order, ...)` all resolve identically on `ManufactureOrder`. Remove the now-unused `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line if nothing else in the file references that namespace (the file still uses `UpdateManufactureOrderRequest`/`UpdateManufactureOrderProductRequest` from that namespace for the mediator call in `UpdateProductsQuantityAsync` — keep the `using` if so; only remove it if a check shows no remaining reference).

- [ ] **Step 4: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds with zero errors.

- [ ] **Step 5: Update the unit test file — constructor and mock**

In `ConfirmProductCompletionWorkflowTests.cs`, add the repository mock field, construct it, and pass it into the workflow:
```csharp
private readonly Mock<IMediator> _mediatorMock;
private readonly Mock<IManufactureOrderRepository> _repositoryMock;
private readonly Mock<IResidueDistributionCalculator> _residueCalculatorMock;
private readonly Mock<IManufactureNameBuilder> _nameBuilderMock;
private readonly Mock<TimeProvider> _timeProviderMock;
private readonly Mock<ICurrentUserService> _currentUserServiceMock;
private readonly Mock<ILogger<ConfirmProductCompletionWorkflow>> _loggerMock;
private readonly ConfirmProductCompletionWorkflow _workflow;

private const int ValidOrderId = 1;
private const string TestUserName = "Test User";
private const string ValidChangeReason = "Testing product completion";

public ConfirmProductCompletionWorkflowTests()
{
    _mediatorMock = new Mock<IMediator>();
    _repositoryMock = new Mock<IManufactureOrderRepository>();
    _residueCalculatorMock = new Mock<IResidueDistributionCalculator>();
    _nameBuilderMock = new Mock<IManufactureNameBuilder>();
    _timeProviderMock = new Mock<TimeProvider>();
    _currentUserServiceMock = new Mock<ICurrentUserService>();
    _loggerMock = new Mock<ILogger<ConfirmProductCompletionWorkflow>>();

    var testTime = DateTime.UtcNow;
    _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testTime));

    var testUser = new CurrentUser("test-user-id", TestUserName, "test@example.com", true);
    _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(testUser);

    _repositoryMock
        .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateOrder());

    _nameBuilderMock
        .Setup(x => x.Build(It.IsAny<ManufactureOrder>(), It.IsAny<ErpManufactureType>()))
        .Returns("Product-Short-Name");

    _workflow = new ConfirmProductCompletionWorkflow(
        _mediatorMock.Object,
        _repositoryMock.Object,
        _residueCalculatorMock.Object,
        _nameBuilderMock.Object,
        _timeProviderMock.Object,
        _currentUserServiceMock.Object,
        _loggerMock.Object);
}
```
Add `using Anela.Heblo.Domain.Features.Manufacture;` if not already present (it is — the file already imports it for `ManufactureOrderState`).

- [ ] **Step 6: Replace the DTO-based order fixtures with domain-entity fixtures**

Replace the two private helper methods at the bottom of the file:
```csharp
private static UpdateManufactureOrderResponse CreateSuccessfulUpdateOrderResponse()
{
    return new UpdateManufactureOrderResponse
    {
        Success = true,
        Order = new UpdateManufactureOrderDto { /* ... */ },
    };
}

private static UpdateManufactureOrderResponse CreateSuccessfulUpdateOrderResponseWithManyProducts(int productCount)
{
    /* builds UpdateManufactureOrderDto with `productCount` products */
}
```
with:
```csharp
private static UpdateManufactureOrderResponse CreateSuccessfulUpdateOrderResponse()
{
    return new UpdateManufactureOrderResponse { Success = true };
}

private static ManufactureOrder CreateOrder()
{
    return new ManufactureOrder
    {
        OrderNumber = "MO-2024-001",
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SP001001",
            ProductName = "Semi Product 1",
            ActualQuantity = 10.5m,
            PlannedQuantity = 10.5m,
            LotNumber = "LOT123",
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        },
        Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct
            {
                ProductCode = "P001",
                ProductName = "Product 1",
                ActualQuantity = 5.0m,
                PlannedQuantity = 5.0m,
            },
        },
    };
}

private static ManufactureOrder CreateOrderWithManyProducts(int productCount)
{
    var products = Enumerable.Range(1, productCount)
        .Select(i => new ManufactureOrderProduct
        {
            ProductCode = $"P{i:D3}",
            ProductName = $"Product {i}",
            ActualQuantity = 1.0m,
            PlannedQuantity = 1.0m,
        })
        .ToList();

    return new ManufactureOrder
    {
        OrderNumber = "MO-2024-LARGE",
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SP001001",
            ProductName = "Semi Product 1",
            ActualQuantity = 10.5m,
            PlannedQuantity = 10.5m,
            LotNumber = "LOT123",
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        },
        Products = products,
    };
}
```
(`CreateOrder()` must produce a domain entity with the exact same field values `CreateSuccessfulUpdateOrderResponse`'s DTO had — the constructor's default `_repositoryMock` setup in Step 5 already wires it to `GetOrderByIdAsync`.)

Update `ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit` (the test that currently calls `CreateSuccessfulUpdateOrderResponseWithManyProducts(ProductCount)`): change it to call `CreateSuccessfulUpdateOrderResponse()` for the mediator's `UpdateManufactureOrderRequest` response, and add:
```csharp
_repositoryMock
    .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
    .ReturnsAsync(CreateOrderWithManyProducts(ProductCount));
```
in that test's Arrange section (overriding the constructor's default for that one test).

Update `ExecuteAsync_WithDirectSemiproductRow_FiltersItFromErpItems` the same way: remove the inline `Order = new UpdateManufactureOrderDto { ... }` block from its `updateOrderResponse` (leave `updateOrderResponse` as `new UpdateManufactureOrderResponse { Success = true }`), and instead add:
```csharp
_repositoryMock
    .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
    .ReturnsAsync(new ManufactureOrder
    {
        OrderNumber = "MO-2024-DIRECT",
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SP001001",
            ProductName = "Semi Product 1",
            ActualQuantity = 1000m,
            PlannedQuantity = 1000m,
            LotNumber = "LOT-DIRECT",
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        },
        Products = new List<ManufactureOrderProduct>
        {
            new ManufactureOrderProduct { ProductCode = "P001", ProductName = "Product 1", ActualQuantity = 5.0m, PlannedQuantity = 5.0m },
            new ManufactureOrderProduct { ProductCode = "P002", ProductName = "Product 2", ActualQuantity = 3.0m, PlannedQuantity = 3.0m },
            new ManufactureOrderProduct { ProductCode = "SP001001", ProductName = "Semi Product 1", ActualQuantity = 200.0m, PlannedQuantity = 200.0m },
        },
    });
```
in its Arrange section, in place of the DTO literal previously embedded in `updateOrderResponse.Order`.

- [ ] **Step 7: Fix up the remaining `It.IsAny<UpdateManufactureOrderDto>()` mock setups**

Every `_residueCalculatorMock.Setup(x => x.CalculateAsync(It.IsAny<UpdateManufactureOrderDto>(), ...))` call in this file (there are 6: in `ExecuteAsync_HappyPath_AllStepsSucceed_ReturnsSuccess`, `ExecuteAsync_WhenResidueExceedsThresholdAndNotOverridden_ReturnsNeedsConfirmation`, `ExecuteAsync_WhenResidueExceedsThresholdButOverridden_AppendsWeightToleranceNote`, `ExecuteAsync_WhenErpSubmitFails_StillTransitionsWithManualActionRequired`, `ExecuteAsync_WhenBoMUpdateFails_AppendsFailureNoteAndSetsManualActionRequired`, `ExecuteAsync_WhenStatusUpdateFailsAfterErpSucceeds_ReturnsStatusChangeError`, `ExecuteAsync_HappyPath_ForwardsFlexiDocCodesToStatusRequest`, `ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit`, and the direct-row test) must change to:
```csharp
_residueCalculatorMock
    .Setup(x => x.CalculateAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
    .ReturnsAsync(distribution);
```
This is a mechanical type-argument rename only — the setup, `ReturnsAsync` value, and surrounding test logic are unchanged. Remove the `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line from the top of the test file once no `UpdateManufactureOrderDto`/`UpdateManufactureOrderSemiProductDto`/`UpdateManufactureOrderProductDto` reference remains in it (the file still needs `UpdateManufactureOrderResponse`, `UpdateManufactureOrderRequest`, and `UpdateManufactureOrderProductRequest`, which live in the same namespace — keep the `using` if any of those three are still referenced, which they are, so **do not remove it** in this file).

- [ ] **Step 8: Run this workflow's tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConfirmProductCompletionWorkflowTests"`
Expected: all tests PASS, including `ExecuteAsync_WhenBoMFailuresProduceOversizedNote_TruncatesToFit2000CharLimit` (30-product note truncation) and `ExecuteAsync_WithDirectSemiproductRow_FiltersItFromErpItems` (ERP item filtering) — both must produce identical results to before, since only the data's *source* changed, not its values.

- [ ] **Step 9: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs \
        test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflowTests.cs
git commit -m "refactor(manufacture): ConfirmProductCompletionWorkflow reads ManufactureOrder from repository instead of UpdateManufactureOrder response DTO"
```

---

### task: confirm-semi-product-workflow-repository-read

`ConfirmSemiProductManufactureWorkflow` reads the domain entity from the repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs`

- [ ] **Step 1: Add the repository dependency**

Add the field and constructor parameter, mirroring Task 3 Step 1:
```csharp
private readonly IMediator _mediator;
private readonly IManufactureOrderRepository _repository;
private readonly IManufactureNameBuilder _nameBuilder;
private readonly TimeProvider _timeProvider;
private readonly ICurrentUserService _currentUserService;
private readonly ILogger<ConfirmSemiProductManufactureWorkflow> _logger;

public ConfirmSemiProductManufactureWorkflow(
    IMediator mediator,
    IManufactureOrderRepository repository,
    IManufactureNameBuilder nameBuilder,
    TimeProvider timeProvider,
    ICurrentUserService currentUserService,
    ILogger<ConfirmSemiProductManufactureWorkflow> logger)
{
    _mediator = mediator;
    _repository = repository;
    _nameBuilder = nameBuilder;
    _timeProvider = timeProvider;
    _currentUserService = currentUserService;
    _logger = logger;
}
```
`IManufactureOrderRepository` lives in `Anela.Heblo.Domain.Features.Manufacture`, already imported by this file.

- [ ] **Step 2: Fetch the domain entity after the update, and use it downstream**

Replace:
```csharp
// Step 2: Create manufacture via external client
var submitManufactureResult = await SubmitToErpAsync(orderId, updateResult.Order!, cancellationToken);
```
with:
```csharp
// Step 2: Fetch the persisted domain entity directly — do not rely on the update
// handler's response DTO (UpdateManufactureOrderDto is an HTTP-response shape, not
// an internal business-logic data carrier; see issue #4212).
var order = await _repository.GetOrderByIdAsync(orderId, cancellationToken);
if (order == null)
{
    _logger.LogError("Order {OrderId} not found after successful update", orderId);
    return new ConfirmSemiProductManufactureResult(false,
        string.Format(ManufactureMessages.QuantityUpdateErrorFormat, ErrorCodes.ResourceNotFound),
        ErrorCodes.ResourceNotFound);
}

// Step 3: Create manufacture via external client
var submitManufactureResult = await SubmitToErpAsync(orderId, order, cancellationToken);
```
Renumber the trailing `// Step 3: Change state to SemiProductManufactured` comment to `// Step 4:` — comment-only renumbering.

- [ ] **Step 3: Update the private helper signature**

Change:
```csharp
private async Task<SubmitManufactureResponse> SubmitToErpAsync(
    int orderId,
    UpdateManufactureOrderDto order,
    CancellationToken cancellationToken)
```
to:
```csharp
private async Task<SubmitManufactureResponse> SubmitToErpAsync(
    int orderId,
    ManufactureOrder order,
    CancellationToken cancellationToken)
```
No line inside the method body changes. The file's `using Anela.Heblo.Application.Features.Manufacture.UseCases.UpdateManufactureOrder;` line must stay — it is still needed for `UpdateManufactureOrderRequest` and `UpdateManufactureOrderSemiProductRequest` used in `UpdateSemiProductQuantityAsync`.

- [ ] **Step 4: Build**

Run: `cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`
Expected: build succeeds with zero errors (this also confirms Task 3's changes to the shared interfaces compile cleanly against both workflows now).

- [ ] **Step 5: Update the unit test file — constructor and mock**

Mirror Task 3 Step 5. Add:
```csharp
private readonly Mock<IManufactureOrderRepository> _repositoryMock;
```
alongside the existing mocks, construct it in the constructor, add a default setup, and pass it into the workflow:
```csharp
public ConfirmSemiProductManufactureWorkflowTests()
{
    _mediatorMock = new Mock<IMediator>();
    _repositoryMock = new Mock<IManufactureOrderRepository>();
    _nameBuilderMock = new Mock<IManufactureNameBuilder>();
    _timeProviderMock = new Mock<TimeProvider>();
    _currentUserServiceMock = new Mock<ICurrentUserService>();
    _loggerMock = new Mock<ILogger<ConfirmSemiProductManufactureWorkflow>>();

    var testTime = DateTime.UtcNow;
    _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(testTime));

    var testUser = new CurrentUser("test-user-id", TestUserName, "test@example.com", true);
    _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(testUser);

    _repositoryMock
        .Setup(x => x.GetOrderByIdAsync(ValidOrderId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(CreateOrder());

    _nameBuilderMock
        .Setup(x => x.Build(It.IsAny<ManufactureOrder>(), It.IsAny<ErpManufactureType>()))
        .Returns("SP-Short-Name");

    _workflow = new ConfirmSemiProductManufactureWorkflow(
        _mediatorMock.Object,
        _repositoryMock.Object,
        _nameBuilderMock.Object,
        _timeProviderMock.Object,
        _currentUserServiceMock.Object,
        _loggerMock.Object);
}
```
Add `using Anela.Heblo.Domain.Features.Manufacture;` if not already present (it already is, per the file's current `using` list).

- [ ] **Step 6: Replace the DTO-based order fixture with a domain-entity fixture**

Find this file's equivalent of `CreateSuccessfulUpdateOrderResponse()` (same pattern as `ConfirmProductCompletionWorkflowTests.cs` Task 3 Step 6 — a private static method returning `UpdateManufactureOrderResponse { Success = true, Order = new UpdateManufactureOrderDto {...} }`). Replace it with:
```csharp
private static UpdateManufactureOrderResponse CreateSuccessfulUpdateOrderResponse()
{
    return new UpdateManufactureOrderResponse { Success = true };
}

private static ManufactureOrder CreateOrder()
{
    return new ManufactureOrder
    {
        OrderNumber = "MO-2024-001",
        SemiProduct = new ManufactureOrderSemiProduct
        {
            ProductCode = "SP001001",
            ProductName = "Semi Product 1",
            ActualQuantity = 10.5m,
            PlannedQuantity = 10.5m,
            LotNumber = "LOT123",
            ExpirationDate = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
        },
        Products = new List<ManufactureOrderProduct>(),
    };
}
```
(Match the exact field values the existing DTO fixture in this file uses — read the file's current helper method before writing `CreateOrder()` so every value carries over unchanged; the values shown above match the sibling fixture in `ConfirmProductCompletionWorkflowTests.cs` and are very likely identical here since both were written from the same original pattern, but verify against this file's actual current literal values before committing.)

- [ ] **Step 7: Fix up the remaining `It.IsAny<UpdateManufactureOrderDto>()` mock setup**

The constructor's `_nameBuilderMock.Setup(x => x.Build(It.IsAny<UpdateManufactureOrderDto>(), It.IsAny<ErpManufactureType>()))` is already updated in Step 5 above. Search the rest of the file for any other `UpdateManufactureOrderDto` reference (this workflow has no `IResidueDistributionCalculator`, so there should be no other occurrence) and confirm none remain outside the `using` line needed for `UpdateManufactureOrderRequest`/`UpdateManufactureOrderResponse`.

- [ ] **Step 8: Run this workflow's tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ConfirmSemiProductManufactureWorkflowTests"`
Expected: all tests PASS with identical results to before.

- [ ] **Step 9: Commit**

```bash
cd backend
git add src/Anela.Heblo.Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs \
        test/Anela.Heblo.Tests/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflowTests.cs
git commit -m "refactor(manufacture): ConfirmSemiProductManufactureWorkflow reads ManufactureOrder from repository instead of UpdateManufactureOrder response DTO"
```

---

### task: full-verification-and-cleanup

Full verification and cleanup

**Files:** none new — verification only.

- [ ] **Step 1: Full backend build**

Run: `cd backend && dotnet build`
Expected: 0 errors, 0 new warnings introduced by this change (pre-existing warnings elsewhere are out of scope).

- [ ] **Step 2: Format check**

Run: `cd backend && dotnet format --verify-no-changes`
Expected: no formatting diffs. If it reports diffs confined to the files touched in Tasks 1–4, run `dotnet format` (no `--verify-no-changes`) to apply them, then re-run `git add` on the affected files and amend the relevant task's commit is NOT done — instead make a new commit:
```bash
git add -A backend/src/Anela.Heblo.Application/Features/Manufacture backend/test/Anela.Heblo.Tests/Features/Manufacture
git commit -m "style(manufacture): apply dotnet format"
```

- [ ] **Step 3: Confirm no remaining production reference to `UpdateManufactureOrderDto` outside its own use case**

Run: `cd backend && grep -rln "UpdateManufactureOrderDto" src/ | grep -v "src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/"`
Expected: empty output — no production file outside the `UpdateManufactureOrder` use case folder references the DTO any more. (Test files may still reference `UpdateManufactureOrderRequest`/`UpdateManufactureOrderResponse`, which is fine and expected — this check is for `UpdateManufactureOrderDto`/`UpdateManufactureOrderSemiProductDto`/`UpdateManufactureOrderProductDto` specifically.)

- [ ] **Step 4: Run the full Manufacture test slice**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Manufacture"`
Expected: all tests PASS — this covers all four modified files plus any other Manufacture test that might incidentally reference these types (e.g. module-boundary or DI-wiring tests).

- [ ] **Step 5: Run the full backend test suite**

Run: `cd backend && dotnet test`
Expected: all tests PASS. This confirms no other module or wiring test (e.g. `ManufactureModule` DI registration tests) was affected by the constructor signature changes in Tasks 3–4.

- [ ] **Step 6: Manual acceptance check against the spec**

Confirm, by re-reading `ConfirmProductCompletionWorkflow.cs` and `ConfirmSemiProductManufactureWorkflow.cs` after all edits:
- Neither file references `UpdateManufactureOrderDto` anywhere in its method bodies (only, if at all, in a `using` retained for `UpdateManufactureOrderRequest`).
- Both constructors take `IManufactureOrderRepository`.
- Both `ExecuteAsync` methods call `_repository.GetOrderByIdAsync` exactly once, immediately after the `_mediator.Send(new UpdateManufactureOrderRequest {...})` call succeeds, and handle a `null` result without throwing.
- `IResidueDistributionCalculator.CalculateAsync` and `IManufactureNameBuilder.Build` take `ManufactureOrder`, not `UpdateManufactureOrderDto`.
- `UpdateManufactureOrderDto.cs`, `UpdateManufactureOrderResponse.cs`, `UpdateManufactureOrderHandler.cs` have zero diff against the pre-change version (`git diff origin/main -- backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/UpdateManufactureOrder/` should be empty).

- [ ] **Step 7: Final commit (if Step 6 required any fix-up)**

If Step 6 surfaced any leftover inconsistency, fix it and commit:
```bash
cd backend
git add -A src/Anela.Heblo.Application/Features/Manufacture test/Anela.Heblo.Tests/Features/Manufacture
git commit -m "fix(manufacture): final cleanup after ManufactureOrder decoupling review"
```
If nothing required fixing, this step is skipped — no empty commit.
