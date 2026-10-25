## Module
Purchase

## Finding
The Create and Update request DTOs for the same entity use different types for date fields:

- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/CreatePurchaseOrder/CreatePurchaseOrderRequest.cs:13-15`
  ```csharp
  public string OrderDate { get; set; } = null!;
  public string? ExpectedDeliveryDate { get; set; }
  ```
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/UpdatePurchaseOrder/UpdatePurchaseOrderRequest.cs:15`
  ```csharp
  public DateTime? ExpectedDeliveryDate { get; set; }
  ```

`CreatePurchaseOrderHandler` then parses the strings via extension methods:
```csharp
// CreatePurchaseOrderHandler.cs:54-55
var orderDate = request.OrderDate.ToUtcDateTime();
var expectedDeliveryDate = request.ExpectedDeliveryDate.ToUtcDateTimeOrNull();
```

`UpdatePurchaseOrderHandler` passes `request.ExpectedDeliveryDate` directly as `DateTime?` to `purchaseOrder.Update(...)`.

## Why it matters
Using `string` for a field with `DateTime` semantics pushes parsing and validation out of the model-binding pipeline into the handler. This means:
- Any invalid string (e.g. `"not-a-date"`) passes model validation and only fails at handler execution — a worse error path and a 500 rather than a 400.
- The two parallel endpoints for the same entity behave differently, confusing API consumers and the generated TypeScript client (one gets `string`, the other gets `Date`).
- `CreatePurchaseOrderRequestValidator` cannot apply date-range constraints at the validator level — it can only inspect the raw string.

The `UpdatePurchaseOrderRequest` already shows the correct pattern was identified and applied; `CreatePurchaseOrderRequest` was not updated to match.

## Suggested fix
Change `CreatePurchaseOrderRequest` to:
```csharp
public DateTime OrderDate { get; set; }
public DateTime? ExpectedDeliveryDate { get; set; }
```
Remove the `ToUtcDateTime()` / `ToUtcDateTimeOrNull()` calls from `CreatePurchaseOrderHandler` lines 54–55. Add UTC normalization directly in the handler (`DateTime.SpecifyKind(request.OrderDate, DateTimeKind.Utc)`) matching what the Update path already does, or handle it inside `PurchaseOrder`'s constructor (which already normalises via `ToUniversalTime()`). Update `CreatePurchaseOrderRequestValidator` to validate date ranges using typed comparisons.

---
_Filed by daily arch-review routine on 2026-10-11._
