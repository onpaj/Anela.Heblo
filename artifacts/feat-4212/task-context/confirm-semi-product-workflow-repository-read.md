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

