## Module
Purchase

## Finding
Three MediatR request types in the Purchase module are declared as C# `record` instead of `class`, violating the project-wide DTO rule from `CLAUDE.md`:

> "DTOs are classes, never C# records. OpenAPI client generators mishandle record parameter order."

Affected files:
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderById/GetPurchaseOrderByIdRequest.cs:5`
  ```csharp
  public record GetPurchaseOrderByIdRequest(int Id) : IRequest<GetPurchaseOrderByIdResponse>;
  ```
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseOrderHistory/GetPurchaseOrderHistoryRequest.cs:7`
  ```csharp
  public record GetPurchaseOrderHistoryRequest(int Id) : IRequest<ListResponse<PurchaseOrderHistoryDto>>;
  ```
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrderStatus/UpdatePurchaseOrderStatusRequest.cs:5-8`
  ```csharp
  public record UpdatePurchaseOrderStatusRequest(
      int Id,
      string Status
  ) : IRequest<UpdatePurchaseOrderStatusResponse>;
  ```

The other request types in this module (`CreatePurchaseOrderRequest`, `UpdatePurchaseOrderRequest`, `GetPurchaseOrdersRequest`, `GetPurchaseStockAnalysisRequest`) are all correctly declared as `class`.

## Why it matters
NSwag's OpenAPI TypeScript client generator infers parameter order from the C# record's primary constructor rather than from JSON property names. This means the generated client may bind `id` and `status` in the wrong positions for `UpdatePurchaseOrderStatusRequest`, producing runtime deserialization bugs. The two route-only records (`GetPurchaseOrderByIdRequest`, `GetPurchaseOrderHistoryRequest`) are lower risk today (single `int Id` parameter) but violate the rule and introduce fragility if a second property is ever added.

## Suggested fix
Convert all three from positional records to standard classes with property setters (matching the existing pattern in the module):

```csharp
// GetPurchaseOrderByIdRequest.cs
public class GetPurchaseOrderByIdRequest : IRequest<GetPurchaseOrderByIdResponse>
{
    public int Id { get; set; }
}

// GetPurchaseOrderHistoryRequest.cs
public class GetPurchaseOrderHistoryRequest : IRequest<ListResponse<PurchaseOrderHistoryDto>>
{
    public int Id { get; set; }
}

// UpdatePurchaseOrderStatusRequest.cs
public class UpdatePurchaseOrderStatusRequest : IRequest<UpdatePurchaseOrderStatusResponse>
{
    public int Id { get; set; }
    public string Status { get; set; } = null!;
}
```

Update the call sites in `PurchaseOrdersController` to use object initialiser syntax instead of positional constructor calls.

---
_Filed by daily arch-review routine on 2026-09-11._
