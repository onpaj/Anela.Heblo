# Design: Type CreatePurchaseOrderRequest date fields as DateTime

## Component Design
- `CreatePurchaseOrderRequest`: `OrderDate` `DateTime`, `ExpectedDeliveryDate` `DateTime?`; remove `[Required]` on OrderDate (no-op for value type).
- `CreatePurchaseOrderRequestValidator`:
  - `OrderDate`: `NotEqual(default(DateTime))` "Order date is required"; `Must(d => d.Date <= DateTime.UtcNow.Date.AddDays(30))` "Order date cannot be more than 30 days in the future".
  - `ExpectedDeliveryDate`: when `HasValue`, `Must((r, d) => d!.Value.Date >= r.OrderDate.Date)` "Expected delivery date must be on or after the order date".
  - Delete `BeAValidDate`, `NotBeTooFarInFuture`, `BeAfterOrEqualToOrderDate` string helpers. Line validator unchanged.
- `CreatePurchaseOrderHandler`: replace the two parse lines with
  `var orderDate = DateTime.SpecifyKind(request.OrderDate.Date, DateTimeKind.Utc);`
  `var expectedDeliveryDate = request.ExpectedDeliveryDate.HasValue ? DateTime.SpecifyKind(request.ExpectedDeliveryDate.Value.Date, DateTimeKind.Utc) : (DateTime?)null;`
  Drop the now-unused `using Anela.Heblo.Application.Common.Extensions;` if unused.
- `PurchaseOrderHelpers.tsx`: create-request mapping builds `Date` from form `YYYY-MM-DD` as UTC midnight.

## Data Schemas
Request JSON:
```json
{ "supplierId": 1, "orderDate": "2026-10-23", "expectedDeliveryDate": "2026-11-05", "contactVia": null, "notes": null, "orderNumber": null, "lines": [] }
```
OpenAPI: `orderDate: { type: string, format: date-time }`, `expectedDeliveryDate: { type: string, format: date-time, nullable: true }`. Response and DB schema unchanged.
