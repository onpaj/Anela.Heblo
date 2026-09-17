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

