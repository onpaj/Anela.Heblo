# Design: Decouple Manufacture confirmation workflows from UpdateManufactureOrder response DTO

## Component Design

### `ConfirmProductCompletionWorkflow` (`Application/Features/Manufacture/Services/Workflows/ConfirmProductCompletionWorkflow.cs`)
**Responsibility (unchanged):** orchestrate product-completion confirmation — persist actual quantities, gate on residue threshold, submit to ERP, update BoM ingredient amounts, transition order state.

**Change:** gains `IManufactureOrderRepository _repository` as a constructor dependency. After the existing `UpdateProductsQuantityAsync` call succeeds, the workflow calls `_repository.GetOrderByIdAsync(orderId, cancellationToken)` to obtain the `ManufactureOrder` domain entity, and passes that entity — not `updateResult.Order` — into every downstream business-logic call:
- `_residueCalculator.CalculateAsync(order, cancellationToken)`
- `SubmitToErpAsync(orderId, order, distribution, cancellationToken)`
- `UpdateBoMIngredientsAsync(submitResult, order, distribution, orderId, cancellationToken)`

`UpdateManufactureOrderResponse` (returned by the mediator call) is consulted only for `.Success` / `.ErrorCode` — the same two members already used for the failure branch today. Its `.Order` member (`UpdateManufactureOrderDto`) is no longer read anywhere in this class.

**New failure branch:** if `GetOrderByIdAsync` returns `null` (defensive — the same order was just successfully updated moments earlier in the same request), return the same error shape as the existing "update failed" branch:
```csharp
return new ConfirmProductCompletionResult(
    string.Format(ManufactureMessages.ProductQuantityUpdateErrorFormat, ErrorCodes.ResourceNotFound));
```

`SubmitToErpAsync` and `UpdateBoMIngredientsAsync` change their `order` parameter type from `UpdateManufactureOrderDto` to `Anela.Heblo.Domain.Features.Manufacture.ManufactureOrder`. Every field they read (`order.SemiProduct.ProductCode/ProductName/LotNumber/ExpirationDate/ActualQuantity/PlannedQuantity`, `order.Products[].ProductCode/ProductName/ActualQuantity/PlannedQuantity`, `order.ManufactureType`, `order.OrderNumber`) exists on `ManufactureOrder` and its nested `ManufactureOrderSemiProduct`/`ManufactureOrderProduct` entities with identical names and nullability — no read-site logic changes, only the declared parameter type.

### `ConfirmSemiProductManufactureWorkflow` (`Application/Features/Manufacture/Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs`)
**Responsibility (unchanged):** orchestrate semi-product-manufacture confirmation — persist actual quantity, submit to ERP, transition order state.

**Change:** same pattern as above, scoped down (no residue/BoM step):
- Gains `IManufactureOrderRepository _repository`.
- After `UpdateSemiProductQuantityAsync` succeeds, calls `_repository.GetOrderByIdAsync(orderId, cancellationToken)`.
- Passes the resulting `ManufactureOrder` into `SubmitToErpAsync(orderId, order, cancellationToken)` instead of `updateResult.Order`.
- Same null-after-success defensive branch, mirroring the existing failure result shape:
```csharp
return new ConfirmSemiProductManufactureResult(false,
    string.Format(ManufactureMessages.QuantityUpdateErrorFormat, ErrorCodes.ResourceNotFound),
    ErrorCodes.ResourceNotFound);
```
`SubmitToErpAsync`'s `order` parameter type changes from `UpdateManufactureOrderDto` to `ManufactureOrder`.

### `IResidueDistributionCalculator` / `ResidueDistributionCalculator` (`Application/Features/Manufacture/Services/`)
**Responsibility (unchanged):** compute residue distribution across products for a given order.

**Change:** `CalculateAsync`'s single parameter changes type:
```csharp
// was:
Task<ResidueDistribution> CalculateAsync(UpdateManufactureOrderDto order, CancellationToken cancellationToken = default);
// becomes:
Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default);
```
No other production caller exists (verified by repo search) — this is a full signature replacement, not an added overload.

### `IManufactureNameBuilder` / `ManufactureNameBuilder` (`Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs`)
**Responsibility (unchanged):** build the truncated ERP manufacture-record name from order data.

**Change:** `Build`'s first parameter changes type:
```csharp
// was:
string Build(UpdateManufactureOrderDto order, ErpManufactureType type);
// becomes:
string Build(ManufactureOrder order, ErpManufactureType type);
```
No other production caller exists (verified by repo search) — full signature replacement.

### `UpdateManufactureOrderHandler` / `UpdateManufactureOrderDto` / `UpdateManufactureOrderResponse`
**No component changes.** These remain exactly as they are: the `UpdateManufactureOrder` use case still returns `UpdateManufactureOrderDto` shaped for its own HTTP callers. The two workflows above simply stop being consumers of that DTO for internal-logic purposes; they still call `_mediator.Send(new UpdateManufactureOrderRequest {...})` to persist the change, unchanged.

### `IManufactureOrderRepository`
**No interface changes.** `GetOrderByIdAsync(int id, CancellationToken)` already exists and is already registered in `ManufactureModule.cs` (`services.AddScoped<IManufactureOrderRepository, ManufactureOrderRepository>()`). The two workflow classes become new consumers via constructor injection — no new DI registration is needed since both workflows are already resolved from the same DI container/scope as the repository.

## Data Schemas

No database schema, HTTP contract, or event-payload changes. This section documents the **internal type substitution** — the shape of data flowing between the changed components — since that is the entire subject of this fix.

### Before
```
UpdateManufactureOrderResponse
├── Success: bool
├── ErrorCode: string?
└── Order: UpdateManufactureOrderDto        <-- consumed by workflows for business logic (the bug)
    ├── Id: int
    ├── OrderNumber: string
    ├── ManufactureType: ManufactureType
    ├── SemiProduct: UpdateManufactureOrderSemiProductDto?
    │   ├── ProductCode: string, ProductName: string
    │   ├── PlannedQuantity: decimal, ActualQuantity: decimal?
    │   └── LotNumber: string?, ExpirationDate: DateOnly?
    └── Products: List<UpdateManufactureOrderProductDto>
        ├── ProductCode: string, ProductName: string
        └── PlannedQuantity: decimal, ActualQuantity: decimal?
```

### After
```
UpdateManufactureOrderResponse
├── Success: bool                            <-- still consumed (persistence-result check only)
├── ErrorCode: string?                       <-- still consumed
└── Order: UpdateManufactureOrderDto         <-- NO LONGER READ by the two workflows

IManufactureOrderRepository.GetOrderByIdAsync(orderId, ct)
└── ManufactureOrder (domain entity)          <-- NEW source of truth for workflow business logic
    ├── Id: int
    ├── OrderNumber: string
    ├── ManufactureType: ManufactureType
    ├── SemiProduct: ManufactureOrderSemiProduct?
    │   ├── ProductCode: string, ProductName: string
    │   ├── PlannedQuantity: decimal, ActualQuantity: decimal?
    │   └── LotNumber: string?, ExpirationDate: DateOnly?
    └── Products: List<ManufactureOrderProduct>
        ├── ProductCode: string, ProductName: string
        └── PlannedQuantity: decimal, ActualQuantity: decimal?
```
Field names and nullability are identical between the two shapes for every field the workflows read today (confirmed against `ManufactureOrderSemiProduct.cs` / `ManufactureOrderProduct.cs` / `ManufactureOrder.cs`) — this is a 1:1 type substitution at the four call sites (`CalculateAsync`, `Build`, both `SubmitToErpAsync` overloads, `UpdateBoMIngredientsAsync`), not a data remodeling exercise.

### Updated method signatures (contract-level diff)
```csharp
// Application/Features/Manufacture/Services/IResidueDistributionCalculator.cs
- Task<ResidueDistribution> CalculateAsync(UpdateManufactureOrderDto order, CancellationToken cancellationToken = default);
+ Task<ResidueDistribution> CalculateAsync(ManufactureOrder order, CancellationToken cancellationToken = default);

// Application/Features/Manufacture/Services/Workflows/ManufactureNameBuilder.cs
- string Build(UpdateManufactureOrderDto order, ErpManufactureType type);
+ string Build(ManufactureOrder order, ErpManufactureType type);

// Services/Workflows/ConfirmProductCompletionWorkflow.cs (private helpers)
- private async Task<SubmitManufactureResponse> SubmitToErpAsync(int orderId, UpdateManufactureOrderDto order, ResidueDistribution distribution, CancellationToken ct)
+ private async Task<SubmitManufactureResponse> SubmitToErpAsync(int orderId, ManufactureOrder order, ResidueDistribution distribution, CancellationToken ct)
- private async Task<List<string>> UpdateBoMIngredientsAsync(SubmitManufactureResponse submitResult, UpdateManufactureOrderDto order, ResidueDistribution distribution, int orderId, CancellationToken ct)
+ private async Task<List<string>> UpdateBoMIngredientsAsync(SubmitManufactureResponse submitResult, ManufactureOrder order, ResidueDistribution distribution, int orderId, CancellationToken ct)

// Services/Workflows/ConfirmSemiProductManufactureWorkflow.cs (private helper)
- private async Task<SubmitManufactureResponse> SubmitToErpAsync(int orderId, UpdateManufactureOrderDto order, CancellationToken ct)
+ private async Task<SubmitManufactureResponse> SubmitToErpAsync(int orderId, ManufactureOrder order, CancellationToken ct)
```
Constructor additions (both workflow classes):
```csharp
+ private readonly IManufactureOrderRepository _repository;
// added as a new constructor parameter, alongside existing dependencies
```

No changes to `IConfirmProductCompletionWorkflow.ExecuteAsync` / `IConfirmSemiProductManufactureWorkflow.ExecuteAsync` public signatures, no changes to `UpdateManufactureOrderDto`/`UpdateManufactureOrderResponse`/`UpdateManufactureOrderRequest`, no OpenAPI/TypeScript client regeneration required.
