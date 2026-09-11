# Design: Convert Purchase Module Request DTOs from Record to Class

## Component Design

No new or restructured components. Three existing MediatR request types, each already a single-file component within its own Vertical Slice `UseCases/{UseCase}/` folder, change only their C# type declaration keyword and property style — their responsibility (carrying request parameters to their existing handler via `IRequest<TResponse>`) is unchanged:

- **`GetPurchaseOrderByIdRequest`** (`Features/Purchase/UseCases/GetPurchaseOrderById/`) — carries the order id for `GetPurchaseOrderByIdHandler`. Constructed by `PurchaseOrdersController` from a route parameter.
- **`GetPurchaseOrderHistoryRequest`** (`Features/Purchase/UseCases/GetPurchaseOrderHistory/`) — carries the order id for `GetPurchaseOrderHistoryHandler`. Constructed by `PurchaseOrdersController` from a route parameter.
- **`UpdatePurchaseOrderStatusRequest`** (`Features/Purchase/UseCases/UpdatePurchaseOrderStatus/`) — carries the order id and target status for `UpdatePurchaseOrderStatusHandler`. Model-bound directly from the HTTP request body via `[FromBody]` in `PurchaseOrdersController` (not constructed positionally by the controller).

Each type's boundary is unchanged: it is consumed only by its own handler and constructed/bound only by `PurchaseOrdersController` (plus test code). No new interfaces, no new dependencies, no change to `IRequest<TResponse>` contracts.

## Data Schemas

Wire format (HTTP route/body shape) is unchanged for all three — only the C# in-memory representation changes, from a record's positional constructor to a class's settable properties. JSON property names and casing are unaffected.

```csharp
// GetPurchaseOrderByIdRequest — GET route parameter, bound as int id, constructed as:
public class GetPurchaseOrderByIdRequest : IRequest<GetPurchaseOrderByIdResponse>
{
    public int Id { get; set; }
}

// GetPurchaseOrderHistoryRequest — GET route parameter, bound as int id, constructed as:
public class GetPurchaseOrderHistoryRequest : IRequest<ListResponse<PurchaseOrderHistoryDto>>
{
    public int Id { get; set; }
}

// UpdatePurchaseOrderStatusRequest — PUT/PATCH request body, model-bound via [FromBody]:
public class UpdatePurchaseOrderStatusRequest : IRequest<UpdatePurchaseOrderStatusResponse>
{
    public int Id { get; set; }
    public string Status { get; set; } = null!;
}
```

Response types (`GetPurchaseOrderByIdResponse`, `ListResponse<PurchaseOrderHistoryDto>`, `UpdatePurchaseOrderStatusResponse`) are out of scope and unmodified. The OpenAPI schema generated from these types is expected to be structurally identical before and after (same property names/types); only the previously-fragile positional-parameter-order inference on the TypeScript client generation side is eliminated.
