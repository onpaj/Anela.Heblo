## Module
Manufacture

## Finding
`GetManufactureProtocolHandler` throws `InvalidOperationException` for two business-rule conditions instead of returning a structured response:

- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs:27` — throws when order is not found
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetManufactureProtocol/GetManufactureProtocolHandler.cs:30–33` — throws when order is not in `Completed` state

```csharp
var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken)
            ?? throw new InvalidOperationException($"Manufacture order {request.Id} not found.");

if (order.State != ManufactureOrderState.Completed)
{
    throw new InvalidOperationException(
        $"Manufacture order {order.OrderNumber} is not completed; cannot generate protocol.");
}
```

This forces `ManufactureOrderController.GetProtocolPdf` (`backend/src/Anela.Heblo.API/Controllers/ManufactureOrderController.cs:169–181`) to catch and translate the exception into an HTTP response:

```csharp
try
{
    var response = await _mediator.Send(request, cancellationToken);
    return File(response.PdfBytes, "application/pdf", response.FileName);
}
catch (InvalidOperationException ex)
{
    return BadRequest(new { message = ex.Message });
}
```

Every other handler in this module (e.g. `ResolveManualActionHandler`, `GetSemiproductRecipePdfHandler`, `ConfirmProductCompletionHandler`) returns a structured response object with error codes. `GetManufactureProtocolHandler` is the only outlier that uses throw-and-catch for business conditions.

## Why it matters
The project rule is explicit: "Business logic must be in MediatR handlers, NOT in controllers." The controller here is doing business-error translation — deciding what HTTP status code to return based on the exception type — which is a business responsibility. More practically, the `InvalidOperationException` is a blunt instrument: if any unexpected infrastructure exception is thrown inside the handler (e.g. a null ERP response), it will also be caught by this `catch (InvalidOperationException)` and returned as a `BadRequest`, hiding the actual error from logs and potentially from the client.

## Suggested fix
Change `GetManufactureProtocolResponse` to carry an error code (following the pattern of `GetSemiproductRecipePdfResponse` which already uses `ErrorCodes.ManufactureTemplateNotFound`). Replace the throws with early returns, and let `ManufactureOrderController.GetProtocolPdf` use the shared `HandleResponse` helper like all other actions:

```csharp
// In handler — replace throws with early returns:
var order = await _repository.GetOrderByIdAsync(request.Id, cancellationToken);
if (order is null)
    return new GetManufactureProtocolResponse(ErrorCodes.ResourceNotFound,
        new Dictionary { { "orderId", request.Id.ToString() } });

if (order.State != ManufactureOrderState.Completed)
    return new GetManufactureProtocolResponse(ErrorCodes.InvalidOperation,
        new Dictionary { { "state", order.State.ToString() } });

// In controller — replace try/catch with HandleResponse:
public async Task GetProtocolPdf(int id, CancellationToken cancellationToken)
{
    var response = await _mediator.Send(new GetManufactureProtocolRequest { Id = id }, cancellationToken);
    if (!response.Success)
        return HandleResponse(response);
    return File(response.PdfBytes, "application/pdf", response.FileName);
}
```

---
_Filed by daily arch-review routine on 2026-07-13._
