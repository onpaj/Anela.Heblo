# [arch-review] Manufacture: ConfirmProductCompletion and ConfirmSemiProduct workflows use HTTP response DTO as internal data carrier

## Module
Manufacture

## Finding
Both `ConfirmProductCompletionWorkflow` and `ConfirmSemiProductManufactureWorkflow` orchestrate multiple steps via `IMediator.Send()`. After calling `UpdateManufactureOrderRequest`, they receive `UpdateManufactureOrderResponse` and extract its inner `UpdateManufactureOrderDto order`, then pass that DTO directly into subsequent business logic steps.

**ConfirmProductCompletionWorkflow.cs** (~line 140):
```csharp
var updateResult = await _mediator.Send(
    new UpdateManufactureOrderRequest { Id = orderId, Products = productRequests }, ct);

// UpdateManufactureOrderDto flows into ERP submission:
var submitResult = await SubmitToErpAsync(orderId, updateResult.Order!, distribution, ct);
```

`SubmitToErpAsync` accepts `UpdateManufactureOrderDto order` and reads `order.SemiProduct`, `order.Products`, `order.ManufactureType`, `order.OrderNumber` for business decisions (selecting ERP items, computing direct output totals, naming the manufacture record).

The same pattern exists in **ConfirmSemiProductManufactureWorkflow.cs**.

`UpdateManufactureOrderDto` lives in `Application/Features/Manufacture/UseCases/UpdateManufactureOrder/` — it is scoped to the `UpdateManufactureOrder` use case and designed as an HTTP response shape. Two workflow services in `Services/Workflows/` now depend on it as an internal domain data carrier.

## Why it matters
**Coupling between API response shape and business logic**: `UpdateManufactureOrderDto` is the response the `UpdateManufactureOrder` use case returns to the HTTP client. If its structure changes for presentation reasons (e.g. flattening a field, renaming a property, changing nullability), the workflows that depend on it for business-logic decisions will silently break at runtime. The unit tests for the workflows mock `IMediator`, so a structural change to the DTO would not be caught by CI.

This is a violation of the principle that use-case response DTOs belong to the HTTP boundary, not the domain logic layer.

## Suggested fix
After calling `UpdateManufactureOrderRequest`, have the workflow fetch the domain entity directly from `IManufactureOrderRepository` rather than relying on the update handler's response DTO. The repository is already injected into the handler — it can be injected into the workflow too:

```csharp
// In ConfirmProductCompletionWorkflow / ConfirmSemiProductManufactureWorkflow:
await _mediator.Send(updateRequest, ct);  // persist the change
var order = await _repository.GetOrderByIdAsync(orderId, ct);  // read the domain entity
// pass order (ManufactureOrder) into SubmitToErpAsync, etc.
```

This decouples the workflow from the HTTP response shape. The `UpdateManufactureOrderDto` becomes free to evolve independently.

## Additional constraints for planning (from repo CLAUDE.md)
- DTOs are classes, never C# records (OpenAPI client generators mishandle record parameter order). Internal domain types may still be records.
- Consult `docs/architecture/development_guidelines.md` before any module-boundary/DTO changes.
